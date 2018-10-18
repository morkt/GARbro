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
using System.Windows.Media.Imaging;
using GameRes.Utility;

// [011130][KeroQ] Nijuubako
// [030131][KeroQ] Moekan

namespace GameRes.Formats.KeroQ
{
    internal class KgdMetaData : ImageMetaData
    {
        public byte BitsPerPlane;
        public byte ColorType;
    }

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
            byte bpp = header[0x11];
            byte color_type = header[0x12];
            switch (color_type)
            {
            case 2: bpp *= 3; break;
            case 3: bpp = 24; break;
            case 4: bpp *= 2; break;
            case 6: bpp *= 4; break;
            case 0: break;
            default: return null;
            }
            return new KgdMetaData {
                Width  = header.ToUInt32 (9),
                Height = header.ToUInt32 (0xD),
                BPP    = bpp,
                BitsPerPlane = header[0x11],
                ColorType    = color_type,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var png = new PngRestoreStream (file, (KgdMetaData)info))
            {
                var decoder = new PngBitmapDecoder (png,
                    BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                int pixel_size = (frame.Format.BitsPerPixel + 7) / 8;
                if (pixel_size < 3)
                {
                    frame.Freeze();
                    return new ImageData (frame, info);
                }
                int stride = frame.PixelWidth * pixel_size;
                var pixels = new byte[stride * frame.PixelHeight];
                frame.CopyPixels (pixels, stride, 0);
                for (int dst = 0; dst < pixels.Length; dst += stride)
                for (int i = 0; i < stride; i += pixel_size)
                {
                    byte r = pixels[dst+i];
                    pixels[dst+i] = pixels[dst+i+2];
                    pixels[dst+i+2] = r;
                }
                return ImageData.Create (info, frame.Format, frame.Palette, pixels, stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("KgdFormat.Write not implemented");
        }
    }

    internal class PngRestoreStream : InputProxyStream
    {
        IBinaryStream   m_input;
        bool            m_eof = false;
        byte[]          m_buffer = new byte[0x200C];
        int             m_buffer_pos = 0;
        int             m_buffer_size = 0;

        public PngRestoreStream (IBinaryStream input, KgdMetaData info) : base (input.AsStream, true)
        {
            m_input = input;
            PrepareHeader (info);
            m_input.Position = 0x19;
        }

        public override bool CanSeek { get { return false; } }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = 0;
            while (count > 0)
            {
                if (m_buffer_pos >= m_buffer_size)
                {
                    if (m_eof)
                        break;
                    FillBuffer();
                    if (0 == m_buffer_size)
                        break;
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
            if (m_buffer_pos >= m_buffer_size)
            {
                if (m_eof)
                    return -1;
                FillBuffer();
                if (0 == m_buffer_size)
                    return -1;
            }
            return m_buffer[m_buffer_pos++];
        }

        void FillBuffer ()
        {
            m_buffer_pos = m_buffer_size = 0;
            if (m_eof)
                return;
            if (m_input.PeekByte() == -1)
            {
                m_eof = true;
                BigEndian.Pack (0, m_buffer, 0);
                BigEndian.Pack (0x49454E44, m_buffer, 4); // 'IEND'
                BigEndian.Pack (0xAE426082, m_buffer, 8);
                m_buffer_size = 12;
                return;
            }
            int chunk_size = m_input.ReadInt32();
            int type = m_input.ReadByte();
            if (type != 2)
            {
                m_eof = true;
                return;
            }
            if (chunk_size + 12 > m_buffer.Length)
                m_buffer = new byte[chunk_size+12];
            chunk_size = m_input.Read (m_buffer, 8, chunk_size);
            BigEndian.Pack (chunk_size, m_buffer, 0);
            BigEndian.Pack (0x49444154, m_buffer, 4); // 'IDAT'
            uint checksum = Adler32.Compute (m_buffer, 8, chunk_size);
            BigEndian.Pack (checksum, m_buffer, 8+chunk_size);
            m_buffer_size = 12+chunk_size;
            return;
        }

        void PrepareHeader (KgdMetaData info)
        {
            Buffer.BlockCopy (PngFormat.HeaderBytes, 0, m_buffer, 0, 8);
            BigEndian.Pack (0x0D, m_buffer, 8);
            BigEndian.Pack (0x49484452, m_buffer, 0x0C); // 'IHDR'
            BigEndian.Pack (info.Width, m_buffer, 0x10);
            BigEndian.Pack (info.Height, m_buffer, 0x14);
            m_buffer[0x18] = info.BitsPerPlane;
            m_buffer[0x19] = info.ColorType;
            uint checksum = Adler32.Compute (m_buffer, 0x10, 0x0D);
            BigEndian.Pack (checksum, m_buffer, 0x1D);
            m_buffer_size = 0x21;
        }
    }
}
