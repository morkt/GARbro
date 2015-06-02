//! \file       ImageDGC.cs
//! \date       Tue Jun 02 03:24:27 2015
//! \brief      DAC engine image format.
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

namespace GameRes.Formats.Dac
{
    internal class DgcMetaData : ImageMetaData
    {
        public uint Flags;
    }

    [Export(typeof(ImageFormat))]
    public class DgcFormat : ImageFormat
    {
        public override string         Tag { get { return "DGC"; } }
        public override string Description { get { return "DAC engine image format"; } }
        public override uint     Signature { get { return 0x00434744; } } // 'DGC'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            using (var input = new ArcView.Reader (stream))
            {
                input.ReadInt32();
                var info = new DgcMetaData();
                info.Flags  = input.ReadUInt32();
                info.Width  = input.ReadUInt16();
                info.Height = input.ReadUInt16();
                if (info.Width > 0x7fff || info.Height > 0x7fff)
                    return null;
                info.BPP    = 0 == (info.Flags & Reader.FlagAlphaChannel) ? 24 : 32;
                return info;
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as DgcMetaData;
            if (null == meta)
                throw new ArgumentException ("DgcFormat.Read should be supplied with DgcMetaData", "info");

            stream.Position = 12;
            using (var reader = new Reader (stream, meta))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, null, reader.Data);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DgcFormat.Write not implemented");
        }

        internal class Reader : IDataUnpacker, IDisposable
        {
            BinaryReader    m_input;
            byte[]          m_output;
            readonly int    m_width;
            readonly int    m_height;
            readonly int    m_max_dict_size;
            readonly int    m_pixel_size;
            readonly int    m_stride;
            readonly bool   m_use_dict;
            readonly bool   m_has_alpha;

            public const int FlagAlphaChannel   = 0x4000000;
            public const int FlagUseDictionary  = 0x2000000;

            public byte[]        Data { get { return m_output; } }
            public PixelFormat Format { get; private set; }

            public Reader (Stream input, DgcMetaData info)
            {
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_input = new ArcView.Reader (input);
                m_use_dict  = 0 != (info.Flags & FlagUseDictionary);
                m_has_alpha = 0 != (info.Flags & FlagAlphaChannel);
                m_max_dict_size = (int)(info.Flags & 0xffffff);
                if (m_has_alpha)
                {
                    Format = PixelFormats.Bgra32;
                    m_pixel_size = 4;
                }
                else
                {
                    Format = PixelFormats.Bgr24;
                    m_pixel_size = 3;
                }
                m_stride = m_width * m_pixel_size;
                m_output = new byte[m_stride*m_height];
            }

            public void Unpack ()
            {
                if (!m_use_dict)
                    UnpackLZ();
                else if (m_max_dict_size > 0x100)
                    UnpackWithDictLarge();
                else
                    UnpackWithDictSmall();
                if (m_has_alpha)
                    UnpackAlphaChannel();
            }

            void UnpackWithDictLarge ()
            {
                var dict = new byte[m_max_dict_size * 3];
                for (int y = 0; y < m_height;)
                {
                    int dict_len = m_input.ReadUInt16() + 1;
                    m_input.Read (dict, 0, dict_len * 3);

                    for (int y_end = m_input.ReadUInt16(); y < y_end; y++)
                    {
                        var dst = y * m_stride;
                        short line_size = m_input.ReadInt16();
                        if (line_size > 0)
                        {
                            if (dict_len > 256)
                                UnpackLine16 (dst, line_size, dict);
                            else
                                UnpackLine8 (dst, line_size, dict);
                        }
                        else if (line_size < 0)
                        {
                            var src_line = (y + line_size) * m_stride;
                            Buffer.BlockCopy (m_output, src_line, m_output, dst, m_stride);
                        }
                        else
                        {
                            for (int x = 0; x < m_width; x++)
                            {
                                int i;
                                if (dict_len > 256)
                                    i = m_input.ReadUInt16();
                                else
                                    i = m_input.ReadByte();
                                i *= 3;
                                m_output[dst]   = dict[i];
                                m_output[dst+1] = dict[i+1];
                                m_output[dst+2] = dict[i+2];
                                dst += m_pixel_size;
                            }
                        }
                    }
                }
            }

            void UnpackWithDictSmall ()
            {
                var dict = new byte[m_max_dict_size * 3];

                int dict_len = m_input.ReadByte() + 1;
                m_input.Read (dict, 0, dict_len * 3);

                for (int y = 0; y < m_height; y++)
                {
                    int dst = y * m_stride;
                    int line_size = m_input.ReadInt16();
                    if (line_size > 0)
                    {
                        UnpackLine8 (dst, line_size, dict);
                    }
                    else if (line_size < 0)
                    {
                        var src_line = (y + line_size) * m_stride;
                        Buffer.BlockCopy (m_output, src_line, m_output, dst, m_stride);
                    }
                    else
                    {
                        for (int x = 0; x < m_width; x++)
                        {
                            int i = 3 * m_input.ReadByte();
                            m_output[dst]   = dict[i];
                            m_output[dst+1] = dict[i+1];
                            m_output[dst+2] = dict[i+2];
                            dst += m_pixel_size;
                        }
                    }
                }
            }

            void UnpackLZ ()
            {
                for (int y = 0; y < m_height; y++)
                {
                    int dst = y * m_stride;
                    short line_size = m_input.ReadInt16();
                    if (line_size > 0)
                    {
                        UnpackLineLZ (dst, line_size);
                    }
                    else if (line_size < 0)
                    {
                        int src_line = (y + line_size) * m_stride;
                        Buffer.BlockCopy (m_output, src_line, m_output, dst, m_stride);
                    }
                    else
                    {
                        for (int x = 0; x < m_width; x++)
                        {
                            m_input.Read (m_output, dst, 3);
                            dst += m_pixel_size;
                        }
                    }
                }
            }

            void UnpackAlphaChannel()
            {
                int alpha_pos = 3;
                for (int y = 0; y < m_height; y++)
                {
                    int dst = alpha_pos + y * m_stride;

                    short line_size = m_input.ReadInt16();
                    if (line_size > 0)
                    {
                        UnpackLineAlpha (dst, line_size);
                    }
                    else if (line_size < 0)
                    {
                        int src_line = alpha_pos + (y + line_size) * m_stride;
                        for (int x = 0; x < m_width; x++)
                        {
                            m_output[dst] = m_output[src_line];
                            dst += m_pixel_size;
                            src_line += m_pixel_size;
                        }
                    }
                    else
                    {
                        for (int x = 0; x < m_width; x++)
                        {
                            m_output[dst] = m_input.ReadByte();
                            dst += m_pixel_size;
                        }
                    }
                }
            }

            void UnpackLine16 (int dst, int length, byte[] dict)
            {
                while (length > 0)
                {
                    short ctl = m_input.ReadInt16();
                    length -= 2;
                    if (0 != (ctl & 0x8000))
                    {
                        int count = (ctl & 0x3F) + 2;
                        int offset = ctl >> 6;
                        offset *= m_pixel_size;
                        Buffer.BlockCopy (m_output, dst+offset, m_output, dst, count*m_pixel_size);
                    }
                    else
                    {
                        int count = ctl & 0x1FFF;
                        if (0 != (ctl & 0x4000))
                        {
                            int index = 0;
                            if (0 != (ctl & 0x2000))
                            {
                                index = m_input.ReadByte();
                                --length;
                            }
                            else
                            {
                                index = m_input.ReadUInt16();
                                length -= 2;
                            }
                            index *= 3;
                            while (0 != count--)
                            {
                                m_output[dst]   = dict[index];
                                m_output[dst+1] = dict[index+1];
                                m_output[dst+2] = dict[index+2];
                                dst += m_pixel_size;
                            }
                        }
                        else
                        {
                            while (0 != count--)
                            {
                                int index = 0;
                                if (0 != (ctl & 0x2000))
                                {
                                    index = m_input.ReadByte();
                                    --length;
                                }
                                else
                                {
                                    index = m_input.ReadUInt16();
                                    length -= 2;
                                }
                                index *= 3;
                                m_output[dst]   = dict[index];
                                m_output[dst+1] = dict[index+1];
                                m_output[dst+2] = dict[index+2];
                                dst += m_pixel_size;
                            }
                        }
                    }
                }
            }

            void UnpackLine8 (int dst, int length, byte[] dict)
            {
                while (length > 0)
                {
                    byte ctl = m_input.ReadByte();
                    --length;
                    if (0 != ctl)
                    {
                        int index = 3 * m_input.ReadByte();
                        --length;
                        while (0 != ctl--)
                        {
                            m_output[dst]   = dict[index];
                            m_output[dst+1] = dict[index+1];
                            m_output[dst+2] = dict[index+2];
                            dst += m_pixel_size;
                        }
                    }
                    else
                    {
                        ctl = m_input.ReadByte();
                        --length;
                        if (0 == (ctl & 0x80))
                        {
                            int count = ctl + 2;
                            while (0 != count--)
                            {
                                int src = 3 * m_input.ReadByte();
                                --length;
                                m_output[dst]   = dict[src];
                                m_output[dst+1] = dict[src+1];
                                m_output[dst+2] = dict[src+2];
                                dst += m_pixel_size;
                            }
                        }
                        else
                        {
                            int offset = (short)((ctl << 8) | m_input.ReadByte());
                            --length;
                            int count = (offset & 0x3F) + 4;
                            offset >>= 6;
                            offset *= m_pixel_size;
                            Buffer.BlockCopy (m_output, dst+offset, m_output, dst, count*m_pixel_size);
                            dst += count*m_pixel_size;
                        }
                    }
                }
            }

            void UnpackLineAlpha (int dst, int length)
            {
                while (length > 0)
                {
                    byte ctl = m_input.ReadByte();
                    --length;
                    if (0 != ctl)
                    {
                        byte alpha = m_input.ReadByte();
                        --length;
                        while (0 != ctl--)
                        {
                            m_output[dst] = alpha;
                            dst += m_pixel_size;
                        }
                    }
                    else
                    {
                        ctl = m_input.ReadByte();
                        --length;
                        if (0 == (ctl & 0x80))
                        {
                            int count = ctl + 2;
                            length -= count;
                            while (0 != count--)
                            {
                                m_output[dst] = m_input.ReadByte();
                                dst += m_pixel_size;
                            }
                        }
                        else
                        {
                            int offset = (short)((ctl << 8) | m_input.ReadByte());
                            --length;
                            int count = (offset & 0x3F) + 4;
                            offset >>= 6;
                            offset *= m_pixel_size;
                            while (0 != count--)
                            {
                                m_output[dst] = m_output[dst+offset];
                                dst += m_pixel_size;
                            }
                        }
                    }
                }
            }

            void UnpackLineLZ (int dst, int length)
            {
                while (length > 0)
                {
                    short ctl = m_input.ReadInt16();
                    length -= 2;

                    if (0 != (ctl & 0x8000))
                    {
                        int count = (ctl & 0x3F) + 1;
                        int offset = ctl >> 6;
                        offset *= m_pixel_size;

                        while (0 != count--)
                        {
                            m_output[dst]   = m_output[dst+offset];
                            m_output[dst+1] = m_output[dst+offset+1];
                            m_output[dst+2] = m_output[dst+offset+2];
                            dst += m_pixel_size;
                        }
                    }
                    else
                    {
                        int count = ctl & 0x1FFF;
                        if (0 != (ctl & 0x4000))
                        {
                            m_input.Read (m_output, dst, 3);
                            length -= 3;
                            dst += m_pixel_size;
                            if (--count > 0)
                            {
                                count *= m_pixel_size;
                                Binary.CopyOverlapped (m_output, dst-m_pixel_size, dst, count);
                                dst += count;
                            }
                        }
                        else
                        {
                            while (0 != count--)
                            {
                                m_input.Read (m_output, dst, 3);
                                length -= 3;
                                dst += m_pixel_size;
                            }
                        }
                    }
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
