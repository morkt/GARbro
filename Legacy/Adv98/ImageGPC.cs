//! \file       ImageGPC.cs
//! \date       2023 Sep 22
//! \brief      Adv98 engine image format (PC-98).
//
// Copyright (C) 2023 by morkt
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Adv98
{
    internal class GpcMetaData : ImageMetaData
    {
        public long PaletteOffset;
        public long DataOffset;
        public int  Interleaving;
    }

    [Export(typeof(ImageFormat))]
    public class GpcFormat : ImageFormat
    {
        public override string         Tag => "GPC/PC98";
        public override string Description => "Adv98 engine image format";
        public override uint     Signature => 0x38394350; // 'PC98'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            if (!header.AsciiEqual (4, ")GPCFILE   \0"))
                return null;
            uint info_pos = header.ToUInt32 (0x18);
            var info = new GpcMetaData
            {
                Interleaving = header.ToUInt16 (0x10),
                PaletteOffset = header.ToUInt32 (0x14),
                DataOffset = info_pos + 0x10,
                BPP = 4,
            };
            file.Position = info_pos;
            info.Width  = file.ReadUInt16();
            info.Height = file.ReadUInt16();
            file.Position = info_pos + 0xA;
            info.OffsetX = file.ReadInt16();
            info.OffsetY = file.ReadInt16();
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new GpcReader (file, (GpcMetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GpcFormat.Write not implemented");
        }
    }

    internal class GpcReader
    {
        IBinaryStream   m_input;
        GpcMetaData     m_info;
        int             m_stride;

        public BitmapPalette Palette { get; private set; }

        public GpcReader (IBinaryStream input, GpcMetaData info)
        {
            m_input = input;
            m_info = info;
        }

        public ImageData Unpack ()
        {
            m_input.Position = m_info.PaletteOffset;
            Palette = ReadPalette();
            int plane_stride = (m_info.iWidth + 7) >> 3;
            int row_size = plane_stride * 4 + 1;
            var data = new byte[row_size * m_info.iHeight];
            m_input.Position = m_info.DataOffset;
            UnpackData (data);
            RestoreData (data, row_size);
            m_stride = plane_stride * 4;
            var pixels = new byte[m_stride * m_info.iHeight];
            ConvertTo8bpp (data, pixels, plane_stride);
            return ImageData.Create (m_info, PixelFormats.Indexed4, Palette, pixels, m_stride);
        }

        void ConvertTo8bpp (byte[] input, byte[] output, int plane_stride)
        {
            int interleaving_step = m_stride * m_info.Interleaving;
            int src_row = 1;
            int dst_row = 0;
            int i = 0;
            for (int y = 0; y < m_info.iHeight; ++y)
            {
                if (dst_row >= output.Length)
                {
                    dst_row = m_stride * ++i;
                }
                int p0 = src_row;
                int p1 = p0 + plane_stride;
                int p2 = p1 + plane_stride;
                int p3 = p2 + plane_stride;
                src_row = p3 + plane_stride + 1;
                int dst = dst_row;
                for (int x = plane_stride; x > 0; --x)
                {
                    byte b0 = input[p0++];
                    byte b1 = input[p1++];
                    byte b2 = input[p2++];
                    byte b3 = input[p3++];
                    for (int j = 0; j < 8; j += 2)
                    {
                        byte px = (byte)((((b0 << j) & 0x80) >> 3)
                                       | (((b1 << j) & 0x80) >> 2)
                                       | (((b2 << j) & 0x80) >> 1)
                                       | (((b3 << j) & 0x80)     ));
                        px |= (byte)((((b0 << j) & 0x40) >> 6)
                                   | (((b1 << j) & 0x40) >> 5)
                                   | (((b2 << j) & 0x40) >> 4)
                                   | (((b3 << j) & 0x40) >> 3));
                        output[dst++] = px;
                    }
                }
                dst_row += interleaving_step;
            }
        }

        void UnpackData (byte[] output)
        {
            int dst = 0;
            int ctl = 0;
            int ctl_mask = 0;
            while (dst < output.Length)
            {
                if (0 == ctl_mask)
                {
                    ctl = m_input.ReadByte();
                    if (-1 == ctl)
                        break;
                    ctl_mask = 0x80;
                }
                if ((ctl & ctl_mask) != 0)
                {
                    int cmd = m_input.ReadByte();
                    for (int cmd_mask = 0x80; cmd_mask != 0; cmd_mask >>= 1)
                    {
                        if ((cmd & cmd_mask) != 0)
                            output[dst++] = m_input.ReadUInt8();
                        else
                            ++dst;
                    }
                }
                else
                {
                    dst += 8;
                }
                ctl_mask >>= 1;
            }
        }

        void RestoreData (byte[] data, int stride)
        {
            int src = 0;
            for (int y = 0; y < m_info.iHeight; ++y)
            {
                int interleave = data[src];
                if (interleave != 0)
                {
                    byte lastValue = 0;
                    for (int i = 0; i < interleave; ++i)
                    {
                        int pos = 1 + i;
                        while (pos < stride)
                        {
                            data[src + pos] ^= lastValue;
                            lastValue = data[src + pos];
                            pos += interleave;
                        }
                    }

                }
                if (y > 0)
                {
                    int prev = src - stride;
                    int length = (stride - 1) & -4;
                    for (int x = 1; x <= length; ++x)
                    {
                        data[src + x] ^= data[prev + x];

                    }
                }
                src += stride;
            }
        }

        BitmapPalette ReadPalette ()
        {
            int count = m_input.ReadUInt16();
            int elem_size = m_input.ReadUInt16();
            if (elem_size != 2)
                throw new InvalidFormatException (string.Format ("Invalid palette element size {0}", elem_size));
            var colors = new Color[count];
            for (int i = 0; i < count; ++i)
            {
                int v = m_input.ReadUInt16();
                int r = (v >> 4) & 0xF;
                int g = (v >> 8) & 0xF;
                int b = (v     ) & 0xF;
                colors[i] = Color.FromRgb ((byte)(r * 0x11), (byte)(g * 0x11), (byte)(b * 0x11));
            }
//            colors[0].A = 0; // force transparency
            return new BitmapPalette (colors);
        }
    }
}
