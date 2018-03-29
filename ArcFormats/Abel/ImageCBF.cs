//! \file       ImageCBF.cs
//! \date       2018 Mar 29
//! \brief      Abel image format.
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
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Abel
{
    internal class CbfMetaData : ImageMetaData
    {
        public int  Compression;
    }

    [Export(typeof(ImageFormat))]
    public class CbfFormat : ImageFormat
    {
        public override string         Tag { get { return "CBF"; } }
        public override string Description { get { return "Abel image format"; } }
        public override uint     Signature { get { return 0x31464243; } } // 'CBF1'

        public CbfFormat ()
        {
            Signatures = new uint[] { 0x30464243, 0x31464243, 0x32464243, 0x33464243 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            if (header.ToInt32 (0x10) != 1)
                return null;
            int compression = header[3] - '0';
            if (compression < 0 || compression > 3)
                return null;
            return new CbfMetaData {
                Width  = header.ToUInt32 (4),
                Height = header.ToUInt32 (8),
                BPP    = header.ToInt32 (12),
                Compression = compression,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new CbfReader (file, (CbfMetaData)info);
            var pixels = reader.Unpack();
            var alp_name = Path.ChangeExtension (file.Name, "alp");
            if (VFS.FileExists (alp_name))
            {
                try
                {
                    using (var alp = VFS.OpenBinaryStream (alp_name))
                        return ReadAlpha (alp, info, pixels);
                }
                catch { /* ignore mask read errors */ }
            }
            return ImageData.Create (info, PixelFormats.Bgr24, null, pixels);
        }

        ImageData ReadAlpha (IBinaryStream alp, ImageMetaData info, byte[] image)
        {
            var header = alp.ReadHeader (0x10);
            if (!header.AsciiEqual ("ALP1"))
                throw new InvalidFormatException();
            int unpacked_size = header.ToInt32 (8);
            var alpha = new byte[unpacked_size];
            int dst = 0;
            while (dst < alpha.Length)
            {
                byte a = alp.ReadUInt8();
                int count = alp.ReadUInt8();
                for (int i = 0; i < count; ++i)
                    alpha[dst++] = a;
            }
            int dst_stride = (int)info.Width * 4;
            var pixels = new byte[dst_stride * (int)info.Height];
            int a_src = 0;
            int src = 0;
            for (dst = 0; dst < pixels.Length; dst += 4)
            {
                pixels[dst  ] = image[src++];
                pixels[dst+1] = image[src++];
                pixels[dst+2] = image[src++];
                pixels[dst+3] = alpha[a_src++];
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels, dst_stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CbfFormat.Write not implemented");
        }
    }

    internal class CbfReader
    {
        IBinaryStream   m_input;
        byte[]          m_output;
        int             m_width;
        int             m_height;
        int             m_stride;
        int             m_compression;

        public byte[]        Data { get { return m_output; } }

        public CbfReader (IBinaryStream input, CbfMetaData info)
        {
            m_input = input;
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            m_stride = 3 * m_width;
            m_output = new byte[m_stride * m_height];
            m_compression = info.Compression;
        }

        public byte[] Unpack ()
        {
            switch (m_compression)
            {
            case 0: UnpackV0(); break;
            case 1: UnpackV1(); break;
            case 2: UnpackV2(); break;
            case 3: UnpackV3(); break;
            default:
                throw new NotImplementedException (string.Format ("CBF compression {0} not implemented.", m_compression));
            }
            return m_output;
        }

        void UnpackV0 ()
        {
            m_input.Position = 0x18;
            m_input.Read (m_output, 0, m_output.Length);
        }

        void UnpackV1 ()
        {
            m_input.Position = 0x18;
            using (var lzss = new LzssStream (m_input.AsStream, LzssMode.Decompress, true))
                lzss.Read (m_output, 0, m_output.Length);
            for (int i = 3; i < m_output.Length; ++i)
                m_output[i] += m_output[i-3];
            var pixels = new byte[m_output.Length];
            int src = 0;
            var z_order = GetZigzagBlock();
            for (int y = 0; y < m_height; y += 8)
            for (int x = 0; x < m_stride; x += 24)
            {
                int dst = x + y * m_stride;
                for (int i = 0; i < 64; ++i)
                {
                    int pos = z_order[i];
                    pixels[dst + pos++] = m_output[src++];
                    pixels[dst + pos++] = m_output[src++];
                    pixels[dst + pos++] = m_output[src++];
                }
            }
            m_output = pixels;
        }

        void UnpackV2 ()
        {
            m_input.Position = 0x18;
            ReadRle (m_input.AsStream);
        }

        void UnpackV3 ()
        {
            m_input.Position = 0x1C;
            using (var lzss = new LzssStream (m_input.AsStream, LzssMode.Decompress, true))
                ReadRle (lzss);
        }

        void ReadRle (Stream input)
        {
            int dst = 0;
            while (dst < m_output.Length)
            {
                if (3 != input.Read (m_output, dst, 3))
                    break;
                int count = input.ReadByte();
                if (count > 0)
                {
                    count *= 3;
                    Binary.CopyOverlapped (m_output, dst, dst+3, count-3);
                    dst += count;
                }
            }
        }

        int[] GetZigzagBlock ()
        {
            var order = new int[64];
            for (int i = 0; i < 64; ++i)
            {
                order[i] = (ZigzagOrder[i] & 7) * 3 + (ZigzagOrder[i] >> 3) * m_stride;
            }
            return order;
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
