//! \file       ImageFCB.cs
//! \date       Thu Aug 11 04:21:02 2016
//! \brief      Caramel BOX compressed image format.
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.CaramelBox
{
    internal class FcbMetaData : ImageMetaData
    {
        public int Method;
    }

    [Export(typeof(ImageFormat))]
    public class FcbFormat : ImageFormat
    {
        public override string         Tag { get { return "FCB"; } }
        public override string Description { get { return "Caramel BOX image format"; } }
        public override uint     Signature { get { return 0x31626366; } } // 'fcb1'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x10];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            return new FcbMetaData
            {
                Width  = LittleEndian.ToUInt32 (header, 4),
                Height = LittleEndian.ToUInt32 (header, 8),
                Method = LittleEndian.ToInt32 (header, 12),
                BPP    = 32,
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = (FcbMetaData)info;
            byte[] input;
            if (1 == meta.Method)
            {
                stream.Position = 0x14;
                using (var reader = new ArcView.Reader (stream))
                {
                    int unpacked_size = Binary.BigEndian (reader.ReadInt32());
                    reader.ReadInt32(); // packed_size
                    input = new byte[unpacked_size];
                    using (var z = new ZLibStream (stream, CompressionMode.Decompress, true))
                        if (unpacked_size != z.Read (input, 0, unpacked_size))
                            throw new EndOfStreamException();
                }
            }
            else if (0 == meta.Method)
            {
                stream.Position = 0x10;
                using (var tz = new TzCompression (stream))
                    input = tz.Unpack();
            }
            else
                throw new InvalidFormatException();
            var pixels = Unpack (input, info);
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("FcbFormat.Write not implemented");
        }

        byte[] Unpack (byte[] input, ImageMetaData info)
        {
            byte[] ref_pixel = { 0x80, 0x80, 0x80, 0xFF };
            var pixel = new byte[4];
            var delta = new int[4];

            var output = new byte[info.Width * info.Height * 4];
            int src = 0;
            int dst = 0;
            for (uint y = 0; y < info.Height; ++y)
            {
                pixel[0] = ref_pixel[0];
                pixel[1] = ref_pixel[1];
                pixel[2] = ref_pixel[2];
                pixel[3] = ref_pixel[3];

                for (uint x = 0; x < info.Width; ++x)
                {
                    int v = input[src++];
                    if (0 != (v & 0x80))
                    {
                        if (0 != (v & 0x40))
                        {
                            if (0 != (v & 0x20))
                            {
                                if (0 != (v & 0x10))
                                {
                                    if (0 != (v & 0x08))
                                    {
                                        if (v == 0xFE)
                                        {
                                            delta[0] = input[src++] - 128;
                                            delta[1] = input[src++] - 128;
                                            delta[2] = input[src++] - 128;
                                            delta[3] = 0;
                                        }
                                        else
                                        {
                                            delta[0] = input[src++] - 128;
                                            delta[1] = input[src++] - 128;
                                            delta[2] = input[src++] - 128;
                                            delta[3] = input[src++] - 128;
                                        }
                                    }
                                    else
                                    {
                                        v = input[src++] | v << 8;
                                        v = input[src++] | v << 8;
                                        v = input[src++] | v << 8;
                                        delta[0] = ((v >> 20) & 0x7F) - 64;
                                        delta[1] = ((v >> 14) & 0x3F) - 32;
                                        delta[2] = ((v >> 8)  & 0x3F) - 32;
                                        delta[3] = v - 128;
                                    }
                                }
                                else
                                {
                                    v = input[src++] | v << 8;
                                    v = input[src++] | v << 8;
                                    delta[0] = ((v >> 14) & 0x3F) - 32;
                                    delta[1] = ((v >> 10) & 0x0F) - 8;
                                    delta[2] = ((v >> 6)  & 0x0F) - 8;
                                    delta[3] = (v & 0x3F) - 32;
                                }
                            }
                            else
                            {
                                v = input[src++] | v << 8;
                                v = input[src++] | v << 8;
                                delta[0] = ((v >> 13) & 0xFF) - 128;
                                delta[1] = ((v >> 7)  & 0x3F) - 32;
                                delta[2] = (v & 0x7F) - 64;
                                delta[3] = 0;
                            }
                        }
                        else
                        {
                            v = input[src++] | v << 8;
                            delta[0] = ((v >> 8) & 0x3F) - 32;
                            delta[1] = ((v >> 4) & 0x0F) - 8;
                            delta[2] = (v & 0xf) - 8;
                            delta[3] = 0;
                        }
                    }
                    else
                    {
                        delta[0] = ((v >> 4) & 7) - 4;
                        delta[1] = ((v >> 2) & 3) - 2;
                        delta[2] = (v & 3) - 2;
                        delta[3] = 0;
                    }

                    pixel[0] += (byte)(delta[0] + delta[1]);
                    pixel[1] += (byte)delta[0];
                    pixel[2] += (byte)(delta[0] + delta[2]);
                    pixel[3] += (byte)delta[3];

                    output[dst++] = pixel[0];
                    output[dst++] = pixel[1];
                    output[dst++] = pixel[2];
                    output[dst++] = pixel[3];

                    if (0 == x)
                    {
                        ref_pixel[0] = pixel[0];
                        ref_pixel[1] = pixel[1];
                        ref_pixel[2] = pixel[2];
                        ref_pixel[3] = pixel[3];
                    }
                }
            }
            return output;
        }
    }
}
