//! \file       ArcPP.cs
//! \date       2022 Jun 01
//! \brief      Illusion resource archive.
//
// Copyright (C) 2022 by morkt
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

using GameRes.Utility;
using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.Illusion
{
    [Serializable]
    public class PpEncryptionScheme
    {
        public byte     Method;
        public uint[]   Key;
    }

    [Serializable]
    public class PpScheme : ResourceScheme
    {
        public IDictionary<string, PpEncryptionScheme>     KnownKeys;
    }

    public class PpArchive : ArcFile
    {
        public readonly byte Method;
        public readonly PpEncryptionScheme Scheme;

        public PpArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte encMethod, PpEncryptionScheme scheme)
            : base (arc, impl, dir)
        {
            Method = encMethod;
            Scheme = scheme;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PpOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PP/ILLUSION"; } }
        public override string Description { get { return "Illusion resource archive"; } }
        public override uint     Signature { get { return 0x5650505B; } } // '[PPVER]'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static readonly byte[][] DefaultIndexKey = new byte[][] {
            new byte[] { 0xFA, 0x49, 0x7B, 0x1C, 0xF9, 0x4D, 0x83, 0x0A },
		    new byte[] { 0x3A, 0xE3, 0x87, 0xC2, 0xBD, 0x1E, 0xA6, 0xFE }
        };

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "[PPVER]\0"))
                return null;

            var buffer = file.View.ReadBytes (8, 9);

            DecryptIndex (buffer, 0, 4);
            int version = buffer.ToInt32 (0);

            DecryptIndex (buffer, 4, 1);
            byte encryption_method = buffer[4];
            if (encryption_method > 4)
                return null;

            DecryptIndex (buffer, 5, 4);
            int count = buffer.ToInt32 (5);
            if (!IsSaneCount (count))
                return null;

            if (version < 0x6C)
                return null;
            var dir = new List<Entry> (count);
            uint index_offset = 0x11;
            uint index_size = (uint)(count * 0x120);
            var index = file.View.ReadBytes (index_offset, index_size);
            index_offset += index_size;
            DecryptIndex (index, 0, index.Length);
            int pos = 0;
            for (int i = 0; i < count; ++i)
            {
                var name = Binary.GetCString (index, pos, 0x104);
                pos += 0x104;
                var entry = Create<Entry> (name);
                entry.Size   = index.ToUInt32 (pos);
                entry.Offset = index.ToUInt32 (pos+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                pos += 0x1C;
            }
            var scheme = QueryEncryptionScheme (file);
            if (null == scheme)
                return null;
            return new PpArchive (file, this, dir, encryption_method, scheme);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var parc = arc as PpArchive;
            if (null == parc || 2 == parc.Method)
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            if (1 == parc.Method)
                DecryptData1 (data, parc.Scheme);
            else if (3 == parc.Method)
                DecryptData3 (data, parc.Scheme);
            else if (4 == parc.Method)
                data = UnpackData (data);
            return new BinMemoryStream (data, entry.Name);
        }

        byte[] UnpackData (byte[] input)
        {
            return input; // decompression not implemented
        }

        void DecryptData1 (byte[] data, PpEncryptionScheme scheme)
        {
            if (scheme.Method > 2 || scheme.Key.Length == 0)
                return;
            if (0 == scheme.Method)
            {
                for (int i = 0; i < data.Length; ++i)
                {
                    data[i] ^= (byte)scheme.Key[i % scheme.Key.Length];
                }
                return;
            }
            unsafe
            {
                fixed (byte* data8 = data)
                {
                    if (1 == scheme.Method)
                    {
                        ushort* data16 = (ushort*)data8;
                        int size = data.Length / 2;
                        for (int i = 0; i < size; ++i)
                        {
                            data16[i] ^= (ushort)scheme.Key[i % scheme.Key.Length];
                        }
                    }
                    else if (2 == scheme.Method)
                    {
                        uint* data32 = (uint*)data8;
                        int size = data.Length / 4;
                        for (int i = 0; i < size; ++i)
                        {
                            data32[i] ^= scheme.Key[i % scheme.Key.Length];
                        }
                    }
                }
            }
        }

        void DecryptData3 (byte[] data, PpEncryptionScheme scheme)
        {
            if (scheme.Key.Length < 8)
                return;
            var key0 = new ushort[] { (ushort)scheme.Key[0], (ushort)scheme.Key[1], (ushort)scheme.Key[2], (ushort)scheme.Key[3] };
            var key1 = new ushort[] { (ushort)scheme.Key[4], (ushort)scheme.Key[5], (ushort)scheme.Key[6], (ushort)scheme.Key[7] };
            unsafe
            {
                fixed (byte* data8 = data)
                {
                    ushort* data16 = (ushort*)data8;
                    int size = data.Length / 2;
                    for (int i = 0; i < size; ++i)
                    {
                        int k = i & 3;
                        key0[k] += key1[k];
                        data16[i] ^= key0[k];
                    }
                }
            }
        }

        internal void DecryptIndex (byte[] data, int pos, int length)
        {
            var key0 = DefaultIndexKey[0].Clone() as byte[];
            var key1 = DefaultIndexKey[1];
            for (int i = 0; i < length; ++i)
            {
                int k = i & 7;
                key0[k] += key1[k];
                data[pos+i] ^= key0[k];
            }
        }

        PpScheme DefaultScheme = new PpScheme
        {
            KnownKeys = new Dictionary<string, PpEncryptionScheme>()
        };

        internal IDictionary<string, PpEncryptionScheme> KnownKeys { get { return DefaultScheme.KnownKeys; } }

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (PpScheme)value; }
        }

        PpEncryptionScheme QueryEncryptionScheme (ArcView file)
        {
            var title = FormatCatalog.Instance.LookupGame (file.Name);
            if (string.IsNullOrEmpty (title))
                title = FormatCatalog.Instance.LookupGame (file.Name, @"..\*.exe");
            if (string.IsNullOrEmpty (title))
                return null;
            PpEncryptionScheme key;
            if (!KnownKeys.TryGetValue (title, out key))
                return null;
            return key;
        }
    }
}
