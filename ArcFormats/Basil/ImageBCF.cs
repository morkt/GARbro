//! \file       ImageBCF.cs
//! \date       2018 Jul 12
//! \brief      BasiL image format.
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
using GameRes.Utility;

namespace GameRes.Formats.Basil
{
    internal class BcfMetaData : ImageMetaData
    {
        public int  Stride;
        public int  DataShift;
        public int  AlphaShift;
        public uint DataOffset;
        public uint DataBitsOffset;
        public uint AlphaOffset;
        public uint AlphaBitsOffset;
    }

    [Export(typeof(ImageFormat))]
    public class BcfFormat : ImageFormat
    {
        public override string         Tag { get { return "BCF"; } }
        public override string Description { get { return "BasiL image format"; } }
        public override uint     Signature { get { return 0x464342; } } // 'BCF'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            var info = new BcfMetaData {
                Width  = header.ToUInt16 (4),
                Height = header.ToUInt16 (6),
                Stride = header.ToInt32 (8),
                DataShift  = header[0xC],
                AlphaShift = header[0xD],
                DataOffset = header.ToUInt32 (0x10),
                DataBitsOffset = header.ToUInt32 (0x14),
                AlphaOffset = header.ToUInt32 (0x18),
                AlphaBitsOffset = header.ToUInt32 (0x1C),
            };
            info.BPP = 0 == info.AlphaOffset ? 24 : 32;
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new BcfReader (file, (BcfMetaData)info);
            return reader.GetImage();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BcfFormat.Write not implemented");
        }
    }

    internal class BcfReader
    {
        IBinaryStream   m_input;
        BcfMetaData     m_info;
        byte[]          m_output;

        public PixelFormat Format { get; private set; }
        public int         Stride { get; private set; }

        public BcfReader (IBinaryStream input, BcfMetaData info)
        {
            m_input = input;
            m_info = info;
            if (24 == m_info.BPP)
            {
                Format = PixelFormats.Bgr24;
                Stride = m_info.Stride;
            }
            else
            {
                Format = PixelFormats.Bgra32;
                Stride = 4 * (int)m_info.Width;
            }
            m_output = new byte[m_info.Stride * (int)info.Height];
        }

        public ImageData GetImage ()
        {
            var pixels = Unpack();
            return ImageData.CreateFlipped (m_info, Format, null, pixels, Stride);
        }

        public byte[] Unpack ()
        {
            m_input.Position = m_info.DataBitsOffset;
            uint data_end = 0 == m_info.AlphaOffset ? (uint)m_input.Length : m_info.AlphaOffset;
            var bits = m_input.ReadBytes ((int)(data_end - m_info.DataBitsOffset));
            m_input.Position = m_info.DataOffset;
            LzUnpack (bits, ShiftTable[m_info.DataShift], m_output);

            if (m_info.AlphaOffset != 0)
            {
                m_input.Position = m_info.AlphaBitsOffset;
                bits = m_input.ReadBytes ((int)(m_input.Length - m_info.AlphaBitsOffset));
                var alpha = new byte[m_info.Width * m_info.Height];
                m_input.Position = m_info.AlphaOffset;
                LzUnpack (bits, ShiftTable[m_info.AlphaShift], alpha);

                var pixels = new byte[Stride * (int)m_info.Height];
                int src_row = 0;
                int asrc = 0;
                int dst = 0;
                for (uint y = 0; y < m_info.Height; ++y)
                {
                    int src = src_row;
                    for (uint x = 0; x < m_info.Width; ++x)
                    {
                        pixels[dst++] = m_output[src++];
                        pixels[dst++] = m_output[src++];
                        pixels[dst++] = m_output[src++];
                        pixels[dst++] = alpha[asrc++];
                    }
                    src_row += m_info.Stride;
                }
                m_output = pixels;
            }
            return m_output;
        }

        void LzUnpack (byte[] bits, int shift, byte[] output)
        {
            int dst = 0;
            int bitsrc = 0;
            byte mask = 1;
            while (dst < output.Length)
            {
                if ((mask & bits[bitsrc]) != 0)
                {
                    ushort v = m_input.ReadUInt16();
                    int count = (v & ~(0xFFFF << shift)) + 3;
                    int offset = (v >> shift) + 1;
                    Binary.CopyOverlapped (output, dst-offset, dst, count);
                    dst += count;
                }
                else
                {
                    output[dst++] = m_input.ReadUInt8();
                }
                mask <<= 1;
                if (0 == mask)
                {
                    ++bitsrc;
                    mask = 1;
                }
            }
        }

        static readonly byte[] ShiftTable = { 3, 4, 5, 6, 7 };
    }
}
