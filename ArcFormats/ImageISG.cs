//! \file       ImageISG.cs
//! \date       Tue Mar 17 10:01:44 2015
//! \brief      ISM engine image format.
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

namespace GameRes.Formats.ISM
{
    internal class IsgMetaData : ImageMetaData
    {
        public byte Type;
        public int  Colors;
        public uint Packed;
        public uint Unpacked;
    }

    [Export(typeof(ImageFormat))]
    public class IsgFormat : ImageFormat
    {
        public override string         Tag { get { return "ISG"; } }
        public override string Description { get { return "ISM engine image format"; } }
        public override uint     Signature { get { return 0x204d5349u; } } // 'ISM '

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("IsgFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x24);
            if (!header.AsciiEqual ("ISM IMAGEFILE\x00"))
                return null;
            int colors = header[0x23];
            if (0 == colors)
                colors = 256;
            return new IsgMetaData
            {
                Width = header.ToUInt16 (0x1d),
                Height = header.ToUInt16 (0x1f),
                BPP = 8,
                Type = header[0x10],
                Colors = colors,
                Packed = header.ToUInt32 (0x11),
                Unpacked = header.ToUInt32 (0x15),
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (IsgMetaData)info;
            if (0x21 != meta.Type && 0x10 != meta.Type)
                throw new InvalidFormatException ("Unsupported ISM image type");

            stream.Position = 0x30;
            using (var input = new Reader (stream, meta))
            {
                if (0x21 == meta.Type)
                    input.Unpack21();
                else
                    input.Unpack10();
                var palette = new BitmapPalette (input.Palette);
                return ImageData.CreateFlipped (info, PixelFormats.Indexed8, palette, input.Data, (int)info.Width);
            }
        }

        internal class Reader : IDisposable
        {
            IBinaryStream   m_input;
            byte[]          m_data;
            Color[]         m_palette;
            int             m_input_size;

            public Color[] Palette { get { return m_palette; } }
            public byte[]     Data { get { return m_data; } }

            public Reader (IBinaryStream file, IsgMetaData info)
            {
                int palette_size = (int)info.Colors*4;
                var palette_data = new byte[Math.Max (0x400, palette_size)];
                if (palette_size != file.Read (palette_data, 0, palette_size))
                    throw new InvalidFormatException();
                m_palette = new Color[0x100];
                for (int i = 0; i < m_palette.Length; ++i)
                {
                    m_palette[i] = Color.FromRgb (palette_data[i*4+2], palette_data[i*4+1], palette_data[i*4]);
                }
                m_input = file;
                m_input_size = (int)info.Packed;
                m_data = new byte[info.Width * info.Height];
            }

            public void Unpack21 ()
            {
                int dst = 0;
                var frame = new byte[2048];
                int frame_pos = 2039;
                int remaining = m_input_size;
                byte ctl = m_input.ReadUInt8();
                --remaining;
                int bit = 0x80;
                while (remaining > 0)
                {
                    if (0 != (ctl & bit))
                    {
                        byte hi = m_input.ReadUInt8();
                        byte lo = m_input.ReadUInt8();
                        remaining -= 2;
                        int offset = (hi & 7) << 8 | lo;
                        for (int count  = (hi >> 3) + 3; count > 0; --count)
                        {
                            byte p = frame[offset];
                            frame[frame_pos] = p;
                            m_data[dst++] = p;
                            offset    = (offset    + 1) & 0x7ff;
                            frame_pos = (frame_pos + 1) & 0x7ff;
                        }
                    }
                    else
                    {
                        byte p = m_input.ReadUInt8();
                        --remaining;
                        m_data[dst++] = p;
                        frame[frame_pos] = p;
                        frame_pos = (frame_pos + 1) & 0x7ff;
                    }
                    if (0 == (bit >>= 1))
                    {
                        ctl = m_input.ReadUInt8();
                        --remaining;
                        bit = 0x80;
                    }
                }
            }

            public void Unpack10 ()
            {
                int dst = 0;
                int remaining = m_input_size;
                byte ctl = m_input.ReadUInt8();
                --remaining;
                int bit = 1;
                while (remaining > 0)
                {
                    byte p = m_input.ReadUInt8();
                    --remaining;
                    if (0 != (ctl & bit))
                    {
                        for (int count = 2 + m_input.ReadUInt8(); count > 0; --count)
                            m_data[dst++] = p;
                        --remaining;
                    }
                    else
                    {
                        m_data[dst++] = p;
                    }
                    if (0x100 == (bit <<= 1))
                    {
                        ctl = m_input.ReadUInt8();
                        --remaining;
                        bit = 1;
                    }
                }
            }

            #region IDisposable Members
            public void Dispose ()
            {
                GC.SuppressFinalize (this);
            }
            #endregion
        }
    }
}
