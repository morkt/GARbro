//! \file       ArcSGS.cs
//! \date       2018 Jul 19
//! \brief      Sogna resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

// [030320][Sogna] Viper-V6 R

namespace GameRes.Formats.Sogna
{
    [Export(typeof(ArchiveFormat))]
    public class SgsDatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/SGS"; } }
        public override string Description { get { return "Sogna resource archive"; } }
        public override uint     Signature { get { return 0x2E534753; } } // 'SGS.DAT 1.00'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "DAT 1.00"))
                return null;
            int count = file.View.ReadInt32 (12);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x10);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.IsPacked      = file.View.ReadByte (index_offset+0x13) != 0;
                entry.Size          = file.View.ReadUInt32 (index_offset+0x14);
                entry.UnpackedSize  = file.View.ReadUInt32 (index_offset+0x18);
                entry.Offset        = file.View.ReadUInt32 (index_offset+0x1C);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
            {
                var output = new byte[pent.UnpackedSize];
                LzUnpack (input, output);
                return new BinMemoryStream (output, entry.Name);
            }
        }

        void LzUnpack (IBinaryStream input, byte[] output)
        {
            int dst = 0;
            int bits = 0;
            byte mask = 0;
            while (dst < output.Length)
            {
                mask >>= 1;
                if (0 == mask)
                {
                    bits = input.ReadByte();
                    if (-1 == bits)
                        break;
                    mask = 0x80;
                }
                if ((mask & bits) != 0)
                {
                    int offset = input.ReadUInt16();
                    int count = (offset >> 12) + 1;
                    offset &= 0xFFF;
                    Binary.CopyOverlapped (output, dst-offset, dst, count);
                    dst += count;
                }
                else
                {
                    output[dst++] = input.ReadUInt8();
                }
            }
        }
    }
}
