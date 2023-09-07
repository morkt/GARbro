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
        public int  RecursionDepth;
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
            if (0x21 != meta.Type && 0x10 != meta.Type && 0x34 != meta.Type)
                throw new InvalidFormatException ("Unsupported ISM image type");

            var input = new Reader (stream, meta);
            var pixels = input.Unpack (this);
            var palette = new BitmapPalette (input.Palette);
            return ImageData.CreateFlipped (info, PixelFormats.Indexed8, palette, input.Data, info.iWidth);
        }

        internal class Reader
        {
            IBinaryStream   m_input;
            byte[]          m_data;
            int             m_input_size;
            IsgMetaData     m_info;

            public const int RecursionLimit = 32; // have seen 18 deep

            public Color[] Palette { get; private set; }
            public byte[]     Data { get { return m_data; } }

            public Reader (IBinaryStream file, IsgMetaData info)
            {
                m_input = file;
                m_input_size = (int)info.Packed;
                m_info = info;
            }

            public byte[] Unpack (IsgFormat isg)
            {
                m_input.Position = 0x30;
                if (0x34 == m_info.Type)
                    return Unpack34(isg);
                Palette = ImageFormat.ReadColorMap (m_input.AsStream);
                m_data = new byte[m_info.Width * m_info.Height];
                if (0x21 == m_info.Type)
                    DecompressLzss (m_input_size, m_data);
                else
                    Unpack10();
                return m_data;
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

            public byte[] Unpack34 (IsgFormat isg)
            {
                if (m_info.RecursionDepth >= RecursionLimit)
                    throw new InvalidFormatException ("Recursion limit reached for ISG image.");
                var base_name = m_input.ReadCString (0x10);
                var base_image = ReadBaseImage (base_name, isg);
                if (null == base_image)
                    throw new InvalidFormatException ("Unable to read baseline ISG image.");
                int count = m_input.ReadInt32();
                int packed_size = m_input.ReadInt32();
                int ovl_ctl_size = m_info.iWidth * m_info.iHeight / 128;
                var overlay_info = m_input.ReadBytes (ovl_ctl_size);
                var overlay_data = new byte[count * 32 + 8];
                DecompressLzss (packed_size, overlay_data);
                int bit_count = 0;
                int ctl_src = 0;
                int data_src = 0;
                for (int y = 0; y < m_info.iHeight; y += 4)
                for (int x = 0; x < m_info.iWidth; x += 4)
                {
                    if (((1 << bit_count) & overlay_info[ctl_src]) != 0)
                    {
                        int dst = y * m_info.iWidth + x;
                        for (int r = 0; r < 4; ++r)
                        {
                            base_image[dst  ] = overlay_data[data_src++];
                            base_image[dst+1] = overlay_data[data_src++];
                            base_image[dst+2] = overlay_data[data_src++];
                            base_image[dst+3] = overlay_data[data_src++];
                            dst += m_info.iWidth;
                        }
                    }
                    if (++bit_count == 8)
                    {
                        bit_count = 0;
                        ++ctl_src;
                    }
                }
                return m_data = base_image;
            }

            internal void DecompressLzss (int remaining, byte[] output)
            {
                int dst = 0;
                var frame = new byte[2048];
                int frame_pos = 2039;
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
                            output[dst++] = p;
                            offset    = (offset    + 1) & 0x7ff;
                            frame_pos = (frame_pos + 1) & 0x7ff;
                        }
                    }
                    else
                    {
                        byte p = m_input.ReadUInt8();
                        --remaining;
                        output[dst++] = p;
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

            internal byte[] ReadBaseImage (string name, IsgFormat isg)
            {
                if (!VFS.FileExists (name))
                {
                    if (name.Length <= 12)
                        return null;
                    name = name.Substring (0, 12);
                    if (!VFS.FileExists (name))
                        return null;
                }
                using (var base_file = VFS.OpenBinaryStream (name))
                {
                    var base_info = isg.ReadMetaData (base_file) as IsgMetaData;
                    if (null == base_info || base_info.Width != m_info.Width || base_info.Height != m_info.Height)
                        throw new InvalidFormatException ("Invalid baseline ISG image.");
                    base_info.RecursionDepth = m_info.RecursionDepth + 1;
                    base_info.FileName = name;
                    var reader = new Reader (base_file, base_info);
                    var pixels = reader.Unpack (isg);
                    this.Palette = reader.Palette;
                    return pixels;
                }
            }
        }
    }
}
