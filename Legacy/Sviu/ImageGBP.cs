//! \file       ImageGBP.cs
//! \date       2018 Aug 27
//! \brief      SVIU System image format.
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

namespace GameRes.Formats.Sviu
{
    internal class GbpMetaData : ImageMetaData
    {
        public int  HeaderSize;
        public int  Method;
    }

    [Export(typeof(ImageFormat))]
    public class GbpFormat : ImageFormat
    {
        public override string         Tag { get { return "GBP"; } }
        public override string Description { get { return "SVIU system image format"; } }
        public override uint     Signature { get { return 0x50425947; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            file.Seek (-0x13, SeekOrigin.End);
            var key = file.ReadBytes (0x13);
            for (int i = 4; i < 0x14; i += 2)
            {
                header[i]   ^= key[0x10];
                header[i+1] ^= key[0x11];
            }
            for (int i = 0; i < 0x10; ++i)
            {
                header[i+4] -= key[i];
            }
            return new GbpMetaData {
                Width  = header.ToUInt16 (0xE),
                Height = header.ToUInt16 (0x10),
                BPP    = header.ToUInt16 (0x12),
                HeaderSize = header.ToInt32 (4),
                Method = header.ToUInt16 (0xC),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new GbpReader (file, (GbpMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GbpFormat.Write not implemented");
        }
    }

    internal class GbpReader
    {
        IBinaryStream   m_input;
        GbpMetaData     m_info;
        byte[]          m_output;

        public PixelFormat Format { get; private set; }

        public GbpReader (IBinaryStream input, GbpMetaData info)
        {
            m_input = input;
            m_info = info;
            if (32 == info.BPP)
                Format = PixelFormats.Bgra32;
            else
                Format = PixelFormats.Bgr32;
            m_output = new byte[4 * (int)m_info.Width * (int)m_info.Height];
        }

        public byte[] Unpack ()
        {
            m_input.Position = m_info.HeaderSize;
            if (3 == m_info.Method)
                UnpackV3();
            else
                throw new NotImplementedException();
            return m_output;
        }

        void UnpackV3 ()
        {
            throw new NotImplementedException();
        }
    }
}
