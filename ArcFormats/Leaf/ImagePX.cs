//! \file       ImagePX.cs
//! \date       Thu Aug 18 05:35:34 2016
//! \brief      Leaf image format.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Leaf
{
    internal class PxMetaData : ImageMetaData
    {
        public int  Type;
        public int  FrameCount;
        public int  BlockSize;
        public int  BlocksWidth;
        public int  BlocksHeight;
    }

    // XXX this format changes significantly from game to game

    [Export(typeof(ImageFormat))]
    public class PxFormat : ImageFormat
    {
        public override string         Tag { get { return "PX"; } }
        public override string Description { get { return "Leaf image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x20);
            int type = header.ToUInt16 (0x10);
            if (0x0C == type)
            {
                int count = header.ToInt32 (0);
                if (!ArchiveFormat.IsSaneCount (count))
                    return null;
                int block_size = header.ToInt32 (4);
                if (block_size <= 0)
                    return null;
                int  bpp    = header.ToUInt16 (0x12);
                uint width  = header.ToUInt16 (0x14);
                uint height = header.ToUInt16 (0x16);
                if (bpp != 32 || 0 == width || 0 == height)
                    return null;
                return new PxMetaData
                {
                    Width = width,
                    Height = height,
                    BPP = bpp,
                    Type = type,
                    FrameCount = count,
                    BlockSize = block_size,
                    BlocksWidth = header.ToUInt16 (0x1C),
                    BlocksHeight = header.ToUInt16 (0x1E),
                };
            }
            else if (0x80 == type || 0x90 == type)
            {
                if (!header.AsciiEqual (0x14, "Leaf"))
                    return null;
                int count = header.ToInt32 (4);
                if (!ArchiveFormat.IsSaneCount (count))
                    return null;
                var header_ex = stream.ReadBytes (0x20);
                if (0x20 != header_ex.Length)
                    return null;
                if (0x0A != LittleEndian.ToUInt16 (header_ex, 0x10))
                    return null;
                return new PxMetaData
                {
                    Width   = LittleEndian.ToUInt32 (header_ex, 0),
                    Height  = LittleEndian.ToUInt32 (header_ex, 0),
                    BPP     = LittleEndian.ToUInt16 (header_ex, 0x12),
                    Type    = type,
                    FrameCount = count,
                };
            }
            return null;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var reader = new PxReader (stream, (PxMetaData)info))
            {
                var pixels = reader.Unpack();
                return ImageData.Create (info, reader.Format, null, pixels, reader.Stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PxFormat.Write not implemented");
        }
    }

    internal sealed class PxReader : IDisposable
    {
        IBinaryStream   m_input;
        PxMetaData      m_info;
        byte[]          m_output;
        int             m_pixel_size;
        int             m_stride;

        public PixelFormat Format { get { return PixelFormats.Bgra32; } }
        public byte[]        Data { get { return m_output; } }
        public int         Stride { get { return m_stride; } }
        public int     FrameCount { get { return m_info.FrameCount; } }

        public PxReader (IBinaryStream input, PxMetaData info)
        {
            m_input = input;
            m_info = info;
            m_pixel_size = m_info.BPP / 8;
            m_stride = (int)m_info.Width * m_pixel_size;
            m_output = new byte[m_stride * (int)m_info.Height];
        }

        public byte[] Unpack (int frame = 0)
        {
            if (frame < 0 || frame >= FrameCount)
                throw new ArgumentException ("[PX] Invalid frame number", "frame");
            switch (m_info.Type)
            {
            case 0x0C:  Unpack0C (frame); break;
            case 0x90:  Unpack90 (frame); break;
            case 0x80:  Unpack80 (frame); break;
            default:    throw new NotImplementedException();
            }
            return m_output;
        }

        void Unpack0C (int frame)
        {
            int block_count = m_info.BlocksWidth * m_info.BlocksHeight;
            var block_table = new ushort[block_count];
            m_input.Position = 0x20 + frame * block_count * 2;
            for (int i = 0; i < block_count; ++i)
                block_table[i] = m_input.ReadUInt16();
            int data_pos = 0x20 + FrameCount * block_count * 2;
            int block_length = 2 + (m_info.BlockSize + 2) * (m_info.BlockSize + 2) * m_pixel_size;
            int current_block = 0;
            int dst_line = 0;
            for (int by = 0; by < m_info.BlocksHeight; ++by)
            {
                for (int bx = 0; bx < m_info.BlocksWidth; ++bx)
                {
                    int dst = dst_line + bx * m_info.BlockSize * m_pixel_size;
                    int block_num = block_table[current_block++];
                    if (block_num != 0)
                    {
                        m_input.Position = data_pos + (block_num - 1) * block_length;
                        int block_width  = m_input.ReadByte() - 2;
                        int block_height = m_input.ReadByte() - 2;
                        int line_length = block_width * m_pixel_size;
                        for (int y = 0; y < block_height; ++y)
                        {
                            m_input.Read (m_output, dst, line_length);
                            dst += m_stride;
                            m_input.Seek (8, SeekOrigin.Current);
                        }
                    }
                }
                dst_line += m_info.BlockSize * m_stride;
            }
        }

        void Unpack90 (int frame)
        {
            m_input.Position = 0x40 + frame * (0x20 + m_output.Length);
            if (m_output.Length != m_input.Read (m_output, 0, m_output.Length))
                throw new EndOfStreamException();
        }

        void Unpack80 (int frame)
        {
            throw new NotImplementedException();
        }

        #region IDisposable Members
        public void Dispose ()
        {
        }
        #endregion
    }
}
