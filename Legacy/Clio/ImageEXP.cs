//! \file       ImageEXP.cs
//! \date       2017 Dec 04
//! \brief      Clio compressed bitmap format.
//
// Copyright (C) 2017 by morkt
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

namespace GameRes.Formats.Clio
{
    internal class ExpMetaData : ImageMetaData
    {
        public int      BitmapSize;
        public byte[]   BitmapFileName;
    }

    [Export(typeof(ImageFormat))]
    public class ExpFormat : ImageFormat
    {
        public override string         Tag { get { return "EXP/CLIO"; } }
        public override string Description { get { return "Clio compressed bitmap"; } }
        public override uint     Signature { get { return 0x4E455850; } } // 'PXEN'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 4;
            var filename = file.ReadBytes (0x20);
            int bitmap_size = file.ReadInt32();
            var reader = new ExpReader (file, filename);
            var bmp_header = reader.Unpack (0x36);
            using (var mem_bmp = new BinMemoryStream (bmp_header, file.Name))
            {
                var info = Bmp.ReadMetaData (mem_bmp);
                if (null == info)
                    return null;
                return new ExpMetaData {
                    Width       = info.Width,
                    Height      = info.Height,
                    BPP         = info.BPP,
                    BitmapSize  = bitmap_size,
                    BitmapFileName = filename,
                };
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (ExpMetaData)info;
            file.Position = 0x28;
            var reader = new ExpReader (file, meta.BitmapFileName);
            var bmp_data = reader.Unpack (meta.BitmapSize);
            using (var mem_bmp = new BinMemoryStream (bmp_data, file.Name))
                return Bmp.Read (mem_bmp, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ExpFormat.Write not implemented");
        }
    }

    internal class ExpReader
    {
        IBinaryStream   m_input;
        byte[]          m_filename;

        public ExpReader (IBinaryStream input, byte[] filename)
        {
            m_input = input;
            m_filename = filename.Clone() as byte[];
        }

        public byte[] Unpack (int unpacked_size)
        {
            var output = new byte[unpacked_size];
            int dst = 0;
            var table = new byte[2,256];
            while (dst < unpacked_size && m_input.PeekByte() != -1)
            {
                for (int i = 0; i < 256; ++i)
                    table[0,i] = (byte)i;
                int count;
                int t_idx = 0;
                do
                {
                    byte ctl = m_input.ReadUInt8();
                    if (ctl > 127)
                    {
                        t_idx += ctl - 127;
                        ctl = 0;
                    }
                    if (t_idx != 256)
                    {
                        count = ctl + 1;
                        while (count --> 0)
                        {
                            ctl = (byte)t_idx;
                            table[0,t_idx] = m_input.ReadUInt8();
                            if (t_idx != table[0,t_idx])
                            {
                                table[1,t_idx] = m_input.ReadUInt8();
                            }
                            ++t_idx;
                        }
                    }
                }
                while (t_idx != 256);
                byte hi = m_input.ReadUInt8();
                byte lo = m_input.ReadUInt8();
                count = hi << 8 | lo;
                int pos = 0;
                for (;;)
                {
                    byte b;
                    if (pos != 0)
                    {
                        b = m_filename[--pos];
                    }
                    else
                    {
                        if (0 == count--)
                            break;
                        b = m_input.ReadUInt8();
                    }
                    if (b == table[0,b])
                    {
                        output[dst++] = b;
                        if (dst >= output.Length)
                            break;
                    }
                    else
                    {
                        m_filename[pos++] = table[1,b];
                        m_filename[pos++] = table[0,b];
                    }
                }
            }
            return output;
        }
    }
}
