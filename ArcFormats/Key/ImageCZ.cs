//! \file       ImageCZ.cs
//! \date       2019 Mar 14
//! \brief      Real Live compressed image format.
//
// Copyright (C) 2019 by morkt
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
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Key
{
    internal class CzMetaData : ImageMetaData
    {
        public int  Version;
        public uint HeaderLength;
    }

    [Export(typeof(ImageFormat))]
    public class CzFormat : ImageFormat
    {
        public override string         Tag { get { return "CZ"; } }
        public override string Description { get { return "Key compressed image format"; } }
        public override uint     Signature { get { return 0x315A43; } } // 'CZ1'

        public CzFormat ()
        {
            Extensions = new[] { "cz", "cz0", "cz1", "cz3" };
            Signatures = new uint[] { 0x305A43, 0x315A43, 0x335A43 }; // 'CZ0', 'CZ1', 'CZ3'
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            var info = new CzMetaData {
                Width  = header.ToUInt16 (8),
                Height = header.ToUInt16 (10),
                BPP = header.ToUInt16 (12),
                HeaderLength = header.ToUInt32 (4),
                Version = header[2] - '0',
            };
            if (info.HeaderLength > 0x18)
            {
                info.OffsetX = file.ReadInt16();
                info.OffsetY = file.ReadInt16();
            }
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new CzDecoder (file, (CzMetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CzFormat.Write not implemented");
        }
    }

    internal class CzDecoder
    {
        IBinaryStream   m_input;
        CzMetaData      m_info;

        BitmapPalette Palette { get; set; }
        PixelFormat    Format { get; set; }

        public CzDecoder (IBinaryStream input, CzMetaData info)
        {
            m_input = input;
            m_info = info;
            m_output = new byte[m_info.iWidth * m_info.iHeight * m_info.BPP / 8];
            Format = 8 == m_info.BPP ? PixelFormats.Indexed8 : PixelFormats.Bgra32;
        }

        byte[]  m_output;
        int     m_dst;

        public ImageData Unpack ()
        {
            m_input.Position = m_info.HeaderLength;
            if (8 == m_info.BPP)
                Palette = ImageFormat.ReadPalette (m_input.AsStream, 0x100, PaletteFormat.RgbA);
            switch (m_info.Version)
            {
            case 3: UnpackCz3(); break;
            case 2: UnpackCz2(); break;
            case 1: UnpackCz1(); break;
            case 0: UnpackCz0(); break;
            }
            if (32 == m_info.BPP)
                ConvertToBgrA();
            return ImageData.Create (m_info, Format, Palette, m_output);
        }

        void UnpackCz0 ()
        {
            m_input.Read (m_output, 0, m_output.Length);
        }

        void UnpackCz1 ()
        {
            int part_count = m_input.ReadInt32();
            var part_sizes = new int[part_count];
            int total_size = 0;
            for (int i = 0; i < part_count; ++i)
            {
                int part_size = m_input.ReadInt32() * 2;
                part_sizes[i] = part_size;
                m_input.ReadInt32(); // unpacked size
                total_size += part_size;
            }
            if (m_input.Position + total_size > m_input.Length)
                throw new InvalidFormatException();
            m_dst = 0;
            for (int i = 0; i < part_count; ++i)
            {
                m_chunkCache.Clear();
                var part = m_input.ReadBytes (part_sizes[i]);
                for (int j = 0; j < part.Length; j += 2)
                {
                    byte ctl = part[j+1];
                    if (0 == ctl)
                    {
                        m_output[m_dst++] = part[j];
                    }
                    else
                    {
                        m_dst += CopyRange (part, GetOffset (part, j), m_dst);
                    }
                }
            }
        }

        void UnpackCz2 ()
        {
            UnpackCz1();
            int stride = m_info.iWidth * m_info.BPP / 8 / 4;
            var pixels = new uint[m_output.Length / 4];
            Buffer.BlockCopy (m_output, 0, pixels, 0, m_output.Length);
            int third = (m_info.iHeight + 2) / 3;
            for (int y = 0; y < m_info.iHeight; ++y)
            {
                int dst = m_info.iWidth * y;
                if (y % third != 0)
                {
                    for (int x = 0; x < stride; ++x)
                    {
                        pixels[dst + x] += pixels[dst + x - stride];
                    }
                }
            }
            Buffer.BlockCopy (pixels, 0, m_output, 0, m_output.Length);
        }

        void UnpackCz3 ()
        {
            UnpackCz1();
            int stride = m_info.iWidth * m_info.BPP / 8;
            int third = (m_info.iHeight + 2) / 3;
            for (int y = 0; y < m_info.iHeight; ++y)
            {
                int dst = y * stride;
                if (y % third != 0)
                {
                    for (int x = 0; x < stride; ++x)
                    {
                        m_output[dst + x] += m_output[dst + x - stride];
                    }
                }
            }
        }

        void ConvertToBgrA ()
        {
            for (int i = 0; i < m_output.Length; i += 4)
            {
                byte r = m_output[i];
                m_output[i]   = m_output[i+2];
                m_output[i+2] = r;
            }
        }

        struct Range
        {
            public int  Start;
            public int  Length;
        }
    
        readonly Dictionary<int, Range> m_chunkCache = new Dictionary<int, Range>();

        static int GetOffset (byte[] input, int src)
        {
            return ((input[src] | input[src+1] << 8) - 0x101) * 2;
        }

        int CopyRange (byte[] input, int src, int dst)
        {
            Range range;
            if (m_chunkCache.TryGetValue (src, out range))
            {
                Binary.CopyOverlapped (m_output, range.Start, dst, range.Length);
                return range.Length;
            }
            int start_pos = dst;

            if (input[src+1] == 0)
                m_output[dst++] = input[src];
            else if (GetOffset (input, src) == src)
                m_output[dst++] = 0;
            else
                dst += CopyRange (input, GetOffset (input, src), dst);

            if (input[src+3] == 0)
                m_output[dst++] = input[src+2];
            else if (GetOffset (input, src+2) == src)
                m_output[dst++] = m_output[start_pos];
            else
                m_output[dst++] = CopyOne (input, GetOffset (input, src+2));

            range.Start = start_pos;
            range.Length = dst - start_pos;
            m_chunkCache[src] = range;
            return range.Length;
        }

        byte CopyOne (byte[] input, int src)
        {
            if (input[src+1] == 0)
                return input[src];
            else if (GetOffset (input, src) == src)
                return 0;
            else
                return CopyOne (input, GetOffset (input, src));
        }
    }
}
