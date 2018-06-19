//! \file       ImageCTF.cs
//! \date       2018 Jun 18
//! \brief      'Unknown' image format.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Compression;

namespace GameRes.Formats.Unknown
{
    internal class CtfMetaData : ImageMetaData
    {
        public int  UnpackedSize;
        public int  ROffset;
        public int  GOffset;
        public int  BOffset;
        public int  AlphaOffset;
        public bool IsCompressed;
    }

    [Export(typeof(ImageFormat))]
    public class CtfFormat : ImageFormat
    {
        public override string         Tag { get { return "CTF"; } }
        public override string Description { get { return "'Unknown' image format"; } }
        public override uint     Signature { get { return 0x46465443; } } // 'CTFF'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x28);
            int bpp = header[0x20];
            if (bpp != 24)
                return null;
            int alpha_offset = header.ToInt32 (0x1C);
            return new CtfMetaData {
                Width  = header.ToUInt16 (4),
                Height = header.ToUInt16 (6),
                BPP = alpha_offset != 0 ? 32 : bpp,
                UnpackedSize = header.ToInt32 (0xC),
                ROffset = header.ToInt32 (0x10),
                GOffset = header.ToInt32 (0x14),
                BOffset = header.ToInt32 (0x18),
                AlphaOffset = alpha_offset,
                IsCompressed = header[0x22] == 0xFF,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new CtfReader (file, (CtfMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, null, pixels, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CtfFormat.Write not implemented");
        }
    }

    internal class CtfReader
    {
        IBinaryStream   m_input;
        CtfMetaData     m_info;
        byte[]          m_output;
        int             m_stride;

        public PixelFormat Format { get; private set; }
        public int         Stride { get { return m_stride; } }

        public CtfReader (IBinaryStream input, CtfMetaData info)
        {
            m_input = input;
            m_info = info;
            m_stride = ((int)info.Width * info.BPP / 8 + 3) & -3;
            m_output = new byte[m_stride * (int)info.Height];
            if (24 == m_info.BPP)
                Format = PixelFormats.Bgr24;
            else
                Format = PixelFormats.Bgra32;
        }

        public byte[] Unpack ()
        {
            m_input.Position = 0x48;
            var channels = new byte[m_info.UnpackedSize];
            using (var lzss = new LzssStream (m_input.AsStream, LzssMode.Decompress, true))
            using (var input = new BinaryStream (lzss, m_input.Name))
                UnpackRle (input, channels);

            int dst = 0;
            int pixel_size = m_info.BPP / 8;
            int rsrc = m_info.ROffset;
            int gsrc = m_info.GOffset;
            int bsrc = m_info.BOffset;
            int asrc = m_info.AlphaOffset;
            bool has_alpha = asrc != 0;
            int width = (int)m_info.Width;
            int height = (int)m_info.Height;
            int gap = m_stride - (width * pixel_size);
            for (uint y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    m_output[dst  ] = channels[bsrc++];
                    m_output[dst+1] = channels[gsrc++];
                    m_output[dst+2] = channels[rsrc++];
                    if (has_alpha)
                        m_output[dst+3] = channels[asrc++];
                    dst += pixel_size;
                }
                dst += gap;
            }
            return m_output;
        }

        void UnpackRle (IBinaryStream input, byte[] output)
        {
            var header = input.ReadBytes (0x18);
            byte count_limit = header[5];
            int dst = 0;
            while (dst < output.Length)
            {
                int v = input.ReadByte();
                if (-1 == v)
                    break;
                int count = 1;
                while (count < count_limit)
                {
                    if (input.PeekByte() != v)
                        break;
                    ++count;
                    input.ReadByte();
                }
                if (count == count_limit)
                {
                    byte ctl = input.ReadUInt8();
                    if (ctl > 0x7F)
                    {
                        byte lo = input.ReadUInt8();
                        count += lo + ((ctl & 0x7F) << 8) + 128;
                    }
                    else
                    {
                        count += ctl;
                    }
                }
                for (int i = 0; i < count; ++i)
                    output[dst++] = (byte)v;
            }
        }
    }
}
