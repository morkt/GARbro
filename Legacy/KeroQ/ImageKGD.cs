//! \file       ImageKGD.cs
//! \date       2018 Aug 22
//! \brief      KeroQ image format.
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
using GameRes.Compression;

// [011130][KeroQ] Nijuubako

namespace GameRes.Formats.KeroQ
{
    [Export(typeof(ImageFormat))]
    public class KgdFormat : ImageFormat
    {
        public override string         Tag { get { return "KGD"; } }
        public override string Description { get { return "KeroQ image format"; } }
        public override uint     Signature { get { return 0x44474B89; } } // '\x89KGD'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x19);
            if (header.ToInt32 (4) != 0x10 || header[8] != 1)
                return null;
            return new ImageMetaData {
                Width  = header.ToUInt32 (9),
                Height = header.ToUInt32 (0xD),
                BPP = 2 == header[0x12] ? 24 : 32,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x19;
            using (var packed = new KgdStream (file))
            using (var input = new ZLibStream (packed, CompressionMode.Decompress))
            {
                int stride = ((int)info.Width * info.BPP + 7) / 8;
                int pixel_size = (info.BPP + 7) / 8;
                var buffer = new byte[stride+1];
                var prev_line = new byte[stride];
                var pixels = new byte[stride * (int)info.Height];
                int dst = 0;
                for (uint i = 0; i < info.Height; ++i)
                {
                    if (input.Read (buffer, 0, buffer.Length) == 0)
                        break;
                    switch (buffer[0])
                    {
                    case 1: // PNG_FILTER_VALUE_SUB
                        for (int j = pixel_size; j < stride; ++j)
                        {
                            buffer[1+j] += buffer[1+j-pixel_size];
                        }
                        break;

                    case 2: // PNG_FILTER_VALUE_UP
                        for (int j = 0; j < stride; ++j)
                        {
                            buffer[1+j] += prev_line[j];
                        }
                        break;

                    case 3: // PNG_FILTER_VALUE_AVG
                        for (int j = 0; j < pixel_size; ++j)
                        {
                            buffer[1+j] += (byte)(prev_line[j] >> 1);
                        }
                        for (int j = pixel_size; j < stride; ++j)
                        {
                            int v = (prev_line[j] + buffer[1+j-pixel_size]) >> 1;
                            buffer[1+j] += (byte)v;
                        }
                        break;

                    case 4: // PNG_FILTER_VALUE_PAETH
                        for (int j = 0; j < pixel_size; ++j)
                        {
                            buffer[1+j] += prev_line[j];
                        }
                        int src = 1;
                        for (int j = pixel_size; j < stride; ++j)
                        {
                            byte y = prev_line[j];
                            byte x = buffer[src++];
                            byte z = prev_line[j-pixel_size];
                            int yz = y - z;
                            int xz = x - z;
                            int ayz = Math.Abs (yz);
                            int axz = Math.Abs (xz);
                            int axy = Math.Abs (xz + yz);
                            if (!(ayz > axz || ayz > axy))
                                z = x;
                            else if (axz <= axy)
                                z = y;
                            buffer[1+j] += (byte)z;
                        }
                        break;

                    case 0:
                        break;
                    }
                    Buffer.BlockCopy (buffer, 1, prev_line, 0, stride);
                    Buffer.BlockCopy (buffer, 1, pixels, dst, stride);
                    dst += stride;
                }
                PixelFormat format = 24 == info.BPP ? PixelFormats.Bgr24 : PixelFormats.Bgra32;
                return ImageData.Create (info, PixelFormats.Bgr24, null, pixels, stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("KgdFormat.Write not implemented");
        }
    }

    internal class KgdStream : InputProxyStream
    {
        IBinaryStream   m_input;
        bool            m_eof = false;
        byte[]          m_buffer = new byte[0x2000];
        int             m_buffer_pos = 0;
        int             m_buffer_size = 0;

        public KgdStream (IBinaryStream input) : base (input.AsStream, true)
        {
            m_input = input;
        }

        public override bool CanSeek { get { return false; } }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = 0;
            while (!m_eof && count > 0)
            {
                if (m_buffer_pos >= m_buffer_size)
                {
                    FillBuffer();
                    continue;
                }
                int avail = Math.Min (count, m_buffer_size - m_buffer_pos);
                Buffer.BlockCopy (m_buffer, m_buffer_pos, buffer, offset, avail);
                m_buffer_pos += avail;
                offset += avail;
                count -= avail;
                read += avail;
            }
            return read;
        }

        public override int ReadByte ()
        {
            if (m_eof)
                return -1;
            if (m_buffer_pos >= m_buffer_size)
            {
                FillBuffer();
                if (m_eof)
                    return -1;
            }
            return m_buffer[m_buffer_pos++];
        }

        void FillBuffer ()
        {
            if (m_input.PeekByte() == -1)
            {
                m_eof = true;
                return;
            }
            int chunk_size = m_input.ReadInt32();
            int type = m_input.ReadByte();
            if (type != 2)
            {
                m_eof = true;
                return;
            }
            if (chunk_size > m_buffer.Length)
                m_buffer = new byte[chunk_size];
            m_buffer_size = m_input.Read (m_buffer, 0, chunk_size);
            m_buffer_pos = 0;
        }
    }
}
