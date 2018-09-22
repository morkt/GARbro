//! \file       ImageKAAS.cs
//! \date       Sat Apr 11 03:09:41 2015
//! \brief      KAAS engine image format implementation.
//
// Copyright (C) 2015-2017 by morkt
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
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.KAAS
{
    internal class PicMetaData : ImageMetaData
    {
        public int  Mode;
        public int  Key;
        public uint CompSize1;
        public uint CompSize2;
        public uint CompSize3;
    }

    [Export(typeof(ImageFormat))]
    public class PicFormat : ImageFormat
    {
        public override string         Tag { get { return "PIC/KAAS"; } }
        public override string Description { get { return "KAAS engine image format"; } }
        public override uint     Signature { get { return 0; } }

        public PicFormat ()
        {
            Extensions = new string[] { "" };
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("PicFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x12);
            int mode = header[0];
            switch (mode)
            {
            case 5: case 6: case 8: case 9:
                break;
            default:
                return null;
            }
            int key = header[1];
            uint width  = LittleEndian.ToUInt16 (header, 2);
            uint height = LittleEndian.ToUInt16 (header, 4);
            if (0 == width || width > 4096 || 0 == height || height > 4096)
                return null;
            var file_len = stream.Length;
            uint comp_size1 = LittleEndian.ToUInt32 (header, 8);
            uint comp_size2 = LittleEndian.ToUInt32 (header, 12);
            uint comp_size3 = (uint)(file_len - (0x12 + comp_size1 + comp_size2));
            if (comp_size1 >= file_len || comp_size2 >= file_len || comp_size3 >= file_len)
                return null;
            return new PicMetaData
            {
                Width = width,
                Height = height,
                BPP = 24,
                Mode = mode,
                Key = key,
                CompSize1 = comp_size1,
                CompSize2 = comp_size2,
                CompSize3 = comp_size3,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var reader = new Reader (stream, (PicMetaData)info))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, null, reader.Data, reader.Stride);
            }
        }

        internal sealed class Reader : IDisposable
        {
            byte[]          m_comp0;
            byte[]          m_comp1;
            byte[]          m_comp2;
            byte[]          m_output;
            int             m_mode;
            int             m_key;

            public PixelFormat Format { get { return PixelFormats.Bgr24; } }
            public byte[]        Data { get { return m_output; } }
            public int         Stride { get; private set; }

            public Reader (IBinaryStream file, PicMetaData info)
            {
                m_mode = info.Mode;
                m_key = info.Key;
                Stride = (int)info.Width * 3;
                file.Position = 0x12;
                if (6 == info.Mode)
                {
                    uint out_len = info.Width * info.Height * 3;
                    m_output = new byte[out_len];
                    if (m_output.Length != file.Read (m_output, 0, m_output.Length))
                        throw new InvalidFormatException ("Unexpected end of file");
                }
                else
                {
                    m_comp0 = new byte[info.CompSize1];
                    if (m_comp0.Length != file.Read (m_comp0, 0, m_comp0.Length))
                        throw new InvalidFormatException ("Unexpected end of file");

                    m_comp1 = new byte[info.CompSize2];
                    if (m_comp1.Length != file.Read (m_comp1, 0, m_comp1.Length))
                        throw new InvalidFormatException ("Unexpected end of file");

                    m_comp2 = new byte[info.CompSize3];
                    if (m_comp2.Length != file.Read (m_comp2, 0, m_comp2.Length))
                        throw new InvalidFormatException ("Unexpected end of file");

                    uint out_len = (info.Width * info.Height + 96) * 3;
                    m_output = new byte[out_len];
                }
            }

            public void Unpack ()
            {
                switch (m_mode)
                {
                case 5: Unpack5(); break;
                case 6: Unpack6(); break;
                case 8: Unpack8(); break;
                case 9: Unpack9(); break;
                default:
                    throw new NotSupportedException ("[KAAS] Not supported image compression");
                }
            }

            private void Unpack5 ()
            {
                int src0 = 0;
                int src1 = 0;
                int src2 = 0;
                int dst = 0;
                int ctl = 1;
                for (;;)
                {
                    if (1 == ctl)
                    {
                        if (src0 == m_comp0.Length)
                            break;
                        ctl = m_comp0[src0++] | 0x100;
                    }
                    int type = ctl & 3;
                    ctl >>= 2;

                    int count, offset;
                    switch (type)
                    {
                    case 0:
                        m_output[dst++] = m_comp1[src1++];
                        break;
                    case 1:
                        count = ((m_comp2[src2 / 2] >> (4 * (src2 & 1))) & 0xf) + 2;
                        ++src2;
                        offset = m_comp1[src1++] + 2;
                        Binary.CopyOverlapped (m_output, dst-offset, dst, count);
                        dst += count;
                        break;
                    case 2:
                        count = LittleEndian.ToUInt16 (m_comp1, src1);
                        if (0 == count)
                            return;
                        src1 += 2;
                        offset = (count & 0xfff) + 2;
                        count = (count >> 12) + 2;
                        Binary.CopyOverlapped (m_output, dst-offset, dst, count);
                        dst += count;
                        break;
                    default:
                        offset = (((m_comp2[src2 / 2] << (4 * (2 - (src2 & 1)))) & 0xf00) | m_comp1[src1]) + 2;
                        count = m_comp1[src1+1] + 18;
                        ++src2;
                        src1 += 2;
                        Binary.CopyOverlapped (m_output, dst-offset, dst, count);
                        dst += count;
                        break;
                    }
                }
            }

            private void Unpack6 ()
            {
                int conv_base = m_key & 0x3f;
                for (int i = 0; i < m_output.Length; ++i)
                    m_output[i] -= ScrambleTable[conv_base + (i&0xff)];
            }

            private void Unpack8 ()
            {
                int src0 = 0;
                int src1 = 0;
                int src2 = 0;
                int dst = 0;
                for (;;)
                {
                    int count, offset;
                    int type = (m_comp0[src0 / 8] >> (src0 & 6)) & 3;
                    src0 += 2;
                    if (type == 0)
                    {
                        m_output[dst++] = m_comp1[src1++];
                        m_output[dst++] = m_comp1[src1++];
                        m_output[dst++] = m_comp1[src1++];
                    }
                    else if (1 == type)
                    {
                        int code = m_comp1[src1++] + ((m_comp2[src2 / 2] << (4 * (2 - (src2 & 1)))) & 0xf00);
                        ++src2;
                        count = ((code >> 10) + 1) * 3;
                        offset = ((code & 0x3FF) + 1) * 3;
                        Binary.CopyOverlapped (m_output, dst-offset, dst, count);
                        dst += count;
                    }
                    else if (2 == type)
                    {
                        int code = LittleEndian.ToUInt16 (m_comp1, src1);
                        src1 += 2;
                        count = ((code >> 14) + 1) * 3;
                        offset = ((code & 0x3FFF) + 1) * 3;
                        Binary.CopyOverlapped (m_output, dst-offset, dst, count);
                        dst += count;
                    }
                    else
                    {
                        int code = ((m_comp2[src2 / 2] << (4 * (4 - (src2 & 1)))) & 0xF0000) | LittleEndian.ToUInt16 (m_comp1, src1);
                        if (0 == code)
                            break;
                        src1 += 2;
                        ++src2;
                        count = ((code >> 14) + 5) * 3;
                        offset = ((code & 0x3FFF) + 1) * 3;
                        Binary.CopyOverlapped (m_output, dst-offset, dst, count);
                        dst += count;
                    }
                }
            }

            private void Unpack9 ()
            {
                int src0 = 0;
                int src1 = 0;
                int src2 = 0;
                int dst = 0;
                for (;;)
                {
                    int count, offset;
                    int type = (m_comp0[src0 / 8] >> (src0 & 6)) & 3;
                    src0 += 2;
                    if (0 == type)
                    {
                        count = (m_comp0[src0 / 8] >> (src0 & 6)) & 3;
                        src0 += 2;
                        count = (count + 1) * 3;
                        Buffer.BlockCopy (m_comp1, src1, m_output, dst, count);
                        dst += count;
                        src1 += count;
                    }
                    else
                    {
                        if (1 == type)
                        {
                            int code = m_comp1[src1++] + ((m_comp2[src2 / 2] << (4 * (2 - (src2 & 1)))) & 0xF00);
                            ++src2;
                            count = ((code >> 10) + 1) * 3;
                            offset = ((code & 0x3FF) + 1) * 3;
                        }
                        else if (2 == type)
                        {
                            int code = LittleEndian.ToUInt16 (m_comp1, src1);
                            src1 += 2;
                            count = ((code >> 14) + 1) * 3;
                            offset = ((code & 0x3FFF) + 1) * 3;
                        }
                        else
                        {
                            int code = ((m_comp2[src2 / 2] << (4 * (4 - (src2 & 1)))) & 0xf0000) | LittleEndian.ToUInt16 (m_comp1, src1);
                            if (0 == code)
                                break;
                            src1 += 2;
                            ++src2;
                            count = ((code >> 14) + 5) * 3;
                            offset = ((code & 0x3FFF) + 1) * 3;
                        }
                        Binary.CopyOverlapped (m_output, dst-offset, dst, count);
                        dst += count;
                    }
                }
            }

            static readonly byte[] ScrambleTable = new byte[] {
                0x29, 0x23, 0xbe, 0x84, 0xe1, 0x6c, 0xd6, 0xae, 0x52, 0x90, 0x49, 0xf1, 0xf1, 0xbb, 0xe9, 0xeb, 
                0xb3, 0xa6, 0xdb, 0x3c, 0x87, 0x0c, 0x3e, 0x99, 0x24, 0x5e, 0x0d, 0x1c, 0x06, 0xb7, 0x47, 0xde, 
                0xb3, 0x12, 0x4d, 0xc8, 0x43, 0xbb, 0x8b, 0xa6, 0x1f, 0x03, 0x5a, 0x7d, 0x09, 0x38, 0x25, 0x1f, 
                0x5d, 0xd4, 0xcb, 0xfc, 0x96, 0xf5, 0x45, 0x3b, 0x13, 0x0d, 0x89, 0x0a, 0x1c, 0xdb, 0xae, 0x32, 
                0x20, 0x9a, 0x50, 0xee, 0x40, 0x78, 0x36, 0xfd, 0x12, 0x49, 0x32, 0xf6, 0x9e, 0x7d, 0x49, 0xdc, 
                0xad, 0x4f, 0x14, 0xf2, 0x44, 0x40, 0x66, 0xd0, 0x6b, 0xc4, 0x30, 0xb7, 0x32, 0x3b, 0xa1, 0x22, 
                0xf6, 0x22, 0x91, 0x9d, 0xe1, 0x8b, 0x1f, 0xda, 0xb0, 0xca, 0x99, 0x02, 0xb9, 0x72, 0x9d, 0x49, 
                0x2c, 0x80, 0x7e, 0xc5, 0x99, 0xd5, 0xe9, 0x80, 0xb2, 0xea, 0xc9, 0xcc, 0x53, 0xbf, 0x67, 0xd6, 
                0xbf, 0x14, 0xd6, 0x7e, 0x2d, 0xdc, 0x8e, 0x66, 0x83, 0xef, 0x57, 0x49, 0x61, 0xff, 0x69, 0x8f, 
                0x61, 0xcd, 0xd1, 0x1e, 0x9d, 0x9c, 0x16, 0x72, 0x72, 0xe6, 0x1d, 0xf0, 0x84, 0x4f, 0x4a, 0x77, 
                0x02, 0xd7, 0xe8, 0x39, 0x2c, 0x53, 0xcb, 0xc9, 0x12, 0x1e, 0x33, 0x74, 0x9e, 0x0c, 0xf4, 0xd5, 
                0xd4, 0x9f, 0xd4, 0xa4, 0x59, 0x7e, 0x35, 0xcf, 0x32, 0x22, 0xf4, 0xcc, 0xcf, 0xd3, 0x90, 0x2d, 
                0x48, 0xd3, 0x8f, 0x75, 0xe6, 0xd9, 0x1d, 0x2a, 0xe5, 0xc0, 0xf7, 0x2b, 0x78, 0x81, 0x87, 0x44, 
                0x0e, 0x5f, 0x50, 0x00, 0xd4, 0x61, 0x8d, 0xbe, 0x7b, 0x05, 0x15, 0x07, 0x3b, 0x33, 0x82, 0x1f, 
                0x18, 0x70, 0x92, 0xda, 0x64, 0x54, 0xce, 0xb1, 0x85, 0x3e, 0x69, 0x15, 0xf8, 0x46, 0x6a, 0x04, 
                0x96, 0x73, 0x0e, 0xd9, 0x16, 0x2f, 0x67, 0x68, 0xd4, 0xf7, 0x4a, 0x4a, 0xd0, 0x57, 0x68, 0x76, 
                0xfa, 0x16, 0xbb, 0x11, 0xad, 0xae, 0x24, 0x88, 0x79, 0xfe, 0x52, 0xdb, 0x25, 0x43, 0xe5, 0x3c, 
                0xf4, 0x45, 0xd3, 0xd8, 0x28, 0xce, 0x0b, 0xf5, 0xc5, 0x60, 0x59, 0x3d, 0x97, 0x27, 0x8a, 0x59, 
                0x76, 0x2d, 0xd0, 0xc2, 0xc9, 0xcd, 0x68, 0xd4, 0x49, 0x6a, 0x79, 0x25, 0x08, 0x61, 0x40, 0x14, 
                0xb1, 0x3b, 0x6a, 0xa5, 0x11, 0x28, 0xc1, 0x8c, 0xd6, 0xa9, 0x0b, 0x87, 0x97, 0x8c, 0x2f, 0xf1, 
            };

            #region IDisposable Members
            public void Dispose ()
            {
                GC.SuppressFinalize (this);
            }
            #endregion
        }
    }
}
