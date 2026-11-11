//! \file       QoiCodec.cs
//! \date       Sun Apr 12 2026
//! \brief      Quite OK Image Format
//
// Copyright (C) 2016 by morkt
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

using System.IO;

namespace GameRes.Formats.QoiCodec
{
    static class QoiConst
    {
        public const int Index = 0x00;
        public const int Diff  = 0x40;
        public const int Luma  = 0x80;
        public const int Run   = 0xC0;
        public const int Rgb   = 0xFE;
        public const int Rgba  = 0xFF;
        public const int Mask2 = 0xC0;
        public const int HashTableSize = 64;
    }

    public class QoiDecodeStream
    {
        readonly IBinaryStream m_input;
        readonly byte[]        m_table;
        uint                   m_pixel;

        public QoiDecodeStream (IBinaryStream input)
        {
            m_input = input;
            m_table = new byte [4*QoiConst.HashTableSize];
            m_pixel = 0xFF000000;
        }

        public int Read (out uint output)
        {
            var r = (byte)m_pixel;
            var g = (byte)(m_pixel >> 8);
            var b = (byte)(m_pixel >> 16);
            var a = (byte)(m_pixel >> 24);
            var run = 1;
            var b1 = m_input.ReadByte ();
            if (-1 == b1)
                throw new EndOfStreamException ();
            if (QoiConst.Rgb == b1)
            {
                var rgb = m_input.ReadInt24 ();
                r = (byte)rgb;
                g = (byte)(rgb >> 8);
                b = (byte)(rgb >> 16);
            }
            else if (QoiConst.Rgba == b1)
            {
                var rgba = m_input.ReadInt32 ();
                r = (byte)rgba;
                g = (byte)(rgba >> 8);
                b = (byte)(rgba >> 16);
                a = (byte)(rgba >> 24);
            }
            else if (QoiConst.Index == (b1 & QoiConst.Mask2))
            {
                var p1 = (b1 & ~QoiConst.Mask2) * 4;
                r = m_table[p1  ];
                g = m_table[p1+1];
                b = m_table[p1+2];
                a = m_table[p1+3];
            }
            else if (QoiConst.Diff == (b1 & QoiConst.Mask2))
            {
                r += (byte)(((b1 >> 4) & 0x03) - 2);
                g += (byte)(((b1 >> 2) & 0x03) - 2);
                b += (byte)((b1 & 0x03) - 2);
            }
            else if (QoiConst.Luma == (b1 & QoiConst.Mask2))
            {
                var b2 = m_input.ReadByte ();
                if (-1 == b2)
                    throw new EndOfStreamException ();
                var vg = (b1 & 0x3F) - 32;
                r += (byte)(vg - 8 + ((b2 >> 4) & 0x0F));
                g += (byte)vg;
                b += (byte)(vg - 8 + (b2 & 0x0F));
            }
            else if (QoiConst.Run == (b1 & QoiConst.Mask2))
            {
                run = (b1 & 0x3F) + 1;
            }
            var p2 = (r*3 + g*5 + b*7 + a*11) % QoiConst.HashTableSize*4;
            m_table[p2  ] = r;
            m_table[p2+1] = g;
            m_table[p2+2] = b;
            m_table[p2+3] = a;
            m_pixel = (uint)(r | (g << 8) | (b << 16) | (a << 24));
            output = (uint)(b | (g << 8) | (r << 16) | (a << 24));
            return run;
        }
    }
}
