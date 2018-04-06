//! \file       ImageCHD.cs
//! \date       2017 Dec 09
//! \brief      Forest image format.
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
using System.Windows.Media;

namespace GameRes.Formats.Forest
{
    internal class ChdMetaData : ImageMetaData
    {
        public uint FirstOffset;
    }

    /// <summary>
    /// ShiinaRio images predecessor.
    /// </summary>
    [Export(typeof(ImageFormat))]
    public class ChdFormat : ImageFormat
    {
        public override string         Tag { get { return "CHD"; } }
        public override string Description { get { return "Forest image format"; } }
        public override uint     Signature { get { return 0x444843; } } // 'CHD'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 4;
            int count = file.ReadInt32();
            if (count < 0 || count > 0xFFFFF)
                return null;
            file.ReadInt32();
            uint first_offset = 0;
            for (int i = 0; i < count && 0 == first_offset; ++i)
                first_offset = file.ReadUInt32();
            if (0 == first_offset)
                return null;
            file.Position = first_offset;
            var info = new ChdMetaData();
            info.Width = file.ReadUInt32();
            info.Height = file.ReadUInt32();
            info.OffsetX = file.ReadInt32();
            info.OffsetY = file.ReadInt32();
            info.FirstOffset = first_offset+0x10;
            info.BPP = 32;
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new ChdReader (file, (ChdMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, PixelFormats.Gray8, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ChdFormat.Write not implemented");
        }
    }

    internal class ChdReader
    {
        IBinaryStream   m_input;
        int             m_width;
        int             m_height;
        uint            m_origin;

        public ChdReader (IBinaryStream input, ChdMetaData info)
        {
            m_input = input;
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            m_origin = info.FirstOffset;
        }

        public byte[] Unpack ()
        {
            m_input.Position = m_origin;
            var rows = new uint[m_height];
            for (int i = 0; i < m_height; ++i)
                rows[i] = m_input.ReadUInt32();
            var output = new byte[m_height*m_width];
            for (int y = 0; y < m_height; ++y)
            {
                m_input.Position = rows[y];
                int dst = y * m_width;
                for (int w = m_width; w > 0; )
                {
                    int count = m_input.ReadUInt8();
                    if (0xFF == count)
                        count = m_input.ReadUInt16();
                    int x = w - count;
                    if (0 == x)
                        break;
                    count = m_input.ReadUInt8();
                    if (0 == count)
                        count = m_input.ReadUInt16();
                    m_input.Read (output, dst, count);
                    for (int i = 0; i < count; ++i)
                        output[dst+i] += 0x57;
                    dst += count;
                    w = x - count;
                }
            }
            return output;
        }
    }
}
