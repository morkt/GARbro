//! \file       ImageEGN.cs
//! \date       Tue Jun 16 12:53:40 2015
//! \brief      EGN compressed image format.
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
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Unknown
{
    internal class EgnMetaData : ImageMetaData
    {
        public int Mode;
        public int Flag;
        public int DataOffset;
        public int UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class EgnFormat : BmpFormat
    {
        public override string         Tag { get { return "EGN"; } }
        public override string Description { get { return "LZSS-compressed BMP image"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            int signature = ~stream.ReadInt32();
            int mode = (signature & 0x70) >> 4; // v6
            if (0 != (mode & 4))
                return null;
            int flag = signature & 0xF; // v7
            int data_size, data_offset;
            if (0 != (signature & 0x80))
            {
                data_offset = 4;
                data_size = Binary.BigEndian (signature) & 0xFFFFFF;
            }
            else
            {
                data_offset = 8;
                data_size = Binary.BigEndian (stream.ReadInt32());
            }
            if (data_size <= 0 || data_size > 0xFFFFFF) // arbitrary max BMP size
                return null;
            var reader = new Reader (stream, 0x36, mode, flag); // size of BMP header
            reader.Unpack();
            using (var bmp = new BinMemoryStream (reader.Data, stream.Name))
            {
                var info = base.ReadMetaData (bmp);
                if (null == info)
                    return null;
                return new EgnMetaData
                {
                    Width = info.Width,
                    Height = info.Height,
                    BPP = info.BPP,
                    Mode = mode,
                    Flag = flag,
                    DataOffset = data_offset,
                    UnpackedSize = data_size,
                };
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (EgnMetaData)info;
            stream.Position = meta.DataOffset;
            var reader = new Reader (stream, meta.UnpackedSize, meta.Mode, meta.Flag);
            reader.Unpack();
            using (var bmp = new BinMemoryStream (reader.Data, stream.Name))
                return base.Read (bmp, info);
        }

        internal class Reader
        {
            IBinaryStream   m_input;
            int             m_mode;
            int             m_flag;
            byte[]          m_output;

            public byte[] Data { get { return m_output; } }

            public Reader (IBinaryStream input, int output_size, int mode, int flag)
            {
                m_input = input;
                m_mode = mode;
                m_flag = flag;
                m_output = new byte[output_size];
            }

            public void Unpack ()
            {
                switch (m_mode)
                {
                case 0:
                    UnpackV0();
                    break;
                case 1:
                case 2:
                case 3:
                    throw new NotSupportedException ("Not supported EGN compression");
                default:
                    throw new InvalidFormatException();
                }
            }

            void UnpackV0 () // sub_4047AF
            {
                int count_shift = ShiftTable[2 * m_flag];
                int offset_mask = (1 << count_shift) - 1;
                int base_offset = ShiftTable[2 * m_flag + 1];
                byte v12 = 0;
                int dst = 0;
                byte v8 = 0;
                while (dst < m_output.Length)
                {
                    v8 >>= 1;
                    if (0 == v8)
                    {
                        v12 = m_input.ReadUInt8();
                        v8 = 0x80;
                    }
                    if (0 != (v8 & v12))
                    {
                        m_output[dst++] = m_input.ReadUInt8();
                    }
                    else
                    {
                        ushort v4 = Binary.BigEndian (m_input.ReadUInt16());
                        int count = base_offset + (v4 >> count_shift);
                        int src = dst - (v4 & offset_mask);
                        do
                        {
                            if (src >= 0)
                                m_output[dst] = m_output[src];
                            else
                                m_output[dst] = 0;
                            ++src;
                            ++dst;
                            --count;
                        }
                        while (count > 0 && dst < m_output.Length);
                    }
                }
            }

            static readonly byte[] ShiftTable = new byte[]
            {
                0x0C, 0x03, 0x0B, 0x03, 0x0A, 0x03, 0x09, 0x03, 0x06, 0x03, 0x05, 0x03, 0x06, 0x02, 0x05, 0x02,
                0x08, 0x04, 0x07, 0x04, 0x80, 0x40, 0x20, 0x10, 0x08, 0x04, 0x02, 0x01, 0x00, 0x00, 0x00, 0x00,
            };
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("EgnFormat.Write not implemented");
        }
    }
}
