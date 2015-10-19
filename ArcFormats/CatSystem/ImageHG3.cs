//! \file       ImageHG3.cs
//! \date       Sat Jul 19 17:31:09 2014
//! \brief      CatSystem HG3 image format implementation.
//
// Copyright (C) 2014-2015 by morkt
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
using System.IO;
using System.Linq;
using System.ComponentModel.Composition;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.CatSystem
{
    internal class Hg3MetaData : ImageMetaData
    {
        public uint CanvasWidth;
        public uint CanvasHeight;
        public uint HeaderSize;
    }

    [Export(typeof(ImageFormat))]
    public class Hg3Format : ImageFormat
    {
        public override string         Tag { get { return "HG3"; } }
        public override string Description { get { return "CatSystem engine image format"; } }
        public override uint     Signature { get { return 0x332d4748; } } // 'HG-3'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x4c];
            if (0x4c != stream.Read (header, 0, header.Length))
                return null;
            if (LittleEndian.ToUInt32 (header, 4) != 0x0c)
                return null;
            if (!Binary.AsciiEqual (header, 0x14, "stdinfo\0"))
                return null;
            return new Hg3MetaData
            {
                HeaderSize = LittleEndian.ToUInt32 (header, 0x1C),
                Width = LittleEndian.ToUInt32 (header, 0x24),
                Height = LittleEndian.ToUInt32 (header, 0x28),
                OffsetX = LittleEndian.ToInt32 (header, 0x30),
                OffsetY = LittleEndian.ToInt32 (header, 0x34),
                BPP = LittleEndian.ToInt32 (header, 0x2C),
                CanvasWidth = LittleEndian.ToUInt32 (header, 0x44),
                CanvasHeight = LittleEndian.ToUInt32 (header, 0x48),
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as Hg3MetaData;
            if (null == meta)
                throw new ArgumentException ("Hg3Format.Read should be supplied with Hg3MetaData", "info");
            if (0x20 != meta.BPP)
                throw new NotSupportedException ("Not supported HG-3 color depth");

            using (var input = new StreamRegion (stream, 0x14, true))
            using (var reader = new Hg3Reader (input, meta))
            {
                var pixels = reader.Unpack();
                int stride = (int)info.Width * info.BPP / 8;
                if (reader.Flipped)
                    return ImageData.CreateFlipped (info, PixelFormats.Bgra32, null, pixels, stride);
                else
                    return ImageData.Create (info, PixelFormats.Bgra32, null, pixels, stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("Hg3Format.Write not implemented");
        }
    }

    internal sealed class Hg3Reader : IDisposable
    {
        BinaryReader    m_input;
        Hg3MetaData     m_info;
        int             m_pixel_size;

        public bool Flipped { get; private set; }

        public Hg3Reader (Stream input, Hg3MetaData info)
        {
            m_input = new ArcView.Reader (input);
            m_info = info;
            m_pixel_size = m_info.BPP / 8;
        }

        public byte[] Unpack ()
        {
            m_input.BaseStream.Position = m_info.HeaderSize;
            var img_type = m_input.ReadChars (8);
            if (img_type.SequenceEqual ("img0000\0"))
                return UnpackImg0000();
            else if (img_type.SequenceEqual ("img_jpg\0"))
                return UnpackJpeg();
            else
                throw new NotSupportedException ("Not supported HG-3 image");
        }

        byte[] UnpackImg0000 ()
        {
            Flipped = true;
            m_input.BaseStream.Position = m_info.HeaderSize+0x18;
            uint packed_data_size = m_input.ReadUInt32();
            int  data_size = m_input.ReadInt32();
            uint packed_ctl_size = m_input.ReadUInt32();
            int  ctl_size = m_input.ReadInt32();

            uint data_offset = m_info.HeaderSize + 0x28;
            uint ctl_offset = data_offset + packed_data_size;
            var data = new byte[data_size];
            using (var z = new StreamRegion (m_input.BaseStream, data_offset, packed_data_size, true))
            using (var data_in = new ZLibStream (z, CompressionMode.Decompress))
                if (data_size != data_in.Read (data, 0, data.Length))
                    throw new EndOfStreamException();

            using (var z = new StreamRegion (m_input.BaseStream, ctl_offset, packed_ctl_size, true))
            using (var ctl_in = new ZLibStream (z, CompressionMode.Decompress))
            using (var bits = new LsbBitStream (ctl_in))
            {
                bool copy = bits.GetNextBit() != 0;
                int output_size = GetBitCount (bits);
                var output = new byte[output_size];
                int src = 0;
                int dst = 0;
                while (dst < output_size)
                {
                    int count = GetBitCount (bits);
                    if (copy)
                    {
                        Buffer.BlockCopy (data, src, output, dst, count);
                        src += count;
                    }
                    dst += count;
                    copy = !copy;
                }
                return ApplyDelta (output);
            }
        }

        byte[] UnpackJpeg ()
        {
            Flipped = false;
            m_input.ReadInt32();
            var jpeg_size = m_input.ReadInt32();
            long next_section = m_input.BaseStream.Position + jpeg_size;
            var decoder = new JpegBitmapDecoder (m_input.BaseStream,
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            if (frame.Format.BitsPerPixel < 24)
                throw new NotSupportedException ("Not supported HG-3 JPEG color depth");
            int src_pixel_size = frame.Format.BitsPerPixel/8;
            int stride = (int)m_info.Width * src_pixel_size;
            var pixels = new byte[stride*(int)m_info.Height];
            frame.CopyPixels (pixels, stride, 0);
            var output = new byte[m_info.Width*m_info.Height*4];
            uint total = m_info.Width * m_info.Height;
            int src = 0;
            int dst = 0;
            for (uint i = 0; i < total; ++i)
            {
                output[dst++] = pixels[src];
                output[dst++] = pixels[src+1];
                output[dst++] = pixels[src+2];
                output[dst++] = 0xFF;
                src += src_pixel_size;
            }

            m_input.BaseStream.Position = next_section;
            var section_header = m_input.ReadChars (8);
            if (!section_header.SequenceEqual ("img_al\0\0"))
                return output;
            m_input.BaseStream.Seek (8, SeekOrigin.Current);
            int alpha_size = m_input.ReadInt32();
            using (var alpha_in = new StreamRegion (m_input.BaseStream, m_input.BaseStream.Position+4, alpha_size, true))
            using (var alpha = new ZLibStream (alpha_in, CompressionMode.Decompress))
            {
                for (int i = 3; i < output.Length; i += 4)
                {
                    int b = alpha.ReadByte();
                    if (-1 == b)
                        throw new EndOfStreamException();
                    output[i] = (byte)b;
                }
                return output;
            }
        }

        static int GetBitCount (LsbBitStream bits)
        {
            int n = 0;
            while (0 == bits.GetNextBit())
            {
                ++n;
                if (n >= 0x20)
                    throw new InvalidFormatException ("Overflow at Hg3Reader.GetBitCount");
            }
            int value = 1;
            while (n --> 0)
            {
                value = (value << 1) | bits.GetNextBit();
            }
            return value;
        }

        byte[] ApplyDelta (byte[] pixels)
        {
            uint[] table1 = new uint[0x100];
            uint[] table2 = new uint[0x100];
            uint[] table3 = new uint[0x100];
            uint[] table4 = new uint[0x100];

            for (uint i = 0; i < 0x100; ++i)
            {
                uint val = i & 0xC0;

                val <<= 6;
                val |= i & 0x30;

                val <<= 6;
                val |= i & 0x0C;

                val <<= 6;
                val |= i & 0x03;

                table4[i] = val;
                table3[i] = val << 2;
                table2[i] = val << 4;
                table1[i] = val << 6;
            }

            int plane_size = pixels.Length / 4;
            int plane1 = 0;
            int plane2 = plane1 + plane_size;
            int plane3 = plane2 + plane_size;
            int plane4 = plane3 + plane_size;

            byte[] output = new byte[pixels.Length];
            int dst = 0;
            while (dst < output.Length)
            {
                uint val = table1[pixels[plane1++]] | table2[pixels[plane2++]]
                         | table3[pixels[plane3++]] | table4[pixels[plane4++]];

                output[dst++] = ConvertValue ((byte)val);
                output[dst++] = ConvertValue ((byte)(val >> 8));
                output[dst++] = ConvertValue ((byte)(val >> 16));
                output[dst++] = ConvertValue ((byte)(val >> 24));
            }

            int stride = (int)m_info.Width * m_pixel_size;

            for (int x = m_pixel_size; x < stride; x++)
            {
                output[x] += output[x - m_pixel_size];
            }

            int line = stride;
            for (uint y = 1; y < m_info.Height; y++)
            {
                int prev = line - stride;
                for (int x = 0; x < stride; x++)
                {
                    output[line+x] += output[prev+x];
                }
                line += stride;
            }
            return output;
        }

        static byte ConvertValue (byte val)
        {
            bool carry = 0 != (val & 1);
            val >>= 1;
            return (byte)(carry ? val ^ 0xFF : val);
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
