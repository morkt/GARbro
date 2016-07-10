//! \file       ImageBITD.cs
//! \date       Fri Jun 26 07:45:01 2015
//! \brief      Selen image format.
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
using System.Linq;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Selen
{
    [Export(typeof(ImageFormat))]
    public class BitdFormat : ImageFormat
    {
        public override string         Tag { get { return "BITD"; } }
        public override string Description { get { return "Selen RLE-compressed bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            if (stream.Length > 0xffffff)
                return null;
            var scanner = new BitdScanner (stream);
            return scanner.GetInfo();
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var reader = new BitdReader (stream, info);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, null, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("BitdFormat.Write not implemented");
        }
    }

    internal class BitdScanner
    {
        Stream  m_input;

        protected Stream Input { get { return m_input; } }

        public BitdScanner (Stream input)
        {
            m_input = input;
        }

        const int MaxScanLine = 2048;

        public ImageMetaData GetInfo ()
        {
            int total = 0;
            var scan_lines = new Dictionary<int, int>();
            var key_lines = new List<int>();
            for (;;)
            {
                int b = m_input.ReadByte();
                if (-1 == b)
                    break;
                int count = b;
                if (b > 0x7f)
                    count = (byte)-(sbyte)b;
                ++count;
                if (count > 0x7f)
                    return null;
                if (b > 0x7f)
                {
                    if (-1 == m_input.ReadByte())
                        return null;
                }
                else
                    m_input.Seek (count, SeekOrigin.Current);

                key_lines.Clear();
                key_lines.AddRange (scan_lines.Keys);
                foreach (var line in key_lines)
                {
                    int width = scan_lines[line];
                    if (width < count)
                        scan_lines.Remove (line);
                    else if (width == count)
                        scan_lines[line] = line;
                    else
                        scan_lines[line] = width - count;
                }

                total += count;
                if (total <= MaxScanLine && total >= 8)
                    scan_lines[total] = total;
                if (total > MaxScanLine && !scan_lines.Any())
                    return null;
            }
            int rem;
            total = Math.DivRem (total, 4, out rem);
            if (rem != 0)
                return null;
            var valid_lines = from line in scan_lines where line.Key == line.Value
                              orderby line.Key
                              select line.Key;
            bool is_eof = -1 == m_input.ReadByte();
            foreach (var width in valid_lines)
            {
                int height = Math.DivRem (total, width, out rem);
                if (0 == rem)
                {
                    return new ImageMetaData
                    {
                        Width = (uint)width,
                        Height = (uint)height,
                        BPP = 32,
                    };
                }
            }
            return null;
        }
    }

    internal class BitdReader : BitdScanner
    {
        byte[]          m_output;
        int             m_width;
        int             m_height;

        public byte[]        Data { get { return m_output; } }
        public PixelFormat Format { get; private set; }

        public BitdReader (Stream input, ImageMetaData info) : base (input)
        {
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            m_output = new byte[m_width * m_height * 4];
            Format = PixelFormats.Bgra32;
        }

        public void Unpack ()
        {
            int stride = m_width * 4;
            var scan_line = new byte[stride];
            for (int line = 0; line < m_output.Length; line += stride)
            {
                int dst = 0;
                while (dst < stride)
                {
                    int b = Input.ReadByte();
                    if (-1 == b)
                        throw new InvalidFormatException ("Unexpected end of file");
                    int count = b;
                    if (b > 0x7f)
                        count = (byte)-(sbyte)b;
                    ++count;
                    if (dst + count > stride)
                        throw new InvalidFormatException();
                    if (b > 0x7f)
                    {
                        b = Input.ReadByte();
                        if (-1 == b)
                            throw new InvalidFormatException ("Unexpected end of file");
                        for (int i = 0; i < count; ++i)
                            scan_line[dst++] = (byte)b;
                    }
                    else
                    {
                        Input.Read (scan_line, dst, count);
                        dst += count;
                    }
                }
                dst = line;
                for (int x = 0; x < m_width; ++x)
                {
                    m_output[dst++] = scan_line[x+m_width*3];
                    m_output[dst++] = scan_line[x+m_width*2];
                    m_output[dst++] = scan_line[x+m_width];
                    m_output[dst++] = scan_line[x];
                }
            }
        }
    }
}
