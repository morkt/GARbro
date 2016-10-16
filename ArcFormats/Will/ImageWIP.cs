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
            Extensions = new string[] { "wip", "msk", "mos" };
        }

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
            if (meta.FrameCount > 1)
                Trace.WriteLine ("Extra frames ignored", info.FileName);
            file.Position = 8 + 24 * meta.FrameCount;
            Color[] palette = null;
            if (8 == meta.BPP)
            {
                var palette_data = new byte[0x400];
                if (palette_data.Length != file.Read (palette_data, 0, palette_data.Length))
                    throw new InvalidFormatException();
                palette = new Color[0x100];
                for (int i = 0; i < 0x100; ++i)
                {
                    palette[i] = Color.FromRgb (palette_data[i*4], palette_data[i*4+1], palette_data[i*4+2]);
                }
            }
            using (var reader = new Reader (file.AsStream, meta))
            {
                reader.Unpack();
                if (24 == meta.BPP)
                {
                    byte[] raw = reader.Data;
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
                    var bmp_palette = new BitmapPalette (palette);
                    return ImageData.Create (meta, PixelFormats.Indexed8, bmp_palette, pixels, (int)meta.Width);
                }
                else
                    throw new InvalidFormatException();
            }
        }

        internal sealed class Reader : IDisposable
        {
            private BinaryReader    m_input;
            private uint            m_length;
            private byte[]          m_data;

            public byte[] Data { get { return m_data; } }

            public Reader (Stream file, WipMetaData info)
            {
                m_length = info.FrameSize;
//                int stride = (int)info.Width*((info.BPP+7)/8);
                int stride = (int)info.Width*4;
                m_data = new byte[stride * (int)info.Height];
                m_input = new ArcView.Reader (file);
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
                        control = m_input.ReadByte() | 0xFF00;
                        --length;
                    }
                    if (0 != (control & 1))
                    {
                        if (length < 1)
                            throw new InvalidFormatException();
                        byte b = m_input.ReadByte();
                        --length;
                        m_data[dst++] = b;
                        m_window[window_index++] = b;
                        window_index &= 0xfff;
                    }
                    else
                    {
                        if (length < 2)
                            throw new InvalidFormatException();
                        int hi = m_input.ReadByte();
                        int lo = m_input.ReadByte();
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
            bool disposed = false;

            public void Dispose ()
            {
                if (!disposed)
                {
                    m_input.Dispose();
                    disposed = true;
                }
                GC.SuppressFinalize (this);
            }
            #endregion
        }
    }
}
