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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Ainos
{
    public class CgMetaData : ImageMetaData
    {
        public int Type;
        public int Right, Bottom;
    }

    [Export(typeof(ArchiveFormat))]
    public class AniOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ANI"; } }
        public override string Description { get { return "Ainos engine animation resource"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint offset = file.View.ReadUInt32 (0);
            if (file.MaxOffset > int.MaxValue || offset >= file.MaxOffset || 0 != (offset & 3))
                return null;
            int frame_count = (int)(offset / 4);
            long index_offset = 4;
            var dir = new List<Entry>();
            for (int i = 0; i < frame_count; ++i)
            {
                uint next_offset;
                if (i+1 != frame_count)
                    next_offset = file.View.ReadUInt32 (index_offset);
                else
                    next_offset = (uint)file.MaxOffset;
                if (next_offset <= offset || next_offset > file.MaxOffset)
                    return null;
                uint size = next_offset - offset;
                if (size > 15)
                {
                    var entry = new Entry
                    {
                        Name = string.Format ("{0:D4}.cg", i),
                        Type = "image",
                        Offset = offset,
                        Size = size
                    };
                    dir.Add (entry);
                }
                offset = next_offset;
                index_offset += 4;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }

    [Export(typeof(ImageFormat))]
    public class CgFormat : ImageFormat
    {
        public override string         Tag { get { return "CG"; } }
        public override string Description { get { return "Ainos image format"; } }
        public override uint     Signature { get { return 0; } }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CgFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            int sig = stream.ReadByte();
            if (sig >= 0x20)
                return null;
            using (var input = new ArcView.Reader (stream))
            {
                int width  = input.ReadInt16();
                int height = input.ReadInt16();
                if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
                    return null;
                var meta = new CgMetaData
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    BPP = 24,
                    Type = sig,
                };
                if (0 != (sig & 0xf))
                {
                    meta.OffsetX = input.ReadInt16();
                    meta.OffsetY = input.ReadInt16();
                    meta.Right   = input.ReadInt16();
                    meta.Bottom  = input.ReadInt16();
                    if (meta.OffsetX > meta.Right || meta.OffsetY > meta.Bottom ||
                        meta.Right > width || meta.Bottom > height ||
                        meta.OffsetX < 0 || meta.OffsetY < 0)
                        return null;
                }
                return meta;
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as CgMetaData;
            if (0 != (meta.Type & 0xf))
                stream.Position = 13;
            else
                stream.Position = 5;
            using (var reader = new Reader (stream, meta))
            {
                if (meta.Type >= 0x10)
                    reader.UnpackRGB();
                else
                    reader.UnpackIndexed();
                byte[] pixels = reader.Data;
                var bitmap = BitmapSource.Create ((int)info.Width, (int)info.Height, 96, 96,
                    PixelFormats.Bgr24, null, pixels, (int)info.Width*3);
                bitmap.Freeze();
                return new ImageData (bitmap, info);
            }
        }

        internal class Reader : IDisposable
        {
            BinaryReader    m_input;
            byte[]          m_output;
            int             m_width;
            int             m_height;
            int             m_left;
            int             m_top;
            int             m_right;
            int             m_bottom;

            public byte[] Data { get { return m_output; } }

            public Reader (Stream file, CgMetaData info)
            {
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_left = info.OffsetX;
                m_top = info.OffsetY;
                m_right = info.Right == 0 ? m_width : info.Right;
                m_bottom = info.Bottom == 0 ? m_height : info.Bottom;
                m_output = new byte[3*m_width*m_height];
                m_input = new BinaryReader (file, Encoding.ASCII, true);
                ShiftTable = InitShiftTable();
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

            public void UnpackRGB ()
            {
                int right = 3 * (m_width * m_top + m_right);
                int left = 3 * (m_width * m_top + m_left);
                for (int i = m_top; i != m_bottom; ++i)
                {
                    int dst = left;
                    while (dst != right)
                    {
                        byte v9 = m_input.ReadByte();
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
                                byte v15 = m_input.ReadByte();
                                m_output[dst] = (byte)(((v9 << 1) + (v15 & 1)) << 1);
                                m_output[dst + 1] = (byte)(v15 & 0xfe);
                                m_output[dst + 2] = m_input.ReadByte();
                            }
                            dst += 3;
                            continue;
                        }
                        uint shift = (uint)v9 >> 4;
                        int count = v9 & 0xF;
                        if (0 == count)
                        {
                            count = (int)m_input.ReadByte() + 15;
                            if (270 == count)
                            {
                                int v12;
                                do
                                {
                                    v12 = m_input.ReadByte();
                                    count += v12;
                                }
                                while (v12 == 0xff);
                            }
                        }
                        if (0 != shift)
                        {
                            int src = dst + ShiftTable[shift];
                            while (0 != count)
                            {
                                m_output[dst] = m_output[src];
                                m_output[dst + 1] = m_output[src+1];
                                m_output[dst + 2] = m_output[src+2];
                                src += 3;
                                dst += 3;
                                --count;
                            }
                        }
                        else
                        {
                            dst += 3 * count;
                        }
                    }
                    left += m_width*3; //640*3;
                    right += m_width*3; //640*3;
                }
            }

            public void UnpackIndexed ()
            {
                byte[] palette = new byte[0x180];
                m_input.Read (palette, 0, 0x180);
                int right = 3 * (m_width * m_top + m_right);
                int left = 3 * (m_width * m_top + m_left); // 3 * (Rect.left + 640 * Rect.top);
                for (int i = m_top; i != m_bottom; ++i)
                {
                    int dst = left;
                    while (dst != right)
                    {
                        byte v13 = m_input.ReadByte();
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
                            count = m_input.ReadByte() + 15;
                            if (270 == count)
                            {
                                int v16;
                                do
                                {
                                    v16 = m_input.ReadByte();
                                    count += v16;
                                }
                                while (v16 == 0xff);
                            }
                        }
                        if (0 != shift)
                        {
                            int src = dst + ShiftTable[shift];
                            while (0 != count)
                            {
                                m_output[dst] = m_output[src];
                                m_output[dst + 1] = m_output[src + 1];
                                m_output[dst + 2] = m_output[src + 2];
                                src += 3;
                                dst += 3;
                                --count;
                            }
                        }
                        else
                        {
                            dst += 3 * count;
                        }
                    }
                    right += m_width*3;
                    left += m_width*3;
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
}
