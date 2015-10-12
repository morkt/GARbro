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

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x10];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            if (header[0] != 'Y' || header[1] != 'B' || header[3] != 3)
                return null;

            return new PrsMetaData
            {
                Width = LittleEndian.ToUInt16 (header, 12),
                Height = LittleEndian.ToUInt16 (header, 14),
                BPP = 24,
                Flag = header[2],
                PackedSize = LittleEndian.ToUInt32 (header, 4),
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as PrsMetaData;
            if (null == meta)
                throw new ArgumentException ("PrsFormat.Read should be supplied with PrsMetaData", "info");

            stream.Position = 0x10;
            using (var reader = new Reader (stream, meta))
            {
                reader.Unpack();
                return ImageData.Create (meta, PixelFormats.Bgr24, null, reader.Data, (int)meta.Width*3);
            }
        }

        internal class Reader : IDisposable
        {
            BinaryReader    m_input;
            byte[]          m_output;
            uint            m_size;
            byte            m_flag;

            public byte[] Data { get { return m_output; } }

            public Reader (Stream file, PrsMetaData info)
            {
                m_input = new BinaryReader (file, Encoding.ASCII, true);
                m_output = new byte[info.Width*info.Height*3];
                m_size = info.PackedSize;
                m_flag = info.Flag;
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
                int dst = 0;
                int remaining = (int)m_size;
                int bit = 0;
                int ctl = 0;
                while (remaining > 0 && dst < m_output.Length)
                {
                    bit >>= 1;
                    if (0 == bit)
                    {
                        ctl = m_input.ReadByte();
                        --remaining;
                        bit = 0x80;
                    }
                    if (0 == (ctl & bit))
                    {
                        m_output[dst++] = m_input.ReadByte();
                        --remaining;
                        continue;
                    }
                    int b = m_input.ReadByte();
                    --remaining;
                    int length = 0;
                    int shift = 0;

                    if (0 != (b & 0x80))
                    {
                        if (remaining <= 0)
                            break;
                        shift = m_input.ReadByte();
                        --remaining;
                        shift |= (b & 0x3f) << 8;
                        if (0 != (b & 0x40))
                        {
                            if (remaining <= 0)
                                break;
                            int offset = m_input.ReadByte();
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
                    Binary.CopyOverlapped (m_output, dst-shift, dst, length);
                    dst += length;
                }
                if (m_flag != 0)
                {
                    for (int i = 3; i < m_output.Length; ++i)
                        m_output[i] += m_output[i-3];
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
