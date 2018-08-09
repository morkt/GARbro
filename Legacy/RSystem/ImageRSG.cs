//! \file       ImageRSG.cs
//! \date       2018 Jul 31
//! \brief      RSystem engine image format.
//
// Copyright (C) 2018 by morkt
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

// [030428][Berserker Freyja] Kikaijikake no Yakata

namespace GameRes.Formats.RSystem
{
    internal class RsgMetaData : ImageMetaData
    {
        public int  ChunkCount;
    }

    [Export(typeof(ImageFormat))]
    public class RsgFormat : ImageFormat
    {
        public override string         Tag { get { return "RSG"; } }
        public override string Description { get { return "RSystem engine image format"; } }
        public override uint     Signature { get { return 0x5352; } } // 'RS'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (12);
            return new RsgMetaData {
                Width   = header.ToUInt16 (4),
                Height  = header.ToUInt16 (6),
                BPP     = 32,
                ChunkCount = header.ToInt32 (8),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new RsgReader (file, (RsgMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, PixelFormats.Bgr32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("RsgFormat.Write not implemented");
        }
    }

    internal class RsgReader
    {
        IBinaryStream   m_input;
        byte[]          m_output;
        int             m_chunk_count;
        int             m_dst;

        public RsgReader (IBinaryStream input, RsgMetaData info)
        {
            m_input = input;
            m_chunk_count = info.ChunkCount;
            int stride = (int)info.Width * 4;
            m_output = new byte[stride * (int)info.Height];
        }

        public byte[] Unpack ()
        {
            m_input.Position = 12;
            m_dst = 0;
            for (int i = 0; i < m_chunk_count; ++i)
            {
                int ctl = m_input.ReadByte();
                if (-1 == ctl)
                    break;
                int count = (ctl & 0xF) + 1;
                switch (ctl & 0xF0)
                {
                case 0x00:  Chunk00 (count); break;
                case 0x10:  Chunk10 (count); break;
                case 0x40:  Chunk40 (count); break;
                case 0x80:  Chunk80 (count); break;
                }
            }
            return m_output;
        }

        void Chunk00 (int count)
        {
            for (int i = 0; i < count; ++i)
            {
                m_input.Read (m_output, m_dst, 3);
                m_dst += 4;
            }
        }

        void Chunk10 (int count)
        {
            m_input.Read (m_output, m_dst, 3);
            count *= 4;
            Binary.CopyOverlapped (m_output, m_dst, m_dst+4, count-4);
            m_dst += count;
        }

        void Chunk40 (int count)
        {
            int offset = m_input.ReadUInt16() * 4;
            int src = m_dst - offset;
            for (int i = 0; i < count; ++i)
            {
                m_output[m_dst  ] = m_output[src  ];
                m_output[m_dst+1] = m_output[src+1];
                m_output[m_dst+2] = m_output[src+2];
                m_dst += 4;
            }
        }

        void Chunk80 (int count)
        {
            byte b = m_output[m_dst-4];
            byte g = m_output[m_dst-3];
            byte r = m_output[m_dst-2];
            for (int i = 0; i < count; ++i)
            {
                int diff = m_input.ReadUInt16();
                int db = diff >> 11;
                if ((diff & 0x8000) != 0)
                    db |= -0x20;
                b += (byte)db;

                int dg = diff >> 6;
                if ((diff & 0x0400) != 0)
                    dg |= -0x20;
                else
                    dg &= 0x1F;
                g += (byte)dg;

                int dr = diff;
                if ((diff & 0x20) != 0)
                    dr |= -0x40;
                else
                    dr &= 0x3F;
                r += (byte)dr;

                m_output[m_dst++] = b;
                m_output[m_dst++] = g;
                m_output[m_dst++] = r;
                m_dst++;
            }
        }
    }
}
