//! \file       ImagePB3.cs
//! \date       Wed Dec 02 13:55:45 2015
//! \brief      Cmvs engine image format.
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

using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Purple
{
    internal class Pb3MetaData : ImageMetaData
    {
        public int  Type;
        public int  SubType;
        public int  InputSize;
    }

    [Export(typeof(ImageFormat))]
    public class Pb3Format : ImageFormat
    {
        public override string         Tag { get { return "PB3"; } }
        public override string Description { get { return "Purple Software image format"; } }
        public override uint     Signature { get { return 0x42334250; } } // 'PB3B'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            stream.Position = 4;
            using (var reader = new ArcView.Reader (stream))
            {
                int input_size = reader.ReadInt32();
                stream.Position = 0x18;
                int t2 = reader.ReadInt32();
                int t1 = reader.ReadUInt16();
                uint width = reader.ReadUInt16();
                uint height = reader.ReadUInt16();
                int bpp = reader.ReadUInt16();
                return new Pb3MetaData
                {
                    Width       = width,
                    Height      = height,
                    BPP         = bpp,
                    Type        = t1,
                    SubType     = t2,
                    InputSize   = input_size,
                };
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var reader = new Pb3Reader (stream, (Pb3MetaData)info);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, null, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Pb3Format.Write not implemented");
        }
    }

    internal sealed class Pb3Reader
    {
        byte[]          m_input;
        Pb3MetaData     m_info;
        int             m_channels;
        int             m_stride;
        byte[]          m_output;
        byte[]          m_lzss_frame;

        public PixelFormat Format { get; private set; }
        public byte[]        Data { get { return m_output; } }

        public Pb3Reader (Stream input, Pb3MetaData info)
        {
            if (info.Type == 1 && info.SubType != 0x10)
                throw new NotSupportedException();
            m_info = info;
            m_input = new byte[m_info.InputSize];
            if (m_input.Length != input.Read (m_input, 0, m_input.Length))
                throw new EndOfStreamException();
            m_channels = m_info.BPP / 8;
            m_stride = 4 * (int)m_info.Width;
            m_lzss_frame = new byte[0x800];
            Format = m_channels < 4 ? PixelFormats.Bgr32 : PixelFormats.Bgra32;
            // output array created by unpack methods as needed.
        }

        public void Unpack ()
        {
            switch (m_info.Type)
            {
            default: throw new InvalidEncryptionScheme();
            case 1: UnpackV1(); break;
            case 5: UnpackV5(); break;
            case 8:
            case 6: UnpackV6(); break;
            case 2:
            case 3:
            case 4:
            case 7: throw new NotSupportedException(string.Format ("PB3 v{0} images not supported", m_info.Type));
            // V3 is pain in the ass to implement, machine code is full of unrolled loops resulting in a
            // thousands lines of spaghetti code.
            }
        }

        void UnpackV1 ()
        {
            int width  = (int)m_info.Width;
            int height = (int)m_info.Height;
            m_output = new byte[m_stride * height];

            int plane_size = width * height;
            byte[] plane = new byte[plane_size];

            int data1 = LittleEndian.ToInt32 (m_input, 0x2C);
            int data2 = LittleEndian.ToInt32 (m_input, 0x30);

            for (int channel = 0; channel < m_channels; ++channel)
            {
                int channel_offset = 4 * m_channels;
                for (int i = 0; i < channel; ++i)
                    channel_offset += LittleEndian.ToInt32 (m_input, data1 + 4*i);
                int v21 = data1 + channel_offset;
                int bit_src = v21 + 12 + LittleEndian.ToInt32 (m_input, v21) + LittleEndian.ToInt32 (m_input, v21+4);
                int channel_size = LittleEndian.ToInt32 (m_input, v21 + 8);

                channel_offset = 4 * m_channels;
                for (int i = 0; i < channel; ++i)
                    channel_offset += LittleEndian.ToInt32 (m_input, data2 + 4*i);
                int data_src = data2 + channel_offset;

                for (int i = 0; i < 0x7DE; ++i)
                    m_lzss_frame[i] = 0;
                LzssUnpack (bit_src, data_src, plane, channel_size);

                int x_blocks = width >> 4;
                if (0 != (width & 0xF))
                    ++x_blocks;
                int y_blocks = height >> 4;
                if (0 != (height & 0xF))
                    ++y_blocks;

                if (0 == y_blocks || 0 == x_blocks)
                    continue;
                int plane_src = 0;
                bit_src = v21 + 12;
                int bit_mask = 128;
                data_src = bit_src + LittleEndian.ToInt32 (m_input, v21);
                int v68 = 16;
                for (int y = 0; y < y_blocks; ++y)
                {
                    int row = 16 * y;
                    int v66 = 16;
                    int dst_origin = m_stride * row + channel; // within m_output
                    for (int x = 0; x < x_blocks; ++x)
                    {
                        int dst = dst_origin;
                        int block_width  = v66 > width  ? width - 16 * x : 16;
                        int block_height = v68 > height ? height - row   : 16;
                        if (0 == bit_mask)
                        {
                            ++bit_src;
                            bit_mask = 128;
                        }
                        if (0 != (bit_mask & m_input[bit_src]))
                        {
                            byte b = m_input[data_src++];
                            for (int j = 0; j < block_height; ++j)
                            {
                                int v49 = dst;
                                for (int i = 0; i < block_width; ++i)
                                {
                                    m_output[v49] = b;
                                    v49 += 4;
                                }
                                dst += m_stride;
                            }
                        }
                        else
                        {
                            for (int j = 0; j < block_height; ++j)
                            {
                                int v49 = dst;
                                for (int i = 0; i < block_width; ++i)
                                {
                                    m_output[v49] = plane[plane_src++];
                                    v49 += 4;
                                }
                                dst += m_stride;
                            }
                        }
                        bit_mask >>= 1;
                        v66 += 16;
                        dst_origin += 64;
                    }
                    v68 += 16;
                }
            }
        }

        void UnpackV5 ()
        {
            m_output = new byte[m_stride * (int)m_info.Height];
            for (int i = 0; i < 4; ++i)
            {
                int bit_src  = 0x54 + LittleEndian.ToInt32 (m_input, 8 * i + 0x34);
                int data_src = 0x54 + LittleEndian.ToInt32 (m_input, 8 * i + 0x38);
                for (int j = 0; j < 0x7DE; ++j)
                    m_lzss_frame[j] = 0;
                int frame_offset = 0x7DE;
                byte accum = 0;
                int bit_mask = 128;
                int dst = i;
                while (dst < m_output.Length)
                {
                    if (0 == bit_mask)
                    {
                        ++bit_src;
                        bit_mask = 128;
                    }
                    if (0 != (bit_mask & m_input[bit_src]))
                    {
                        int v = LittleEndian.ToUInt16 (m_input, data_src);
                        data_src += 2;
                        int count = (v & 0x1F) + 3;
                        int offset = v >> 5;
                        for (int k = 0; k < count; ++k)
                        {
                            byte b = m_lzss_frame[(k + offset) & 0x7FF];
                            m_lzss_frame[frame_offset++] = b;
                            accum += b;
                            m_output[dst] = accum;
                            dst += 4;
                            frame_offset &= 0x7FF;
                        }
                    }
                    else
                    {
                        byte b = m_input[data_src++];
                        m_lzss_frame[frame_offset++] = b;
                        accum += b;
                        m_output[dst] = accum;
                        dst += 4;
                        frame_offset &= 0x7FF;
                    }
                    bit_mask >>= 1;
                }
            }
        }

        static readonly byte[] NameKeyV6 = {
            0xA6, 0x75, 0xF3, 0x9C, 0xC5, 0x69, 0x78, 0xA3, 0x3E, 0xA5, 0x4F, 0x79, 0x59, 0xFE, 0x3A, 0xC7,
        };

        void UnpackV6 ()
        {
            var name_bytes = new byte[0x20];
            int name_offset = 0x34;
            int i;
            for (i = 0; i < 0x20; ++i)
            {
                name_bytes[i] = (byte)(m_input[name_offset+i] ^ NameKeyV6[i & 0xF]);
                if (0 == name_bytes[i])
                    break;
            }
            m_output = LoadBaseImage (Encodings.cp932.GetString (name_bytes, 0, i) + ".pb3");
            BlendInput();
        }

        byte[] LoadBaseImage (string name)
        {
            // judging by the code, files with "pb3" extension could as well contain PNG or BMP images,
            // so we couldn't just shortcut to another instance of Pb3Reader here.

            var path = Path.GetDirectoryName (m_info.FileName);
            name = VFS.CombinePath (path, name);
            if (name.Equals (m_info.FileName, StringComparison.InvariantCultureIgnoreCase))
                throw new InvalidFormatException();
            // two files referencing each other still could create infinite recursion
            using (var base_file = VFS.OpenSeekableStream (name))
            {
                var image_data = ImageFormat.Read (name, base_file);
                int stride = image_data.Bitmap.PixelWidth * 4;
                var pixels = new byte[stride * image_data.Bitmap.PixelHeight];
                image_data.Bitmap.CopyPixels (pixels, stride, 0);
                return pixels;
            }
        }

        void LzssUnpack (int bit_src, int data_src, byte[] output, int output_size)
        {
            int dst = 0;
            int bit_mask = 0x80;
            int frame_offset = 0x7DE;
            while (dst < output_size)
            {
                if (0 == bit_mask)
                {
                    bit_mask = 0x80;
                    ++bit_src;
                }
                if (0 != (bit_mask & m_input[bit_src]))
                {
                    int v = LittleEndian.ToUInt16 (m_input, data_src);
                    data_src += 2;
                    int count = (v & 0x1F) + 3;
                    int offset = v >> 5;
                    for (int i = 0; i < count; ++i)
                    {
                        byte b = m_lzss_frame[(i + offset) & 0x7FF];
                        output[dst++] = b;
                        m_lzss_frame[frame_offset++] = b;
                        frame_offset &= 0x7FF;
                    }
                }
                else
                {
                    byte b = m_input[data_src++];
                    output[dst++] = b;
                    m_lzss_frame[frame_offset++] = b;
                    frame_offset &= 0x7FF;
                }
                bit_mask >>= 1;
            }
        }

        void BlendInput ()
        {
            int bit_src = 0x20 + LittleEndian.ToInt32 (m_input, 0xC);
            int data_src = bit_src + LittleEndian.ToInt32 (m_input, 0x2C);
            int overlay_size = LittleEndian.ToInt32 (m_input, 0x18);
            var overlay = new byte[overlay_size];
            LzssUnpack (bit_src, data_src, overlay, overlay_size);

            int width  = (int)m_info.Width;
            int height = (int)m_info.Height;
            bit_src = 8; // within overlay
            data_src = 8 + LittleEndian.ToInt32 (overlay, 0); // within overlay

            int bit_mask = 0x80;
            int x_blocks = width >> 3;
            if (0 != (width & 7))
                ++x_blocks;
            int y_blocks = height >> 3;
            if (0 != (height & 7))
                ++y_blocks;
            if (0 == x_blocks)
                return;
            int h = 0;
            int dst_origin = 0;
            while (y_blocks > 0)
            {
                int w = 0;
                for (int x = 0; x < x_blocks; ++x)
                {
                    if (0 == bit_mask)
                    {
                        ++bit_src;
                        bit_mask = 0x80;
                    }
                    if (0 == (bit_mask & overlay[bit_src]))
                    {
                        int dst = 8 * (dst_origin + 4 * x); // within m_output
                        int x_count = Math.Min (8, width - w);
                        int y_count = Math.Min (8, height - h);
                        for (int v30 = y_count; v30 > 0; --v30)
                        {
                            int count = 4 * x_count;
                            Buffer.BlockCopy (overlay, data_src, m_output, dst, count);
                            data_src += count;
                            dst += m_stride;
                        }
                    }
                    bit_mask >>= 1;
                    w += 8;
                }
                dst_origin += m_stride;
                h += 8;
                --y_blocks;
            }
        }
    }
}
