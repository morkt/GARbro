//! \file       ImageYCG.cs
//! \date       Fri Dec 18 06:51:10 2015
//! \brief      YU-RIS compressed image.
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

using GameRes.Compression;
using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.YuRis
{
    internal class YcgMetaData : ImageMetaData
    {
        public int  CompressionMethod;
        public int  CompressedSize1;
        public int  CompressedSize2;
        public int  UnpackedSize1;
        public int  UnpackedSize2;
    }

    [Export(typeof(ImageFormat))]
    public class YcgFormat : ImageFormat
    {
        public override string         Tag { get { return "YCG"; } }
        public override string Description { get { return "YU-RIS compressed image format"; } }
        public override uint     Signature { get { return 0x474359; } } // 'YCG'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x38];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            return new YcgMetaData
            {
                Width   = LittleEndian.ToUInt32 (header, 4),
                Height  = LittleEndian.ToUInt32 (header, 8),
                BPP     = LittleEndian.ToInt32 (header, 12),
                CompressionMethod   = LittleEndian.ToInt32 (header, 0x10),
                UnpackedSize1       = LittleEndian.ToInt32 (header, 0x20),
                CompressedSize1     = LittleEndian.ToInt32 (header, 0x24),
                UnpackedSize2       = LittleEndian.ToInt32 (header, 0x30),
                CompressedSize2     = LittleEndian.ToInt32 (header, 0x34),
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var reader = new YcgReader (stream, (YcgMetaData)info);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, null, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("YcgFormat.Write not implemented");
        }
    }

    internal sealed class YcgReader
    {
        Stream          m_input;
        YcgMetaData     m_info;
        byte[]          m_output;

        public PixelFormat Format { get; private set; }
        public byte[]        Data { get { return m_output; } }

        public YcgReader (Stream input, YcgMetaData info)
        {
            m_input = input;
            m_info = info;
            int stride = (int)m_info.Width * 4;
            m_output = new byte[stride * (int)m_info.Height];
            Format = PixelFormats.Bgra32;
        }

        public void Unpack ()
        {
            if (1 == m_info.CompressionMethod)
                UnpackZlib();
            else if (2 == m_info.CompressionMethod)
                throw new NotImplementedException ("YSSnp compression not implemented");
            else
                throw new InvalidFormatException ("Unknown YCG compression method");
        }

        void UnpackZlib ()
        {
            m_input.Position = 0x38;
            using (var z = new ZLibStream (m_input, CompressionMode.Decompress, true))
                if (m_info.UnpackedSize1 != z.Read (m_output, 0, m_info.UnpackedSize1))
                    throw new EndOfStreamException();
            m_input.Position = 0x38 + m_info.CompressedSize1;
            using (var z = new ZLibStream (m_input, CompressionMode.Decompress, true))
                if (m_info.UnpackedSize2 != z.Read (m_output, m_info.UnpackedSize1, m_info.UnpackedSize2))
                    throw new EndOfStreamException();
        }
    }
}
