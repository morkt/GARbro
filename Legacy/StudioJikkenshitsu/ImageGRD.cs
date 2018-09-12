//! \file       ImageGRD.cs
//! \date       2018 Jul 27
//! \brief      Studio Jikkenshitsu image format.
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

// [030411][Studio Jikkenshitsu] Giin Oyako

namespace GameRes.Formats.Jikkenshitsu
{
    internal class GrdMetaData : ImageMetaData
    {
        public int  PackedLength;
        public int  AlphaLength;
        public bool IsEncrypted;
    }

    [Export(typeof(ImageFormat))]
    public class GrdFormat : ImageFormat
    {
        public override string         Tag { get { return "GRD/SJ"; } }
        public override string Description { get { return "Studio Jikkenshitsu image format"; } }
        public override uint     Signature { get { return 0x20445247; } } // 'GRD '

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            return new GrdMetaData {
                Width  = header.ToUInt16 (6),
                Height = header.ToUInt16 (8),
                BPP    = header[4],
                PackedLength = header.ToInt32 (0xC),
                AlphaLength  = header.ToInt32 (0x10),
                IsEncrypted  = (header[5] & 0x80) != 0,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new GrdReader (file, (GrdMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GrdFormat.Write not implemented");
        }
    }

    internal class GrdReader
    {
        IBinaryStream   m_input;
        GrdMetaData     m_info;
        byte[]          m_output;

        public PixelFormat Format { get; private set; }

        public GrdReader (IBinaryStream input, GrdMetaData info)
        {
            m_input = input;
            m_info = info;
            if (8 == m_info.BPP)
                Format = PixelFormats.Gray8;
            else if (24 == m_info.BPP)
                Format = PixelFormats.Bgr24;
            else
                throw new InvalidFormatException();
            int image_size = (int)m_info.Width * (int)m_info.Height;
            if (m_info.BPP >= 8)
                image_size *= (m_info.BPP + 1) / 8;
            else
                image_size = image_size * m_info.BPP / 8;
            m_output = new byte[image_size];
        }

        public byte[] Unpack ()
        {
            m_input.Position = 0x18;
            var input = m_input.ReadBytes (m_info.PackedLength);
            if (m_info.IsEncrypted)
                DecryptData (input, 0, input.Length & -8);
            using (var mem = new MemoryStream (input))
            using (var lzss = new LzssStream (mem))
                lzss.Read (m_output, 0, m_output.Length);
            if (m_info.AlphaLength > 0)
            {
                var alpha = new byte[m_info.AlphaLength];
                using (var lzss = new LzssStream (m_input.AsStream, LzssMode.Decompress, true))
                    lzss.Read (alpha, 0, alpha.Length);
            }
            return m_output;
        }

        void DecryptData (byte[] data, int pos, int length)
        {
            for (int i = 0; i < length; i += 8)
            {
                DecryptBlock (data, pos + i);
            }
        }

        void DecryptBlock (byte[] data, int pos)
        {
        }
    }
}
