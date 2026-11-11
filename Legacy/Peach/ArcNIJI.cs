//! \file       ArcNIJI.cs
//! \date       2026-09-06
//! \brief      PEACH resource archive.
//
// Copyright (C) 2026 by morkt
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

namespace GameRes.Formats.Peach
{
    [Export(typeof(ArchiveFormat))]
    public class NijiDatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/NIJI"; } }
        public override string Description { get { return "PEACH resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".dat"))
                return null;
            uint index_offset = 0;
            uint index_end = file.View.ReadUInt32 (0xC);
            var dir = new List<Entry> ();
            while (index_offset < index_end)
            {
                var name = file.View.ReadString (index_offset, 0xC);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset + 0xC);
                entry.Size = file.View.ReadUInt32 (index_offset + 0x10);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (name.HasExtension (".ESB"))
                    entry.Type = "image";
                else if (name.HasExtension (".EST"))
                    entry.Type = "script";
                dir.Add (entry);
                index_offset += 0x14;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            uint unpacked_size = input.ReadUInt32();
            uint t = input.ReadUInt32();
            uint bits_offset = (t & 0xFFFFFF) + 4;
            byte flags = (byte)(t >> 24);
            if (flags == 0xFF)
                return input;
            var bits = arc.File.View.ReadBytes (entry.Offset + bits_offset, entry.Size - bits_offset);
            var output = new byte[unpacked_size];
            LzUnpack (input, output, bits, flags);
            return new BinMemoryStream (output);
        }

        void LzUnpack (IBinaryStream input, byte[] output, byte[] bits, byte flags)
        {
            int dst = 0;
            int bitsrc = 0;
            byte shift = (byte)(flags & 0xF);
            uint ctl = 0xFFFF;
            uint mask = (1u << shift) - 1u;
            uint mask2 = ((flags & 0x80) == 0) ? 0xFFFFFFFF : mask;

            while (dst < output.Length)
            {
                if (ctl == 0xFFFF)
                    ctl = input.ReadUInt16() | 0xFFFF0000;
                if ((ctl & 1) == 0)
                {
                    ushort v = input.ReadUInt16();
                    int count = (int)(v & mask);
                    int offset = (int)(v >> shift);
                    if (shift == 0 && count == 0 || shift != 0 && offset == 0)
                        offset = input.ReadUInt16();
                    if (count == mask2)
                        count += bits[bitsrc++];
                    count += 3;
                    Binary.CopyOverlapped (output, dst - offset, dst, count);
                    dst += count;
                }
                else
                {
                    output[dst++] = bits[bitsrc++];
                }
                ctl >>= 1;
            }
        }
    }
}
