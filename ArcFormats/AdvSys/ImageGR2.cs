//! \file       ImageGR2.cs
//! \date       Tue Jul 21 03:54:51 2015
//! \brief      AdvSys engine image format.
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

namespace GameRes.Formats.AdvSys
{
    [Export(typeof(ImageFormat))]
    public class Gr2Format : ImageFormat
    {
        public override string         Tag { get { return "GR2"; } }
        public override string Description { get { return "AdvSys engine image format"; } }
        public override uint     Signature { get { return 0x5F325247; } } // 'GR2_'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[16];
            if (16 != stream.Read (header, 0, 16))
                return null;
            return new ImageMetaData
            {
                Width  = LittleEndian.ToUInt16 (header, 4),
                Height = LittleEndian.ToUInt16 (header, 6),
                BPP    = LittleEndian.ToInt16 (header, 12) * 8
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            stream.Position = 0x10;
            int stride = ((int)info.Width * info.BPP/8 + 3) & ~3;
            var pixels = new byte[stride * info.Height];
            if (pixels.Length != stream.Read (pixels, 0, pixels.Length))
                throw new InvalidFormatException ("Unexpected end of file");
            PixelFormat format;
            switch (info.BPP)
            {
            case 32: format = PixelFormats.Bgra32; break;
            case 24: format = PixelFormats.Bgr24; break;
            case 16: format = PixelFormats.Bgr565; break;
            default: throw new NotSupportedException ("Not supported image bitdepth");
            }
            return ImageData.Create (info, format, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Gr2Format.Write not implemented");
        }
    }

    internal class PolaMetaData : ImageMetaData
    {
        public int UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class PolaFormat : Gr2Format
    {
        public override string         Tag { get { return "GR2/Pola"; } }
        public override string Description { get { return "AdvSys engine compressed image format"; } }
        public override uint     Signature { get { return 0x6C6F502A; } } // '*Pola*'

        public PolaFormat ()
        {
            Extensions = new string[] { "gr2" };
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[20];
            if (20 != stream.Read (header, 0, 20))
                return null;
            if (!Binary.AsciiEqual (header, "*Pola*  "))
                return null;
            int unpacked_size = LittleEndian.ToInt32 (header, 8);
            using (var reader = new PolaReader (stream, 64))
            {
                reader.Unpack();
                using (var temp = new MemoryStream (reader.Data))
                {
                    var info = base.ReadMetaData (temp);
                    if (null == info)
                        return null;
                    return new PolaMetaData
                    {
                        Width   = info.Width,
                        Height  = info.Height,
                        BPP     = info.BPP,
                        UnpackedSize = unpacked_size,
                    };
                }
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as PolaMetaData;
            if (null == meta)
                throw new ArgumentException ("PolaFormat.Read should be supplied with PolaMetaData", "info");

            stream.Position = 0x14;
            using (var reader = new PolaReader (stream, meta.UnpackedSize))
            {
                reader.Unpack();
                using (var temp = new MemoryStream (reader.Data))
                    return base.Read (temp, info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PolaFormat.Write not implemented");
        }
    }

    internal sealed class PolaReader : IDisposable
    {
        BinaryReader    m_input;
        byte[]          m_output;

        public byte[] Data { get { return m_output; } }

        public PolaReader (Stream input, int unpacked_size)
        {
            m_input = new ArcView.Reader (input);
            m_output = new byte[unpacked_size+2];
        }

        public void Unpack ()
        {
            NextBit();
            int dst = 0;
            while (dst < m_output.Length-2)
            {
                if (0 != NextBit())
                {
                    m_output[dst++] = m_input.ReadByte();
                    continue;
                }
                int offset, count = 0;

                if (0 != NextBit())
                {
                    offset = m_input.ReadByte() - 256;

                    if (0 == NextBit())
                    {
                        count += 256;
                    }
                    if (0 == NextBit())
                    {
                        offset -= 512;
                        if (0 == NextBit())
                        {
                            count *= 2;
                            if (0 == NextBit())
                                count += 256;
                            offset -= 512;
                            if (0 == NextBit())
                            {
                                count *= 2;
                                if (0 == NextBit())
                                    count += 256;
                                offset -= 1024;
                                if (0 == NextBit())
                                {
                                    offset -= 2048;
                                    count *= 2;
                                    if (0 == NextBit())
                                        count += 256;
                                }
                            }
                        }
                    }

                    offset -= count;
                    if (0 != NextBit())
                        count = 3;
                    else if (0 != NextBit())
                        count = 4;
                    else if (0 != NextBit())
                        count = 5;
                    else if (0 != NextBit())
                        count = 6;
                    else if (0 != NextBit())
                    {
                        if (0 != NextBit())
                            count = 8;
                        else
                            count = 7;
                    }
                    else if (0 == NextBit())
                    {
                        count = 9;
                        if (0 != NextBit())
                            count += 4;
                        if (0 != NextBit())
                            count += 2;
                        if (0 != NextBit())
                            ++count;
                    }
                    else
                    {
                        count = m_input.ReadByte() + 17;
                    }
                    if (dst + count > m_output.Length)
                        count = m_output.Length - dst;
                    Binary.CopyOverlapped (m_output, dst + offset, dst, count);
                    dst += count;
                }
                else
                {
                    offset = m_input.ReadByte() - 256;
                    if (0 == NextBit())
                    {
                        if (offset != -1)
                        {
                            int src = dst + offset;
                            m_output[dst++] = m_output[src++];
                            m_output[dst++] = m_output[src++];
                        }
                        else if (0 == NextBit())
                            break;
                    }
                    else
                    {
                        offset -= 256;
                        if (0 == NextBit())
                            offset -= 1024;
                        if (0 == NextBit())
                            offset -= 512;
                        if (0 == NextBit())
                            offset -= 256;
                        int src = dst + offset;
                        m_output[dst++] = m_output[src++];
                        m_output[dst++] = m_output[src++];
                    }
                }
            }
        }

        int m_flag = 2;

        int NextBit ()
        {
            int bit = m_flag & 1;
            m_flag >>= 1;
            if (1 == m_flag)
            {
                m_flag = m_input.ReadUInt16() | 0x10000;
            }
            return bit;
        }

        #region IDisposable Members
        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }
}

