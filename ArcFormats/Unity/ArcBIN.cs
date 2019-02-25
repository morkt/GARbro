//! \file       ArcBIN.cs
//! \date       2019 Feb 23
//! \brief      Unity binary data asset.
//
// Copyright (C) 2019 by morkt
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to
// deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
// sell copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Unity
{
    internal class BinArchive : ArcFile
    {
        public readonly Aes Encryption;

        public BinArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, Aes enc)
            : base (arc, impl, dir)
        {
            Encryption = enc;
        }

        #region IDisposable Members
        bool _bin_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (_bin_disposed)
                return;

            if (disposing)
                Encryption.Dispose();
            _bin_disposed = true;
            base.Dispose (disposing);
        }
        #endregion
    }

    [Serializable]
    public class BinPackKey
    {
        public byte[]   Key;
        public byte[]   IV;

        public BinPackKey (string key, string iv)
        {
            Key = Encoding.UTF8.GetBytes (key);
            IV  = Encoding.UTF8.GetBytes (iv);
        }
    }

    [Serializable]
    public class BinPackScheme : ResourceScheme
    {
        public Dictionary<string, BinPackKey>   KnownKeys;
    }

    [Export(typeof(ArchiveFormat))]
    public class BinOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BIN/IDX"; } }
        public override string Description { get { return "Unity engine resource asset"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".bin"))
                return null;
            var idx_name = Path.ChangeExtension (file.Name, "idx");
            if (!VFS.FileExists (idx_name))
                return null;
            var scheme = QueryScheme (file.Name);
            if (null == scheme)
                return null;
            var dir = new List<Entry>();
            using (var idx = VFS.OpenBinaryStream (idx_name))
            using (var aes = Aes.Create())
            {
                aes.Padding = PaddingMode.PKCS7;
                aes.Mode = CipherMode.CBC;
                aes.KeySize = 128;
                aes.Key = scheme.Key;
                aes.IV = scheme.IV;
                var input_buffer = new byte[0x100];
                var unpacker = new BinDeserializer();
                while (idx.PeekByte() != -1)
                {
                    int length = idx.ReadInt32();
                    if (length <= 0)
                        return null;
                    if (length > input_buffer.Length)
                        input_buffer = new byte[length];
                    if (idx.Read (input_buffer, 0, length) < length)
                        return null;
                    using (var decryptor = aes.CreateDecryptor())
                    using (var encrypted = new MemoryStream (input_buffer, 0, length))
                    using (var input = new InputCryptoStream (encrypted, decryptor))
                    {
                        var info = unpacker.DeserializeEntry (input);
                        var filename = info["fileName"] as string;
                        if (string.IsNullOrEmpty (filename))
                            return null;
                        filename = filename.TrimStart ('/', '\\');
                        var entry = Create<Entry> (filename);
                        entry.Offset = Convert.ToInt64 (info["index"]);
                        entry.Size   = Convert.ToUInt32 (info["size"]);
                        if (!entry.CheckPlacement (file.MaxOffset))
                            return null;
                        dir.Add (entry);
                    }
                }
            }
            if (0 == dir.Count)
                return null;
            var arc_aes = Aes.Create();
            arc_aes.Padding = PaddingMode.PKCS7;
            arc_aes.Mode = CipherMode.CBC;
            arc_aes.KeySize = 256;
            arc_aes.Key = scheme.Key;
            arc_aes.IV = scheme.IV;
            return new BinArchive (file, this, dir, arc_aes);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var bin_arc = (BinArchive)arc;
            var decryptor = bin_arc.Encryption.CreateDecryptor();
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new InputCryptoStream (input, decryptor);
        }

        BinPackKey QueryScheme (string arc_name)
        {
            return DefaultScheme.KnownKeys.Values.FirstOrDefault();
        }

        static BinPackScheme DefaultScheme = new BinPackScheme { KnownKeys = new Dictionary<string, BinPackKey>() };

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (BinPackScheme)value; }
        }
    }

    internal class BinDeserializer
    {
        byte[] m_buffer = new byte[0x20];

        public IDictionary DeserializeEntry (Stream input)
        {
            int id = input.ReadByte();
            if (id < 0x80 || id > 0x8F)
                throw new FormatException();
            int field_count = id & 0xF;
            var map = new Hashtable (field_count);
            for (int i = 0; i < field_count; ++i)
            {
                id = input.ReadByte();
                if (id < 0xA0 || id > 0xBF)
                    throw new FormatException();
                int length = id & 0x1F;
                if (input.Read (m_buffer, 0, length) < length)
                    throw new FormatException();
                var key = Encoding.UTF8.GetString (m_buffer, 0, length);
                var value = ReadField (input);
                map[key] = value;
            }
            return map;
        }

        object ReadField (Stream input)
        {
            int id = input.ReadByte();
            if (id >= 0 && id < 0x80)
            {
                return id;
            }
            else if (id >= 0xA0 && id < 0xC0)
            {
                int length = id & 0x1F;
                return ReadString (input, length);
            }
            switch (id)
            {
            case 0xD0: // Int8
                int value = input.ReadByte();
                if (-1 == value)
                    throw new FormatException();
                return (sbyte)value;

            case 0xD1: // Int16
                if (input.Read (m_buffer, 0, 2) < 2)
                    throw new FormatException();
                return BigEndian.ToInt16 (m_buffer, 0);

            case 0xD2: // Int32
                if (input.Read (m_buffer, 0, 4) < 4)
                    throw new FormatException();
                return BigEndian.ToInt32 (m_buffer, 0);

            case 0xDA: // Raw16
                if (input.Read (m_buffer, 0, 2) < 2)
                    throw new FormatException();
                int length = BigEndian.ToUInt16 (m_buffer, 0);
                return ReadString (input, length);

            default:
                throw new FormatException();
            }
        }

        string ReadString (Stream input, int length)
        {
            if (length > m_buffer.Length)
                m_buffer = new byte[(length + 0xF) & ~0xF];
            input.Read (m_buffer, 0, length);
            return Encoding.UTF8.GetString (m_buffer, 0, length);
        }
    }
}
