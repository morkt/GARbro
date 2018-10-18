//! \file       ImageWIP.cs
//! \date       Fri Oct 31 14:52:49 2014
//! \brief      Will image format implementation.
//
// Copyright (C) 2014 by morkt
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
using System.Text;
using System.Diagnostics;
using System.ComponentModel.Composition;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Will
{
    internal class WipMetaData : ImageMetaData
    {
        public int  FrameCount;
        public uint FrameSize;
    }

    [Export(typeof(ImageFormat))]
    public class WipFormat : ImageFormat
    {
        public override string         Tag { get { return "WIP"; } }
        public override string Description { get { return "Will Co. image format"; } }
        public override uint     Signature { get { return 0x46504957u; } } // 'WIPF'

        public WipFormat ()
        {
            Extensions = new string[] { "wip", "wi0", "msk", "mos" };
        }

        public bool ApplyMask = false;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 4;
            int frames  = file.ReadUInt16();
            int bpp     = file.ReadUInt16();
            uint width  = file.ReadUInt32();
            uint height = file.ReadUInt32();
            int x = file.ReadInt32();
            int y = file.ReadInt32();
            file.ReadInt32(); // 0
            uint frame_size = file.ReadUInt32();
            if (24 != bpp && 8 != bpp)
            {
                Trace.WriteLine ("unsupported bpp", "WipFormat");
                return null;
            }
            return new WipMetaData
            {
                Width   = width,
                Height  = height,
                OffsetX = x,
                OffsetY = y,
                BPP     = bpp,
                FrameCount  = frames,
                FrameSize   = frame_size,
            };
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("WipFormat.Write not implemented");
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (WipMetaData)info;
            using (var reader = new Reader (file, meta))
            {
                reader.Unpack();
                if (24 == meta.BPP)
                {
                    byte[] raw = reader.Data;
                    if (ApplyMask && !meta.FileName.HasExtension (".msk"))
                    {
                        var mask_name = Path.ChangeExtension (meta.FileName, "msk");
                        if (VFS.FileExists (mask_name))
                        {
                            try
                            {
                                return ApplyMaskToWipData (meta, raw, mask_name);
                            }
                            catch { /* ignore mask read errors */ }
                        }
                    }
                    int size = (int)meta.Width * (int)meta.Height;
                    byte[] pixels = new byte[size*3];
                    int dst = 0;
                    for (int i = 0; i < size; ++i)
                    {
                        pixels[dst++] = raw[i];
                        pixels[dst++] = raw[i+size];
                        pixels[dst++] = raw[i+size*2];
                    }
                    return ImageData.Create (meta, PixelFormats.Bgr24, null, pixels, (int)meta.Width*3);
                }
                else if (8 == meta.BPP)
                {
                    byte[] pixels = reader.Data;
                    var bmp_palette = new BitmapPalette (reader.Palette);
                    return ImageData.Create (meta, PixelFormats.Indexed8, bmp_palette, pixels, (int)meta.Width);
                }
                else
                    throw new InvalidFormatException();
            }
        }

        ImageData ApplyMaskToWipData (ImageMetaData info, byte[] image, string mask_name)
        {
            using (var mask_file = VFS.OpenBinaryStream (mask_name))
            {
                if (mask_file.Signature != Signature)
                    throw new InvalidFormatException();
                var mask_info = ReadMetaData (mask_file) as WipMetaData;
                if (null == mask_info || 8 != mask_info.BPP
                    || info.Width != mask_info.Width || info.Height != mask_info.Height)
                    throw new InvalidFormatException();
                using (var reader = new Reader (mask_file, mask_info))
                {
                    reader.Unpack();
                    var palette = reader.Palette;
                    int dst_stride = (int)info.Width * 4;
                    var pixels = new byte[dst_stride * (int)info.Height];
                    int plane_size = (int)info.Width * (int)info.Height;
                    var alpha = reader.Data;
                    int dst = 0;
                    for (int src = 0; src < plane_size; ++src)
                    {
                        pixels[dst++] = image[src];
                        pixels[dst++] = image[src+plane_size];
                        pixels[dst++] = image[src+plane_size*2];
                        var color = palette[alpha[src]];
                        pixels[dst++] = (byte)((color.B + color.G + color.R) / 3);
                    }
                    return ImageData.Create (info, PixelFormats.Bgra32, null, pixels, dst_stride);
                }
            }
        }

        internal sealed class Reader : IDisposable
        {
            private IBinaryStream   m_input;
            private uint            m_length;
            private byte[]          m_data;

            public byte[]     Data { get { return m_data; } }
            public Color[] Palette { get; private set; }

            public Reader (IBinaryStream file, WipMetaData info)
            {
                m_length = info.FrameSize;
//                int stride = (int)info.Width*((info.BPP+7)/8);
                int stride = (int)info.Width*4;
                m_data = new byte[stride * (int)info.Height];
                m_input = file;
                m_input.Position = 8 + 24 * info.FrameCount;
                if (8 == info.BPP)
                    Palette = ReadColorMap (file.AsStream, 0x100, PaletteFormat.RgbX);
            }

            private byte[] m_window = new byte[0x1000];

            public void Unpack ()
            {
                int dst = 0;
                int window_index = 1;
                int control = 0;
                for (int length = (int)m_length; length > 0; )
                {
                    control >>= 1;
                    if (0 == (control & 0x100))
                    {
                        control = m_input.ReadUInt8() | 0xFF00;
                        --length;
                    }
                    if (0 != (control & 1))
                    {
                        if (length < 1)
                            throw new InvalidFormatException();
                        byte b = m_input.ReadUInt8();
                        --length;
                        m_data[dst++] = b;
                        m_window[window_index++] = b;
                        window_index &= 0xfff;
                    }
                    else
                    {
                        if (length < 2)
                            throw new InvalidFormatException();
                        int hi = m_input.ReadUInt8();
                        int lo = m_input.ReadUInt8();
                        length -= 2;
                        int offset = hi << 4 | lo >> 4;
                        for (int count = (lo & 0xF) + 2; count > 0; --count)
                        {
                            byte b = m_window[offset++ & 0xfff];
                            m_data[dst++] = b;
                            m_window[window_index++] = b;
                            window_index &= 0xfff;
                        }
                    }
                }
            }

            #region IDisposable Members
            public void Dispose ()
            {
            }
            #endregion
        }
    }
}
