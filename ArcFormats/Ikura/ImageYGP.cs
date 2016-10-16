//! \file       ImageYGP.cs
//! \date       Sun Mar 20 13:24:00 2016
//! \brief      YGP image format.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Ikura
{
    internal class YgpMetaData : ImageMetaData
    {
        public byte Type;
        public byte Flags;
        public int  DataOffset;
        public int  DataSize;
    }

    [Export(typeof(ImageFormat))]
    public class YgpFormat : ImageFormat
    {
        public override string         Tag { get { return "YGP"; } }
        public override string Description { get { return "Ikura GDL image format"; } }
        public override uint     Signature { get { return 0x504759; } } // 'YGP'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Position = 4;
            int mask_pos = stream.ReadUInt16();         // 04
            byte type = stream.ReadUInt8();             // 06
            if (type != 1 && type != 2)
                return null;
            var info = new YgpMetaData { Type = type, BPP = 32 };
            info.Flags = stream.ReadUInt8();            // 07
            int header_size = stream.ReadInt32();       // 08
            stream.Position = header_size;
            info.DataSize = stream.ReadInt32();         // XX+00
            info.Width = stream.ReadUInt16();           // XX+04
            info.Height = stream.ReadUInt16();          // XX+06
            info.DataOffset = header_size+8;
            if (0 != (info.Flags & 4))
            {
                stream.Position = 0x14;
                info.OffsetX = stream.ReadInt16();
                info.OffsetY = stream.ReadInt16();
            }
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (YgpMetaData)info;
            stream.Position = meta.DataOffset;
            int stride = (int)meta.Width * 4;
            var pixels = new byte[stride * (int)meta.Height];
            if (0 != (meta.Flags & 1))
                Unpack (stream, meta.DataSize, pixels);
            else
                stream.Read (pixels, 0, pixels.Length);
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        void Unpack (IBinaryStream input, int input_size, byte[] output)
        {
            int dst = 0;
            while (input_size > 0)
            {
                int count;
                int ctl = input.ReadByte();
                --input_size;
                if (0 == (ctl & 0xC0))
                {
                    count = ((ctl & 0x3F) + 1) * 4;
                    input.Read (output, dst, count);
                    input_size -= count;
                }
                else
                {
                    int offset;
                    if (0x40 == (ctl & 0xC0))
                    {
                        count = (ctl & 0x3F) + 1;
                        offset = 1;
                    }
                    else if (0x80 == (ctl & 0xE0))
                    {
                        count = (ctl & 0x1F) + 1;
                        offset = input.ReadByte();
                        --input_size;
                    }
                    else if (0xA0 == (ctl & 0xE0))
                    {
                        count = (ctl & 0x1F) + 1;
                        offset = input.ReadUInt16();
                        input_size -= 2;
                    }
                    else
                    {
                        continue;
                    }
                    count *= 4;
                    offset *= 4;
                    Binary.CopyOverlapped (output, dst-offset, dst, count);
                }
                dst += count;
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("YgpFormat.Write not implemented");
        }
    }
}
