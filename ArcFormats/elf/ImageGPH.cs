//! \file       ImageGPH.cs
//! \date       Sun May 29 16:26:55 2016
//! \brief      Ancient GPH image format.
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

namespace GameRes.Formats.Elf
{
    internal class GphMetaData :ImageMetaData
    {
        public int  DataOffset;
        public int  DataSize;
        public int  Flags;
    }

    [Export(typeof(ImageFormat))]
    public class GphFormat : ImageFormat
    {
        public override string         Tag { get { return "GPH"; } }
        public override string Description { get { return "Elf GPH image format"; } }
        public override uint     Signature { get { return 0x1D485047; } } // 'GPH\x1D'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            stream.Position = 4;
            using (var input = new ArcView.Reader (stream))
            {
                int frame_count = input.ReadUInt16();
                int frame_offset = input.ReadInt32();
                if (0 == frame_count || frame_offset > stream.Length)
                    return null;
                stream.Position = frame_offset;
                int frame_length = input.ReadInt32();
                int flags = input.ReadUInt16();
                if (0 == (flags & 4))
                    stream.Seek (0x20, SeekOrigin.Current);
                int left = input.ReadInt16();
                int top = input.ReadInt16();
                int right = input.ReadInt16() + 1;
                int bottom = input.ReadInt16() + 1;
                left *= 2;
                right *= 2;
                return new GphMetaData
                {
                    Width = (uint)(right - left),
                    Height = (uint)(bottom - top),
                    OffsetX = left,
                    OffsetY = top,
                    BPP = 4,
                    DataOffset = frame_offset+4,
                    DataSize = frame_length,
                    Flags = flags,
                };
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            using (var reader = new GphReader (stream, (GphMetaData)info))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, reader.Palette, reader.Data, reader.Stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GphFormat.Write not implemented");
        }
    }

    internal sealed class GphReader : IDisposable
    {
        BinaryReader    m_input;
        GphMetaData     m_info;
        byte[]          m_output;

        public byte[]           Data { get { return m_output; } }
        public PixelFormat    Format { get { return PixelFormats.Indexed4; } }
        public BitmapPalette Palette { get; private set; }
        public int            Stride { get; private set; }

        public GphReader (Stream input, GphMetaData info)
        {
            m_input = new ArcView.Reader (input);
            m_info = info;
            Stride = (int)m_info.Width / 2;
            m_output = new byte[Stride * (int)m_info.Height];
        }

        ushort[] OffsetTable = new ushort[0x100];
        byte[] m_buffer = new byte[0x1400];

        int bit_count, bits;
        int m_out_pos;

        public void Unpack ()
        {
            m_input.BaseStream.Position = m_info.DataOffset+2;
            if (0 == (m_info.Flags & 4))
                ReadPalette();
            else
                SetDefaultPalette();
            m_input.BaseStream.Seek (8, SeekOrigin.Current);

            int stride = Stride;
            if (stride <= 0x10)
            {
                for (int i = 0; i < 0x100; ++i)
                    OffsetTable[i] = (ushort)(i+1);
            }
            else
            {
                for (int i = 0; i < 0x100; ++i)
                {
                    int x = i >> 4;
                    if (0 != (i & 8))
                    {
                        ++x;
                        x *= stride;
                        x += i & 0xF;
                        x -= 0xF;
                    }
                    else
                    {
                        x *= stride;
                        x += i & 0xF;
                        ++x;
                    }
                    OffsetTable[i] = (ushort)x;
                }
            }

            bits = m_input.ReadUInt16();
            bits = bits >> 8 | bits << 8;
            bit_count = 9;
            CreateHuffmanTree();
            int dst = 0;
            m_out_pos = 0;

            int total_count = stride * (int)m_info.Height;
            while (total_count > 0)
            {
                int token = GetToken();
                if (token < 0x100)
                {
                    m_buffer[dst++] = (byte)token;
                    if (dst >= m_buffer.Length)
                    {
                        CopyPixels (m_buffer.Length);
                        dst = 0;
                    }
                    --total_count;
                }
                else
                {
                    int count = (token & 0xFF) + 3;
                    int offset = GetOffset();
                    int src = dst - OffsetTable[offset];
                    if (src < 0)
                        src += 0x1400;
                    for (int i = 0; i < count; ++i)
                    {
                        m_buffer[dst++] = m_buffer[src++];
                        if (src >= m_buffer.Length)
                            src = 0;
                        if (dst >= m_buffer.Length)
                        {
                            CopyPixels (m_buffer.Length);
                            dst = 0;
                        }
                    }
                    total_count -= count;
                }
            }
            if (dst != 0)
            {
                CopyPixels (dst);
            }
        }

        int m_next_token;

        void CreateHuffmanTree ()
        {
            m_next_token = 0;
            int root = CreateTokenNode();
            for (int i = 0; i < 0x100; ++i)
                ProcessTokenNode (i, root);

            m_next_token = 0;
            root = CreateOffsetNode();
            for (int i = 0; i < 0x100; ++i)
                ProcessOffsetNode (i, root);
        }

        byte[] LengthTable = new byte[0x200];
        short[] TokenTable = new short[0x200];
        short[] NodeTable  = new short[0x600];

        int CreateTokenNode ()
        {
            --bit_count;
            if (0 == bit_count)
            {
                bit_count = 8;
                ReadNext();
            }
            int node;
            bits <<= 1;
            if (0 != (bits & 0x10000))
            {
                node = m_next_token++;
                int idx = node << 1;
                NodeTable[idx]   = (short)CreateTokenNode();
                NodeTable[idx+1] = (short)CreateTokenNode();
                return node + 0x200;
            }
            --bit_count;
            if (0 == bit_count)
            {
                bit_count = 8;
                ReadNext();
            }
            node = (bits >> 7) & 0x1FF;
            bits <<= bit_count;
            ReadNext();
            bits <<= 9 - bit_count;
            return node;
        }

        void ProcessTokenNode (int idx, int b)
        {
            int x = idx;
            byte i = 0;
            do
            {
                ++i;
                b = b << 1 | ((x >> 7) & 1);
                b = NodeTable[b & 0x3FF];
                if (b < 0x200)
                    break;
                x <<= 1;
            }
            while (i < 8);
            LengthTable[idx] = i;
            TokenTable[idx] = (short)b;
        }

        int CreateOffsetNode ()
        {
            --bit_count;
            if (0 == bit_count)
            {
                bit_count = 8;
                ReadNext();
            }
            int node;
            bits <<= 1;
            if (0 != (bits & 0x10000))
            {
                node = m_next_token++;
                int idx = (node << 1) + 0x400;
                NodeTable[idx]   = (short)CreateOffsetNode();
                NodeTable[idx+1] = (short)CreateOffsetNode();
                return node + 0x100;
            }
            node = (bits >> 8) & 0xFF;
            bits <<= bit_count - 1;
            ReadNext();
            bits <<= 9 - bit_count;
            return node;
        }

        void ProcessOffsetNode (int idx, int b)
        {
            int x = idx;
            byte i = 0;
            do
            {
                ++i;
                b = b << 1 | ((x >> 7) & 1);
                b &= 0x1FF;
                b = NodeTable[b + 0x400];
                if (b < 0x100)
                    break;
                x <<= 1;
            }
            while (i < 8);
            LengthTable[idx+0x100] = i;
            TokenTable[idx+0x100] = (short)b;
        }

        int GetToken ()
        {
            int token = (bits >> 8) & 0xFF;
            int length = LengthTable[token];
            token = TokenTable[token];
            if (length >= bit_count)
            {
                --bit_count;
                bits <<= bit_count;
                length -= bit_count;
                bit_count = 9;
                ReadNext();
            }
            bits <<= length;
            bit_count -= length;
            while (token >= 0x200)
            {
                --bit_count;
                if (0 == bit_count)
                {
                    bit_count = 8;
                    ReadNext();
                }
                token = (token << 1) | ((bits >> 15) & 1);
                bits <<= 1;
                token &= 0x3FF;
                token = NodeTable[token];
            }
            return token;
        }

        int GetOffset ()
        {
            int token = (bits >> 8) & 0xFF;
            int length = LengthTable[token+0x100];
            token = TokenTable[token+0x100];
            if (length >= bit_count)
            {
                --bit_count;
                bits <<= bit_count;
                length -= bit_count;
                bit_count = 9;
                ReadNext();
            }
            bits <<= length;
            bit_count -= length;
            while (token >= 0x100)
            {
                --bit_count;
                if (0 == bit_count)
                {
                    bit_count = 8;
                    ReadNext();
                }
                token = (token << 1) | ((bits >> 15) & 1);
                bits <<= 1;
                token &= 0x1FF;
                token = NodeTable[token+0x400];
            }
            return token;
        }

        void ReadNext ()
        {
            bits &= 0xFF00;
            int b = m_input.BaseStream.ReadByte();
            if (-1 != b)
                bits |= b;
        }

        void CopyPixels (int count)
        {
            int src = 0;
            while (count --> 0)
            {
                byte b = m_buffer[src++];
                int p;
                p  = (b & 0x80) | (b & 0x20) << 1 | (b & 0x08) << 2 | (b & 0x02) << 3;
                p |= (b & 0x01) | (b & 0x04) >> 1 | (b & 0x10) >> 2 | (b & 0x40) >> 3;
                m_output[m_out_pos++] = (byte)p;
            }
        }

        void ReadPalette ()
        {
            var palette = new Color[0x10];
            for (int i = 0; i < 0x10; ++i)
            {
                int rgb = m_input.ReadByte();
                int r = (rgb >> 2) & 0x3C;
                int b = (rgb << 2) & 0x3C;
                rgb = m_input.ReadByte();
                int g = (rgb << 2) & 0x3C;
                palette[i] = Color.FromRgb (Clamp (r), Clamp (g), Clamp (b));
            }
            Palette = new BitmapPalette (palette);
        }

        void SetDefaultPalette ()
        {
            var palette = new Color[0x10]
            {
                Color.FromRgb (0x00, 0x00, 0x00), Color.FromRgb (0x00, 0x00, 0xAA),
                Color.FromRgb (0x00, 0xAA, 0x00), Color.FromRgb (0x00, 0xAA, 0xAA),
                Color.FromRgb (0xAA, 0x00, 0x00), Color.FromRgb (0xAA, 0x00, 0xAA),
                Color.FromRgb (0xAA, 0xAA, 0x00), Color.FromRgb (0xAA, 0xAA, 0xAA),
                Color.FromRgb (0x88, 0x88, 0x88), Color.FromRgb (0x00, 0x00, 0xFF),
                Color.FromRgb (0x00, 0xFF, 0x00), Color.FromRgb (0x00, 0xFF, 0xFF),
                Color.FromRgb (0xFF, 0x00, 0x00), Color.FromRgb (0xFF, 0x00, 0xFF),
                Color.FromRgb (0xFF, 0xFF, 0x00), Color.FromRgb (0xFF, 0xFF, 0xFF),
            };
            Palette = new BitmapPalette (palette);
        }

        static byte Clamp (int color)
        {
            return (byte)(color * 0xFF / 0x3C);
        }

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }
}
