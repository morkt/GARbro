//! \file       ImageKG.cs
//! \date       Mon Aug 17 01:50:50 2015
//! \brief      Interheart image format.
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
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Interheart
{
    [Export(typeof(ImageFormat))]
    public class KgFormat : ImageFormat
    {
        public override string         Tag { get { return "KG"; } }
        public override string Description { get { return "Interheart image format"; } }
        public override uint     Signature { get { return 0x4B474347; } } // 'GCGK'
        public override bool      CanWrite { get { return true; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Position = 4;
            uint width = stream.ReadUInt16();
            uint height = stream.ReadUInt16();
            int packed_size = stream.ReadInt32();
            if (packed_size <= 0)
                return null;
            return new ImageMetaData
            {
                Width = width,
                Height = height,
                BPP = 32,
            };
        }

        public override ImageData Read (IBinaryStream input, ImageMetaData info)
        {
            byte[] pixels = new byte[info.Width*info.Height*4];
            input.Position = 12;
            uint[] offset_table = new uint[info.Height];
            for (uint i = 0; i < info.Height; ++i)
                offset_table[i] = input.ReadUInt32();

            long base_offset = input.Position;
            int dst = 0;
            foreach (var offset in offset_table)
            {
                input.Position = base_offset + offset;
                for (int x = 0; x < info.Width; )
                {
                    byte alpha = input.ReadUInt8();
                    int count = input.ReadUInt8();
                    if (0 == count)
                        count = 0x100;
                    if (0 == alpha)
                    {
                        dst += count * 4;
                    }
                    else
                    {
                        for (int n = 0; n < count; ++n)
                        {
                            pixels[dst+3] = alpha;
                            pixels[dst+2] = input.ReadUInt8();
                            pixels[dst+1] = input.ReadUInt8();
                            pixels[dst]   = input.ReadUInt8();
                            dst += 4;
                        }
                    }
                    x += count;
                }
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image) {
          int stride = (int)image.Width * 4;
          byte[] bitmap = new byte[stride * image.Height];
          {
            var image_bgra = image.Bitmap.Format != PixelFormats.Bgra32
              ? new FormatConvertedBitmap(image.Bitmap, PixelFormats.Bgra32, null, 0)
              : image.Bitmap;
            image_bgra.CopyPixels(bitmap, stride, 0);
          }
          uint[] offset_table = new uint[image.Height];
          var kg_data = new List<byte>();

          for (int y = 0; y < image.Height; y++) {
            offset_table[y] = (uint)kg_data.Count();

            bitmap
              .AsMemory()
              .Slice(y * stride, stride)
              .Chunk(4)
              .Aggregate(new List<(byte alpha, List<byte[]> data)> {  }, (chunks, x) => {
                var pixel = x.ToArray();
                void new_chunk () => chunks.Add((alpha: pixel[3], data: new List<byte[]> { pixel }));

                if (!chunks.Any()) {
                  new_chunk();
                }
                else {
                  var (alpha, data) = chunks.Last();
                  if (pixel[3] != data.Last()[3] || data.Count >= 256) {
                    new_chunk();
                  }
                  else {
                    data.Add(pixel);
                  }
                }
                return chunks;
              }
            )
            .ForEach(chunk => {
              kg_data.Add(chunk.alpha);
              kg_data.Add((byte)chunk.data.Count());
              if (chunk.alpha > 0) {
                kg_data.AddRange(chunk.data.SelectMany(p => new[] { p[2], p[1], p[0] }));
              }
            });
          }


          var bw = new BinaryWriter(file);

          bw.Write(new byte[] { 0x47, 0x43, 0x47, 0x4B });
          bw.Write((UInt16)image.Width);
          bw.Write((UInt16)image.Height);
          bw.Write((uint)kg_data.Count);

          {
            byte[] buf = new byte[offset_table.Length * sizeof(uint)];
            Buffer.BlockCopy(offset_table, 0, buf, 0, buf.Length);
            bw.Write(buf);
          }

          bw.Write(kg_data.ToArray());
        }
      }

      static class Extensions {
        public static IEnumerable<Memory<T>> Chunk<T>(this Memory<T> arr, int size) {
          for (var i = 0; i < arr.Length / size + 1; i++) {
            if (i * size < arr.Length) {
              yield return arr.Slice(i * size, size);
            }
          }
        }
    }
}
