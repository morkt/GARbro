//! \file       ImageG00.cs
//! \date       Mon Apr 18 14:06:48 2016
//! \brief      RealLive engine image format.
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.RealLive
{
    internal class G00MetaData : ImageMetaData
    {
        public int  Type;
    }

    [Export(typeof(ImageFormat))]
    public class G00Format : ImageFormat
    {
        public override string         Tag { get { return "G00"; } }
        public override string Description { get { return "RealLive engine image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            int type = file.ReadByte();
            if (type > 2)
                return null;
            uint width  = file.ReadUInt16();
            uint height = file.ReadUInt16();
            if (0 == width || width > 0x8000 || 0 == height || height > 0x8000)
                return null;
            if (2 == type)
            {
                int count = file.ReadInt32();
                if (count <= 0 || count > 0x100)
                    return null;
            }
            else
            {
                uint length = file.ReadUInt32();
                if (length + 5 != file.Length)
                    return null;
            }
            return new G00MetaData {
                Width  = width,
                Height = height,
                BPP    = 1 == type ? 8 : 24,
                Type   = type,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var reader = new G00Reader (stream, (G00MetaData)info))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, reader.Palette, reader.Data);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("G00Format.Write not implemented");
        }
    }

    internal class Tile
    {
        public int  X;
        public int  Y;
        public uint Offset;
        public int  Length;
    }

    internal sealed class G00Reader : IDisposable
    {
        IBinaryStream   m_input;
        byte[]          m_output;
        int             m_width;
        int             m_height;
        int             m_type;

        public byte[]           Data { get { return m_output; } }
        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }

        public G00Reader (IBinaryStream input, G00MetaData info)
        {
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            m_type = info.Type;
            m_input = input;
        }

        public void Unpack ()
        {
            m_input.Position = 5;
            if (0 == m_type)
                UnpackV0();
            else if (1 == m_type)
                UnpackV1();
            else
                UnpackV2();
        }

        void UnpackV0 ()
        {
            m_output = LzDecompress (m_input, 1, 3);
            Format = PixelFormats.Bgr24;
        }

        void UnpackV1 ()
        {
            m_output = LzDecompress (m_input, 2, 1);
            int colors = LittleEndian.ToUInt16 (m_output, 0);
            int src = 2;
            var palette = new Color[colors];
            for (int i = 0; i < colors; ++i)
            {
                palette[i] = Color.FromArgb (m_output[src+3], m_output[src+2], m_output[src+1], m_output[src]);
                src += 4;
            }
            Palette = new BitmapPalette (palette);
            Format = PixelFormats.Indexed8;
            Buffer.BlockCopy (m_output, src, m_output, 0, m_output.Length-src);
        }

        void UnpackV2 ()
        {
            Format = PixelFormats.Bgra32;
            int tile_count = m_input.ReadInt32();
            var tiles = new List<Tile> (tile_count);
            for (int i = 0; i < tile_count; ++i)
            {
                var tile = new Tile();
                tile.X = m_input.ReadInt32();
                tile.Y = m_input.ReadInt32();
                tiles.Add (tile);
                m_input.Seek (0x10, SeekOrigin.Current);
            }
            using (var input = new MemoryStream (LzDecompress (m_input, 2, 1)))
            using (var reader = new BinaryReader (input))
            {
                if (reader.ReadInt32() != tile_count)
                    throw new InvalidFormatException();
                int dst_stride = m_width * 4;
                m_output = new byte[m_height * dst_stride];
                for (int i = 0; i < tile_count; ++i)
                {
                    tiles[i].Offset = reader.ReadUInt32();
                    tiles[i].Length = reader.ReadInt32();
                }
                var tile = tiles.First (t => t.Length != 0);

                input.Position = tile.Offset;
                int tile_type = reader.ReadUInt16();
                int count = reader.ReadUInt16();
                if (tile_type != 1)
                    throw new InvalidFormatException();
                input.Seek (0x70, SeekOrigin.Current);
                for (int i = 0; i < count; ++i)
                {
                    int tile_x = reader.ReadUInt16();
                    int tile_y = reader.ReadUInt16();
                    reader.ReadInt16();
                    int tile_width = reader.ReadUInt16();
                    int tile_height = reader.ReadUInt16();
                    input.Seek (0x52, SeekOrigin.Current);

                    tile_x += tile.X;
                    tile_y += tile.Y;
                    if (tile_x + tile_width > m_width || tile_y + tile_height > m_height)
                        throw new InvalidFormatException();
                    int dst = tile_y * dst_stride + tile_x * 4;
                    int tile_stride = tile_width * 4;
                    for (int row = 0; row < tile_height; ++row)
                    {
                        reader.Read (m_output, dst, tile_stride);
                        dst += dst_stride;
                    }
                }
            }
        }

        public static byte[] LzDecompress (IBinaryStream input, int min_count, int bytes_pp)
        {
            int packed_size = input.ReadInt32() - 8;
            int output_size = input.ReadInt32();
            var output = new byte[output_size];
            int dst = 0;
            int bits = 2;
            while (dst < output.Length && packed_size > 0)
            {
                bits >>= 1;
                if (1 == bits)
                {
                    bits = input.ReadUInt8() | 0x100;
                    --packed_size;
                }
                if (0 != (bits & 1))
                {
                    input.Read (output, dst, bytes_pp);
                    dst += bytes_pp;
                    packed_size -= bytes_pp;
                }
                else
                {
                    if (packed_size < 2)
                        break;
                    int offset = input.ReadUInt16();
                    packed_size -= 2;
                    int count = (offset & 0xF) + min_count;
                    offset >>= 4;
                    offset *= bytes_pp;
                    count *= bytes_pp;
                    Binary.CopyOverlapped (output, dst-offset, dst, count);
                    dst += count;
                }
            }
            return output;
        }

        #region IDisposable Members
        public void Dispose ()
        {
        }
        #endregion
    }
}
