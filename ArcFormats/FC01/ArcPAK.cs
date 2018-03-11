//! \file       ArcPAK.cs
//! \date       2018 Feb 27
//! \brief      AGSI resource archive.
//
// Copyright (C) 2018 by morkt
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
using System.Security.Cryptography;
using GameRes.Compression;
using GameRes.Cryptography;
using GameRes.Utility;

// Advanced Game Script Interpreter

namespace GameRes.Formats.FC01
{
    internal class AgsiEntry : PackedEntry
    {
        public int  Method;
        public bool IsEncrypted;
        public bool IsSpecial;
    }

    internal class AgsiArchive : ArcFile
    {
        public readonly byte[] Key;

        public AgsiArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/AGSI"; } }
        public override string Description { get { return "AGSI engine resource archive"; } }
        public override uint     Signature { get { return 0x24A02028; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static readonly Dictionary<string, byte[]> KnownKeys = new Dictionary<string,byte[]> {
            { "01.pak", new byte[] { 0x4A, 0xC9, 0x75, 0x62, 0x39, 0xC1, 0xBE, 0x54 } },
            { "02.pak", new byte[] { 0xC9, 0x46, 0x32, 0x69, 0x7B, 0xD2, 0x58, 0x54 } },
            { "03.pak", new byte[] { 0x33, 0x34, 0x37, 0x38, 0x73, 0x68, 0x61, 0x6F } }, // "3478shao"
            { "04.pak", new byte[] { 0x73, 0x69, 0x6F, 0x62, 0x6E, 0x72, 0x61, 0x68 } }, // "siobnrah"
            { "05.pak", new byte[] { 0xB5, 0x37, 0x70, 0x38, 0x3D, 0x62, 0x48, 0xD1 } },
            { "06.pak", new byte[] { 0x4F, 0x7D, 0x40, 0x24, 0x57, 0xCD, 0x68, 0x6E } },
        };

        public override ArcFile TryOpen (ArcView file)
        {
            var header = file.View.ReadBytes (0, 12);
            byte k1 = file.View.ReadByte (file.MaxOffset-9);
            byte k2 = file.View.ReadByte (file.MaxOffset-6);
            DecryptHeader (header, k1, k2);
            if (!header.AsciiEqual ("PACK"))
                return null;
            int count = header.ToInt32 (4);
            int entry_size = header.ToInt32 (8);
            if (!IsSaneCount (count) || entry_size <= 0x10)
                return null;
            int index_size = count * entry_size;
            var index = file.View.ReadBytes (12, (uint)index_size);
            Decrypt (index, 0, index_size, 7524u);
            long data_offset = 12 + index_size;
            int index_offset = 0;
            int name_length = entry_size - 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = Binary.GetCString (index, index_offset+0x10, name_length);
                var entry = FormatCatalog.Instance.Create<AgsiEntry> (name);
                entry.UnpackedSize = index.ToUInt32 (index_offset);
                entry.Size         = index.ToUInt32 (index_offset+4);
                entry.Method       = index.ToInt32  (index_offset+8);
                entry.Offset       = index.ToUInt32 (index_offset+0xC) + data_offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.IsPacked = entry.Method != 0;
                entry.IsEncrypted = entry.Method >= 3 && (entry.Method <= 5 || entry.Method == 7);
                entry.IsSpecial = name.Equals ("Copyright.Dat", StringComparison.OrdinalIgnoreCase);
                dir.Add (entry);
                index_offset += entry_size;
            }
            var arc_name = Path.GetFileName (file.Name).ToLowerInvariant();
            byte[] key;
            if (!KnownKeys.TryGetValue (arc_name, out key) || key == null)
                return new ArcFile (file, this, dir);
            return new AgsiArchive (file, this, dir, key);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var aent = entry as AgsiEntry;
            if (null == aent || 0 == aent.Method)
                return base.OpenEntry (arc, entry);
            var aarc = arc as AgsiArchive;
            Stream input;
            if (aent.IsEncrypted && aarc != null && aarc.Key != null)
            {
                uint enc_size = entry.Size;
                if (enc_size > 1024)
                {
                    enc_size = 1032;
                }
                using (var des = DES.Create())
                {
                    des.Key = aarc.Key;
                    des.Mode = CipherMode.ECB;
                    des.Padding = PaddingMode.Zeros;
                    using (var enc = arc.File.CreateStream (entry.Offset, enc_size))
                    using (var dec = new InputCryptoStream (enc, des.CreateDecryptor()))
                    {
                        var output = new byte[enc_size];
                        dec.Read (output, 0, output.Length);
                        int header_size;
                        if (!aent.IsSpecial)
                            header_size = output.ToInt32 (output.Length-4);
                        else
                            header_size = (int)aent.UnpackedSize;
                        if (!aent.IsSpecial && entry.Size > enc_size)
                        {
                            var header = new byte[header_size];
                            Buffer.BlockCopy (output, 0, header, 0, header_size);
                            input = arc.File.CreateStream (entry.Offset + enc_size, entry.Size - enc_size);
                            input = new PrefixStream (header, input);
                        }
                        else
                            input = new BinMemoryStream (output, 0, header_size, entry.Name);
                    }
                }
            }
            else
                input = arc.File.CreateStream (entry.Offset, entry.Size);
            switch (aent.Method)
            {
            case 6:
            case 7:
                input = new LzssStream (input);
                break;
            }
            return input;
        }

        void DecryptHeader (byte[] header, byte k1, byte k2)
        {
            int shift = k2 & 7;
            if (0 == shift)
                shift = 1;
            for (int i = 0; i < header.Length; ++i)
            {
                byte x = Binary.RotByteL (header[i], shift);
                header[i] = (byte)(x ^ k1++);
            }
        }

        void Decrypt (byte[] data, int pos, int length, uint seed)
        {
            var rnd = new MersenneTwister (seed);
            for (int i = 0; i < length; ++i)
            {
                uint key = rnd.Rand();
                int shift = (int)key & 7;
                if (0 == shift)
                    shift = 1;
                byte x = Binary.RotByteL (data[pos+i], shift);
                data[pos+i] = (byte)(key ^ x);
            }
        }
    }
}
