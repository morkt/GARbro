//! \file       ArcDAT.cs
//! \date       2026-09-19
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
using System.Windows.Media;

namespace GameRes.Formats.Peach
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/KARTE"; } }
        public override string Description { get { return "PEACH resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".dat"))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);

            uint offset = 0;
            uint i = 1;
            var dir = new List<Entry> ();
            do
            {
                uint ofs_encrypted = file.View.ReadUInt32 (offset);
                var entry = new PackedEntry {
                    Name = string.Format ("{0}#{1:D4}", base_name, i - 1),
                    Offset = ofs_encrypted + 0x24b7935b * i,
                    Size = file.View.ReadUInt32 (offset + 4) - ofs_encrypted + 0x24b7935b
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                uint unpacked_size = file.View.ReadUInt32 (entry.Offset);
                if ((unpacked_size & 0xffffff) == 0x1ff) // XXX this value is small and might cause wrong matching
                {
                    entry.UnpackedSize = file.View.ReadUInt32 (entry.Offset + 8);
                    entry.Type = "image";
                }
                else
                {
                    entry.UnpackedSize = unpacked_size;
                    entry.Type = "script";
                }
                dir.Add (entry);
                offset += 4;
                i++;
            }
            while (offset < dir[0].Offset - 4);

            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (pent.Type != "script")
                return base.OpenEntry (arc, entry); // leave it to OpenImage

            var input = arc.File.CreateStream (pent.Offset + 4, pent.Size - 4);
            var output = new byte[pent.UnpackedSize];

            LzUnpack (input, output, 0x37);
            return new BinMemoryStream (output);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.OpenBinaryEntry (entry);
            if (input.ReadByte() != 0xff || input.ReadByte() != 0x01 || input.ReadByte() != 0)
            {
                input.Position = 0;
                return base.OpenImage (arc, entry);
            }

            int type = input.ReadByte();
            uint width = input.ReadUInt16();
            uint height = input.ReadUInt16();
            uint unpacked_size = input.ReadUInt32();
            var buffer = new byte[unpacked_size];
            LzUnpack (input, buffer, 0);
            return new BitmapDecoder (new BinMemoryStream (buffer), type, width, height);
        }

        void LzUnpack (IBinaryStream input, byte[] output, byte delta)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                int ctl = input.ReadByte();
                for (int bit = 1; bit != 0x100 && dst < output.Length; bit <<= 1)
                {
                    if (0 != (ctl & bit))
                    {
                        output[dst++] = (byte)(input.ReadByte() - delta);
                    }
                    else
                    {
                        ushort v = input.ReadUInt16();
                        int offset = v >> 4;
                        for (int count = 3 + (v & 0xF); count != 0; --count)
                        {
                            int src = dst - offset;
                            if (src < 0)
                                output[dst++] = 0;
                            else
                                output[dst++] = output[src];
                        }
                    }
                }
            }
        }
    }

    internal class BitmapDecoder : BinaryImageDecoder
    {
        int m_type;

        public BitmapDecoder (IBinaryStream input, int type, uint width, uint height) : base (input)
        {
            m_type = type;
            Info = new ImageMetaData {
                Width  = width,
                Height = height,
                BPP    = 16
            };
        }

        protected override ImageData GetImageData ()
        {
            int strike = Info.iWidth * 2;
            var pixels = new byte[strike * Info.iHeight];

            if (m_type == 1)
            {
                m_input.Read (pixels, 0, pixels.Length);
            }
            else
            {
                int dst = 0;
                uint run_type = m_input.ReadUInt32();
                uint data_offset = m_input.ReadUInt32();
                var run_lengths = new int[(data_offset - 8) / 4];
                for (int i = 0; i < run_lengths.Length; i++)
                    run_lengths[i] = m_input.ReadInt32() * 2;
                foreach (int len in run_lengths)
                {
                    if (run_type != 0)
                    {
                        m_input.Read (pixels, dst, len);
                    }
                    run_type ^= 1;
                    dst += len;
                }
            }

            return ImageData.CreateFlipped (Info, PixelFormats.Bgr565, null, pixels, strike);
        }
    }
}
