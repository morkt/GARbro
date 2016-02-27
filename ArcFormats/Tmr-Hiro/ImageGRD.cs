//! \file       ImageGRD.cs
//! \date       Wed Dec 23 17:00:30 2015
//! \brief      Tmr-Hiro ADV System image format.
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

using GameRes.Utility;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.TmrHiro
{
    internal class GrdMetaData : ImageMetaData
    {
        public int      Format;
        public int      AlphaSize;
        public int      RSize;
        public int      GSize;
        public int      BSize;
    }

    [Export(typeof(ImageFormat))]
    public class GrdFormat : ImageFormat
    {
        public override string         Tag { get { return "GRD/TMR-HIRO"; } }
        public override string Description { get { return "Tmr-Hiro ADV System image format"; } }
        public override uint     Signature { get { return 0; } }

        public GrdFormat ()
        {
            Extensions = new string[] { "grd", "" };
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x20];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            if (header[0] != 1 && header[0] != 2)
                return null;
            if (header[1] != 1 && header[1] != 0xA1 && header[1] != 0xA2)
                return null;
            int bpp = LittleEndian.ToUInt16 (header, 6);
            if (bpp != 24 && bpp != 32)
                return null;
            int left    = LittleEndian.ToUInt16 (header, 8);
            int right   = LittleEndian.ToUInt16 (header, 0xA);
            int top     = LittleEndian.ToUInt16 (header, 0xC);
            int bottom  = LittleEndian.ToUInt16 (header, 0xE);
            var info = new GrdMetaData {
                Format      = LittleEndian.ToUInt16 (header, 0),
                Width       = (uint)(right-left),
                Height      = (uint)(bottom-top),
                BPP         = bpp,
                OffsetX     = left,
                OffsetY     = top,
                AlphaSize   = LittleEndian.ToInt32 (header, 0x10),
                RSize       = LittleEndian.ToInt32 (header, 0x14),
                GSize       = LittleEndian.ToInt32 (header, 0x18),
                BSize       = LittleEndian.ToInt32 (header, 0x1C),
            };
            if (0x20 + info.AlphaSize + info.RSize + info.BSize + info.GSize != stream.Length)
                return null;
            return info;
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = (GrdMetaData)info;
            var reader = new GrdReader (stream, meta);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, null, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GrdFormat.Write not implemented");
        }
    }

    internal sealed class GrdReader
    {
        Stream      m_input;
        GrdMetaData m_info;
        byte[]      m_output;
        int         m_pack_type;
        int         m_pixel_size;
        byte[]      m_channel;

        public PixelFormat Format { get; private set; }
        public        byte[] Data { get { return m_output; } }

        public GrdReader (Stream input, GrdMetaData info)
        {
            m_input = input;
            m_info  = info;
            if (24 == m_info.BPP)
                Format = PixelFormats.Bgr24;
            else if (m_info.AlphaSize > 0)
                Format = PixelFormats.Bgra32;
            else
                Format = PixelFormats.Bgr32;
            int channel_size = (int)(m_info.Width * m_info.Height);
            m_pack_type = m_info.Format >> 8;
            m_pixel_size = m_info.BPP / 8;
            m_output = new byte[m_pixel_size * channel_size];
            m_channel = new byte[channel_size];
        }

        public void Unpack ()
        {
            int next_pos = 0x20;
            if (32 == m_info.BPP && m_info.AlphaSize > 0)
            {
                UnpackChannel (3, next_pos, m_info.AlphaSize);
                next_pos += m_info.AlphaSize;
            }
            UnpackChannel (2, next_pos, m_info.RSize);
            next_pos += m_info.RSize;
            UnpackChannel (1, next_pos, m_info.GSize);
            next_pos += m_info.GSize;
            UnpackChannel (0, next_pos, m_info.BSize);
        }

        void UnpackChannel (int dst, int src_pos, int src_size)
        {
            m_input.Position = src_pos;

            if (1 == m_pack_type)
            {
                UnpackRLE (src_size);
            }
            else
            {
                throw new System.NotImplementedException ("Huffman-compressed images not implemented");
            }
            for (int y = (int)m_info.Height-1; y >= 0; --y)
            {
                int src = y * (int)m_info.Width;
                for (uint x = 0; x < m_info.Width; ++x)
                {
                    m_output[dst] = m_channel[src++];
                    dst += m_pixel_size;
                }
            }
        }

        void UnpackRLE (int src_size)
        {
            int src = 0;
            int dst = 0;
            while (src < src_size)
            {
                int count = m_input.ReadByte();
                if (-1 == count)
                    return;
                ++src;
                if (count > 0x7F)
                {
                    count &= 0x7F;
                    byte v = (byte)m_input.ReadByte();
                    ++src;
                    for (int i = 0; i < count; ++i)
                        m_channel[dst++] = v;
                }
                else
                {
                    m_input.Read (m_channel, dst, count);
                    src += count;
                    dst += count;
                }
            }
        }
    }
}
