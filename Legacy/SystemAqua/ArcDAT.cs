//! \file       ArcDAT.cs
//! \date       2017 Dec 18
//! \brief      SystemAQUA engine resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.SystemAqua
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/CATF"; } }
        public override string Description { get { return "SystemAQUA engine resource archive"; } }
        public override uint     Signature { get { return 0x46544143; } } // 'CATF'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0x10);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (8);
            if (index_offset >= file.MaxOffset)
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new PackedEntry {
                    Name   = string.Format ("{0}#{1:D4}", base_name, i),
                    Size   = file.View.ReadUInt32 (index_offset),
                    Offset = file.View.ReadUInt32 (index_offset+4),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            DetectFileTypes (dir, file);
            return new ArcFile (file, this, dir);
        }

        void DetectFileTypes (List<Entry> dir, ArcView file)
        {
            var type = new byte[3];
            foreach (PackedEntry entry in dir)
            {
                uint signature = file.View.ReadUInt32 (entry.Offset);
                if (signature != 0x34655A4C) // 'LZe4'
                {
                    entry.ChangeType (AutoEntry.DetectFileType (signature));
                }
                else if (entry.Size > 0x40)
                {
                    entry.IsPacked = true;
                    entry.UnpackedSize = file.View.ReadUInt32 (entry.Offset+8);
                    file.View.Read (entry.Offset+0xC, type, 0, 3);
                    if (type.AsciiEqual ("000"))
                    {
                        entry.ChangeType (AudioFormat.Wav);
                    }
                    else
                    {
                        DecryptType (type);
                        if (type.AsciiEqual ("BMP"))
                            entry.ChangeType (ImageFormat.Bmp);
                        else if (type.AsciiEqual ("WAV"))
                            entry.ChangeType (AudioFormat.Wav);
                        else if (type.AsciiEqual ("MID"))
                            entry.Name = Path.ChangeExtension (entry.Name, "mid");
                    }
                }
            }
        }

        void DecryptType (byte[] type)
        {
            type[0] = Binary.RotByteL ((byte)~type[0], 4);
            type[1] = Binary.RotByteL ((byte)~type[1], 4);
            type[2] = Binary.RotByteL ((byte)~type[2], 4);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            var type = arc.File.View.ReadBytes (entry.Offset+0xC, 3);
            DecryptType (type);
            using (var input = arc.File.CreateStream (entry.Offset+0x40, entry.Size-0x40))
            {
                var data = new byte[pent.UnpackedSize];
                int output_pos = 0;
                if (type.AsciiEqual ("BMP"))
                    output_pos = PrepareBmpHeader (input, data);
                LzUnpack (input, data, output_pos);
                return new BinMemoryStream (data, entry.Name);
            }
        }

        void LzUnpack (Stream input, byte[] output, int dst)
        {
            var frame = new byte[0x4000];
            int frame_pos = 1;
            using (var bits = new MsbBitStream (input, true))
            {
                while (dst < output.Length)
                {
                    int ctl = bits.GetNextBit();
                    if (-1 == ctl)
                        break;
                    if (ctl != 0)
                    {
                        int v = bits.GetBits (8);
                        output[dst++] = frame[frame_pos++ & 0x3FFF] = (byte)v;
                    }
                    else
                    {
                        int offset = bits.GetBits (14);
                        int count = bits.GetBits (4) + 3;
                        while (count --> 0)
                        {
                            byte v = frame[offset++ & 0x3FFF];
                            output[dst++] = frame[frame_pos++ & 0x3FFF] = v;
                        }
                    }
                }
            }
        }

        int PrepareBmpHeader (IBinaryStream input, byte[] output)
        {
            input.ReadInt32();
            uint h1 = ~input.ReadUInt32();
            uint h2 = ~input.ReadUInt32();
            uint h3 = ~input.ReadUInt32();
            uint width = Binary.BigEndian ((ushort)h1);
            uint height = Binary.BigEndian ((ushort)(h1 >> 16));
            output[0] = (byte)'B';
            output[1] = (byte)'M';
            LittleEndian.Pack (output.Length, output, 2);
            output[10] = 54; // offset to the body
            output[14] = 40; // header size
            LittleEndian.Pack (width,  output, 18);
            LittleEndian.Pack (height, output, 22);
            output[26] = 1; // planes
            output[28] = Binary.RotByteL ((byte)h2, 4); // bpp
            LittleEndian.Pack (Binary.RotL (h3, 16), output, 34);
            LittleEndian.Pack (0xB12, output, 38);
            LittleEndian.Pack (0xB12, output, 42);
            LittleEndian.Pack ((uint)Binary.RotByteL ((byte)(h2 >> 16), 4), output, 46);
            LittleEndian.Pack ((uint)Binary.RotByteL ((byte)(h2 >> 24), 4), output, 50);
            return 54;
        }
    }
}
