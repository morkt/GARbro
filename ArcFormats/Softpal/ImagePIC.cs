//! \file       ImagePIC.cs
//! \date       Sun Jan 24 18:48:25 2016
//! \brief      Softpal engine image format.
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

namespace GameRes.Formats.Softpal
{
    internal class PicMetaData : ImageMetaData
    {
        public int  BlocksWidth;
        public int  BlocksHeight;
    }

    [Export(typeof(ImageFormat))]
    public class PicFormat : ImageFormat
    {
        public override string         Tag { get { return "PIC/SOFTPAL"; } }
        public override string Description { get { return "Softpal engine image format"; } }
        public override uint     Signature { get { return 0; } }

        public PicFormat ()
        {
            Extensions = new string[] { "pic", "" };
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[8];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            int bpp = LittleEndian.ToInt16 (header, 0);
            if (1 != bpp && 3 != bpp && 4 != bpp)
                return null;
            uint width  = LittleEndian.ToUInt16 (header, 2);
            uint height = LittleEndian.ToUInt16 (header, 4);
            if (0 == width || 0 == height || header[6]*8 < width || header[7]*8 < height)
                return null;
            return new PicMetaData
            {
                Width   = width,
                Height  = height,
                BPP     = bpp * 8,
                BlocksWidth  = header[6],
                BlocksHeight = header[7],
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = (PicMetaData)info;
            using (var reader = new PicReader (stream, meta))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, null, reader.Data);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PicFormat.Write not implemented");
        }
    }

    internal sealed class PicReader : IDisposable
    {
        BinaryReader    m_input;
        byte[]          m_output;
        PicMetaData     m_info;
        byte[]          m_control;
        int             m_src_stride;

        public PixelFormat Format { get; private set; }
        public byte[]        Data { get { return m_output; } }

        readonly byte[] Values2bit = InitBlockValues (2);
        readonly byte[] Values4bit = InitBlockValues (8);
        readonly byte[] Values6bit = InitBlockValues (0x20);

        public PicReader (Stream input, PicMetaData info)
        {
            m_input = new ArcView.Reader (input);
            m_info = info;
            m_src_stride = m_info.BlocksWidth * m_info.BPP;
        }

        static byte[] InitBlockValues (int length)
        {
            var values = new byte[length*2];
            for (int i = 0; i < length; ++i)
            {
                values[i] = (byte)i;
                values[values.Length-1-i] = (byte)(-1-i);
            }
            return values;
        }

        public void Unpack ()
        {
            m_input.BaseStream.Position = 8;
            m_control = m_input.ReadBytes (m_info.BlocksHeight * m_info.BlocksWidth);
            m_output = new byte[m_src_stride * m_info.BlocksHeight * 8];
            if (8 == m_info.BPP)
            {
                Format = PixelFormats.Gray8;
                Unpack8bpp();
            }
            else if (24 == m_info.BPP)
            {
                Format = PixelFormats.Bgr24;
                Unpack24bpp();
            }
            else if (32 == m_info.BPP)
            {
                Format = PixelFormats.Bgra32;
                Unpack32bpp();
            }
            else
                throw new InvalidFormatException();

            int dst_stride = (int)m_info.Width * m_info.BPP / 8;
            byte[] flipped = new byte[dst_stride*(int)m_info.Height];
            int dst = 0;
            int src = m_output.Length - m_src_stride;
            while (dst < flipped.Length)
            {
                Buffer.BlockCopy (m_output, src, flipped, dst, dst_stride);
                dst += dst_stride;
                src -= m_src_stride;
            }
            m_output = flipped;
        }

        void Unpack24bpp()
        {
            int pixel_size = 3;
            var pixel = new byte[4];
            var block = new byte[64];
            for (int y = 0; y < m_info.BlocksHeight; ++y)
            {
                for (int x = 0; x < m_info.BlocksWidth; ++x)
                {
                    byte ctl = m_control[x + m_info.BlocksWidth * (m_info.BlocksHeight - y - 1)];
                    if (0x80 == ctl)
                    {
                        int dst = pixel_size * 8 * (x + 8 * y * m_info.BlocksWidth);
                        for (int i = 0; i < 8; ++i)
                        {
                            for (int j = 0; j < 24; ++j)
                                m_output[dst+j] = 0;
                            dst += m_src_stride;
                        }
                    }
                    else
                    {
                        m_input.Read (pixel, 0, pixel_size);
                        for (int channel = 0; channel < pixel_size; ++channel)
                        {
                            DecodeBlock (ctl & 3, pixel[channel], block);
                            int dst = pixel_size * 8 * (x + 8 * y * m_info.BlocksWidth) + channel;
                            int src = 0;
                            for (int i = 0; i < 8; ++i)
                            {
                                m_output[dst]    = block[src++];
                                m_output[dst+3]  = block[src++];
                                m_output[dst+6]  = block[src++];
                                m_output[dst+9]  = block[src++];
                                m_output[dst+12] = block[src++];
                                m_output[dst+15] = block[src++];
                                m_output[dst+18] = block[src++];
                                m_output[dst+21] = block[src++];
                                dst += m_src_stride;
                            }
                            ctl >>= 2;
                        }
                    }
                }
            }
        }

        void Unpack32bpp()
        {
            int pixel_size = 4;
            var pixel = new byte[4];
            var block = new byte[64];
            for (int y = 0; y < m_info.BlocksHeight; ++y)
            {
                for (int x = 0; x < m_info.BlocksWidth; ++x)
                {
                    byte ctl = m_control[x + m_info.BlocksWidth * (m_info.BlocksHeight - y - 1)];
                    m_input.Read (pixel, 0, pixel_size);
                    for (int channel = 0; channel < pixel_size; ++channel)
                    {
                        DecodeBlock (ctl & 3, pixel[channel], block);
                        int dst = 8 * pixel_size * (x + 8 * y * m_info.BlocksWidth) + ChannelOrder[channel];
                        int src = 0;
                        for (int i = 0; i < 8; ++i)
                        {
                            m_output[dst]    = block[src++];
                            m_output[dst+4]  = block[src++];
                            m_output[dst+8]  = block[src++];
                            m_output[dst+12] = block[src++];
                            m_output[dst+16] = block[src++];
                            m_output[dst+20] = block[src++];
                            m_output[dst+24] = block[src++];
                            m_output[dst+28] = block[src++];
                            dst += m_src_stride;
                        }
                        ctl >>= 2;
                    }
                }
            }
        }

        static readonly byte[] ChannelOrder = { 3, 0, 1, 2 };

        void Unpack8bpp()
        {
            var block = new byte[64];
            for (int y = 0; y < m_info.BlocksHeight; ++y)
            {
                for (int x = 0; x < m_info.BlocksWidth; ++x)
                {
                    byte ctl = m_control[x + m_info.BlocksWidth * (m_info.BlocksHeight - y - 1)];
                    if (0x80 == ctl)
                    {
                        for (int i = 0; i < block.Length; ++i)
                            block[i] = 0;
                    }
                    else if (0x81 == ctl)
                    {
                        for (int i = 0; i < block.Length; ++i)
                            block[i] = 0xFF;
                    }
                    else
                    {
                        byte pixel = m_input.ReadByte();
                        DecodeBlock (ctl, pixel, block);
                    }
                    int dst = 8 * (x + 8 * y * m_info.BlocksWidth);
                    int src = 0;
                    for (int i = 0; i < 8; ++i)
                    {
                        m_output[dst]   = block[src++];
                        m_output[dst+1] = block[src++];
                        m_output[dst+2] = block[src++];
                        m_output[dst+3] = block[src++];
                        m_output[dst+4] = block[src++];
                        m_output[dst+5] = block[src++];
                        m_output[dst+6] = block[src++];
                        m_output[dst+7] = block[src++];
                        dst += m_src_stride;
                    }
                }
            }
        }

        void DecodeBlock (int control, byte pixel, byte[] block)
        {
            int dst = 0;
            switch (control)
            {
            case 0:
                for (int i = 0; i < 16; ++i)
                {
                    int b = m_input.ReadByte();
                    block[dst++] = Values2bit[b >> 6];
                    block[dst++] = Values2bit[(b >> 4) & 3];
                    block[dst++] = Values2bit[(b >> 2) & 3];
                    block[dst++] = Values2bit[b & 3];
                }
                break;
            case 1:
                for (int i = 0; i < 32; ++i)
                {
                    int b = m_input.ReadByte();
                    block[dst++] = Values4bit[b >> 4];
                    block[dst++] = Values4bit[b & 0xF];
                }
                break;
            case 2:
                for (int i = 0; i < 16; ++i)
                {
                    int b1 = m_input.ReadByte();
                    int b2 = m_input.ReadByte();
                    int b3 = m_input.ReadByte();
                    int b4 = (b3 >> 6) | ((b2 >> 4) & 0xC) | ((b1 >> 2) & 0x30);
                    block[dst++] = Values6bit[b1 & 0x3F];
                    block[dst++] = Values6bit[b2 & 0x3F];
                    block[dst++] = Values6bit[b3 & 0x3F];
                    block[dst++] = Values6bit[b4];
                }
                break;
            case 3:
                if (64 != m_input.Read (block, 0, 64))
                    throw new EndOfStreamException();
                break;
            }
            int v32 = pixel + block[27];
            int v33 = v32   + block[26];
            block[27] = (byte)v32;
            block[26] = (byte)v33;
            block[25] += (byte)v33;
            block[24] += block[25];
            block[19] += (byte)v32;
            int v34 = v32 + block[18];
            int v35 = v34 + block[17];
            block[18] = (byte)v34;
            block[17] = (byte)v35;
            block[16] += (byte)v35;
            block[11] += block[19];
            block[10] += (byte)v34;
            int v36 = v34 + block[9];
            int v37 = v36 + block[8];
            block[9] = (byte)v36;
            block[8] = (byte)v37;
            block[3] += block[11];
            block[2] += block[10];
            block[1] += (byte)v36;
            block[0] += (byte)v36;
            int v39 = pixel + block[28];
            int v40 = v39   + block[29];
            block[28] = (byte)v39;
            block[29] = (byte)v40;
            block[30] += (byte)v40;
            block[31] += block[30];
            block[20] += (byte)v39;
            int v41 = v39 + block[21];
            int v42 = v41 + block[22];
            block[21] = (byte)v41;
            block[22] = (byte)v42;
            block[23] += (byte)v42;
            block[12] += block[20];
            block[13] += (byte)v41;
            int v43 = v41 + block[14];
            int v44 = v43 + block[15];
            block[14] = (byte)v43;
            block[15] = (byte)v44;
            block[4] += block[12];
            block[5] += block[13];
            block[6] += (byte)v43;
            block[7] += (byte)v43;
            int v46 = pixel + block[35];
            int v47 = v46   + block[34];
            block[35] = (byte)v46;
            block[34] = (byte)v47;
            block[33] += (byte)v47;
            block[32] += block[33];
            block[43] += (byte)v46;
            int v48 = v46 + block[42];
            int v49 = v48 + block[41];
            block[42] = (byte)v48;
            block[41] = (byte)v49;
            block[40] += (byte)v49;
            block[51] += block[43];
            block[50] += (byte)v48;
            int v50 = v48 + block[49];
            int v51 = v50 + block[48];
            block[49] = (byte)v50;
            block[48] = (byte)v51;
            block[59] += block[51];
            block[58] += block[50];
            block[57] += (byte)v50;
            block[56] += (byte)v50;
            int v53 = pixel + block[36];
            int v54 = v53   + block[37];
            block[36] = (byte)v53;
            block[37] = (byte)v54;
            block[38] += (byte)v54;
            block[39] += block[38];
            int v56 = v53 + block[45];
            int v57 = v56 + block[46];
            block[47] += (byte)v57;
            block[46] = (byte)v57;
            block[45] = (byte)v56;
            block[44] += (byte)v53;
            block[53] += (byte)v56;
            block[52] += block[44];
            int v58 = v56 + block[54];
            block[54] = (byte)v58;
            block[55] += (byte)v58;
            block[60] += block[52];
            block[61] += block[53];
            block[62] += (byte)v58;
            block[63] += (byte)v58;
        }

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
    }
}
