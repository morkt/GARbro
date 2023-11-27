//! \file       ImageGIZ.cs
//! \date       2023 Oct 02
//! \brief      ADVIZ engine image format (PC-98).
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

using GameRes.Utility;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// [960830][Ange] Coin

namespace GameRes.Formats.Adviz
{
    internal class GizMetaData : ImageMetaData
    {
        public byte     RleCode;
        public byte     PlaneMap;
        public bool     HasPalette;
    }

    [Export(typeof(ImageFormat))]
    public class Giz3Format : ImageFormat
    {
        public override string         Tag => "GIZ";
        public override string Description => "ADVIZ engine image format";
        public override uint     Signature => 0x335A4947; // 'GIZ3'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            int xy = header.ToUInt16 (4);
            return new GizMetaData {
                Width  = (uint)header.ToUInt16 (6) << 3,
                Height = header.ToUInt16 (8),
                OffsetX = (xy % 0x50) << 3,
                OffsetY = xy / 0x50,
                HasPalette = header[0xC] != 0,
                PlaneMap = header[0xE],
                BPP = 4,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new Giz3Reader (file, (GizMetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Giz3Format.Write not implemented");
        }
    }

    internal class Giz3Reader
    {
        IBinaryStream   m_input;
        GizMetaData     m_info;
        BitmapPalette   m_palette;
        int             m_stride;
        int             m_output_stride;

        public Giz3Reader (IBinaryStream input, GizMetaData info)
        {
            m_input = input;
            m_info = info;
        }

        byte[] m_buffer;
        byte[] m_output;

        public ImageData Unpack ()
        {
            m_input.Position = 0x10;
            if (m_info.HasPalette)
                m_palette = ReadPalette();
            else
                m_palette = BitmapPalettes.Gray16; // palette is stored somewhere else
            long data_pos = m_input.Position;
            ReadHuffmanTree();
            data_pos += m_dataOffset;

            m_bitCount = 1;
            m_stride = m_info.iWidth >> 3;
            m_buffer = new byte[0x2000];
            m_output_stride = m_info.iWidth >> 1;
            m_output = new byte[m_output_stride * m_info.iHeight];
            m_input.Position = data_pos;
            UnpackBits();
            return ImageData.Create (m_info, PixelFormats.Indexed4, m_palette, m_output, m_output_stride);
        }

        int m_dataOffset;
        int m_gizColumn;
        int m_outputPos1;
        int m_outputPos2;

        void UnpackBits () // sub_15426
        {
            m_gizColumn = 0;
            m_outputPos1 = 0;
            m_outputPos2 = 0;
            int dst = 0;
            for (int x = 0; x < m_stride; ++x)
            {
                int src1 = m_outputPos1;
                int src2 = 0;
                for (int j = 0; j < 2; ++j)
                {
                    src2 = m_outputPos1;
                    int plane_mask = 1;
                    for (int i = 0; i < 4; ++i)
                    {
                        if ((m_info.PlaneMap & plane_mask) == 0)
                        {
                            UnpackPlane (m_outputPos1);
                        }
                        plane_mask <<= 1;
                        m_outputPos1 += 0x800;
                        m_outputPos2 += 0x800;
                    }
                    m_gizColumn = (m_gizColumn + 1) & 3;
                    m_outputPos1 = m_gizColumn << 9;
                    m_outputPos2 = 0;
                }
                CopyPlanes (src1, src2, dst);
                dst += 4;
            }
        }

        void CopyPlanes (int src1, int src2, int dst)
        {
            for (int y = 0; y < m_info.iHeight; ++y)
            {
                int b0 = m_buffer[src1+y       ] << 4 | m_buffer[src2+y       ];
                int b1 = m_buffer[src1+y+0x0800] << 4 | m_buffer[src2+y+0x0800];
                int b2 = m_buffer[src1+y+0x1000] << 4 | m_buffer[src2+y+0x1000];
                int b3 = m_buffer[src1+y+0x1800] << 4 | m_buffer[src2+y+0x1800];
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
                    m_output[dst+j/2] = px;
                }
                dst += m_output_stride;
            }
        }

        int m_root;
        ushort[] m_treeTable;

        void ReadHuffmanTree ()
        {
            m_dataOffset = m_input.ReadUInt16();
            m_treeTable = new ushort[(m_dataOffset-2) * 2 / 3 + 1];
            int di = 0;
            for (int si = 2; si + 2 < m_dataOffset; si += 3)
            {
                ushort bx = m_input.ReadUInt16();
                int ax = bx & 0xFFF;
                if ((ax & 0x800) == 0)
                    ax = (ax - 2) >> 1;
                m_treeTable[di++] = (ushort)ax;
                ax = m_input.ReadUInt8() << 4;
                ax |= bx >> 12;
                if ((ax & 0x800) == 0)
                    ax = (ax - 2) >> 1;
                m_treeTable[di++] = (ushort)ax;
            }
            m_root = di - 2;
        }

        byte ReadToken ()
        {
            int token = m_root;
            do
            {
                if (GetNextBit())
                    ++token;
                token = m_treeTable[token];
            }
            while ((token & 0x800) == 0);
            return (byte)token;
        }

        void UnpackPlane (int dst)
        {
            int y = 0;
            while (y < m_info.iHeight)
            {
                byte ctl = ReadToken();
                if (ctl < 0x10)
                {
                    m_buffer[dst++] = ctl;
                    ++y;
                }
                else
                {
                    int count = ReadToken() + 2;
                    ctl -= 0x10;
                    switch (ctl)
                    {
                    case 0:
                        for (int i = 0; i < count; ++i)
                            m_buffer[dst+i] = 0;
                        break;

                    case 1:
                        for (int i = 0; i < count; ++i)
                            m_buffer[dst+i] = 0xF;
                        break;

                    case 2:
                        Binary.CopyOverlapped (m_buffer, dst-1, dst, count);
                        break;

                    case 3:
                        Binary.CopyOverlapped (m_buffer, dst-2, dst, count);
                        break;

                    case 4:
                    case 5:
                    case 6:
                        {
                            int off = (ctl - 3) << 11;
                            Binary.CopyOverlapped (m_buffer, dst-off, dst, count);
                            break;
                        }
                    case 7:
                        {
                            int src = dst - m_outputPos1;
                            int ax = (m_gizColumn - 1) & 3;
                            src += (ax << 9) + m_outputPos2;
                            Binary.CopyOverlapped (m_buffer, src, dst, count);
                            break;
                        }
                    case 8:
                        {
                            int src = dst - m_outputPos1;
                            int ax = (m_gizColumn - 2) & 3;
                            src += (ax << 9) + m_outputPos2;
                            Binary.CopyOverlapped (m_buffer, src, dst, count);
                            break;
                        }
                    }
                    dst += count;
                    y += count;
                }
            }
        }

        BitmapPalette ReadPalette ()
        {
            const int count = 16;
            var colors = new Color[count];
            for (int i = 0; i < count; ++i)
            {
                byte b = m_input.ReadUInt8();
                byte r = m_input.ReadUInt8();
                byte g = m_input.ReadUInt8();
                colors[i] = Color.FromRgb ((byte)(r * 0x11), (byte)(g * 0x11), (byte)(b * 0x11));
            }
            return new BitmapPalette (colors);
        }

        int m_bitCount;
        int m_bits;

        bool GetNextBit ()
        {
            if (--m_bitCount == 0)
            {
                m_bits = m_input.ReadUInt16();
                m_bitCount = 16;
            }
            bool bit = (m_bits & 0x8000) != 0;
            m_bits <<= 1;
            return bit;
        }
    }
}
