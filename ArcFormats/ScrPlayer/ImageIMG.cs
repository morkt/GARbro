//! \file       ImageIMG.cs
//! \date       2023 Aug 30
//! \brief      ScrPlayer image format.
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
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [990709][Love Gun] Koi no Sweet Tart wa Ikaga

namespace GameRes.Formats.ScrPlayer
{
    [Export(typeof(ImageFormat))]
    public class ImgFormat : ImageFormat
    {
        public override string         Tag { get => "IMG"; }
        public override string Description { get => "ScrPlayer image format"; }
        public override uint     Signature { get => 0x20474D49; } // 'IMG '

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            int bpp = header.ToUInt16 (0x10);
            if (bpp != 24 && bpp != 32)
                return null;
            return new ImageMetaData {
                Width  = header.ToUInt16 (0xC),
                Height = header.ToUInt16 (0xE),
                BPP = bpp,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var reader = new ImgReader (file, info))
                return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ImgFormat.Write not implemented");
        }
    }

    internal sealed class ImgReader : IDisposable
    {
        ImgBitStream    m_input;
        ImageMetaData   m_info;
        int             m_stride;
        byte[]          m_output;

        public PixelFormat Format { get; private set; }

        public ImgReader (IBinaryStream input, ImageMetaData info)
        {
            m_input = new ImgBitStream (input.AsStream, true);
            m_info = info;
            m_stride = m_info.iWidth * 4;
            m_output = new byte[m_stride * (m_info.iHeight + 1)];
            Format = m_info.BPP == 32 ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
        }

        public ImageData Unpack ()
        {
            m_input.Input.Position = 0x18;
            if (32 == m_info.BPP)
                Unpack32bpp();
            else
                Unpack24bpp();
            return ImageData.Create (m_info, Format, null, m_output);
        }

        byte[] m_row_buffer = new byte[0x2580];
        int[]  m_rows_ptr = new int[3] { 0, 0xC80, 0xC80 * 2 };

        void Unpack24bpp ()
        {
            var offset_table = Img2Reader.OffsetTable.Clone() as int[];
            int dst = 0;
            for (int y = 0; y < m_info.iHeight; ++y)
            {
                int row_pos = m_rows_ptr[2];
                m_rows_ptr[2] = m_rows_ptr[1];
                m_rows_ptr[1] = m_rows_ptr[0];
                m_rows_ptr[0] = row_pos;
                for (int x = 0; x < m_info.iWidth; )
                {
                    int ctl = m_input.GetBits (ControlTable1, 13);
                    if (0xEF == ctl)
                        ctl += m_input.GetBits (ControlTable2, 11);

                    int pos = m_input.GetBits (PosTable24, 5) * 2;
                    int x_offset = offset_table[pos];
                    int y_offset = offset_table[pos + 1];
                    if (pos > 0)
                    {
                        offset_table[pos]   = offset_table[pos-2];
                        offset_table[pos+1] = offset_table[pos-1];
                        offset_table[pos-2] = x_offset;
                        offset_table[pos-1] = y_offset;
                    }
                    int src = m_rows_ptr[y_offset] + (x + x_offset) * 4;
                    if (ctl >= 0xD8)
                    {
                        int count = ctl - 0xD6;
                        Binary.CopyOverlapped (m_row_buffer, src, row_pos, count * 4);
                        row_pos += count * 4;
                        x += count;
                    }
                    else
                    {
                        switch (ColorCode[ctl])
                        {
                        case 0:
                            m_row_buffer[row_pos + 2] = (byte)(m_row_buffer[src + 2] - MapRed[ctl]);
                            m_row_buffer[row_pos + 1] = (byte)(m_row_buffer[src + 1] - MapGreen[ctl]);
                            m_row_buffer[row_pos    ] = (byte)(m_row_buffer[src    ] - MapBlue[ctl]);
                            break;
                        case 1:
                            m_row_buffer[row_pos + 2] = (byte)(m_row_buffer[src + 2] - GetDelta());
                            m_row_buffer[row_pos + 1] = (byte)(m_row_buffer[src + 1] - MapGreen[ctl]);
                            m_row_buffer[row_pos    ] = (byte)(m_row_buffer[src    ] - MapBlue[ctl]);
                            break;
                        case 2:
                            m_row_buffer[row_pos + 2] = (byte)(m_row_buffer[src + 2] - MapRed[ctl]);
                            m_row_buffer[row_pos + 1] = (byte)(m_row_buffer[src + 1] - GetDelta());
                            m_row_buffer[row_pos    ] = (byte)(m_row_buffer[src    ] - MapBlue[ctl]);
                            break;
                        case 3:
                            m_row_buffer[row_pos + 2] = (byte)(m_row_buffer[src + 2] - GetDelta());
                            m_row_buffer[row_pos + 1] = (byte)(m_row_buffer[src + 1] - GetDelta());
                            m_row_buffer[row_pos    ] = (byte)(m_row_buffer[src    ] - MapBlue[ctl]);
                            break;
                        case 4:
                            m_row_buffer[row_pos + 2] = (byte)(m_row_buffer[src + 2] - MapRed[ctl]);
                            m_row_buffer[row_pos + 1] = (byte)(m_row_buffer[src + 1] - MapGreen[ctl]);
                            m_row_buffer[row_pos    ] = (byte)(m_row_buffer[src    ] - GetDelta());
                            break;
                        case 5:
                            m_row_buffer[row_pos + 2] = (byte)(m_row_buffer[src + 2] - GetDelta());
                            m_row_buffer[row_pos + 1] = (byte)(m_row_buffer[src + 1] - MapGreen[ctl]);
                            m_row_buffer[row_pos    ] = (byte)(m_row_buffer[src    ] - GetDelta());
                            break;
                        case 6:
                            m_row_buffer[row_pos + 2] = (byte)(m_row_buffer[src + 2] - MapRed[ctl]);
                            m_row_buffer[row_pos + 1] = (byte)(m_row_buffer[src + 1] - GetDelta());
                            m_row_buffer[row_pos    ] = (byte)(m_row_buffer[src    ] - GetDelta());
                            break;
                        case 7:
                            m_row_buffer[row_pos + 2] = (byte)(m_row_buffer[src + 2] - GetDelta());
                            m_row_buffer[row_pos + 1] = (byte)(m_row_buffer[src + 1] - GetDelta());
                            m_row_buffer[row_pos    ] = (byte)(m_row_buffer[src    ] - GetDelta());
                            break;
                        }
                        row_pos += 4;
                        ++x;
                    }
                }
                Buffer.BlockCopy (m_row_buffer, m_rows_ptr[0], m_output, dst, m_stride);
                dst += m_stride;
            }
        }

        void Unpack32bpp ()
        {
            for (int i = 3; i < m_row_buffer.Length; i += 4)
                m_row_buffer[i] = 0xFF;
            var offset_table = Img2Reader.OffsetTable.Clone() as int[];
            int dst = 0;
            for (int y = 0; y < m_info.iHeight; ++y)
            {
                int row_pos = m_rows_ptr[2];
                m_rows_ptr[2] = m_rows_ptr[1];
                m_rows_ptr[1] = m_rows_ptr[0];
                m_rows_ptr[0] = row_pos;
                for (int x = 0; x < m_info.iWidth; )
                {
                    int ctl = m_input.GetBits (ControlTable1, 13);
                    if (0xEF == ctl)
                        ctl += m_input.GetBits (ControlTable2, 11);

                    int t = m_input.GetBits (ControlTable32, 13) * 2;
                    int pos = PosTable32[t] * 2;
                    int alpha = PosTable32[t+1];
                    int x_offset = offset_table[pos];
                    int y_offset = offset_table[pos + 1];
                    if (pos > 0)
                    {
                        offset_table[pos]   = offset_table[pos-2];
                        offset_table[pos+1] = offset_table[pos-1];
                        offset_table[pos-2] = x_offset;
                        offset_table[pos-1] = y_offset;
                    }
                    int src = m_rows_ptr[y_offset] + (x + x_offset) * 4;
                    if (ctl >= 0xD8)
                    {
                        int count = ctl - 0xD6;
                        Binary.CopyOverlapped (m_row_buffer, src, row_pos, count * 4);
                        row_pos += count * 4;
                        x += count;
                    }
                    else
                    {
                        switch (ColorCode[ctl])
                        {
                        case 0:
                            m_row_buffer[row_pos + 2] = (byte)(m_row_buffer[src + 2] - MapRed[ctl]);
                            m_row_buffer[row_pos + 1] = (byte)(m_row_buffer[src + 1] - MapGreen[ctl]);
                            m_row_buffer[row_pos    ] = (byte)(m_row_buffer[src    ] - MapBlue[ctl]);
                            break;
                        case 1:
                            m_row_buffer[row_pos + 2] = (byte)(m_row_buffer[src + 2] - GetDelta());
                            m_row_buffer[row_pos + 1] = (byte)(m_row_buffer[src + 1] - MapGreen[ctl]);
                            m_row_buffer[row_pos    ] = (byte)(m_row_buffer[src    ] - MapBlue[ctl]);
                            break;
                        case 2:
                            m_row_buffer[row_pos + 2] = (byte)(m_row_buffer[src + 2] - MapRed[ctl]);
                            m_row_buffer[row_pos + 1] = (byte)(m_row_buffer[src + 1] - GetDelta());
                            m_row_buffer[row_pos    ] = (byte)(m_row_buffer[src    ] - MapBlue[ctl]);
                            break;
                        case 3:
                            m_row_buffer[row_pos + 2] = (byte)(m_row_buffer[src + 2] - GetDelta());
                            m_row_buffer[row_pos + 1] = (byte)(m_row_buffer[src + 1] - GetDelta());
                            m_row_buffer[row_pos    ] = (byte)(m_row_buffer[src    ] - MapBlue[ctl]);
                            break;
                        case 4:
                            m_row_buffer[row_pos + 2] = (byte)(m_row_buffer[src + 2] - MapRed[ctl]);
                            m_row_buffer[row_pos + 1] = (byte)(m_row_buffer[src + 1] - MapGreen[ctl]);
                            m_row_buffer[row_pos    ] = (byte)(m_row_buffer[src    ] - GetDelta());
                            break;
                        case 5:
                            m_row_buffer[row_pos + 2] = (byte)(m_row_buffer[src + 2] - GetDelta());
                            m_row_buffer[row_pos + 1] = (byte)(m_row_buffer[src + 1] - MapGreen[ctl]);
                            m_row_buffer[row_pos    ] = (byte)(m_row_buffer[src    ] - GetDelta());
                            break;
                        case 6:
                            m_row_buffer[row_pos + 2] = (byte)(m_row_buffer[src + 2] - MapRed[ctl]);
                            m_row_buffer[row_pos + 1] = (byte)(m_row_buffer[src + 1] - GetDelta());
                            m_row_buffer[row_pos    ] = (byte)(m_row_buffer[src    ] - GetDelta());
                            break;
                        case 7:
                            m_row_buffer[row_pos + 2] = (byte)(m_row_buffer[src + 2] - GetDelta());
                            m_row_buffer[row_pos + 1] = (byte)(m_row_buffer[src + 1] - GetDelta());
                            m_row_buffer[row_pos    ] = (byte)(m_row_buffer[src    ] - GetDelta());
                            break;
                        }
                        if (-3 == alpha)
                        {
                            alpha = GetDelta();
                        }
                        m_row_buffer[row_pos + 3] = (byte)(m_row_buffer[src + 3] - alpha);
                        row_pos += 4;
                        ++x;
                    }
                }
                Buffer.BlockCopy (m_row_buffer, m_rows_ptr[0], m_output, dst, m_stride);
                dst += m_stride;
            }
        }

        int GetDelta ()
        {
            int d = m_input.GetBits (DeltaTable1, 8);
            if (0x2B == d)
                d += m_input.GetBits (DeltaTable2, 13);
            return DeltaTable3[d];
        }

        static byte[] LoadResource (string name) => EmbeddedResource.Load (name, typeof(ImgReader));

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
        }
        #endregion

        #region Bitmap Tables
        static readonly Lazy<byte[]> s_control_table1 = new Lazy<byte[]> (() => LoadResource ("ImgControlTable1"));
        static readonly Lazy<byte[]> s_control_table2 = new Lazy<byte[]> (() => LoadResource ("ImgControlTable2"));
        static readonly Lazy<byte[]> s_control_table32 = new Lazy<byte[]> (() => LoadResource ("ImgControlTable32"));
        static readonly Lazy<byte[]> s_delta_table2   = new Lazy<byte[]> (() => LoadResource ("ImgDeltaTable2"));

        static byte[] ControlTable1   { get { return s_control_table1.Value; } }
        static byte[] ControlTable2   { get { return s_control_table2.Value; } }
        static byte[] ControlTable32  { get { return s_control_table32.Value; } }
        static byte[] DeltaTable2     { get { return s_delta_table2.Value; } }

        static readonly byte[] PosTable24 = {
            4, 2, 1, 0, 4, 3, 1, 0, 3, 1, 1, 0, 4, 4, 1, 0, 5, 5, 1, 0, 5, 7, 1, 0, 3, 1, 1, 0, 5, 9, 1, 0, 4,
            2, 1, 0, 4, 3, 1, 0, 3, 1, 1, 0, 4, 4, 1, 0, 5, 6, 1, 0, 5, 8, 1, 0, 3, 1, 1, 0, 5, 10, 1, 0,
        };
        static readonly sbyte[] PosTable32 = {
            0, 0, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6, 0, 7, 0, 8, 0, 9, 0, 10, 0, 0, -3, 1, -3, 2, -3, 3, -3, 4,
            -3, 5, -3, 6, -3, 7, -3, 8, -3, 9, -3, 10, -3, 0, -1, 1, -1, 2, -1, 3, -1, 4, -1, 5, -1, 6, -1, 7,
            -1, 8, -1, 9, -1, 10, -1, 0, 1, 1, 1, 2, 1, 3, 1, 4, 1, 5, 1, 6, 1, 7, 1, 8, 1, 9, 1, 10, 1, 0,
            -2, 1, -2, 2, -2, 3, -2, 4, -2, 5, -2, 6, -2, 7, -2, 8, -2, 9, -2, 10, -2, 0, 2, 1, 2, 2, 2, 3, 2,
            4, 2, 5, 2, 6, 2, 7, 2, 8, 2, 9, 2, 10, 2,
        };
        static readonly byte[] ColorCode = {
            7, 3, 3, 3, 3, 3, 5, 1, 1, 1, 1, 1, 5, 1, 1, 1, 1, 1, 5, 1, 1, 1, 1, 1,
            5, 1, 1, 1, 1, 1, 5, 1, 1, 1, 1, 1, 6, 2, 2, 2, 2, 2, 4, 0, 0, 0, 0, 0,
            4, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0,
            6, 2, 2, 2, 2, 2, 4, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0,
            4, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 6, 2, 2, 2, 2, 2, 4, 0, 0, 0, 0, 0,
            4, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0,
            6, 2, 2, 2, 2, 2, 4, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0,
            4, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 6, 2, 2, 2, 2, 2, 4, 0, 0, 0, 0, 0,
            4, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0,
        };

        static readonly byte[] DeltaTable1 = {
            7, 0x13, 3, 0, 6, 0x0D, 4, 4, 5, 7, 3, 1, 5, 8, 4, 5, 4, 2, 3, 0, 4, 0x2B, 6, 0x10, 4, 3, 3, 1, 5,
            9, 5, 0x0B, 5, 6, 3, 0, 6, 0x0F, 4, 4, 6, 0x0C, 3, 1, 8, 0x23, 4, 5, 4, 2, 3, 0, 4, 0x2B, 6, 0x11,
            4, 3, 3, 1, 5, 0x0A, 8, 0x27, 7, 0x15, 3, 0, 6, 0x0E, 4, 4, 5, 7, 3, 1, 5, 8, 4, 5, 4, 2, 3, 0, 4,
            0x2B, 7, 0x1B, 4, 3, 3, 1, 5, 9, 5, 0x0B, 5, 6, 3, 0, 7, 0x18, 4, 4, 8, 0x22, 3, 1, 7, 0x19, 4, 5,
            4, 2, 3, 0, 4, 0x2B, 6, 0x12, 4, 3, 3, 1, 5, 0x0A, 6, 0x14, 8, 0x1D, 3, 0, 6, 0x0D, 4, 4, 5, 7, 3,
            1, 5, 8, 4, 5, 4, 2, 3, 0, 4, 0x2B, 6, 0x10, 4, 3, 3, 1, 5, 9, 5, 0x0B, 5, 6, 3, 0, 6, 0x0F, 4, 4,
            6, 0x0C, 3, 1, 7, 0x1A, 4, 5, 4, 2, 3, 0, 4, 0x2B, 6, 0x11, 4, 3, 3, 1, 5, 0x0A, 7, 0x1C, 8, 0x20,
            3, 0, 6, 0x0E, 4, 4, 5, 7, 3, 1, 5, 8, 4, 5, 4, 2, 3, 0, 4, 0x2B, 8, 0x28, 4, 3, 3, 1, 5, 9, 5,
            0x0B, 5, 6, 3, 0, 7, 0x17, 4, 4, 7, 0x16, 3, 1, 8, 0x26, 4, 5, 4, 2, 3, 0, 4, 0x2B, 6, 0x12, 4, 3,
            3, 1, 5, 0x0A, 6, 0x14, 7, 0x13, 3, 0, 6, 0x0D, 4, 4, 5, 7, 3, 1, 5, 8, 4, 5, 4, 2, 3, 0, 4, 0x2B,
            6, 0x10, 4, 3, 3, 1, 5, 9, 5, 0x0B, 5, 6, 3, 0, 6, 0x0F, 4, 4, 6, 0x0C, 3, 1, 8, 0x24, 4, 5, 4, 2,
            3, 0, 4, 0x2B, 6, 0x11, 4, 3, 3, 1, 5, 0x0A, 8, 0x29, 7, 0x15, 3, 0, 6, 0x0E, 4, 4, 5, 7, 3, 1, 5,
            8, 4, 5, 4, 2, 3, 0, 4, 0x2B, 7, 0x1B, 4, 3, 3, 1, 5, 9, 5, 0x0B, 5, 6, 3, 0, 7, 0x18, 4, 4, 8,
            0x21, 3, 1, 7, 0x19, 4, 5, 4, 2, 3, 0, 4, 0x2B, 6, 0x12, 4, 3, 3, 1, 5, 0x0A, 6, 0x14, 8, 0x1E, 3,
            0, 6, 0x0D, 4, 4, 5, 7, 3, 1, 5, 8, 4, 5, 4, 2, 3, 0, 4, 0x2B, 6, 0x10, 4, 3, 3, 1, 5, 9, 5, 0x0B,
            5, 6, 3, 0, 6, 0x0F, 4, 4, 6, 0x0C, 3, 1, 7, 0x1A, 4, 5, 4, 2, 3, 0, 4, 0x2B, 6, 0x11, 4, 3, 3, 1,
            5, 0x0A, 7, 0x1C, 8, 0x1F, 3, 0, 6, 0x0E, 4, 4, 5, 7, 3, 1, 5, 8, 4, 5, 4, 2, 3, 0, 4, 0x2B, 8,
            0x2A, 4, 3, 3, 1, 5, 9, 5, 0x0B, 5, 6, 3, 0, 7, 0x17, 4, 4, 7, 0x16, 3, 1, 8, 0x25, 4, 5, 4, 2, 3,
            0, 4, 0x2B, 6, 0x12, 4, 3, 3, 1, 5, 0x0A, 6, 0x14,
        };
        static readonly byte[] DeltaTable3 = {
            0x03, 0xFD, 0x04, 0xFC, 0x05, 0xFB, 0x06, 0xFA, 0x07, 0xF9, 0x08, 0xF8, 0x09, 0xF7, 0x0A, 0xF6,
            0x0B, 0xF5, 0x0C, 0xF4, 0x0D, 0xF3, 0x0E, 0xF2, 0x0F, 0xF1, 0x10, 0xF0, 0x11, 0xEF, 0x12, 0xEE,
            0x13, 0xED, 0x14, 0xEC, 0x15, 0xEB, 0x16, 0xEA, 0x17, 0xE9, 0x18, 0xE8, 0x19, 0xE7, 0x1A, 0xE6,
            0x1B, 0xE5, 0x1C, 0xE4, 0x1D, 0xE3, 0x1E, 0xE2, 0x1F, 0xE1, 0x20, 0xE0, 0x21, 0xDF, 0x22, 0xDE,
            0x23, 0xDD, 0x24, 0xDC, 0x25, 0xDB, 0x26, 0xDA, 0x27, 0xD9, 0x28, 0xD8, 0x29, 0xD7, 0x2A, 0xD6,
            0x2B, 0xD5, 0x2C, 0xD4, 0x2D, 0xD3, 0x2E, 0xD2, 0x2F, 0xD1, 0x30, 0xD0, 0x31, 0xCF, 0x32, 0xCE,
            0x33, 0xCD, 0x34, 0xCC, 0x35, 0xCB, 0x36, 0xCA, 0x37, 0xC9, 0x38, 0xC8, 0x39, 0xC7, 0x3A, 0xC6,
            0x3B, 0xC5, 0x3C, 0xC4, 0x3D, 0xC3, 0x3E, 0xC2, 0x3F, 0xC1, 0x40, 0xC0, 0x41, 0xBF, 0x42, 0xBE,
            0x43, 0xBD, 0x44, 0xBC, 0x45, 0xBB, 0x46, 0xBA, 0x47, 0xB9, 0x48, 0xB8, 0x49, 0xB7, 0x4A, 0xB6,
            0x4B, 0xB5, 0x4C, 0xB4, 0x4D, 0xB3, 0x4E, 0xB2, 0x4F, 0xB1, 0x50, 0xB0, 0x51, 0xAF, 0x52, 0xAE,
            0x53, 0xAD, 0x54, 0xAC, 0x55, 0xAB, 0x56, 0xAA, 0x57, 0xA9, 0x58, 0xA8, 0x59, 0xA7, 0x5A, 0xA6,
            0x5B, 0xA5, 0x5C, 0xA4, 0x5D, 0xA3, 0x5E, 0xA2, 0x5F, 0xA1, 0x60, 0xA0, 0x61, 0x9F, 0x62, 0x9E,
            0x63, 0x9D, 0x64, 0x9C, 0x65, 0x9B, 0x66, 0x9A, 0x67, 0x99, 0x68, 0x98, 0x69, 0x97, 0x6A, 0x96,
            0x6B, 0x95, 0x6C, 0x94, 0x6D, 0x93, 0x6E, 0x92, 0x6F, 0x91, 0x70, 0x90, 0x71, 0x8F, 0x72, 0x8E,
            0x73, 0x8D, 0x74, 0x8C, 0x75, 0x8B, 0x76, 0x8A, 0x77, 0x89, 0x78, 0x88, 0x79, 0x87, 0x7A, 0x86,
            0x7B, 0x85, 0x7C, 0x84, 0x7D, 0x83, 0x7E, 0x82, 0x7F, 0x81, 0x80, 0,
        };
        static readonly byte[] MapRed = {
            0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD,
            0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD,
            0xFD, 0xFD, 0xFD, 0xFD, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE,
            0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE,
            0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2,
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 
        };
        static readonly byte[] MapGreen = {
            0x0FD, 0x0FD, 0x0FD, 0x0FD, 0x0FD, 0x0FD, 0x0FE, 0x0FE, 0x0FE, 0x0FE, 0x0FE, 0x0FE, 0x0FF, 0x0FF,
            0x0FF, 0x0FF, 0x0FF, 0x0FF, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 0x0FD, 0x0FD,
            0x0FD, 0x0FD, 0x0FD, 0x0FD, 0x0FE, 0x0FE, 0x0FE, 0x0FE, 0x0FE, 0x0FE, 0x0FF, 0x0FF, 0x0FF, 0x0FF,
            0x0FF, 0x0FF, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 0x0FD, 0x0FD, 0x0FD, 0x0FD,
            0x0FD, 0x0FD, 0x0FE, 0x0FE, 0x0FE, 0x0FE, 0x0FE, 0x0FE, 0x0FF, 0x0FF, 0x0FF, 0x0FF, 0x0FF, 0x0FF,
            0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 0x0FD, 0x0FD, 0x0FD, 0x0FD, 0x0FD, 0x0FD,
            0x0FE, 0x0FE, 0x0FE, 0x0FE, 0x0FE, 0x0FE, 0x0FF, 0x0FF, 0x0FF, 0x0FF, 0x0FF, 0x0FF, 0, 0, 0, 0, 0,
            0, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 0x0FD, 0x0FD, 0x0FD, 0x0FD, 0x0FD, 0x0FD, 0x0FE, 0x0FE,
            0x0FE, 0x0FE, 0x0FE, 0x0FE, 0x0FF, 0x0FF, 0x0FF, 0x0FF, 0x0FF, 0x0FF, 0, 0, 0, 0, 0, 0, 1, 1, 1,
            1, 1, 1, 2, 2, 2, 2, 2, 2, 0x0FD, 0x0FD, 0x0FD, 0x0FD, 0x0FD, 0x0FD, 0x0FE, 0x0FE, 0x0FE, 0x0FE,
            0x0FE, 0x0FE, 0x0FF, 0x0FF, 0x0FF, 0x0FF, 0x0FF, 0x0FF, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 2, 2,
            2, 2, 2, 2,
        };
        static readonly byte[] MapBlue = {
            0x0FD, 0x0FE, 0x0FF, 0, 1, 2, 0x0FD, 0x0FE, 0x0FF, 0, 1, 2,
            0x0FD, 0x0FE, 0x0FF, 0, 1, 2, 0x0FD, 0x0FE, 0x0FF, 0, 1, 2,
            0x0FD, 0x0FE, 0x0FF, 0, 1, 2, 0x0FD, 0x0FE, 0x0FF, 0, 1, 2,
            0x0FD, 0x0FE, 0x0FF, 0, 1, 2, 0x0FD, 0x0FE, 0x0FF, 0, 1, 2,
            0x0FD, 0x0FE, 0x0FF, 0, 1, 2, 0x0FD, 0x0FE, 0x0FF, 0, 1, 2,
            0x0FD, 0x0FE, 0x0FF, 0, 1, 2, 0x0FD, 0x0FE, 0x0FF, 0, 1, 2,
            0x0FD, 0x0FE, 0x0FF, 0, 1, 2, 0x0FD, 0x0FE, 0x0FF, 0, 1, 2,
            0x0FD, 0x0FE, 0x0FF, 0, 1, 2, 0x0FD, 0x0FE, 0x0FF, 0, 1, 2,
            0x0FD, 0x0FE, 0x0FF, 0, 1, 2, 0x0FD, 0x0FE, 0x0FF, 0, 1, 2,
            0x0FD, 0x0FE, 0x0FF, 0, 1, 2, 0x0FD, 0x0FE, 0x0FF, 0, 1, 2,
            0x0FD, 0x0FE, 0x0FF, 0, 1, 2, 0x0FD, 0x0FE, 0x0FF, 0, 1, 2,
            0x0FD, 0x0FE, 0x0FF, 0, 1, 2, 0x0FD, 0x0FE, 0x0FF, 0, 1, 2,
            0x0FD, 0x0FE, 0x0FF, 0, 1, 2, 0x0FD, 0x0FE, 0x0FF, 0, 1, 2,
            0x0FD, 0x0FE, 0x0FF, 0, 1, 2, 0x0FD, 0x0FE, 0x0FF, 0, 1, 2,
            0x0FD, 0x0FE, 0x0FF, 0, 1, 2, 0x0FD, 0x0FE, 0x0FF, 0, 1, 2,
            0x0FD, 0x0FE, 0x0FF, 0, 1, 2, 0x0FD, 0x0FE, 0x0FF, 0, 1, 2,
            0x0FD, 0x0FE, 0x0FF, 0, 1, 2, 0x0FD, 0x0FE, 0x0FF, 0, 1, 2,
            0x0FD, 0x0FE, 0x0FF, 0, 1, 2, 0x0FD, 0x0FE, 0x0FF, 0, 1, 2,
        };
        #endregion
    }

    internal class ImgBitStream : BitStream
    {
        public ImgBitStream (Stream file, bool leave_open = false) : base (file, leave_open)
        {
        }

        public int GetBits (byte[] table, int count)
        {
            int n = PeekBits (count) * 2;
            count = table[n];
            m_bits >>= count;
            m_cached_bits -= count;
            return table[n + 1];
        }

        public int PeekBits (int count)
        {
            if (m_cached_bits < count)
            {
                int b = m_input.ReadByte();
                if (-1 == b)
                    b = 0; // throw new EndOfStreamException();
                m_bits |= ByteMap[b] << m_cached_bits;
                m_cached_bits += 8;
                if (m_cached_bits < count)
                {
                    b = m_input.ReadByte();
                    if (-1 == b)
                        b = 0; // throw new EndOfStreamException();
                    m_bits |= ByteMap[b] << m_cached_bits;
                    m_cached_bits += 8;
                }
            }
            return m_bits & g_bitMask[count];
        }

        static readonly int[] g_bitMask = {
            0x00000000, 0x00000001, 0x00000003, 0x00000007, 0x0000000F, 0x0000001F, 0x0000003F, 0x0000007F,
            0x000000FF, 0x000001FF, 0x000003FF, 0x000007FF, 0x00000FFF, 0x00001FFF, 0x00003FFF, 0x00007FFF,
            0x0000FFFF, 0x0001FFFF, 0x0003FFFF, 0x0007FFFF, 0x000FFFFF, 0x001FFFFF, 0x003FFFFF, 0x007FFFFF,
            0x00FFFFFF, 0x01FFFFFF, 0x03FFFFFF, 0x07FFFFFF, 0x0FFFFFFF, 0x1FFFFFFF, 0x3FFFFFFF, 0x7FFFFFFF,
        };

        internal static readonly byte[] ByteMap = {
            0x00, 0x80, 0x40, 0xC0, 0x20, 0xA0, 0x60, 0xE0, 0x10, 0x90, 0x50, 0xD0, 0x30, 0xB0, 0x70, 0xF0,
            0x08, 0x88, 0x48, 0xC8, 0x28, 0xA8, 0x68, 0xE8, 0x18, 0x98, 0x58, 0xD8, 0x38, 0xB8, 0x78, 0xF8,
            0x04, 0x84, 0x44, 0xC4, 0x24, 0xA4, 0x64, 0xE4, 0x14, 0x94, 0x54, 0xD4, 0x34, 0xB4, 0x74, 0xF4,
            0x0C, 0x8C, 0x4C, 0xCC, 0x2C, 0xAC, 0x6C, 0xEC, 0x1C, 0x9C, 0x5C, 0xDC, 0x3C, 0xBC, 0x7C, 0xFC,
            0x02, 0x82, 0x42, 0xC2, 0x22, 0xA2, 0x62, 0xE2, 0x12, 0x92, 0x52, 0xD2, 0x32, 0xB2, 0x72, 0xF2,
            0x0A, 0x8A, 0x4A, 0xCA, 0x2A, 0xAA, 0x6A, 0xEA, 0x1A, 0x9A, 0x5A, 0xDA, 0x3A, 0xBA, 0x7A, 0xFA,
            0x06, 0x86, 0x46, 0xC6, 0x26, 0xA6, 0x66, 0xE6, 0x16, 0x96, 0x56, 0xD6, 0x36, 0xB6, 0x76, 0xF6,
            0x0E, 0x8E, 0x4E, 0xCE, 0x2E, 0xAE, 0x6E, 0xEE, 0x1E, 0x9E, 0x5E, 0xDE, 0x3E, 0xBE, 0x7E, 0xFE,
            0x01, 0x81, 0x41, 0xC1, 0x21, 0xA1, 0x61, 0xE1, 0x11, 0x91, 0x51, 0xD1, 0x31, 0xB1, 0x71, 0xF1,
            0x09, 0x89, 0x49, 0xC9, 0x29, 0xA9, 0x69, 0xE9, 0x19, 0x99, 0x59, 0xD9, 0x39, 0xB9, 0x79, 0xF9,
            0x05, 0x85, 0x45, 0xC5, 0x25, 0xA5, 0x65, 0xE5, 0x15, 0x95, 0x55, 0xD5, 0x35, 0xB5, 0x75, 0xF5,
            0x0D, 0x8D, 0x4D, 0xCD, 0x2D, 0xAD, 0x6D, 0xED, 0x1D, 0x9D, 0x5D, 0xDD, 0x3D, 0xBD, 0x7D, 0xFD,
            0x03, 0x83, 0x43, 0xC3, 0x23, 0xA3, 0x63, 0xE3, 0x13, 0x93, 0x53, 0xD3, 0x33, 0xB3, 0x73, 0xF3,
            0x0B, 0x8B, 0x4B, 0xCB, 0x2B, 0xAB, 0x6B, 0xEB, 0x1B, 0x9B, 0x5B, 0xDB, 0x3B, 0xBB, 0x7B, 0xFB,
            0x07, 0x87, 0x47, 0xC7, 0x27, 0xA7, 0x67, 0xE7, 0x17, 0x97, 0x57, 0xD7, 0x37, 0xB7, 0x77, 0xF7,
            0x0F, 0x8F, 0x4F, 0xCF, 0x2F, 0xAF, 0x6F, 0xEF, 0x1F, 0x9F, 0x5F, 0xDF, 0x3F, 0xBF, 0x7F, 0xFF,
        };
    }
}
