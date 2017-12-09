//! \file       ImageGRP.cs
//! \date       Fri May 20 20:30:01 2016
//! \brief      NeXAS GRP image format.
//
// Copyright (C) 2016 by morkt
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
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.NeXAS
{
    internal class GrpMetaData : ImageMetaData
    {
        public int  Version;
        public int  UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class GrpFormat : ImageFormat
    {
        public override string         Tag { get { return "GR3"; } }
        public override string Description { get { return "NeXAS engine image format"; } }
        public override uint     Signature { get { return 0x18335247; } }

        public GrpFormat ()
        {
            Signatures = new uint[] { 0x18335247, 0x08325247, 0x10325247, 0x18325247, 0 };
            Extensions = new string[] { "grp" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x11);
            int version = header[2] - '0';
            if (!header.AsciiEqual ("GR") || version < 1 || version > 3)
                return null;
            var info = new GrpMetaData {
                Width   = header.ToUInt32 (5),
                Height  = header.ToUInt32 (9),
                BPP     = header.ToUInt16 (3),
                Version = version
            };
            if (version > 1)
                info.UnpackedSize = header.ToInt32 (0xD);
            else
                info.UnpackedSize = (int)info.Width * (int)info.Height * info.BPP / 8
                                  + (info.BPP == 8 ? 0x300 : 0);
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var reader = new GrpReader (stream, (GrpMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, reader.Palette, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GrpFormat.Write not implemented");
        }
    }

    internal sealed class GrpReader
    {
        IBinaryStream   m_input;
        byte[]          m_output;
        GrpMetaData     m_info;
        readonly int    m_count_bits;
        readonly int    m_count_mask;

        public byte[]           Data { get { return m_output; } }
        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }

        public GrpReader (IBinaryStream input, GrpMetaData info)
        {
            m_input = input;
            m_output = new byte[info.UnpackedSize];
            m_info = info;
            switch (info.BPP)
            {
            case 8:  Format = PixelFormats.Indexed8; break;
            case 16: Format = PixelFormats.Bgr555; break;
            case 24: Format = PixelFormats.Bgr24; break;
            case 32: Format = PixelFormats.Bgr32; break;
            default: throw new NotSupportedException ("Not supported GRP image color depth");
            }
            m_count_bits = info.Version > 2 ? 3 : 5;
            m_count_mask = ~(-1 << m_count_bits);
        }

        public byte[] Unpack ()
        {
            if (m_info.Version < 2)
            {
                m_input.Position = 0xD;
                m_input.Read (m_output, 0, m_output.Length);
            }
            else
            {
                Decompress();
            }
            if (8 == m_info.BPP)
            {
                using (var input = new MemoryStream (m_output, m_output.Length-0x300, 0x300))
                    Palette = ImageFormat.ReadPalette (input, 0x100, PaletteFormat.Rgb);
            }
            return m_output;
        }

        void Decompress ()
        {
            m_input.Position = 0x11;
            int ctl_length = (m_input.ReadInt32() + 7) / 8;
            var ctl_bytes = m_input.ReadBytes (ctl_length);
            m_input.ReadInt32();
            using (var ctl_mem = new MemoryStream (ctl_bytes))
            using (var bits = new LsbBitStream (ctl_mem))
            {
                int dst = 0;
                while (dst < m_output.Length)
                {
                    int bit = bits.GetNextBit();
                    if (-1 == bit)
                        break;
                    if (0 == bit)
                    {
                        m_output[dst++] = m_input.ReadUInt8();
                    }
                    else
                    {
                        int offset = m_input.ReadUInt16();
                        int count = (offset & m_count_mask) + 1;
                        offset = (offset >> m_count_bits) + 1;
                        Binary.CopyOverlapped (m_output, dst - offset, dst, count);
                        dst += count;
                    }
                }
            }
        }
    }
}
