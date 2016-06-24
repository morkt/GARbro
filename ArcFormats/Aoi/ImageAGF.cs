//! \file       ImageAGF.cs
//! \date       Thu Jun 23 16:23:03 2016
//! \brief      Aoi engine image format.
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

using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Aoi
{
    internal class AgfMetaData : ImageMetaData
    {
        public int  Version;
        public uint DataOffset;
        public uint Flags;
        public uint BaseNameOffset;
    }

    [Export(typeof(ImageFormat))]
    public class AgfFormat : ImageFormat
    {
        public override string         Tag { get { return "AGF/AOI"; } }
        public override string Description { get { return "Aoi engine image format"; } }
        public override uint     Signature { get { return 0x464741; } } // 'AGF'

        public AgfFormat ()
        {
            Extensions = new string[] { "agf" };
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x80];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            int version = LittleEndian.ToInt32 (header, 4);
            if (version != 1 && version != 2)
                return null;
            var info = new AgfMetaData
            {
                Width   = LittleEndian.ToUInt32 (header, 0x1C),
                Height  = LittleEndian.ToUInt32 (header, 0x20),
                BPP     = 32,
                Version = version,
            };
            if (1 == version)
            {
                info.DataOffset = LittleEndian.ToUInt32 (header, 0x0C);
            }
            else
            {
                info.DataOffset = LittleEndian.ToUInt32 (header, 0x10);
                info.Flags = LittleEndian.ToUInt32 (header, 0x54);
                info.BaseNameOffset = LittleEndian.ToUInt32 (header, 0x6C);
            }
            return info;
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = (AgfMetaData)info;
            var pixels = new byte[meta.Width * meta.Height * 4];
            int dst = 0;
            stream.Position = meta.DataOffset;
            using (var input = new BinaryReader (stream, Encoding.Unicode, true))
            {
                while (dst < pixels.Length)
                {
                    uint op = input.ReadUInt32();
                    int count = (int)(op >> 8);
                    switch (op & 0xFF)
                    {
                    case 1:
                        count *= 4;
                        input.Read (pixels, dst, count);
                        break;

                    case 2:
                        input.Read (pixels, dst, 4);
                        count *= 4;
                        Binary.CopyOverlapped (pixels, dst, dst+4, count - 4);
                        break;

                    case 3:
                        int chunk_size = (count >> 8) * 4;
                        count = (count & 0xFF) * chunk_size;
                        input.Read (pixels, dst, chunk_size);
                        Binary.CopyOverlapped (pixels, dst, dst + chunk_size, count - chunk_size);
                        break;

                    case 4:
                        int offset = (count & 0xFFF) * 4;
                        count = (count >> 12) * 4;
                        Binary.CopyOverlapped (pixels, dst - offset, dst, count);
                        break;

                    case 5:
                        count = (count >> 8) & 0xFF;
                        input.BaseStream.Seek ((count - count / 4) * 4, SeekOrigin.Current);
                        count *= 4;
                        break;

                    default:
                        throw new InvalidFormatException();
                    }
                    dst += count;
                }
                if (0 != (meta.Flags & 0x10) && 0 != meta.BaseNameOffset)
                {
                    try
                    {
                        var base_name = ReadBaseName (input, meta);
                        if (VFS.FileExists (base_name))
                        {
                            using (var base_file = VFS.OpenSeekableStream (base_name))
                            {
                                var base_image = Read (base_name, base_file);
                                BlendImage (meta, pixels, base_image.Bitmap);
                            }
                        }
                    }
                    catch (Exception X)
                    {
                        Trace.WriteLine (string.Format ("{0}: baseline image read error: {1}",
                                                        meta.FileName, X.Message), "[AGF]");
                    }
                }
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        string ReadBaseName (BinaryReader input, AgfMetaData info)
        {
            input.BaseStream.Position = info.DataOffset + info.BaseNameOffset;
            var name = new StringBuilder();
            for (;;)
            {
                char c = input.ReadChar();
                if (0 == c)
                    break;
                name.Append (c);
            }
            var dir_name = VFS.GetDirectoryName (info.FileName);
            return VFS.CombinePath (dir_name, name.ToString());
        }

        void BlendImage (AgfMetaData info, byte[] overlay, BitmapSource bitmap)
        {
            if (bitmap.PixelWidth != info.Width || bitmap.PixelHeight != info.Height)
                return;
            if (bitmap.Format.BitsPerPixel != 32)
                bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgra32, null, 0);

            int stride = bitmap.PixelWidth * 4;
            byte[] pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels (pixels, stride, 0);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int pix = overlay[i] | overlay[i+1] | overlay[i+2] | overlay[i+3];
                if (0 == pix)
                {
                    overlay[i  ] = pixels[i  ];
                    overlay[i+1] = pixels[i+1];
                    overlay[i+2] = pixels[i+2];
                    overlay[i+3] = pixels[i+3];
                }
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AgfFormat.Write not implemented");
        }
    }
}
