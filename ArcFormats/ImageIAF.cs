//! \file       ImageIAF.cs
//! \date       Sun May 31 21:17:54 2015
//! \brief      IAF image format.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Triangle
{
    internal class IafMetaData : ImageMetaData
    {
        public int UnpackedSize;
        public int PackType;
    }

    [Export(typeof(ImageFormat))]
    public class IafFormat : BmpFormat
    {
        public override string         Tag { get { return "IAF"; } }
        public override string Description { get { return "Triangle compressed bitmap format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[12];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            int x = LittleEndian.ToInt32 (header, 0);
            int y = LittleEndian.ToInt32 (header, 4);
            if (Math.Abs (x) > 4096 || Math.Abs (y) > 4096)
                return null;
            int unpacked_size = LittleEndian.ToInt32 (header, 8);
            int pack_type = (unpacked_size >> 30) & 3;
            unpacked_size &= (int)~0xC0000000;
            byte[] bmp;
            if (0 == pack_type)
            {
                using (var reader = new LzssReader (stream, (int)stream.Length-12, 0x26))
                {
                    reader.Unpack();
                    bmp = reader.Data;
                }
            }
            else if (2 == pack_type)
            {
                using (var reader = new RleReader (stream, (int)stream.Length-12, 0x26))
                {
                    reader.Unpack();
                    bmp = reader.Data;
                }
            }
            else if (1 == pack_type)
            {
                bmp = new byte[0x26];
                stream.Read (bmp, 0, bmp.Length);
            }
            else
            {
                return null;
            }
            if (bmp[0] != 'B' && bmp[0] != 'C' || bmp[1] != 'M')
                return null;
            int width = LittleEndian.ToInt32 (bmp, 0x12);
            int height = LittleEndian.ToInt32 (bmp, 0x16);
            int bpp = LittleEndian.ToInt16 (bmp, 0x1c);
            return new IafMetaData
            {
                Width = (uint)width,
                Height = (uint)height,
                OffsetX = x,
                OffsetY = y,
                BPP = bpp,
                UnpackedSize = unpacked_size,
                PackType = pack_type,
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as IafMetaData;
            if (null == meta)
                throw new ArgumentException ("IafFormat.Read should be supplied with IafMetaData", "info");

            stream.Position = 12;
            int packed_size = (int)stream.Length-12;
            byte[] pixels;
            if (2 == meta.PackType)
            {
                using (var reader = new RleReader (stream, packed_size, meta.UnpackedSize))
                {
                    reader.Unpack();
                    pixels = reader.Data;
                }
            }
            else if (0 == meta.PackType)
            {
                using (var reader = new LzssReader (stream, packed_size, meta.UnpackedSize))
                {
                    reader.Unpack();
                    pixels = reader.Data;
                }
            }
            else
            {
                pixels = new byte[meta.UnpackedSize];
                if (pixels.Length != stream.Read (pixels, 0, pixels.Length))
                    throw new InvalidFormatException ("Unexpected end of file");
            }
            if ('C' == pixels[0])
            {
                pixels[0] = (byte)'B';
                if (info.BPP > 8)
                    pixels = ConvertCM (pixels, (int)info.Width, (int)info.Height, info.BPP);
            }
            using (var bmp = new MemoryStream (pixels))
                return base.Read (bmp, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("IafFormat.Write not implemented");
        }

        static byte[] ConvertCM (byte[] input, int width, int height, int bpp)
        {
            int src = LittleEndian.ToInt32 (input, 0x0a);
            int pixel_size = bpp / 8;
            var bitmap = new byte[input.Length];
            Buffer.BlockCopy (input, 0, bitmap, 0, src);
            int stride = width * pixel_size;
            int i = src;
            for (int p = 0; p < pixel_size; ++p)
            {
                for (int y = 0; y < height; ++y)
                {
                    int pixel = y * stride + p;
                    for (int x = 0; x < width; ++x)
                    {
                        bitmap[src+pixel] = input[i++];
                        pixel += pixel_size;
                    }
                }
            }
            return bitmap;
        }
    }

    internal class RleReader : IDataUnpacker, IDisposable
    {
        BinaryReader    m_input;
        byte[]          m_output;
        int             m_size;

        public byte[] Data { get { return m_output; } }

        public RleReader (Stream input, int input_length, int output_length)
        {
            m_input = new ArcView.Reader (input);
            m_output = new byte[output_length];
            m_size = input_length;
        }

        public void Unpack ()
        {
            int src = 0;
            int dst = 0;
            while (dst < m_output.Length && src < m_size)
            {
                byte ctl = m_input.ReadByte();
                ++src;
                if (0 == ctl)
                {
                    int count = m_input.ReadByte();
                    ++src;
                    count = Math.Min (count, m_output.Length - dst);
                    int read = m_input.Read (m_output, dst, count);
                    dst += count;
                    src += count;
                }
                else
                {
                    int count = ctl;
                    byte b = m_input.ReadByte();
                    ++src;
                    count = Math.Min (count, m_output.Length - dst);

                    for (int i = 0; i < count; i++)
                        m_output[dst++] = b;
                }
            }
        }

        #region IDisposable Members
        bool disposed = false;

        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    m_input.Dispose();
                }
                disposed = true;
            }
        }
        #endregion
    }
}
