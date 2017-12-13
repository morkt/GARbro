//! \file       ImageBPE.cs
//! \date       2017 Dec 12
//! \brief      Qsoft image format.
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
using GameRes.Utility;

namespace GameRes.Formats.QSoft
{
    [Export(typeof(ImageFormat))]
    public class BpeFormat : ImageFormat
    {
        public override string         Tag { get { return "BPE"; } }
        public override string Description { get { return "Qsoft image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".bpe") || file.Signature < 0x36)
                return null;
            file.Position = 4;
            var unpacked = Decompress (file, 0x36);
            if (!unpacked.AsciiEqual ("BM"))
                return null;
            using (var bmp = new BinMemoryStream (unpacked, file.Name))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            int unpacked_size = (int)file.Signature;
            file.Position = 4;
            var unpacked = Decompress (file, unpacked_size);
            using (var bmp = new BinMemoryStream (unpacked, file.Name))
                return Bmp.Read (bmp, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BpeFormat.Write not implemented");
        }

        byte[] Decompress (IBinaryStream input, int unpacked_size)
        {
            var output = new byte[unpacked_size];
            int dst = 0;
            var lhs_nodes = new byte[256];
            var rhs_nodes = new byte[256];
            var seq = new byte[1024];
            int ctl;
            while ((ctl = input.ReadByte()) != -1)
            {
                for (int i = 0; i < 256; ++i)
                    lhs_nodes[i] = (byte)i;
                int token = 0;
                for (;;)
                {
                    if (ctl > 127)
                    {
                        token += ctl - 127;
                        ctl = 0;
                    }
                    if (256 == token)
                        break;
                    for (int i = 0; i <= ctl; ++i)
                    {
                        lhs_nodes[token] = (byte)input.ReadByte();
                        if (token != lhs_nodes[token])
                            rhs_nodes[token] = (byte)input.ReadByte();

                        ++token;
                    }
                    if (256 == token)
                        break;
                    ctl = input.ReadByte() & 0xFF;
                }
                int chunk_size = Binary.BigEndian (input.ReadUInt16());
                int x = 0;
                for (;;)
                {
                    if (x != 0)
                    {
                        token = seq[--x];
                    }
                    else
                    {
                        if (chunk_size-- == 0)
                            break;
                        token = input.ReadByte();
                        if (-1 == token)
                            break;
                    }
                    if (token != lhs_nodes[token])
                    {
                        if (x+1 >= 1024)
                            throw new InvalidFormatException();
                        seq[x++] = rhs_nodes[token];
                        seq[x++] = lhs_nodes[token];
                    }
                    else
                    {
                        output[dst++] = (byte)token;
                        if (output.Length == dst)
                            return output;
                    }
                }
            }
            return output;
        }
    }
}
