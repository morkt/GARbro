//! \file       ImageLMG.cs
//! \date       2023 Aug 07
//! \brief      Cotton Club encrypted image.
//
// Copyright (C) 2023 by morkt
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

// [071231][Cotton Club] Kureuta

namespace GameRes.Formats.CottonClub
{
    public class LmgMetaData : ImageMetaData
    {
        public byte     Method;
    }

    [Export(typeof(ImageFormat))]
    public class LmgFormat : ImageFormat
    {
        public override string         Tag { get { return "LMG"; } }
        public override string Description { get { return "Cotton Club encrypted image"; } }
        public override uint     Signature { get { return 0x03474D4C; } } // 'LMG'

        public LmgFormat ()
        {
            Signatures = new uint[] { 0x03474D4C, 0x02474D4C, 0x01474D4C };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (12);
            return new LmgMetaData {
                Width = header.ToUInt32 (4),
                Height = header.ToUInt32 (8),
                BPP = header[3] == 2 ? 32 : 24,
                Method = header[3],
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (LmgMetaData)info;
            var data = new byte[file.Length - 12];
            file.Position = 12;
            file.Read (data, 0, data.Length);
            DecryptData (data, file.Name);
            if (3 == meta.Method)
            {
                using (var input = new BinMemoryStream (data))
                {
                    var decoder = new JpegBitmapDecoder (input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames[0];
                    frame.Freeze();
                    return new ImageData (frame, info);
                }
            }
            else if (2 == meta.Method)
            {
                var reader = new LmgReader (info, data);
                data = reader.Unpack();
                return ImageData.Create (info, PixelFormats.Bgra32, null, data, reader.Stride);
            }
            else
            {
                return ImageData.Create (info, PixelFormats.Bgr24, null, data, info.iWidth * 3);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("LmgFormat.Write not implemented");
        }

        internal void DecryptData (byte[] data, string filename)
        {
            filename = Path.GetFileName (filename).ToLowerInvariant();
            byte key = 0;
            for (int i = 0; i < filename.Length; ++i)
            {
                key ^= (byte)filename[i];
            }
            for (int i = 0; i < data.Length; ++i)
            {
                byte x = data[i];
                data[i] ^= key;
                key = x;
            }
        }
    }

    internal class LmgReader
    {
        byte[]          m_data;
        byte[]          m_output;
        int             m_stride;

        public int  Stride { get { return m_stride; } }

        public LmgReader (ImageMetaData info, byte[] data)
        {
            m_data = data;
            m_stride = info.iWidth * 4;
            m_output = new byte[m_stride * info.iHeight];
        }

        int m_src;
        int m_dst;

        public byte[] Unpack ()
        {
            m_src = 0;
            m_dst = 0;
            while (m_src+1 < m_data.Length)
            {
                byte alpha = m_data[m_src++];
                if (0xFF == alpha)
                {
                    int length = GetLength16();
                    for (int i = 0; i < length; ++i)
                    {
                        m_output[m_dst++] = m_data[m_src++];
                        m_output[m_dst++] = m_data[m_src++];
                        m_output[m_dst++] = m_data[m_src++];
                        m_output[m_dst++] = 0xFF;
                    }
                }
                else if (0 == alpha)
                {
                    int length = GetLength16();
                    m_dst += length * 4;
                }
                else
                {
                    ReadARGB (alpha);
                }
            }
            return m_output;
        }

        void ReadARGB (byte alpha)
        {
            int length = GetLength8();
            m_output[m_dst++] = m_data[m_src++];
            m_output[m_dst++] = m_data[m_src++];
            m_output[m_dst++] = m_data[m_src++];
            m_output[m_dst++] = alpha;
            while (--length > 0)
            {
                alpha = m_data[m_src++];
                m_output[m_dst++] = m_data[m_src++];
                m_output[m_dst++] = m_data[m_src++];
                m_output[m_dst++] = m_data[m_src++];
                m_output[m_dst++] = alpha;
            }
        }

        int GetLength8 ()
        {
            int i = 0;
            while (m_data[m_src] == 0)
            {
                i += 0xFF;
                ++m_src;
            }
            return i + m_data[m_src++];
        }

        int GetLength16 ()
        {
            int i = 0;
            while (m_data[m_src] == 0 && m_data[m_src+1] == 0)
            {
                i += 0xFFFF;
                m_src += 2;
            }
            i += m_data.ToUInt16 (m_src);
            m_src += 2;
            return i;
        }
    }
}
