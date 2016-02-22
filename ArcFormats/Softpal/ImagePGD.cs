//! \file       ImagePGD.cs
//! \date       Sat Feb 13 13:56:06 2016
//! \brief      Image format used by subsidiaries of Amuse Craft (former Softpal).
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

namespace GameRes.Formats.Softpal
{
    [Export(typeof(ImageFormat))]
    public class Pgd11Format : ImageFormat
    {
        public override string         Tag { get { return "PGD/11_C"; } }
        public override string Description { get { return "Image format used by Softpal subsidiaries"; } }
        public override uint     Signature { get { return 0x1C4547; } } // 'GE\x1C'

        public Pgd11Format ()
        {
            Extensions = new string[] { "pgd" };
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x20];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            if (!Binary.AsciiEqual (header, 0x1C, "11_C"))
                return null;
            return new ImageMetaData
            {
                Width   = LittleEndian.ToUInt32 (header, 0x0C),
                Height  = LittleEndian.ToUInt32 (header, 0x10),
                OffsetX = LittleEndian.ToInt32 (header, 4),
                OffsetY = LittleEndian.ToInt32 (header, 8),
                BPP     = 32,
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            using (var reader = new PgdReader (stream, 0x20))
            {
                var planes = reader.Unpack11();
                var pixels = new byte[planes.Length];
                int plane_size = (int)info.Width*(int)info.Height;
                int b_src = 0;
                int g_src = b_src+plane_size;
                int r_src = g_src+plane_size;
                int alpha_src = r_src+plane_size;
                int dst = 0;
                while (dst < pixels.Length)
                {
                    pixels[dst++] = planes[b_src++];
                    pixels[dst++] = planes[g_src++];
                    pixels[dst++] = planes[r_src++];
                    pixels[dst++] = planes[alpha_src++];
                }
                return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("Pgd11Format.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class Pgd00Format : ImageFormat
    {
        public override string         Tag { get { return "PGD/00_C"; } }
        public override string Description { get { return "Image format used by Softpal subsidiaries"; } }
        public override uint     Signature { get { return 0; } }

        public Pgd00Format ()
        {
            Extensions = new string[] { "pgd" };
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x24];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            if (!Binary.AsciiEqual (header, 0x18, "00_C"))
                return null;
            return new ImageMetaData
            {
                Width   = LittleEndian.ToUInt32 (header, 8),
                Height  = LittleEndian.ToUInt32 (header, 12),
                OffsetX = LittleEndian.ToInt32 (header, 0),
                OffsetY = LittleEndian.ToInt32 (header, 4),
                BPP     = 32,
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            using (var reader = new PgdReader (stream, 0x1C))
            {
                var data = reader.Unpack00();
                using (var tga = new MemoryStream (data))
                {
                    var tga_info = Tga.ReadMetaData (tga);
                    if (null == tga_info)
                        throw new InvalidFormatException();
                    tga.Position = 0;
                    return Tga.Read (tga, tga_info);
                }
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("Pgd00Format.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class PgdTgaFormat : TgaFormat
    {
        public override string         Tag { get { return "PGD/TGA"; } }
        public override string Description { get { return "Image format used by Softpal subsidiaries"; } }
        public override uint     Signature { get { return 0; } }

        public PgdTgaFormat ()
        {
            Extensions = new string[] { "pgd" };
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x2A];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            int x = LittleEndian.ToInt32 (header, 0);
            int y = LittleEndian.ToInt32 (header, 4);
            if (Math.Abs (x) > 0x2000 || Math.Abs (y) > 0x2000)
                return null;
            uint width  = LittleEndian.ToUInt32 (header, 8);
            uint height = LittleEndian.ToUInt32 (header, 12);
            if (0 == width || 0 == height
                || width != LittleEndian.ToUInt16 (header, 0x24)
                || height != LittleEndian.ToUInt16 (header, 0x26))
                return null;
            stream.Position = 0x18;
            var tga_info = base.ReadMetaData (stream);
            if (null == tga_info)
                return null;
            tga_info.OffsetX = x;
            tga_info.OffsetY = y;
            return tga_info;
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            using (var tga = new StreamRegion (stream, 0x18))
                return base.Read (tga, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("PgdTgaFormat.Write not implemented");
        }
    }

    internal sealed class PgdReader : IDisposable
    {
        BinaryReader        m_input;
        byte[]              m_output;

        public byte[] Data { get { return m_output; } }

        public PgdReader (Stream input, int position)
        {
            input.Position = position;
            m_input = new ArcView.Reader (input);
            int unpacked_size = m_input.ReadInt32();
            m_input.ReadInt32(); // packed_size
            m_output = new byte[unpacked_size];
        }

        public byte[] Unpack00 ()
        {
            return Unpack (3000);
        }

        public byte[] Unpack11 ()
        {
            return Unpack (0xFFC);
        }

        byte[] Unpack (int look_behind)
        {
            int dst = 0;
            int ctl = 2;
            while (dst < m_output.Length)
            {
                ctl >>= 1;
                if (1 == ctl)
                {
                    ctl = m_input.ReadByte() | 0x100;
                }
                int count;
                if (0 != (ctl & 1))
                {
                    int src = m_input.ReadUInt16();
                    count = m_input.ReadByte();
                    if (dst > look_behind)
                        src += dst - look_behind;
                    Binary.CopyOverlapped (m_output, src, dst, count);
                }
                else
                {
                    count = m_input.ReadByte();
                    m_input.Read (m_output, dst, count);
                }
                dst += count;
            }
            return m_output;
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
