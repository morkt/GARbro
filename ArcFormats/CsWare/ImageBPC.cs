//! \file       ImageBPC.cs
//! \date       2017 Nov 22
//! \brief      C's ware bitmap.
//
// Copyright (C) 2017 by morkt
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
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.CsWare
{
    [Export(typeof(ImageFormat))]
    public class BpcFormat : ImageFormat
    {
        public override string         Tag { get { return "BPC"; } }
        public override string Description { get { return "C's ware bitmap format"; } }
        public override uint     Signature { get { return 0x28; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            uint width  = header.ToUInt32 (4);
            uint height = header.ToUInt32 (8);
            int bpp = header.ToUInt16 (0xE);
            if (bpp != 1 && bpp != 8 && bpp != 24)
                return null;
            return new ImageMetaData { Width = width, Height = height, BPP = bpp };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var bpc = new BpcReader (file, info);
            bpc.Unpack();
            return ImageData.CreateFlipped (info, bpc.Format, bpc.Palette, bpc.Data, bpc.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BpcFormat.Write not implemented");
        }
    }

    internal sealed class BpcReader
    {
        IBinaryStream   m_input;
        int             m_width;
        int             m_height;
        int             m_bpp;
        byte[]          m_output;

        public BitmapPalette Palette { get; private set; }
        public PixelFormat    Format { get; private set; }
        public byte[]           Data { get { return m_output; } }
        public int            Stride { get { return m_width * m_bpp / 8; } }

        public BpcReader (IBinaryStream input, ImageMetaData info)
        {
            m_input = input;
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            m_bpp = info.BPP;
        }

        public void Unpack ()
        {
            m_input.Position = m_input.Signature;
            if (m_bpp <= 8)
                Palette = ImageFormat.ReadPalette (m_input.AsStream, 1 << m_bpp);
            switch (m_bpp)
            {
            case 1: Unpack1bpp(); break;
            case 8: Unpack8bpp(); break;
            case 24: Unpack24bpp(); break;
            default: throw new InvalidFormatException();
            }
        }

        void Unpack1bpp ()
        {
            Format = PixelFormats.Indexed1;
            m_output = m_input.ReadBytes (m_height * Stride);
        }

        void Unpack8bpp ()
        {
            Format = PixelFormats.Indexed8;
            m_output = new byte[m_height * m_width];

            int packed_size = m_input.ReadInt32();
            byte index = 0;
            byte ctl = m_input.ReadUInt8();
            if (0xF5 == ctl)
                index = m_input.ReadUInt8();
            var input = m_input.ReadBytes (packed_size);
            if (input.Length != packed_size)
                throw new InvalidFormatException();
            int src = 0;
            int dst = 0;
            while (src < packed_size)
            {
                if (input[src] != ctl)
                {
                    m_output[dst++] = input[src++];
                }
                else if (ctl != 0xF5)
                {
                    int count = input[src+1];
                    for (int i = 0; i < count; ++i)
                        m_output[dst++] = input[src-1];
                    src += 2;
                }
                else if (input[src+1] == index)
                {
                    int count = input[src+2];
                    for (int i = 0; i < count; ++i)
                        m_output[dst++] = input[src-1];
                    src += 3;
                }
                else
                {
                    m_output[dst++] = input[src++];
                }
            }
        }

        void Unpack24bpp ()
        {
            Format = PixelFormats.Bgr24;
            m_output = new byte[m_height * Stride];

            var plane_size = new int[3];
            plane_size[0] = m_input.ReadInt32();
            plane_size[1] = m_input.ReadInt32();
            plane_size[2] = m_input.ReadInt32();
            var ctl   = m_input.ReadBytes (3);
            var pixel = m_input.ReadBytes (3);
            var input = m_input.ReadBytes (plane_size.Sum());
            int src = 0;
            for (int plane = 0; plane < 3; ++plane)
            {
                int dst = plane;
                for (int p = 0; p < plane_size[plane]; ++p)
                {
                    if (ctl[plane] != input[src])
                    {
                        m_output[dst] = input[src++];
                        dst += 3;
                    }
                    else if (ctl[plane] != 0xF5)
                    {
                        int count = input[src + 1];
                        for (int i = 0; i < count; ++i)
                        {
                            m_output[dst] = input[src - 1];
                            dst += 3;
                        }
                        ++p;
                        src += 2;
                    }
                    else if (pixel[plane] == input[src + 1])
                    {
                        int count = input[src + 2];
                        for (int i = 0; i < count; ++i)
                        {
                            m_output[dst] = input[src - 1];
                            dst += 3;
                        }
                        p += 2;
                        src  += 3;
                    }
                    else
                    {
                        m_output[dst] = input[src++];
                        dst += 3;
                    }
                }
            }
        }
    }
}
