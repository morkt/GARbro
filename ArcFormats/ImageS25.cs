//! \file       ImageS25.cs
//! \date       Sat Apr 18 17:00:54 2015
//! \brief      ShiinaRio S25 multi-image format.
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
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.ShiinaRio
{
    internal class S25MetaData : ImageMetaData
    {
        public uint FirstOffset;
    }

    [Export(typeof(ImageFormat))]
    public class S25Format : ImageFormat
    {
        public override string         Tag { get { return "S25"; } }
        public override string Description { get { return "ShiinaRio image format"; } }
        public override uint     Signature { get { return 0x00353253; } } // 'S25'

        // in current implementation, only the first frame is returned.
        // per-frame access is provided by S25Opener class.

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            using (var input = new ArcView.Reader (stream))
            {
                input.ReadUInt32();
                int count = input.ReadInt32();
                if (count < 0 || count > 0xfffff)
                    return null;
                uint first_offset = input.ReadUInt32();
                if (0 == first_offset)
                    return null;
                input.BaseStream.Position = first_offset;
                var info = new S25MetaData();
                info.Width = input.ReadUInt32();
                info.Height = input.ReadUInt32();
                info.OffsetX = input.ReadInt32();
                info.OffsetY = input.ReadInt32();
                info.FirstOffset = first_offset+0x14;
                info.BPP = 32;
                return info;
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as S25MetaData;
            if (null == meta)
                throw new ArgumentException ("S25Format.Read should be supplied with S25MetaData", "info");

            using (var reader = new Reader (stream, meta))
            {
                var pixels = reader.Unpack();
                return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("S25Format.Write not implemented");
        }

        internal class Reader : IDisposable
        {
            BinaryReader    m_input;
            int             m_width;
            int             m_height;
            uint            m_origin;
            uint[]          m_rows;
            byte[]          m_output;

            public byte[]   Data { get { return m_output; } }

            public Reader (Stream file, S25MetaData info)
            {
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_rows = new uint[m_height];
                m_output = new byte[m_width * m_height * 4];
                m_input = new ArcView.Reader (file);
                m_origin = info.FirstOffset;
            }

            public byte[] Unpack ()
            {
                m_input.BaseStream.Position = m_origin;
                for (int i = 0; i < m_rows.Length; ++i)
                    m_rows[i] = m_input.ReadUInt32();
                int dst = 0;
                int stride = m_width * 4;
                for (int y = 0; y < m_height && dst != m_output.Length; ++y)
                {
                    byte b, g, r, a;
                    uint row_pos = m_rows[y];
                    m_input.BaseStream.Position = row_pos;
                    m_input.ReadUInt16();
                    for (int x = m_width; x > 0 && dst != m_output.Length; )
                    {
                        if (0 != (row_pos & 1))
                        {
                            m_input.ReadByte();
                            ++row_pos;
                        }
                        int count = m_input.ReadUInt16();
                        row_pos += 2;
                        int method = count >> 13;
                        int skip = (count & 0x1800) >> 11;
                        if (0 != skip)
                        {
                            m_input.BaseStream.Seek (skip, SeekOrigin.Current);
                            row_pos += (uint)skip;
                        }
                        count &= 0x7ff;
                        if (count > x) count = x;
                        x -= count;
                        if (0 == method || 1 == method)
                        {
                            dst += count * 4;
                        }
                        else if (2 == method)
                        {
                            for (int i = 0; i < count; ++i)
                            {
                                m_output[dst++] = m_input.ReadByte();
                                m_output[dst++] = m_input.ReadByte();
                                m_output[dst++] = m_input.ReadByte();
                                m_output[dst++] = 0xff;
                                row_pos += 3;
                            }
                        }
                        else if (3 == method)
                        {
                            b = m_input.ReadByte();
                            g = m_input.ReadByte();
                            r = m_input.ReadByte();
                            row_pos += 3;
                            for (int i = 0; i < count; ++i)
                            {
                                m_output[dst++] = b;
                                m_output[dst++] = g;
                                m_output[dst++] = r;
                                m_output[dst++] = 0xff;
                            }
                        } 
                        else if (4 == method)
                        {
                            for (int i = 0; i < count; ++i)
                            {
                                a = m_input.ReadByte();
                                m_output[dst] += (byte)((m_input.ReadByte() - m_output[dst]) * a / 256);
                                ++dst;
                                m_output[dst] += (byte)((m_input.ReadByte() - m_output[dst]) * a / 256);
                                ++dst;
                                m_output[dst] += (byte)((m_input.ReadByte() - m_output[dst]) * a / 256);
                                ++dst;
                                m_output[dst++] = a;
                                row_pos += 4;
                            }
                        }
                        else
                        {
                            row_pos += 4;
                            a = m_input.ReadByte();
                            b = m_input.ReadByte();
                            g = m_input.ReadByte();
                            r = m_input.ReadByte();
                            for (int i = 0; i < count; ++i)
                            {
                                m_output[dst] += (byte)((b - m_output[dst]) * a / 256);
                                ++dst;
                                m_output[dst] += (byte)((g - m_output[dst]) * a / 256);
                                ++dst;
                                m_output[dst] += (byte)((r - m_output[dst]) * a / 256);
                                ++dst;
                                m_output[dst++] = a;
                            }
                        }
                    }
                }
                return m_output;
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
                        m_input.Dispose();
                    disposed = true;
                }
            }
            #endregion
        }
    }
}
