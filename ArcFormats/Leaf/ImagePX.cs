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
using System.Windows.Media.Imaging;
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
            else if (0x90 == type)
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
                    Height  = LittleEndian.ToUInt32 (header_ex, 4),
                    BPP     = LittleEndian.ToUInt16 (header_ex, 0x12),
                    Type    = type,
                    FrameCount = count,
                };
            }
            else if (0x40 == type || 0x44 == type)
            {
                int count = header.ToInt32 (0);
                if (!ArchiveFormat.IsSaneCount (count))
                    return null;
                return new PxMetaData
                {
                    Width   = header.ToUInt32 (0x14),
                    Height  = header.ToUInt32 (0x18),
                    Type    = 0x40,
                    BPP     = 32,
                    FrameCount = count,
                };
            }
            else if (1 == type || 4 == type || 7 == type)
            {
                int bpp = header.ToUInt16 (0x12);
                if (bpp != 32 && bpp != 8)
                    return null;
                return new PxMetaData
                {
                    Width   = header.ToUInt32 (0x14),
                    Height  = header.ToUInt32 (0x18),
                    Type    = type,
                    BPP     = bpp,
                    FrameCount = 1,
                };
            }
            return null;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var reader = new PxReader (stream, (PxMetaData)info))
            {
                var pixels = reader.Unpack();
                return ImageData.Create (info, reader.Format, reader.Palette, pixels, reader.Stride);
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

        public PixelFormat Format { get; private set; }
        public byte[]        Data { get { return m_output; } }
        public int         Stride { get { return m_stride; } }
        public int     FrameCount { get { return m_info.FrameCount; } }
        public BitmapPalette Palette { get; private set; }

        public PxReader (IBinaryStream input, PxMetaData info)
        {
            m_input = input;
            m_info = info;
            m_pixel_size = m_info.BPP / 8;
            m_stride = (int)m_info.Width * m_pixel_size;
            m_output = new byte[m_stride * (int)m_info.Height];
            if (1 == m_pixel_size)
                Format = PixelFormats.Gray8;
            else
                Format = PixelFormats.Bgra32;
        }

        public byte[] Unpack (int frame = 0)
        {
            if (frame < 0 || frame >= FrameCount)
                throw new ArgumentException ("[PX] Invalid frame number", "frame");
            switch (m_info.Type)
            {
            case 0x0C:  Unpack0C (frame); break;
            case 0x90:  Unpack90 (frame); break;
            case 0x40:  Unpack40(); break;
            default:    ReadBlock (0); break;
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

        void Unpack40 ()
        {
            m_input.Position = 0x20;
            uint data_offset = 0x20 + (uint)m_info.FrameCount * 4;
            var offsets = new uint[m_info.FrameCount];
            for (int i = 0; i < offsets.Length; ++i)
                offsets[i] = data_offset + m_input.ReadUInt32();

            foreach (var offset in offsets)
                ReadBlock (offset);
        }

        internal class PxBlock
        {
            public int  Width;
            public int  Height;
            public int  X;
            public int  Y;
            public int  Type;
            public int  Bits;
        }

        void ReadBlock (uint offset)
        {
            m_input.Position = offset;
            var px = new PxBlock();
            px.Width    = m_input.ReadInt32();
            px.Height   = m_input.ReadInt32();
            px.X        = m_input.ReadInt32();
            px.Y        = m_input.ReadInt32();
            px.Type     = m_input.ReadUInt16();
            px.Bits     = m_input.ReadUInt16();
            m_input.Position = offset + 0x20;
            switch (px.Type)
            {
            case 0:
                Palette = ImageFormat.ReadPalette (m_input.AsStream, (int)px.Width);
                break;

            case 1:
                switch (px.Bits)
                {
                case 8:     UnpackBlock_1_8 (px); break;
                case 0x20:  UnpackBlock_1_20 (px); break;
                }
                break;

            case 4:
                switch (px.Bits)
                {
                case 8:     UnpackBlock_4_8 (px); break;
                case 9:     UnpackBlock_4_9 (px); break;
                case 0x20:  UnpackBlock_4_20 (px); break;
                case 0x30:  UnpackBlock_4_30 (px); break;
                }
                break;

            case 7:
                UnpackBlock_7 (px);
                break;

            default:
                throw new InvalidFormatException();
            }
        }

        void UnpackBlock_1_8 (PxBlock block)
        {
            m_input.Read (m_output, 0, (int)m_info.Width * (int)m_info.Height);
        }

        void UnpackBlock_1_20 (PxBlock block)
        {
            int dst = 0;
            for (int y = 0; y < block.Height; ++y)
            for (int x = 0; x < block.Width; ++x)
            {
                m_input.Read (m_output, dst, 4);
                if (0 != m_output[dst+3])
                {
                    byte alpha = (byte)((m_output[dst+3] << 1 | m_output[dst+2] >> 7) + 0xFF);
                    m_output[dst+3] = alpha;
                }
                else
                {
                    m_output[dst+3] = 0xFF;
                }
                dst += 4;
            }
        }

        void UnpackBlock_4_8 (PxBlock block)
        {
            throw new NotImplementedException();
        }

        const int MaxBlockSize = 1024;
        // lazily evaluated to avoid unnecessary allocation
        Lazy<uint[]> m_block = new Lazy<uint[]> (() => new uint[MaxBlockSize * MaxBlockSize]);

        uint[] NewBlock (PxBlock block_info)
        {
            var block = m_block.Value;
            Array.Clear (block, 0, MaxBlockSize * block_info.Height);
            return block;
        }

        void UnpackBlock_4_9 (PxBlock block_info)
        {
            var output = NewBlock (block_info);
            int dst = 0;
            bool has_alpha = true;
            for (;;)
            {
                int code = m_input.ReadInt32();
                if (-1 == code)
                    break;
                if (0 != (code & 0x180000))
                    has_alpha = !has_alpha;
                dst += (code & 0x1FF) * MaxBlockSize;
                dst += code >> 21;

                int count = (code >> 9) & 0x3FF;
                for (int i = 0; i < count; ++i)
                {
                    byte alpha = (byte)(has_alpha ? (m_input.ReadUInt8() << 1) - 1 : 0xFF);
                    int color_idx = m_input.ReadByte();
                    if (dst < output.Length)
                    {
                        var color = Palette.Colors[color_idx];
                        output[dst] = (uint)(color.B | color.G << 8 | color.R << 16 | alpha << 24);
                    }
                    dst++;
                }
            }
            PutBlock (block_info);
        }

        void UnpackBlock_4_20 (PxBlock block_info)
        {
            var block = NewBlock (block_info);
            int dst = 0;
            int next;
            while ((next = m_input.ReadInt32()) != -1)
            {
                if (next < 0 || next > 0xFFFFFF)
                    continue;
                dst += next / 4;
                m_input.ReadInt32();

                int count = m_input.ReadInt32();
                while (count --> 0)
                {
                    uint color = m_input.ReadUInt32();
                    if (dst < block.Length)
                    {
                        if (0 != (color & 0xFF000000))
                        {
                            uint alpha = ((color >> 23) + 0xFF) << 24;
                            block[dst] = (color & 0xFFFFFFu) | alpha;
                        }
                        else
                        {
                            block[dst] = color | 0xFF000000u;
                        }
                    }
                    ++dst;
                }
            }
            PutBlock (block_info);
        }

        void UnpackBlock_4_30 (PxBlock block_info)
        {
            var output = NewBlock (block_info);
            int dst = 0;
            for (;;)
            {
                int next = m_input.ReadInt32();
                if (-1 == next)
                    break;
                if (0 != (next & 0xFF000000))
                    continue;
                dst += next / 4;
                m_input.ReadInt32();

                int count = m_input.ReadInt32();
                for (int i = 0; i < count; ++i)
                {
                    uint color = m_input.ReadUInt32();
                    m_input.ReadInt16();
                    if (dst < output.Length)
                    {
                        if (0 != (color & 0xFF000000))
                        {
                            uint alpha = ((color >> 23) + 0xFF) << 24;
                            output[dst] = (color & 0xFFFFFF) | alpha;
                        }
                        else
                        {
                            output[dst] = 0;
                        }
                    }
                    dst++;
                }
            }
            PutBlock (block_info);
        }

        void UnpackBlock_7 (PxBlock block)
        {
            m_stride = 4 * block.Width;
            m_output = new byte[m_stride * block.Height];
            Format = PixelFormats.Bgra32;
            m_info.OffsetX = block.X;
            m_info.OffsetY = block.Y;
            int dst = 0;
            var color_map = ImageFormat.ReadColorMap (m_input.AsStream, 0x100, PaletteFormat.BgrA);
            for (int y = 0; y < block.Height; ++y)
            for (int x = 0; x < block.Width; ++x)
            {
                int idx = m_input.ReadUInt8();
                var c = color_map[idx];
                m_output[dst++] = c.B;
                m_output[dst++] = c.G;
                m_output[dst++] = c.R;
                if (c.A != 0)
                    m_output[dst++] = (byte)((c.A << 1 | c.R >> 7) + 0xFF);
                else
                    m_output[dst++] = 0xFF;
            }
        }

        void PutBlock (PxBlock block_info)
        {
            var block = m_block.Value;
            int left   = Math.Max (0, block_info.X);
            int top    = Math.Max (0, block_info.Y);
            int right  = Math.Min (block_info.X + block_info.Width, (int)m_info.Width);
            int bottom = Math.Min (block_info.Y + block_info.Height, (int)m_info.Height);
            int dst_row = top * m_stride + left * 4;
            int row_size = (right - left) * 4;
            for (int y = top; y < bottom; ++y)
            {
                int src = (left - block_info.X) + (y - block_info.Y) * MaxBlockSize;
                Buffer.BlockCopy (block, src * 4, m_output, dst_row, row_size);
                dst_row += m_stride;
            }
        }

        #region IDisposable Members
        public void Dispose ()
        {
        }
        #endregion
    }
}
