//! \file       ImageGEM.cs
//! \date       2018 Sep 04
//! \brief      Wing image format.
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

// [030912][Wing] Minato Gensou ~Venice Fantastica~
// [031010][Hachimitsu] Yori Shiro

namespace GameRes.Formats.Wing
{
    internal class GemMetaData : ImageMetaData
    {
        public int  Method;
        public int  Alpha;
    }

    [Export(typeof(ImageFormat))]
    public class GemFormat : ImageFormat
    {
        public override string         Tag { get { return "GEM"; } }
        public override string Description { get { return "Wing image format"; } }
        public override uint     Signature { get { return 0; } }

        public GemFormat ()
        {
            Signatures = new uint[] { 0x14000A, 0x32000A };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            int alpha = header.ToInt16 (2);
            int method = header.ToInt16 (4);
            if (method != 0x64 && method != 0xE6)
                return null;
            int bpp = header.ToInt16 (6);
            if (bpp != 32)
                return null;
            return new GemMetaData {
                Width = header.ToUInt16 (10),
                Height = header.ToUInt16 (8),
                BPP = bpp,
                Method = method,
                Alpha = alpha,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new GemReader (file, (GemMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, null, pixels, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GemFormat.Write not implemented");
        }
    }

    internal class GemReader
    {
        IBinaryStream   m_input;
        GemMetaData     m_info;
        int             m_stride;
        int             m_height;

        public PixelFormat Format { get; private set; }
        public int         Stride { get { return m_stride; } }

        public GemReader (IBinaryStream input, GemMetaData info)
        {
            m_input = input;
            m_info = info;
            m_stride = (int)info.Width * 4;
            m_height = (int)info.Height;
            Format = m_info.Alpha == 50 ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
        }

        public byte[] Unpack ()
        {
            m_input.Position = 0x10;
            var pixels = new byte[m_stride * m_height];
            using (var input = new LzssStream (m_input.AsStream, LzssMode.Decompress, true))
                input.Read (pixels, 0, pixels.Length);
            if (0x64 == m_info.Method)
                RestoreRGB (pixels);
            if (50 == m_info.Alpha)
                RestoreAlpha (pixels);
            return pixels;
        }

        void RestoreRGB (byte[] pixels)
        {
            for (int y = 1; y < m_height; ++y)
            {
                int dst = y * m_stride + 4;
                for (int x = 4; x < m_stride; x += 4)
                {
                    int b = pixels[dst - 4] + pixels[dst - m_stride    ] - pixels[dst - m_stride - 4];
                    int g = pixels[dst - 3] + pixels[dst - m_stride + 1] - pixels[dst - m_stride - 3];
                    int r = pixels[dst - 2] + pixels[dst - m_stride + 2] - pixels[dst - m_stride - 2];
                    pixels[dst  ] += (byte)b;
                    pixels[dst+1] += (byte)g;
                    pixels[dst+2] += (byte)r;
                    dst += 4;
                }
            }
        }

        void RestoreAlpha (byte[] pixels)
        {
            for (int src = 3; src < pixels.Length; src += 4)
            {
                pixels[src] ^= 0xFF;
            }
        }
    }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "CGR")]
    [ExportMetadata("Target", "PSD")]
    public class CgrFormat : ResourceAlias
    {
    }
}
