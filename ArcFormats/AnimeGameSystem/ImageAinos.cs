//! \file       ImageAinos.cs
//! \date       Sun Apr 05 04:16:27 2015
//! \brief      Ainos image format.
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
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Ags
{
    public class CgMetaData : ImageMetaData
    {
        public int Type;
        public int Right, Bottom;
    }

    [Export(typeof(ImageFormat))]
    [ExportMetadata("Priority", -1)]
    public class CgFormat : ImageFormat
    {
        public override string         Tag { get { return "CG"; } }
        public override string Description { get { return "Anime Game System image format"; } }
        public override uint     Signature { get { return 0; } }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CgFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            int sig = file.ReadByte();
            if (sig >= 0x20)
                return null;
            int width  = file.ReadInt16();
            int height = file.ReadInt16();
            if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
                return null;
            var meta = new CgMetaData
            {
                Width = (uint)width,
                Height = (uint)height,
                BPP = 24,
                Type = sig,
            };
            if (0 != (sig & 7))
            {
                meta.OffsetX = file.ReadInt16();
                meta.OffsetY = file.ReadInt16();
                meta.Right   = file.ReadInt16();
                meta.Bottom  = file.ReadInt16();
                if (meta.OffsetX > meta.Right || meta.OffsetY > meta.Bottom ||
                    meta.Right > width || meta.Bottom > height ||
                    meta.OffsetX < 0 || meta.OffsetY < 0)
                    return null;
            }
            return meta;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var reader = new Reader (stream, (CgMetaData)info))
                return reader.Image;
        }

        internal sealed class Reader : IImageDecoder
        {
            IBinaryStream   m_input;
            ImageData       m_image;
            byte[]          m_output;
            int             m_type;
            int             m_width;
            int             m_height;
            int             m_left;
            int             m_top;
            int             m_right;
            int             m_bottom;
            bool            m_should_dispose;

            public Stream            Source { get { m_input.Position = 0; return m_input.AsStream; } }
            public ImageFormat SourceFormat { get { return null; } }
            public ImageMetaData       Info { get; private set; }
            public ImageData Image
            {
                get
                {
                    if (null == m_image)
                    {
                        Unpack();
                        m_image = ImageData.Create (Info, PixelFormats.Bgr24, null, m_output, m_width*3);
                    }
                    return m_image;
                }
            }
            public byte[] Data { get { return m_output; } }

            public Reader (IBinaryStream file, CgMetaData info) : this (file, info, null, true)
            {
            }

            public Reader (IBinaryStream file, CgMetaData info, byte[] base_image, bool leave_open = false)
            {
                m_type = info.Type;
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_left = info.OffsetX;
                m_top = info.OffsetY;
                m_right = info.Right == 0 ? m_width : info.Right;
                m_bottom = info.Bottom == 0 ? m_height : info.Bottom;
                m_output = base_image ?? CreateBackground();
                m_input = file;
                Info = info;
                ShiftTable = InitShiftTable();

                if (0 != (info.Type & 7))
                    m_input.Position = 13;
                else
                    m_input.Position = 5;
                m_should_dispose = !leave_open;
            }

            static readonly short[] ShiftX = new short[] { // 409b6c
                0, -1, -3, -2, -1, 0, 1, 2
            };
            static readonly short[] ShiftY = new short[] { // 409b7c
                0, 0, -1, -1, -1, -1, -1, -1
            };
            readonly int[] ShiftTable;

            private int[] InitShiftTable ()
            {
                var table = new int[8];
                for (int i = 0; i < 8; ++i)
                {
                    table[i] = 3 * (ShiftX[i] + ShiftY[i] * m_width);
                }
                return table;
            }

            private byte[] CreateBackground ()
            {
                var bg = new byte[3*m_width*m_height];
                for (int i = 1; i < bg.Length; i += 3)
                    bg[i] = 0xFF;
                return bg;
            }

            public void Unpack ()
            {
                if (0 != (m_type & 0x10))
                    UnpackRGB();
                else
                    UnpackIndexed();
            }

            public void UnpackRGB ()
            {
                int right = 3 * (m_width * m_top + m_right);
                int left = 3 * (m_width * m_top + m_left);
                for (int i = m_top; i != m_bottom; ++i)
                {
                    int dst = left;
                    while (dst != right)
                    {
                        byte v9 = m_input.ReadUInt8();
                        if (0 != (v9 & 0x80))
                        {
                            if (0 != (v9 & 0x40))
                            {
                                m_output[dst] = (byte)(m_output[dst - 3] + ((v9 >> 3) & 6) - 2);
                                m_output[dst + 1] = (byte)(m_output[dst - 2] + ((v9 >> 1) & 6) - 2);
                                m_output[dst + 2] = (byte)(m_output[dst - 1] + ((v9 & 3) + 127) * 2);
                            }
                            else
                            {
                                byte v15 = m_input.ReadUInt8();
                                m_output[dst] = (byte)(((v9 << 1) + (v15 & 1)) << 1);
                                m_output[dst + 1] = (byte)(v15 & 0xfe);
                                m_output[dst + 2] = m_input.ReadUInt8();
                            }
                            dst += 3;
                            continue;
                        }
                        uint shift = (uint)v9 >> 4;
                        int count = v9 & 0xF;
                        if (0 == count)
                        {
                            count = (int)m_input.ReadUInt8() + 15;
                            if (270 == count)
                            {
                                int v12;
                                do
                                {
                                    v12 = m_input.ReadUInt8();
                                    count += v12;
                                }
                                while (v12 == 0xff);
                            }
                        }
                        if (0 != shift)
                        {
                            int src = dst + ShiftTable[shift];
                            Binary.CopyOverlapped (m_output, src, dst, count * 3);
                        }
                        dst += 3 * count;
                    }
                    left += m_width*3; //640*3;
                    right += m_width*3; //640*3;
                }
            }

            public void UnpackIndexed ()
            {
                byte[] palette = m_input.ReadBytes (0x180);
                int right = 3 * (m_width * m_top + m_right);
                int left = 3 * (m_width * m_top + m_left); // 3 * (Rect.left + 640 * Rect.top);
                for (int i = m_top; i != m_bottom; ++i)
                {
                    int dst = left;
                    while (dst != right)
                    {
                        byte v13 = m_input.ReadUInt8();
                        if (0 != (v13 & 0x80))
                        {
                            int color = 3 * (v13 & 0x7F);
                            m_output[dst] = palette[color];
                            m_output[dst+1] = palette[color+1];
                            m_output[dst+2] = palette[color+2];
                            dst += 3;
                            continue;
                        }
                        uint shift = (uint)v13 >> 4;
                        int count = v13 & 0xF;
                        if (0 == count)
                        {
                            count = m_input.ReadUInt8() + 15;
                            if (270 == count)
                            {
                                int v16;
                                do
                                {
                                    v16 = m_input.ReadUInt8();
                                    count += v16;
                                }
                                while (v16 == 0xff);
                            }
                        }
                        if (0 != shift)
                        {
                            int src = dst + ShiftTable[shift];
                            Binary.CopyOverlapped (m_output, src, dst, count * 3);
                        }
                        dst += 3 * count;
                    }
                    right += m_width*3;
                    left += m_width*3;
                }
            }

            #region IDisposable Members
            bool m_disposed = false;
            public void Dispose ()
            {
                if (!m_disposed)
                {
                    if (m_should_dispose)
                    {
                        m_input.Dispose();
                    }
                    m_disposed = true;
                }
                GC.SuppressFinalize (this);
            }
            #endregion
        }
    }
}
