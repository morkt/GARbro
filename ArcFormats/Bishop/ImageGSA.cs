//! \file       ImageGSA.cs
//! \date       2017 Dec 08
//! \brief      Bishop image format.
//
// Copyright (C) 2017 by morkt
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
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Bishop
{
    internal class GsaMetaData : ImageMetaData
    {
        public int  Type;
    }

    [Export(typeof(ImageFormat))]
    public class GsaFormat : ImageFormat
    {
        public override string         Tag { get { return "GSA"; } }
        public override string Description { get { return "Bishop image format"; } }
        public override uint     Signature { get { return 0x4D428E8C; } }

        public GsaFormat ()
        {
            var ext_list = Enumerable.Range (1, 12).Select (x => string.Format ("g{0:D2}", x))
                    .Concat (Enumerable.Range (1, 9).Select (x => string.Format ("gs{0}", x)));
            Extensions = Extensions.Concat (ext_list).ToArray();
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 0xC0;
            int type = file.ReadInt32();
            uint w = file.ReadUInt32();
            uint h = file.ReadUInt32();
            int x = file.ReadInt32();
            int y = file.ReadInt32();
            return new GsaMetaData {
                Width = w,
                Height = h,
                OffsetX = x,
                OffsetY = y,
                BPP = 32,
                Type = type,
            };
        }

        static readonly Regex PartFileNameRe = new Regex (@"\.G[01S]\d$", RegexOptions.IgnoreCase);

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var reader = new GsaReader (file, (GsaMetaData)info))
            {
                var pixels = reader.Unpack();
                if (PartFileNameRe.IsMatch (file.Name))
                {
                    var base_name = Path.ChangeExtension (file.Name, "GSA");
                    if (VFS.FileExists (base_name))
                    {
                        try
                        {
                            var image = TryBlendImage (base_name, reader, info);
                            if (image != null)
                                return image;
                        }
                        catch { /* ignore failed blending attempt */ }
                    }
                }
                return ImageData.CreateFlipped (info, reader.Format, null, pixels, reader.Stride);
            }
        }

        ImageData TryBlendImage (string base_name, GsaReader overlay, ImageMetaData overlay_info)
        {
            int ovl_x = overlay_info.OffsetX;
            int ovl_y = overlay_info.OffsetY;
            int ovl_width = (int)overlay_info.Width;
            int ovl_height = (int)overlay_info.Height;
            if (ovl_x < 0)
            {
                ovl_width += ovl_x;
                ovl_x = 0;
            }
            if (ovl_y < 0)
            {
                ovl_height += ovl_y;
                ovl_y = 0;
            }
            using (var input = VFS.OpenBinaryStream (base_name))
            {
                var base_info = ReadMetaData (input) as GsaMetaData;
                if (null == base_info)
                    return null;
                int base_width = (int)base_info.Width;
                int base_height = (int)base_info.Height;
                if (checked(ovl_x + ovl_width) > base_width)
                    ovl_width = base_width - ovl_x;
                if (checked(ovl_y + ovl_height) > base_height)
                    ovl_height = base_height - ovl_y;
                if (ovl_height <= 0 || ovl_width <= 0)
                    return null;

                input.Position = 0;
                var reader = new GsaReader (input, base_info);
                var base_pixels = reader.Unpack();

                int src_pixel_size = overlay.PixelSize;
                int dst_pixel_size = reader.PixelSize;
                int dst = ovl_y * reader.Stride + ovl_x * dst_pixel_size;
                int src = 0;
                for (int y = 0; y < ovl_height; ++y)
                {
                    int src_pixel = src;
                    int dst_pixel = dst;
                    for (int x = 0; x < ovl_width; ++x)
                    {
                        int src_alpha = overlay.Data[src_pixel+3];
                        if (src_alpha > 0)
                        {
                            if (0xFF == src_alpha)
                            {
                                Buffer.BlockCopy (overlay.Data, src_pixel, base_pixels, dst_pixel, dst_pixel_size);
                            }
                            else // assume destination has no alpha channel
                            {
                                base_pixels[dst_pixel+0] = (byte)((overlay.Data[src_pixel+0] * src_alpha
                                                         + base_pixels[dst_pixel+0] * (0xFF - src_alpha)) / 0xFF);
                                base_pixels[dst_pixel+1] = (byte)((overlay.Data[src_pixel+1] * src_alpha
                                                         + base_pixels[dst_pixel+1] * (0xFF - src_alpha)) / 0xFF);
                                base_pixels[dst_pixel+2] = (byte)((overlay.Data[src_pixel+2] * src_alpha
                                                         + base_pixels[dst_pixel+2] * (0xFF - src_alpha)) / 0xFF);
                            }
                        }
                        src_pixel += src_pixel_size;
                        dst_pixel += dst_pixel_size;
                    }
                    src += overlay.Stride;
                    dst += reader.Stride;
                }
                return ImageData.CreateFlipped (base_info, reader.Format, null, base_pixels, reader.Stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GsaFormat.Write not implemented");
        }
    }

    internal sealed class GsaReader : IDisposable
    {
        LsbBitStream    m_input;
        readonly int    m_type;
        readonly int    m_width;
        readonly int    m_height;
        int             m_stride;
        int             m_bpp;      // bytes per pixel
        byte[]          m_output;

        public PixelFormat Format { get; private set; }
        public int         Stride { get { return m_stride; } }
        public byte[]        Data { get { return m_output; } }
        public int      PixelSize { get { return m_bpp; } }

        public GsaReader (IBinaryStream file, GsaMetaData info)
        {
            m_input = new LsbBitStream (file.AsStream, true);
            m_type = info.Type;
            m_width = (int)info.Width;
            m_height = (int)info.Height;
        }

        public byte[] Unpack ()
        {
            m_input.Input.Position = 0xD8;
            if (3 == m_type || 0x83 == m_type)
                UnpackV3();
            else
                UnpackV4();
            return m_output;
        }

        void UnpackV3 ()
        {
            Format = PixelFormats.Bgr24;
            m_bpp = 3;
            m_stride = (m_width * 3 + 3) & ~3;
            int reserved_lines = (m_height + 1) & ~1;
            m_output = new byte[m_stride * reserved_lines];
            UnpackRgb();
        }

        void UnpackV4 ()
        {
            Format = PixelFormats.Bgra32;
            m_bpp = 4;
            m_stride = m_width * 4;
            int reserved_lines = (m_height + 1) & ~1;
            m_output = new byte[m_stride * reserved_lines];
            ReadPlane (3);
            UnpackRgb();
        }

        void UnpackRgb ()
        {
            for (int plane = 0; plane < 3; ++plane)
            {
                ReadPlane (plane);
            }
            if (0 != (m_type & 0x80))
            {
                int dst_row = 0;
                for (int y = 0; y < m_height; ++y)
                {
                    int dst = dst_row;
                    dst_row += m_stride;
                    for (int x = 0; x < m_width; ++x)
                    {
                        m_output[dst  ] <<= 3;
                        m_output[dst+1] <<= 2;
                        m_output[dst+2] <<= 2;
                        dst += m_bpp;
                    }
                }
            }
        }

        void ReadPlane (int dst_row)
        {
            for (int y = 0; y < m_height; y += 2)
            {
                int dst = dst_row;
                dst_row += 2 * m_stride;
                for (int x = 0; x < m_width; x += 2)
                {
                    switch (m_input.GetBits (3))
                    {
                    case 0: CopyBits0 (dst); break;
                    case 1: ReadBits1 (dst, 1, 0); break;
                    case 2: ReadBits1 (dst, 2, 0xFF); break;
                    case 3: ReadBits1 (dst, 3, 0xFD); break;
                    case 4: ReadBits4 (dst, 4, 0xF9); break;
                    case 5: ReadBits5 (dst, 6); break;
                    case 6: ReadBits5 (dst, 7); break;
                    case 7: ReadBits5 (dst, 8); break;
                    case -1: throw new EndOfStreamException();
                    }
                    dst += m_bpp * 2;
                }
            }
        }

        void CopyBits0 (int dst)
        {
            m_output[dst]                = m_output[dst-m_bpp*2];
            m_output[dst+m_bpp]          = m_output[dst-m_bpp];
            m_output[dst+m_stride]       = m_output[dst+m_stride-m_bpp*2];
            m_output[dst+m_stride+m_bpp] = m_output[dst+m_stride-m_bpp];
        }

        void ReadBits1 (int dst, int count, byte n)
        {
            m_output[dst]                = (byte)(n + m_input.GetBits (count) + m_output[dst-m_bpp*2]);
            m_output[dst+m_bpp]          = (byte)(n + m_input.GetBits (count) + m_output[dst-m_bpp]);
            m_output[dst+m_stride]       = (byte)(n + m_input.GetBits (count) + m_output[dst+m_stride-m_bpp*2]);
            m_output[dst+m_stride+m_bpp] = (byte)(n + m_input.GetBits (count) + m_output[dst+m_stride-m_bpp]);
        }

        void ReadBits4 (int dst, int count, byte n)
        {
            int prev = dst - 2 * m_stride;
            m_output[dst]                = (byte)(n + m_input.GetBits (count) + m_output[prev]);
            m_output[dst+m_bpp]          = (byte)(n + m_input.GetBits (count) + m_output[prev+m_bpp]);
            m_output[dst+m_stride]       = (byte)(n + m_input.GetBits (count) + m_output[dst-m_stride]);
            m_output[dst+m_stride+m_bpp] = (byte)(n + m_input.GetBits (count) + m_output[dst-m_stride+m_bpp]);
        }

        void ReadBits5 (int dst, int count)
        {
            m_output[dst]                = (byte)m_input.GetBits (count);
            m_output[dst+m_bpp]          = (byte)m_input.GetBits (count);
            m_output[dst+m_stride]       = (byte)m_input.GetBits (count);
            m_output[dst+m_stride+m_bpp] = (byte)m_input.GetBits (count);
        }

        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
    }
}
