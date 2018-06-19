//! \file       ImageMTL.cs
//! \date       2018 May 26
//! \brief      Zyx image format.
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
using GameRes.Utility;

namespace GameRes.Formats.Zyx
{
    internal class MtlMetaData : ImageMetaData
    {
        public long     DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class MtlFormat : ImageFormat
    {
        public override string         Tag { get { return "MTL"; } }
        public override string Description { get { return "Zyx image format"; } }
        public override uint     Signature { get { return 0x4154454D; } } // 'METAL'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x2C);
            if (!header.AsciiEqual ("METAL") || header.ToInt32 (0x10) != 0x28 || header[0x15] == 0)
                return null;
            int name_length = header.ToInt32 (0x28);
            if (name_length <= 0)
                return null;
            var name = file.ReadCString (name_length);
            if (file.ReadInt32() != 0xC)
                return null;
            int frame_count = file.ReadInt32();
            if (!ArchiveFormat.IsSaneCount (frame_count))
                return null;
            var data_pos = file.Position + frame_count * 0x18;
            return new MtlMetaData {
                Width  = header.ToUInt32 (0x20),
                Height = header.ToUInt32 (0x24),
                BPP    = 32,
                DataOffset = data_pos,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new MtlReader (file, (MtlMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MtlFormat.Write not implemented");
        }
    }

    internal class MtlReader
    {
        IBinaryStream   m_input;
        MtlMetaData     m_info;
        byte[]          m_output;

        public PixelFormat Format { get; private set; }
        public byte[]        Data { get { return m_output; } }

        public MtlReader (IBinaryStream input, MtlMetaData info)
        {
            m_input = input;
            m_info = info;
            m_output = new byte[m_info.Width * m_info.Height * 4];
            Format = PixelFormats.Bgr32;
        }

        public byte[] Unpack ()
        {
            int stride = (int)m_info.Width * 4;
            var offsets = new int[] { 4, stride, stride + 4, stride - 4 };

            m_input.Position = m_info.DataOffset;
            int dst = 0;
            while (dst < m_output.Length)
            {
                byte ctl = m_input.ReadUInt8();
                int count = 0;
                if (0 == (ctl & 0x80))
                {
                    count = (ctl & 0x7F) + 1;
                    int pos = 0;
                    for (int i = 0; i < count; ++i)
                    {
                        m_input.Read (m_output, dst + pos, 3);
                        pos += 4;
                    }
                }
                else if (0x80 == (ctl & 0xC0))
                {
                    count = (ctl & 0x3F) + 1;
                }
                else if (0xC0 == (ctl & 0xE0))
                {
                    count = (ctl & 0x1F) + 1;
                    m_input.Read (m_output, dst, 3);
                    Binary.CopyOverlapped (m_output, dst, dst + 4, count * 4);
                    ++count;
                }
                else if (0xE0 == (ctl & 0xF0))
                {
                    count = 1;
                    int offset = offsets[ctl & 3];
                    Buffer.BlockCopy (m_output, dst - offset, m_output, dst, 4);
                }
                else if (0xF0 == (ctl & 0xF0))
                {
                    int offset = 0;
                    if (0 != (ctl & 1))
                    {
                        offset = m_input.ReadUInt16();
                    }
                    else
                    {
                        offset = m_input.ReadUInt8();
                    }
                    if (8 == (ctl & 8))
                    {
                        if (0 != (ctl & 2))
                        {
                            count = m_input.ReadUInt16();
                        }
                        else
                        {
                            count = m_input.ReadUInt8();
                        }
                    }
                    ++offset;
                    ++count;
                    Binary.CopyOverlapped (m_output, dst - offset * 4, dst, count * 4);
                }
                dst += count * 4;
            }
            return m_output;
        }
    }
}
