//! \file       ArcDAT.cs
//! \date       2017 Oct 27
//! \brief      RepiPack resource archive.
//
// Copyright (C) 2017 by morkt
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Compression;
using GameRes.Cryptography;
using GameRes.Utility;

namespace GameRes.Formats.Littlewitch
{
    [Serializable]
    public class RepiScheme : ResourceScheme
    {
        public IDictionary<string, uint[]>  KnownSchemes;
    }

    internal class RepiEntry : PackedEntry
    {
        public bool HasEncryptionKey;

        public byte[] CreateKey ()
        {
            var name_bytes = Name.ToLowerShiftJis();
            int name_length = name_bytes.Length;
            var md5 = new MD5();
            Array.Reverse (name_bytes);
            var key = new byte[1024];
            int key_pos = 0;
            for (int i = 0; i < 64; ++i)
            {
                int name_pos = i % name_length;
                md5.Update (name_bytes, name_pos, name_length - name_pos);
                md5.Final();
                Buffer.BlockCopy (md5.State, 0, key, key_pos, 16);
                key_pos += 16;
            }
            return key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/RepiPack"; } }
        public override string Description { get { return "Littlewitch engine resource archive"; } }
        public override uint     Signature { get { return 0x69706552; } } // 'Repi'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static readonly string ListFileName = "littlewitch.lst";

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "Pack"))
                return null;
            int version = file.View.ReadInt32 (8);
            if (version != 5)
                return null;
            uint name_length = file.View.ReadUInt32 (0xC);
            if (name_length < 4)
                return null;
            uint name_key = file.View.ReadUInt32 (0x10);
            var key = FindKey (file.Name, name_key);
            if (null == key)
                return null;
            int count = file.View.ReadInt32 (0x10 + name_length);
            if (!IsSaneCount (count))
                return null;
            var index = file.View.ReadBytes (0x14 + name_length, (uint)count * 0x20);
            int pos = 0;
            var dir = new List<Entry> (count);
            var name_builder = new StringBuilder();
            for (int i = 0; i < count; ++i)
            {
                DecryptData (index, pos, 0x20, key[2], key[1]);
                var entry = EntryFromMd5 (index, pos, name_builder);
                entry.Offset        = index.ToUInt32 (pos+0x10);
                entry.Size          = index.ToUInt32 (pos+0x14);
                entry.UnpackedSize  = index.ToUInt32 (pos+0x18);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.IsPacked = entry.Size != entry.UnpackedSize;
                dir.Add (entry);
                pos += 0x20;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var rent = entry as RepiEntry;
            if (null == rent || !rent.HasEncryptionKey)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var key = rent.CreateKey();
            uint enc_length = Math.Min ((uint)key.Length, entry.Size);
            byte[] encrypted = arc.File.View.ReadBytes (entry.Offset, enc_length);
            DecryptEntry (encrypted, key);
            Stream input;
            if (enc_length == entry.Size)
            {
                input = new BinMemoryStream (encrypted, entry.Name);
            }
            else
            {
                input = arc.File.CreateStream (entry.Offset + enc_length, entry.Size - enc_length);
                input = new PrefixStream (encrypted, input);
            }
            if (rent.IsPacked)
            {
                input = new LzssStream (input);
            }
            return input;
        }

        static void DecryptEntry (byte[] data, byte[] key)
        {
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] ^= key[i];
            }
        }

        static unsafe void DecryptData (byte[] data, int pos, int length, uint key, uint seed)
        {
            if (pos < 0 || pos + length > data.Length)
                throw new ArgumentOutOfRangeException ("pos", "Invalid byte array index.");
            fixed (byte* data8 = &data[pos])
            {
                uint* data32 = (uint*)data8;
                for (int count = length / 4; count > 0; --count)
                {
                    *data32 ^= key;
                    key += Binary.RotL (*data32, 16) ^ seed;
                    data32++;
                }
            }
        }

        RepiEntry EntryFromMd5 (byte[] data, int pos, StringBuilder builder)
        {
            var key = new CowArray<byte> (data, pos, 16).ToArray();
            string name;
            if (KnownNames.TryGetValue (key, out name))
            {
                var entry = FormatCatalog.Instance.Create<RepiEntry> (name);
                entry.HasEncryptionKey = true;
                return entry;
            }
            builder.Clear();
            for (int i = 0; i < 16; ++i)
            {
                builder.AppendFormat ("{0:x2}", key[i]);
            }
            return new RepiEntry { Name = builder.ToString() };
        }

        static uint[] FindKey (string arc_name, uint arc_key)
        {
            arc_name = Path.GetFileName (arc_name);
            var name_bytes = Encodings.cp932.GetBytes (arc_name);
            arc_key ^= name_bytes.ToUInt32 (0);
            return DatScheme.KnownSchemes.Values.FirstOrDefault (k => k[0] == arc_key);
        }

        static RepiScheme DatScheme = new RepiScheme { KnownSchemes = new Dictionary<string, uint[]>() };

        public override ResourceScheme Scheme
        {
            get { return DatScheme; }
            set { DatScheme = (RepiScheme)value; }
        }

        internal Dictionary<byte[], string> KnownNames { get { return s_known_file_names.Value; } }

        static Lazy<Dictionary<byte[], string>> s_known_file_names = new Lazy<Dictionary<byte[], string>> (ReadFileList);

        static Dictionary<byte[], string> ReadFileList ()
        {
            var dict = new Dictionary<byte[], string> (new Md5Comparer());
            try
            {
                var md5 = new MD5();
                FormatCatalog.Instance.ReadFileList (ListFileName, name => {
                    var name_bytes = name.ToLowerShiftJis();
                    var hash = md5.ComputeHash (name_bytes);
                    dict[hash] = name;
                });
            }
            catch (Exception X)
            {
                System.Diagnostics.Trace.WriteLine (X.Message, "[RepiPack]");
            }
            return dict;
        }
    }

    internal class Md5Comparer : IEqualityComparer<byte[]>
    {
        public bool Equals (byte[] left, byte[] right)
        {
            if (left == null || right == null)
                return left == right;
            if (left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; ++i)
            {
                if (left[i] != right[i])
                    return false;
            }
            return true;
        }

        public int GetHashCode (byte[] key)
        {
            if (null == key)
                throw new ArgumentNullException ("key");
            if (key.Length < 16)
                throw new ArgumentException ("Invalid key length.", "key");
            var hash = key.ToInt32 (0);
            hash ^= key.ToInt32 (4);
            hash ^= key.ToInt32 (8);
            hash ^= key.ToInt32 (12);
            return hash;
        }
    }
}
