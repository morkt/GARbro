//! \file       ImageCP2.cs
//! \date       2018 Sep 27
//! \brief      Aquarium image format.
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
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Aquarium
{
    internal class Cp2MetaData : ImageMetaData
    {
        public int  Flags;
        
        public bool IsCompressed { get { return (Flags & 0x0F) != 0; } }
        public bool     HasAlpha { get { return (Flags & 0x20) != 0; } }
    }

    [Export(typeof(ImageFormat))]
    public class Cp2Format : ImageFormat
    {
        public override string         Tag { get { return "CP2"; } }
        public override string Description { get { return "Aquarium image format"; } }
        public override uint     Signature { get { return 0x325043; } } // 'CP2'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            return new Cp2MetaData {
                Width  = header.ToUInt32 (4),
                Height = header.ToUInt32 (8),
                BPP    = header.ToInt32 (0x0C),
                Flags  = header.ToInt32 (0x14),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new Cp2Reader (file, (Cp2MetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Cp2Format.Write not implemented");
        }
    }

    internal class Cp2Reader
    {
        IBinaryStream   m_input;
        Cp2MetaData     m_info;
        byte[]          m_output;

        public BitmapPalette Palette { get; private set; }
        public PixelFormat    Format { get; private set; }
        public int            Stride { get; private set; }

        public Cp2Reader (IBinaryStream input, Cp2MetaData info)
        {
            m_input = input;
            m_info = info;
            switch (info.BPP)
            {
            case 8:  Format = PixelFormats.Indexed8; break;
            case 24: Format = PixelFormats.Bgr24; break;
            case 32: Format = PixelFormats.Bgr32; break;
            default: throw new InvalidFormatException();
            }
            Stride = (((int)info.Width + 31) & ~31) * (info.BPP / 8);
            m_output = new byte[Stride * (int)info.Height];
        }

        public ImageData Unpack ()
        {
            m_input.Position = 0x20;
            if (8 == m_info.BPP)
                Palette = ImageFormat.ReadPalette (m_input.AsStream);
            if (m_info.IsCompressed)
                DecompressLz (m_input, m_output);
            else
                m_input.Read (m_output, 0, m_output.Length);

            if (m_info.HasAlpha)
            {
                int stride = Stride / (m_info.BPP / 8);
                var alpha = new byte[stride * (int)m_info.Height];
                if (m_info.IsCompressed)
                    DecompressLz (m_input, alpha);
                else
                    m_input.Read (alpha, 0, alpha.Length);
                return ApplyAlpha (alpha, stride);
            }
            return ImageData.CreateFlipped (m_info, Format, Palette, m_output, Stride);
        }

        ImageData ApplyAlpha (byte[] alpha, int alpha_stride)
        {
            int width = (int)m_info.Width;
            int dst_stride = width * 4;
            var pixels = new byte[dst_stride * (int)m_info.Height];
            int src = m_output.Length - Stride;
            int dst = 0;
            int asrc = 0;
            while (src >= 0)
            {
                int i = 0;
                for (int x = 0; x < width; ++x)
                {
                    if (8 == m_info.BPP)
                    {
                        var color = Palette.Colors[m_output[src+i++]];
                        pixels[dst++] = color.B;
                        pixels[dst++] = color.G;
                        pixels[dst++] = color.R;
                    }
                    else if (16 == m_info.BPP)
                    {
                        var color = m_output.ToUInt16 (src+i);
                        pixels[dst++] = (byte)((color & 0x1F) * 0xFF / 0x1F);
                        pixels[dst++] = (byte)(((color >> 5)  & 0x1F) * 0xFF / 0x1F);
                        pixels[dst++] = (byte)(((color >> 10) & 0x1F) * 0xFF / 0x1F);
                        i += 2;
                    }
                    else
                    {
                        pixels[dst++] = m_output[src+i++];
                        pixels[dst++] = m_output[src+i++];
                        pixels[dst++] = m_output[src+i++];
                    }
                    pixels[dst++] = alpha[asrc+x];
                }
                src -= Stride;
                asrc += alpha_stride;
            }
            return ImageData.Create (m_info, PixelFormats.Bgra32, null, pixels, dst_stride);
        }

        internal static void DecompressLz (IBinaryStream input, byte[] output)
        {
            int remaining = input.ReadInt32();
            input.ReadInt32();
            int dst = 0;
            while (dst < output.Length && remaining > 0)
            {
                int b = input.ReadByte();
                if (-1 == b)
                    break;
                if (b != 0)
                {
                    output[dst++] = (byte)b;
                    --remaining;
                }
                else
                {
                    int count = input.ReadByte();
                    if (count != 0)
                    {
                        int offset = input.ReadUInt16();
                        Binary.CopyOverlapped (output, dst - offset, dst, count);
                        dst += count;
                        remaining -= 4;
                    }
                    else
                    {
                        output[dst++] = 0;
                        remaining -= 2;
                    }
                }
            }
        }
    }
}
