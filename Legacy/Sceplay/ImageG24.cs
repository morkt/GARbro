//! \file       ImageG24.cs
//! \date       2017 Dec 07
//! \brief      Sceplayer engine image format.
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
using System.Windows.Media;
using GameRes.Compression;

namespace GameRes.Formats.Sceplayer
{
    internal class G24MetaData : ImageMetaData
    {
        public byte Type;
    }

    [Export(typeof(ImageFormat))]
    public class G24AFormat : ImageFormat
    {
        public override string         Tag { get { return "G24A"; } }
        public override string Description { get { return "Sceplayer image format"; } }
        public override uint     Signature { get { return 0x61343267; } } // 'g24a'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            return new ImageMetaData { 
                Width = header.ToUInt32 (8),
                Height = header.ToUInt32 (0xC),
                BPP = 24,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x2C;
            var reader = new G24Reader (file);
            var pixels = reader.Unpack();

            byte b = pixels[0];
            byte g = pixels[1];
            byte r = pixels[2];
            for (int i = 3; i < pixels.Length; i += 3)
            {
                pixels[i  ] += b;
                pixels[i+1] += g;
                pixels[i+2] += r;
                b = pixels[i  ];
                g = pixels[i+1];
                r = pixels[i+2];
            }
            return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, (int)info.Width * 3);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("G24AFormat.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class G2408Format : ImageFormat
    {
        public override string         Tag { get { return "G2408A"; } }
        public override string Description { get { return "Sceplayer bitmap format"; } }
        public override uint     Signature { get { return 0x30343267; } } // 'g240'

        public G2408Format ()
        {
            Extensions = new string[] { "g2408a", "g2408b" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            byte type = header[5];
            if (!header.AsciiEqual ("g2408") || (type != 'a' && type != 'b'))
                return null;
            return new G24MetaData { 
                Width = header.ToUInt32 (0xC),
                Height = header.ToUInt32 (0x10),
                BPP = 8,
                Type = type
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (G24MetaData)info;
            file.Position = 0x30;
            var reader = new G24Reader (file);
            var pixels = reader.Unpack();
            if ('a' == meta.Type)
            {
                byte p = pixels[pixels.Length-1];
                for (int i = pixels.Length-2; i >= 0; --i)
                {
                    p += pixels[i];
                    pixels[i] = p;
                }
            }
            return ImageData.CreateFlipped (info, PixelFormats.Gray8, null, pixels, (int)info.Width);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("G2408BFormat.Write not implemented");
        }
    }

    internal class G24Reader
    {
        IBinaryStream       m_input;

        public G24Reader (IBinaryStream input)
        {
            m_input = input;
        }

        public byte[] Unpack ()
        {
            int signature = m_input.ReadInt32();
            int unpacked_size = m_input.ReadInt32();
            var output = new byte[unpacked_size];
            switch (signature)
            {
            case 0x6572: // 're'
                UnpackRle (output);
                break;
            case 0x656C: // 'le'
                UnpackLzss (output);
                break;
            default:
                throw new InvalidFormatException();
            }
            return output;
        }

        void UnpackLzss (byte[] output)
        {
            using (var input = new LzssStream (m_input.AsStream, LzssMode.Decompress, true))
                input.Read (output, 0, output.Length);
        }

        void UnpackRle (byte[] output)
        {
            int dst = 0;
            while (m_input.PeekByte() != -1 && dst < output.Length)
            {
                byte ctl = m_input.ReadUInt8();
                if (ctl != 0xF0)
                {
                    output[dst++] = (byte)ctl;
                }
                else
                {
                    byte count = m_input.ReadUInt8();
                    if (0 == count)
                    {
                        output[dst++] = (byte)ctl;
                    }
                    else if (1 == count)
                    {
                        output[dst++] = 0xF0;
                    }
                    else if (2 == count)
                    {
                        output[dst++] = 0xF0;
                        output[dst++] = 0xF0;
                    }
                    else
                    {
                        byte v = m_input.ReadUInt8();
                        while (count --> 0)
                            output[dst++] = v;
                    }
                }
            }
        }
    }
}
