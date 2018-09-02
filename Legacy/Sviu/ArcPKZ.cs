//! \file       ArcPKZ.cs
//! \date       2018 Aug 26
//! \brief      SVIU System resource archive.
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
using System.Linq;
using GameRes.Utility;

// [030725][Hayashigumi] Fall in Love

namespace GameRes.Formats.Sviu
{
    [Serializable]
    public class PkzScheme : ResourceScheme
    {
        public Dictionary<string, byte[]> KnownSchemes;
    }

    internal class PkzArchive : ArcFile
    {
        public readonly byte[] Key;

        public PkzArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PkzOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PKZ"; } }
        public override string Description { get { return "SVIU System resource archive"; } }
        public override uint     Signature { get { return 0x305A4B50; } } // 'PKZ0'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        PkzScheme DefaultScheme = new PkzScheme { KnownSchemes = new Dictionary<string, byte[]>() };

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (PkzScheme)value; }
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            var key = QueryKey();
            if (null == key)
                return null;
            uint data_offset = (uint)count * 0x2Cu + 0x14u;
            var index = file.View.ReadBytes (8, data_offset - 8);
            DecryptData (index, key);
            var dir = new List<Entry> (count);
            int index_offset = 0xC;
            for (int i = 0; i < count; ++i)
            {
                var name = Binary.GetCString (index, index_offset, 0x20);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Size   = index.ToUInt32 (index_offset+0x20);
                entry.Offset = index.ToUInt32 (index_offset+0x24) + data_offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x2C;
            }
            return new PkzArchive (file, this, dir, key);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var parc = (PkzArchive)arc;
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            DecryptData (data, parc.Key);
            if (data.AsciiEqual (0, "SVS18"))
                data = UnpackScript (data);
            return new BinMemoryStream (data, entry.Name);
        }

        byte[] UnpackScript (byte[] data)
        {
            if (data.ToInt32 (0x10) == 0)
                return data;
            int unpacked_size = data.ToInt32 (0xC);
            int header_size = data.ToInt32 (0x14);
            int packed_size = data.ToInt32 (0x1C);
            var output = new byte[unpacked_size];
            Buffer.BlockCopy (data, 0, output, 0, header_size);
            LittleEndian.Pack (0, output, 0x10);
            using (var input = new BinMemoryStream (data, header_size, packed_size))
                LzUnpack (input, output, header_size);
            return output;
        }

        void LzUnpack (IBinaryStream input, byte[] output, int dst)
        {
            var frame = new byte[0x800];
            int frame_pos = 0x7E8;
            int ctl = 1;
            while (dst < output.Length)
            {
                if (1 == ctl)
                {
                    ctl = input.ReadByte();
                    if (-1 == ctl)
                        break;
                    ctl |= 0x100;
                }
                if (0 != (ctl & 1))
                {
                    byte b = input.ReadUInt8();
                    output[dst++] = b;
                    frame[frame_pos++ & 0x7FF] = b;
                }
                else
                {
                    byte lo = input.ReadUInt8();
                    byte hi = input.ReadUInt8();
                    int offset = lo | (hi & 0xE0) << 3;
                    int count = (hi & 0x1F) + 2;
                    for (int i = 0; i < count; ++i)
                    {
                        byte b = frame[(offset + i) & 0x7FF];
                        output[dst++] = b;
                        frame[frame_pos++ & 0x7FF] = b;
                    }
                }
                ctl >>= 1;
            }
        }

        public static byte[] CreateKey (string key)
        {
            var bkey = Encodings.cp932.GetBytes (key);
            for (int i = 0; i < bkey.Length; ++i)
            {
                bkey[i] += 6;
            }
            return bkey;
        }

        void DecryptData (byte[] data, byte[] key)
        {
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] ^= key[i % key.Length];
                data[i] += 0x80;
            }
        }

        byte[] QueryKey ()
        {
            if (DefaultScheme.KnownSchemes.Count == 0)
                return null;
            return DefaultScheme.KnownSchemes.Values.First();
        }
    }
}
