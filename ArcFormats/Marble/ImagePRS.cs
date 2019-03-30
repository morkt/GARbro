//! \file       ImagePRS.cs
//! \date       Sat Mar 28 00:15:43 2015
//! \brief      Marble engine image format.
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

namespace GameRes.Formats.Marble
{
    internal class PrsMetaData : ImageMetaData
    {
        public byte Flag;
        public uint PackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class PrsFormat : ImageFormat
    {
        public override string         Tag { get { return "PRS"; } }
        public override string Description { get { return "Marble engine image format"; } }
        public override uint     Signature { get { return 0; } }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("PrsFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x10);
            if (header[0] != 'Y' || header[1] != 'B')
                return null;
            int bpp = header[3];
            if (bpp != 3 && bpp != 4)
                return null;

            return new PrsMetaData
            {
                Width = header.ToUInt16 (12),
                Height = header.ToUInt16 (14),
                BPP = 8 * bpp,
                Flag = header[2],
                PackedSize = header.ToUInt32 (4),
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var reader = new Reader (stream, (PrsMetaData)info))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, null, reader.Data, reader.Stride);
            }
        }

        internal class Reader : IDisposable
        {
            IBinaryStream   m_input;
            byte[]          m_output;
            uint            m_size;
            byte            m_flag;
            int             m_depth;

            public byte[]        Data { get { return m_output; } }
            public PixelFormat Format { get; private set; }
            public int         Stride { get; private set; }

            public Reader (IBinaryStream file, PrsMetaData info)
            {
                m_input = file;
                m_size = info.PackedSize;
                m_flag = info.Flag;
                m_depth = info.BPP / 8;
                if (3 == m_depth)
                    Format = PixelFormats.Bgr24;
                else
                    Format = PixelFormats.Bgra32;
                Stride = (int)info.Width * m_depth;
                m_output = new byte[Stride * (int)info.Height];
            }

            static readonly int[] LengthTable = InitLengthTable();

            private static int[] InitLengthTable ()
            {
                var length_table = new int[256];
                for (int i = 0; i < 0xfe; ++i)
                    length_table[i] = i + 3;
                length_table[0xfe] = 0x400;
                length_table[0xff] = 0x1000;
                return length_table;
            }

            public void Unpack ()
            {
                m_input.Position = 0x10;
                int dst = 0;
                int remaining = (int)m_size;
                int bit = 0;
                int ctl = 0;
                while (remaining > 0 && dst < m_output.Length)
                {
                    bit >>= 1;
                    if (0 == bit)
                    {
                        ctl = m_input.ReadUInt8();
                        --remaining;
                        bit = 0x80;
                    }
                    if (remaining <= 0)
                        break;
                    if (0 == (ctl & bit))
                    {
                        m_output[dst++] = m_input.ReadUInt8();
                        --remaining;
                        continue;
                    }
                    int b = m_input.ReadUInt8();
                    --remaining;
                    int length = 0;
                    int shift = 0;

                    if (0 != (b & 0x80))
                    {
                        if (remaining <= 0)
                            break;
                        shift = m_input.ReadUInt8();
                        --remaining;
                        shift |= (b & 0x3f) << 8;
                        if (0 != (b & 0x40))
                        {
                            if (remaining <= 0)
                                break;
                            int offset = m_input.ReadUInt8();
                            --remaining;
                            length = LengthTable[offset];
                        }
                        else
                        {
                            length = (shift & 0xf) + 3;
                            shift >>= 4;
                        }
                    }
                    else
                    {
                        length = b >> 2;
                        b &= 3;
                        if (3 == b)
                        {
                            length += 9;
                            int read = m_input.Read (m_output, dst, length);
                            if (read < length)
                                break;
                            remaining -= length;
                            dst += length;
                            continue;
                        }
                        shift = length;
                        length = b + 2;
                    }
                    ++shift;
                    if (dst < shift)
                        throw new InvalidFormatException ("Invalid offset value");
                    length = Math.Min (length, m_output.Length - dst);
                    Binary.CopyOverlapped (m_output, dst-shift, dst, length);
                    dst += length;
                }
                if ((m_flag & 0x80) != 0)
                {
                    for (int i = m_depth; i < m_output.Length; ++i)
                        m_output[i] += m_output[i-m_depth];
                }
                if (4 == m_depth && IsDummyAlphaChannel())
                    Format = PixelFormats.Bgr32;
            }

            bool IsDummyAlphaChannel ()
            {
                byte alpha = m_output[3];
                if (0xFF == alpha)
                    return false;
                for (int i = 7; i < m_output.Length; i += 4)
                    if (m_output[i] != alpha)
                        return false;
                return true;
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
