//! \file       ImageSPL.cs
//! \date       Thu Sep 10 00:17:47 2015
//! \brief      Zyx tiled image.
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
using GameRes.Utility;

namespace GameRes.Formats.Zyx
{
    internal class Tile
    {
        public int Left, Top;
        public int Right, Bottom;
    }

    internal class SplMetaData : ImageMetaData
    {
        public Tile[]   Tiles;
        public long     DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class SplFormat : ImageFormat
    {
        public override string         Tag { get { return "SPL"; } }
        public override string Description { get { return "Zyx tiled image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            int count = file.ReadInt16();
            if (count < 0 || count > 0x100)
                return null;
            Tile[] tiles = null;
            if (count > 0)
            {
                tiles = new Tile[count];
                for (int i = 0; i < count; ++i)
                {
                    var tile = new Tile();
                    tile.Left = file.ReadInt16();
                    tile.Top  = file.ReadInt16();
                    if (tile.Left < 0 || tile.Top < 0)
                        return null;
                    tile.Right  = file.ReadInt16();
                    tile.Bottom = file.ReadInt16();
                    if (tile.Right <= tile.Left || tile.Bottom <= tile.Top)
                        return null;
                    tiles[i] = tile;
                }
            }
            int width = file.ReadInt16();
            int height = file.ReadInt16();
            if (width <= 0 || height <= 0)
                return null;
            if (tiles != null)
            {
                foreach (var tile in tiles)
                {
                    if (tile.Right > width || tile.Bottom > height)
                        return null;
                }
            }
            return new SplMetaData
            {
                Width = (uint)width,
                Height = (uint)height,
                BPP = 24,
                Tiles = tiles,
                DataOffset = file.Position,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (SplMetaData)info;
            var reader = new SplReader (stream.AsStream, meta);
            reader.Unpack ();
            return ImageData.Create (info, PixelFormats.Bgr24, null, reader.Data);
        }
        
        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("SplFormat.Write not implemented");
        }
    }

    internal class SplReader
    {
        Stream      m_input;
        byte[]      m_output;

        public byte[] Data { get { return m_output; } }

        public SplReader (Stream input, SplMetaData info)
        {
            m_output = new byte[3 * info.Width * info.Height];
            m_input = input;
            m_input.Position = info.DataOffset;
        }

        public void Unpack ()
        {
            int dst = 0;
            while (dst < m_output.Length)
            {
                int count;
                int b = m_input.ReadByte();
                if (-1 == b)
                    break;
                switch (b)
                {
                case 0:
                    count = m_input.ReadByte();
                    if (count != -1)
                    {
                        byte p1 = m_output[dst - 3];
                        byte p2 = m_output[dst - 2];
                        byte p3 = m_output[dst - 1];
                        for (int i = 0; i < count; ++i)
                        {
                            m_output[dst++] = p1;
                            m_output[dst++] = p2;
                            m_output[dst++] = p3;
                        }
                    }
                    break;
                case 1:
                    count = m_input.ReadByte();
                    b = m_input.ReadByte();
                    if (-1 != count && -1 != b)
                    {
                        int src = dst - 3 * b;
                        count *= 3;
                        Binary.CopyOverlapped (m_output, src, dst, count);
                        dst += count;
                    }
                    break;
                case 2:
                    count = m_input.ReadByte();
                    b = ReadWord();
                    if (-1 != count && -1 != b)
                    {
                        int src = dst - 3 * b;
                        count *= 3;
                        Binary.CopyOverlapped (m_output, src, dst, count);
                        dst += count;
                    }
                    break;
                case 3:
                    b = m_input.ReadByte();
                    if (b != -1)
                    {
                        int src = dst - 3 * b;
                        m_output[dst++] = m_output[src++];
                        m_output[dst++] = m_output[src++];
                        m_output[dst++] = m_output[src++];
                    }
                    break;
                case 4:
                    b = ReadWord();
                    if (b != -1)
                    {
                        int src = dst - 3 * b;
                        m_output[dst++] = m_output[src++];
                        m_output[dst++] = m_output[src++];
                        m_output[dst++] = m_output[src++];
                    }
                    break;
                default:
                    count = 3 * (b - 4);
                    m_input.Read (m_output, dst, count);
                    dst += count;
                    break;
                }
            }
        }

        private int ReadWord ()
        {
            int lo = m_input.ReadByte();
            if (-1 == lo)
                return -1;
            int hi = m_input.ReadByte();
            if (-1 == hi)
                return -1;
            return hi << 8 | lo;
        }
    }
}
