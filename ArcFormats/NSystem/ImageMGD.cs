//! \file       ImageMGD.cs
//! \date       Thu Nov 24 14:37:18 2016
//! \brief      NSystem image format.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.NSystem
{
    internal class MgdMetaData : ImageMetaData
    {
        public int  DataOffset;
        public int  UnpackedSize;
        public int  Mode;
    }

    [Export(typeof(ImageFormat))]
    public class MgdFormat : ImageFormat
    {
        public override string         Tag { get { return "MGD/NSystem"; } }
        public override string Description { get { return "NSystem image format"; } }
        public override uint     Signature { get { return 0x2044474D; } } // 'MGD '

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x1C);
            int header_size = header.ToUInt16 (4);
            uint width      = header.ToUInt16 (0xC);
            uint height     = header.ToUInt16 (0xE);
            int unpacked_size = header.ToInt32 (0x10);
            int mode        = header.ToInt32 (0x18);
            if (mode < 0 || mode > 2)
                return null;
            return new MgdMetaData
            {
                Width = width,
                Height = height,
                BPP = 32,
                DataOffset = header_size,
                UnpackedSize = unpacked_size,
                Mode = mode,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (MgdMetaData)info;
            file.Position = meta.DataOffset;
            int data_size = file.ReadInt32();
            switch (meta.Mode)
            {
            case 0:
                {
                    var pixels = file.ReadBytes (data_size);
                    var format = PixelFormats.Bgr32;
                    for (int i = 3; i < data_size; i += 4)
                    {
                        if (pixels[i] != 0)
                        {
                            format = PixelFormats.Bgra32;
                            break;
                        }
                    }
                    return ImageData.Create (info, format, null, pixels);
                }

            case 1:
                {
                    var decoder = new MgdDecoder (file, meta, data_size);
                    decoder.Unpack();
                    return ImageData.Create (info, decoder.Format, null, decoder.Data);
                }

            case 2:
                using (var png = new StreamRegion (file.AsStream, file.Position, data_size, true))
                {
                    var decoder = new PngBitmapDecoder (png, 
                        BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames[0];
                    frame.Freeze();
                    return new ImageData (frame, info);
                }

            default:
                throw new InvalidFormatException();
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MgdFormat.Write not implemented");
        }
    }

    internal class MgdDecoder
    {
        IBinaryStream   m_input;
        MgdMetaData     m_info;
        byte[]          m_output;

        public byte[] Data { get { return m_output; } }

        public PixelFormat Format { get; private set; }

        public MgdDecoder (IBinaryStream input, MgdMetaData info, int packed_size)
        {
            m_input = input;
            m_info = info;
            m_output = new byte[m_info.UnpackedSize];
            Format = PixelFormats.Bgra32;
        }

        public void Unpack ()
        {
            int alpha_size = m_input.ReadInt32();
            if (!UnpackAlpha (alpha_size))
                Format = PixelFormats.Bgr32;
            int rgb_size = m_input.ReadInt32();
            UnpackColor (rgb_size);
        }

        bool UnpackAlpha (int length)
        {
            bool has_alpha = false;
            int dst = 3;
            while (length > 0)
            {
                int count = m_input.ReadInt16();
                length -= 2;
                if (count < 0)
                {
                    count = (count & 0x7FFF) + 1;
                    byte a = m_input.ReadUInt8();
                    has_alpha = has_alpha || a != 0;
                    length--;
                    for (int i = 0; i < count; ++i)
                    {
                        m_output[dst] = a;
                        dst += 4;
                    }
                }
                else
                {
                    for (int i = 0; i < count; ++i)
                    {
                        byte a = m_input.ReadUInt8();
                        has_alpha = has_alpha || a != 0;
                        m_output[dst] = a;
                        dst += 4;
                    }
                    length -= count;
                }
            }
            return has_alpha;
        }

        void UnpackColor (int length)
        {
            int dst = 0;
            while (length > 0)
            {
                int count = m_input.ReadUInt8();
                length--;
                switch (count & 0xC0)
                {
                case 0x80:
                    count &= 0x3F;
                    int b = m_output[dst-4];
                    int g = m_output[dst-3];
                    int r = m_output[dst-2];
                    for (int i = 0; i < count; ++i)
                    {
                        ushort delta = m_input.ReadUInt16();
                        length -= 2;
                        if (0 != (delta & 0x8000))
                        {
                            r += (delta >> 10) & 0x1F;
                            g += (delta >> 5) & 0x1F;
                            b += delta & 0x1F;
                        }
                        else
                        {
                            if (0 != (delta & 0x4000))
                                r -= (delta >> 10) & 0xF;
                            else
                                r += (delta >> 10) & 0xF;

                            if (0 != (delta & 0x0200))
                                g -= (delta >> 5) & 0xF;
                            else
                                g += (delta >> 5) & 0xF;

                            if (0 != (delta & 0x0010))
                                b -= delta & 0xF;
                            else
                                b += delta & 0xF;
                        }
                        m_output[dst  ] = (byte)b;
                        m_output[dst+1] = (byte)g;
                        m_output[dst+2] = (byte)r;
                        dst += 4;
                    }
                    break;

                case 0x40:
                    count &= 0x3F;
                    m_input.Read (m_output, dst, 3);
                    length -= 3;
                    int src = dst;
                    dst += 4;
                    for (int i = 0; i < count; ++i)
                    {
                        m_output[dst  ] = m_output[src  ];
                        m_output[dst+1] = m_output[src+1];
                        m_output[dst+2] = m_output[src+2];
                        dst += 4;
                    }
                    break;

                case 0:
                    for (int i = 0; i < count; ++i)
                    {
                        m_input.Read (m_output, dst, 3);
                        length -= 3;
                        dst += 4;
                    }
                    break;

                default:
                    throw new InvalidFormatException();
                }
            }
        }
    }
}
