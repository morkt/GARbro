//! \file       ImageSGF.cs
//! \date       2018 Jun 11
//! \brief      Eternity engine image format.
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

namespace GameRes.Formats.Eternity
{
    internal class SgfMetaData : ImageMetaData
    {
        public bool HasAlpha;
        public int  BlockSize;
        public uint DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class SgfFormat : ImageFormat
    {
        public override string         Tag { get { return "SGF"; } }
        public override string Description { get { return "Eternity engine image format"; } }
        public override uint     Signature { get { return 0x644753; } } // 'SG'

        public SgfFormat ()
        {
            Signatures = new uint[] { 0x644753, 0 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            if (!header.AsciiEqual ("SG"))
                return null;
            int version = header.ToUInt16 (2);
            if (version != 100)
                return null;
            return new SgfMetaData {
                Width  = header.ToUInt16 (4),
                Height = header.ToUInt16 (6),
                BPP    = 24,
                HasAlpha   = header.ToInt32 (8) != 0,
                BlockSize  = header.ToUInt16 (0xC),
                DataOffset = header.ToUInt32 (0x14),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new SgfReader (file, (SgfMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("SgfFormat.Write not implemented");
        }
    }

    internal class SgfReader
    {
        IBinaryStream   m_input;
        SgfMetaData     m_info;
        byte[]          m_output;

        public byte[]        Data { get { return m_output; } }
        public PixelFormat Format { get { return PixelFormats.Bgr24; } }

        public SgfReader (IBinaryStream input, SgfMetaData info)
        {
            m_input = input;
            m_info = info;
            m_output = new byte[3 * info.Width * info.Height];
        }

        public byte[] Unpack ()
        {
            long next_pos = m_info.DataOffset;
            int height = (int)m_info.Height;
            int dst = 0;
            byte b = 0, g = 0, r = 0;
            for (int y = 0; y < height; ++y)
            {
                int start_pos = dst;
                if (0 == (y % m_info.BlockSize))
                {
                    m_input.Position = next_pos;
                    next_pos += m_input.ReadUInt32();
                    b = m_input.ReadUInt8();
                    g = m_input.ReadUInt8();
                    r = m_input.ReadUInt8();
                    m_input.ReadUInt8();
                    mask1 = mask2 = mask3 = mask4 = mask5 = 1;
                }
                for (uint x = 0; x < m_info.Width; ++x)
                {
                    b = GetNextByte (b);
                    g = GetNextByte (g);
                    r = GetNextByte (r);
                    m_output[dst++] = b;
                    m_output[dst++] = g;
                    m_output[dst++] = r;
                }
                b = m_output[start_pos];
                g = m_output[start_pos+1];
                r = m_output[start_pos+2];
            }
            return m_output;
        }

        uint mask1;
        uint mask2;
        uint mask3;
        uint mask4;
        uint mask5;
        uint in1;
        uint in2;
        uint in3;
        uint in4;
        uint in5;

        byte GetNextByte (byte prev)
        {
            if (mask1 == 1)
                in1 = m_input.ReadUInt32();
            if ((mask1 & in1) == 0)
            {
                if (mask2 == 1)
                    in2 = m_input.ReadUInt32();
                if ((mask2 & in2) != 0)
                {
                    if (mask3 == 1)
                        in3 = m_input.ReadUInt32();
                    if (mask4 == 1)
                        in4 = m_input.ReadUInt32();
                    uint diff = (in4 & 0xF) + 1;
                    if ((mask3 & in3) != 0)
                        prev -= (byte)diff;
                    else
                        prev += (byte)diff;
                    in4 >>= 4;
                    mask3 = Binary.RotL (mask3, 1);
                    mask4 = Binary.RotL (mask4, 4);
                }
                else
                {
                    if (mask5 == 1)
                        in5 = m_input.ReadUInt32();
                    prev = (byte)in5;
                    in5 >>= 8;
                    mask5 = Binary.RotL (mask5, 8);
                }
                mask2 = Binary.RotL (mask2, 1);
            }
            mask1 = Binary.RotL (mask1, 1);
            return prev;
        }
    }
}
