//! \file       ImageMCG.cs
//! \date       Mon Jul 13 17:58:33 2015
//! \brief      F&C Co. image format.
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
using System.Windows.Media;
using GameRes.Formats.Properties;
using GameRes.Utility;

namespace GameRes.Formats.FC01
{
    internal class McgMetaData : ImageMetaData
    {
        public int DataOffset;
        public int PackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class McgFormat : ImageFormat
    {
        public override string         Tag { get { return "MCG"; } }
        public override string Description { get { return "F&C Co. image format"; } }
        public override uint     Signature { get { return 0x2047434D; } } // 'MCG'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            byte[] header = new byte[0x40];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            if (!Binary.AsciiEqual (header, 4, "2.00"))
                throw new NotSupportedException ("Not supported MCG format version");
            int header_size = LittleEndian.ToInt32 (header, 0x10);
            if (header_size < 0x40)
                return null;
            int bpp = LittleEndian.ToInt32 (header, 0x24);
            if (24 != bpp)
                throw new NotSupportedException ("Not supported MCG image bitdepth");
            return new McgMetaData
            {
                Width = LittleEndian.ToUInt32 (header, 0x1c),
                Height = LittleEndian.ToUInt32 (header, 0x20),
                OffsetX = LittleEndian.ToInt32 (header, 0x14),
                OffsetY = LittleEndian.ToInt32 (header, 0x18),
                BPP = bpp,
                DataOffset = header_size,
                PackedSize = LittleEndian.ToInt32 (header, 0x38) - header_size,
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as McgMetaData;
            if (null == meta)
                throw new ArgumentException ("McgFormat.Read should be supplied with McgMetaData", "info");

            var reader = new McgDecoder (stream, meta);
            reader.Unpack();
            return ImageData.Create (info, PixelFormats.Bgr24, null, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("McgFormat.Write not implemented");
        }

        public static readonly IReadOnlyDictionary<string, byte> KnownKeys = new Dictionary<string, byte>()
        {
            { "Konata yori Kanata made", 0xD5 },
        };
    }

    // mcg decompression // graphic.unt @ 100047B0

    internal class McgDecoder
    {
        byte[]  m_input;
        byte[]  m_output;
        uint    m_width;
        uint    m_height;
        uint    m_pixels;
        byte    m_key;

        public byte[] Data { get { return m_output; } }

        public McgDecoder (Stream input, McgMetaData info)
        {
            input.Position = info.DataOffset;
            m_input = new byte[info.PackedSize];
            if (m_input.Length != input.Read (m_input, 0, m_input.Length))
                throw new InvalidFormatException ("Unexpected end of file");
            m_width = info.Width;
            m_height = info.Height;
            m_pixels = m_width*m_height;
            m_output = new byte[m_pixels*3];
            m_key = Settings.Default.MCGLastKey;
        }

        static readonly byte[] ChannelOrder = { 1, 0, 2 };

        public void Unpack ()
        {
            var reader = new MrgDecoder (m_input, 0, m_pixels);
            do
            {
                reader.ResetKey (m_key);
                try
                {
                    for (int i = 0; i < 3; ++i)
                    {
                        reader.Unpack();
                        var plane = reader.Data;
                        int src = 0;
                        for (int j = ChannelOrder[i]; j < m_output.Length; j += 3)
                        {
                            m_output[j] = plane[src++];
                        }
                    }
//                    Trace.WriteLine (string.Format ("Found matching key {0:X2}", key), "[MCG]");
                }
                catch (InvalidFormatException)
                {
                    m_key++;
                    continue;
                }
                Transform();
                Settings.Default.MCGLastKey = m_key;
                return;
            }
            while (m_key != Settings.Default.MCGLastKey);
            throw new UnknownEncryptionScheme();
        }

        void Transform ()
        {
            uint dst = 0;
            uint stride = (m_width - 1) * 3;
            for (uint y = m_height-1; y > 0; --y) // @@1a
            {
                for (uint x = stride; x > 0; --x) // @@1b
                {
                    int p0 = m_output[dst];
                    int py = m_output[dst+stride+3] - p0;
                    int px = m_output[dst+3] - p0;
                    p0 = Math.Abs (px + py);
                    py = Math.Abs (py);
                    px = Math.Abs (px);
                    byte pv;
                    if (p0 >= px && py >= px)
                        pv = m_output[dst+stride+3];
                    else if (p0 < py)
                        pv = m_output[dst];
                    else
                        pv = m_output[dst+3];

                    m_output[dst+stride+6] += (byte)(pv + 0x80);
                    ++dst;
                }
                dst += 3;
            }
            dst = 0;
            for (uint i = 0; i < m_pixels; ++i)
            {
                sbyte b = -128;
                sbyte r = -128;
                b += (sbyte)m_output[dst];
                r += (sbyte)m_output[dst+2];
                int g = m_output[dst+1] - ((b + r) >> 2);
                m_output[dst++] = (byte)(b + g);
                m_output[dst++] = (byte)g;
                m_output[dst++] = (byte)(r + g);
            }
        }
    }
}
