//! \file       ImageHG2.cs
//! \date       Sun Nov 29 06:33:49 2015
//! \brief      CatSystem HG2 image format implementation.
//
// Copyright (C) 2015 by morkt
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

namespace GameRes.Formats.CatSystem
{
    internal class Hg2MetaData : HgMetaData
    {
        public int  Version;
        public int  DataPacked;
        public int  DataUnpacked;
        public int  CtlPacked;
        public int  CtlUnpacked;
        public int  Depth;
    }

    [Export(typeof(ImageFormat))]
    public class Hg2Format : ImageFormat
    {
        public override string         Tag { get { return "HG2"; } }
        public override string Description { get { return "CatSystem engine image format"; } }
        public override uint     Signature { get { return 0x322D4748; } } // 'HG-2'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Position = 8;
            var info = new Hg2MetaData();
            int version = stream.ReadInt32();
            if (0x25 == version)
                info.HeaderSize = 0x58;
            else if (0x20 == version)
                info.HeaderSize = 0x50;
            else if (0x10 == version)
                info.HeaderSize = 0x30;
            else
                return null;
            info.Version = version;
            info.Width  = stream.ReadUInt32();
            info.Height = stream.ReadUInt32();
            info.BPP    = stream.ReadInt16();
            info.Depth  = stream.ReadInt16();
            stream.Seek (8, SeekOrigin.Current);
            info.DataPacked     = stream.ReadInt32();
            info.DataUnpacked   = stream.ReadInt32();
            info.CtlPacked      = stream.ReadInt32();
            info.CtlUnpacked    = stream.ReadInt32();
            if (info.HeaderSize > 0x30)
            {
                stream.Seek (8, SeekOrigin.Current);
                info.CanvasWidth    = stream.ReadUInt32();
                info.CanvasHeight   = stream.ReadUInt32();
                info.OffsetX        = stream.ReadInt32();
                info.OffsetY        = stream.ReadInt32();
            }
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var reader = new Hg2Reader (stream, (Hg2MetaData)info))
                return reader.Image;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Hg2Format.Write not implemented");
        }
    }

    internal sealed class Hg2Reader : HgReader
    {
        Hg2MetaData     m_hg2;

        public override ImageData Image
        {
            get { return m_image ?? (m_image = CreateImage()); }
        }

        public Hg2Reader (IBinaryStream input, Hg2MetaData info) : base (input, info)
        {
            m_hg2 = info;
        }

        ImageData CreateImage ()
        {
            var pixels = UnpackStream (m_hg2.HeaderSize, m_hg2.DataPacked, m_hg2.DataUnpacked,
                                       m_hg2.CtlPacked, m_hg2.CtlUnpacked);
            var format = 24 == m_hg2.BPP ? PixelFormats.Bgr24 : PixelFormats.Bgra32;
            if (m_hg2.Version > 0x10)
                return ImageData.CreateFlipped (m_hg2, format, null, pixels, Stride);

            if (m_hg2.Depth > 0 && m_hg2.Depth < 8)
            {
                byte max = (byte)((1 << m_hg2.Depth) - 1);
                for (int i = 0; i < pixels.Length; i += m_pixel_size)
                {
                    pixels[i  ] = (byte)(pixels[i  ] * 0xFF / max);
                    pixels[i+1] = (byte)(pixels[i+1] * 0xFF / max);
                    pixels[i+2] = (byte)(pixels[i+2] * 0xFF / max);
                }
            }
            if (32 == m_hg2.BPP)
            {
                for (int i = 3; i < pixels.Length; i += 4)
                    pixels[i] ^= 0xFF;
            }
            return ImageData.Create (m_hg2, format, null, pixels, Stride);
        }
    }
}
