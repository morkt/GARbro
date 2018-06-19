//! \file       ArcARC.cs
//! \date       2018 May 05
//! \brief      GSS engine resource archive.
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

namespace GameRes.Formats.Gss
{
    [Export(typeof(ArchiveFormat))]
    public class LsdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/LSD"; } }
        public override string Description { get { return "GSS engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".arc"))
                return null;
            var bin_name = Path.ChangeExtension (file.Name, "BIN");
            if (!VFS.FileExists (bin_name))
                return null;
            using (var bin = VFS.OpenView (bin_name))
            {
                if (!bin.View.AsciiEqual (0, "LSDARC V.100"))
                    return null;
                int count = bin.View.ReadInt32 (0xC);
                if (!IsSaneCount (count))
                    return null;
                using (var index = bin.CreateStream())
                {
                    index.Position = 0x10;
                    var dir = new List<Entry> (count);
                    for (int i = 0; i < count; ++i)
                    {
                        var entry = new PackedEntry();
                        entry.IsPacked     = index.ReadInt32() != 0;
                        entry.Offset       = index.ReadUInt32();
                        entry.UnpackedSize = index.ReadUInt32();
                        entry.Size         = index.ReadUInt32();
                        entry.Name         = index.ReadCString();
                        if (!entry.CheckPlacement (file.MaxOffset))
                            return null;
                        dir.Add (entry);
                    }
                    return new ArcFile (file, this, dir);
                }
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked || !arc.File.View.AsciiEqual (entry.Offset, "LSD\x1A"))
                return base.OpenEntry (arc, entry);
            byte enc_method = arc.File.View.ReadByte (entry.Offset+4);
            byte pack_method = arc.File.View.ReadByte (entry.Offset+5);
            uint unpacked_size = arc.File.View.ReadUInt32 (entry.Offset+6);
            using (var input = arc.File.CreateStream (entry.Offset+12, entry.Size-12))
            {
                var data = new byte[unpacked_size];
                switch ((char)pack_method)
                {
                case 'D':   UnpackD (input, data); break;
                case 'R':   UnpackR (input, data); break;
                case 'H':   UnpackH (input, data); break;
                case 'W':   UnpackW (input, data); break;
                default:    input.Read (data, 0, data.Length); break;
                }
                switch ((char)enc_method)
                {
                case 'B':
                case 'W':
                case 'S':
                    break;
                }
                return new BinMemoryStream (data, entry.Name);
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            throw new NotImplementedException();
        }

        void UnpackD (IBinaryStream input, byte[] output)
        {
            throw new NotImplementedException();
        }

        void UnpackR (IBinaryStream input, byte[] output)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                int ctl = input.ReadByte();
                if (-1 == ctl)
                    break;
                int count;
                if ((ctl & 0xC0) == 0xC0)
                {
                    count = ctl & 0xF;
                    ctl &= 0xF0;
                }
                else
                {
                    count = ctl & 0x3F;
                    ctl &= 0xC0;
                }
                switch (ctl)
                {
                case 0xF0: return;
                case 0x40:
                    input.Read (output, dst, count);
                    dst += count;
                    break;

                case 0xD0:
                    count = count << 8 | input.ReadUInt8();
                    input.Read (output, dst, count);
                    dst += count;
                    break;

                case 0x80:
                    {
                        byte v = input.ReadUInt8();
                        while (count --> 0)
                            output[dst++] = v;
                        break;
                    }

                case 0xE0:
                    {
                        count = count << 8 | input.ReadUInt8();
                        byte v = input.ReadUInt8();
                        while (count --> 0)
                            output[dst++] = v;
                        break;
                    }

                case 0x00:
                    dst += count;
                    break;

                case 0xC0:
                    count = count << 8 | input.ReadUInt8();
                    dst += count;
                    break;
                }
            }
        }

        void UnpackH (IBinaryStream input, byte[] output)
        {
            throw new NotImplementedException();
        }

        void UnpackW (IBinaryStream input, byte[] output)
        {
            throw new NotImplementedException();
            /*
            int header_length = input.ReadUInt8();
            int shift = input.ReadUInt8();
            input.Read (output, 0, header_length);
            int dst = header_length & ~1;
            int v7 = 0;
            while (dst < output.Length)
            {
                int bit = v7 & 0x1F;
                int v9 = (v7 >> 3) & 0x1FFFFFFC;
                int v10;
                if (bit < 0x1C)
                {
                    v10 = input.ReadInt32(); //MemInt32(&src[v9]);
                }
                else
                {
                    v10 = input.ReadInt32(); //MemInt32(&src[v9 + 1]);
                    bit -= 8;
                }
                v7 += 5;
                int v13 = (v10 >> bit) & 0x1F;
                bit = v7 & 0x1F;
                int v14 = (v7 >> 3) & 0x1FFFFFFC;
                if (bit < 8)
                {
                    v10 = MemInt32(&src[v14]);
                }
                else if (bit < 0x10)
                {
                    v10 = MemInt32(&src[v14 + 1]);
                    bit -= 8;
                }
                else if (bit < 0x18)
                {
                    v10 = MemInt32(&src[v14 + 2]);
                    bit -= 16;
                }
                else
                {
                    v10 = MemInt32(&src[v14 + 3]);
                    bit -= 24;
                }
                v17 = v13 & 0xF;
                int sample = dword_455580[v17] + (((v10 >> bit) & (dword_4554E8[v17] >> shift)) << shift);
                if ((v13 & 0x10) != 0)
                    sample = -sample;
                LittleEndian.Pack ((short)sample, output, dst);
                dst += 2;
                v20 = dword_455540[v17];
                if (v20 > shift)
                {
                    v7 += v20 - shift;
                }
            }
            */
        }

        static readonly int[] dword_455540 = {
            0x0, 0x0, 0x0, 0x0, 0x3, 0x4, 0x5, 0x6,
            0x7, 0x8, 0x9, 0xA, 0xB, 0xC, 0xD, 0xE
        };
        static readonly int[] dword_455580 = {
            0x00, 0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80,
            0x0100, 0x0200, 0x0400, 0x0800, 0x1000, 0x2000, 0x4000
        };
        static readonly int[] dword_4554E8 = {
            0x00, 0x00, 0x00, 0x00, 0x07, 0x0F, 0x1F, 0x3F,
            0x7F, 0xFF, 0x01FF, 0x03FF, 0x07FF, 0x0FFF, 0x1FFF, 0x3FFF
        };
    }
}
