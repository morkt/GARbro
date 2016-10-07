//! \file       ImageGBC.cs
//! \date       Mon Oct 03 04:16:11 2016
//! \brief      Primel the Adventure System resource archive.
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
using GameRes.Utility;

namespace GameRes.Formats.Primel
{
    internal class GbcMetaData : ImageMetaData
    {
        public int  Flags;
    }

    [Export(typeof(ImageFormat))]
    public class GbcFormat : ImageFormat
    {
        public override string         Tag { get { return "GBC"; } }
        public override string Description { get { return "Primel Adventure System image format"; } }
        public override uint     Signature { get { return 0x46434247; } } // 'GBCF'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x14];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            return new GbcMetaData
            {
                Width   = LittleEndian.ToUInt32 (header, 8),
                Height  = LittleEndian.ToUInt32 (header, 0xC),
                BPP     = LittleEndian.ToUInt16 (header, 0x10),
                Flags   = LittleEndian.ToUInt16 (header, 0x12),
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            using (var reader = new GbcReader (stream, (GbcMetaData)info))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, null, reader.Pixels);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GbcFormat.Write not implemented");
        }
    }

    internal sealed class GbcReader : IDisposable
    {
        MsbBitStream    m_input;
        byte[]          m_output;
        GbcMetaData     m_info;
        int             m_stride;

        public byte[]      Pixels { get { return m_output; } }
        public PixelFormat Format { get; private set; }

        public GbcReader (Stream input, GbcMetaData info)
        {
            if (32 == info.BPP)
                Format = PixelFormats.Bgra32;
            else if (24 == info.BPP)
                Format = PixelFormats.Bgr24;
            else if (8 == info.BPP)
                Format = PixelFormats.Gray8;
            else
                throw new InvalidFormatException();
            m_input = new MsbBitStream (input, true);
            m_info = info;
            m_stride = (int)m_info.Width * m_info.BPP / 8;
            m_output = new byte[m_stride * (int)m_info.Height];
        }

        public void Unpack ()
        {
            m_input.Input.Position = 0x30;
            int pixel_size = m_info.BPP / 8;
            int blocks_w = (int)(m_info.Width + 7) / 8;
            int blocks_h = (int)(m_info.Height + 7) / 8;
            short[,] block = new short[pixel_size, 64];

            for (int by = 0; by < blocks_h; ++by)
            {
                int dst_line = by * 8 * m_stride;
                for (int bx = 0; bx < blocks_w; ++bx)
                {
                    int dst_block = dst_line + bx * 8 * pixel_size;

                    short last = 0;
                    for (int i = 0; i < 64; i++)
                    {
                        last += (short)GetInt();
                        block[0, ZigzagOrder[i]] = last;
                    }
                    for (int j = 1; j < pixel_size; ++j)
                    {
                        for (int i = 0; i < 64; ++i)
                            block[j, ZigzagOrder[i]] = (short)GetInt();
                        RestoreBlock (block, j);
                    }

                    for (int y = 0; y < 8; ++y)
                    {
                        if (by + 1 == blocks_h && (by * 8 + y) >= m_info.Height)
                            break;

                        int src = y * 8;
                        int dst = dst_block + y * m_stride;
                        for (int x = 0; x < 8; ++x)
                        {
                            if (bx + 1 == blocks_w && (bx * 8 + x) >= m_info.Width)
                                break;

                            int p = dst + (x + 1) * pixel_size - 1;
                            for (int j = 0; j < pixel_size; ++j)
                            {
                                m_output[p--] = (byte)(block[j, src] - 128);
                            }
                            ++src;
                        }
                    }
                }
            }
        }

        void RestoreBlock (short[,] block, int n)
        {
            int row = 8;
            for (int col = 1; col < 8; ++col)
            {
                block[n, col] += block[n, col - 1];
                block[n, row] += block[n, row - 8];
                row += 8;
            }
            row = 8;
            for (int y = 1; y < 8; ++y)
            {
                for (int x = 1; x < 8; ++x)
                {
                    block[n, row + x] += block[n, row + x - 9];
                }
                row += 8;
            }
        }

        int GetInt ()
        {
            int count = m_input.GetBits (4);
            switch (count)
            {
            case 0: return 0;
            case 1: return 1;

            case 2:
            case 3:
            case 4:
            case 5:
            case 6:
            case 7:
                return m_input.GetBits (count - 1) + (1 << (count - 1));

            case 8: return -1;
            case 9: return -2;

            default:
                return m_input.GetBits (count - 9) - (2 << (count - 9));

            case -1: throw new EndOfStreamException();
            }
        }

        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
        }

        readonly static byte[] ZigzagOrder = {
            0x00, 0x01, 0x08, 0x10, 0x09, 0x02, 0x03, 0x0A,
            0x11, 0x18, 0x20, 0x19, 0x12, 0x0B, 0x04, 0x05,
            0x0C, 0x13, 0x1A, 0x21, 0x28, 0x30, 0x29, 0x22,
            0x1B, 0x14, 0x0D, 0x06, 0x07, 0x0E, 0x15, 0x1C,
            0x23, 0x2A, 0x31, 0x38, 0x39, 0x32, 0x2B, 0x24,
            0x1D, 0x16, 0x0F, 0x17, 0x1E, 0x25, 0x2C, 0x33,
            0x3A, 0x3B, 0x34, 0x2D, 0x26, 0x1F, 0x27, 0x2E,
            0x35, 0x3C, 0x3D, 0x36, 0x2F, 0x37, 0x3E, 0x3F,
        };
    }
}
