using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.MAGES
{
    [Export(typeof(ImageFormat))]
    public class BinFormat : ImageFormat
    {
        public override string Tag { get { return "MAGES PS3/PSV Image Format"; } }
        public override string Description { get { return "MAGES PS3/PSV Image Format"; } }
        public override uint Signature { get { return 0; } }
        public override bool CanWrite { get { return true; } }

        public BinFormat()
        {
            Extensions = new string[] { "" };
        }

        public override ImageMetaData ReadMetaData(IBinaryStream file)
        {
            //var header = file.ReadHeader(8);
            int width = file.ReadInt16();
            int height = file.ReadInt16();
            if (width <= 0 || height <= 0)
                return null;
            int bpp = file.ReadInt16();
            if (32 == bpp)
            {
                uint imagedatasize = (uint)width * (uint)height * 4;
                if (file.Length != imagedatasize + 8)
                    return null;
            }
            else if (8 == bpp)
            {
                uint imagedatasize = (uint)width * (uint)height + 256;
                if (file.Length != imagedatasize + 8)
                    return null;
            }
            else
                return null;

            return new ImageMetaData
            {
                Width = (uint)width,
                Height = (uint)height,
                BPP = bpp,
            };
        }
        public override ImageData Read(IBinaryStream file, ImageMetaData info)
        {
            if (info == null)
                throw new NotSupportedException(string.Format("Not BIN texture format."));
            if (info.BPP == 32)
            {
                uint pixelnum = info.Width * info.Height;
                if (file.Length != pixelnum * 4 + 8) throw new NotSupportedException(string.Format("Not BIN 32ARGB texture format."));

                file.Position = 8;
                //var data = file.ReadBytes(info.iWidth * info.iHeight * 4);
                List<byte> pixels = new List<byte>();
                for (int i = 0; i < pixelnum; i++)
                {
                    var pixel = file.ReadBytes(4); //ARGB
                    //BGRA
                    pixels.Add(pixel[3]);
                    pixels.Add(pixel[2]);
                    pixels.Add(pixel[1]);
                    pixels.Add(pixel[0]);
                }
                return ImageData.Create(info, PixelFormats.Bgra32, null, pixels.ToArray());
            }
            else if (info.BPP == 8)
            {
                uint imagedatasize = info.Width * info.Height + 256;
                if (file.Length != imagedatasize + 8) throw new NotSupportedException(string.Format("Not BIN 256colors texture format."));
                file.Position = 8;
                //var pixelColor = file.ReadBytes(256 * 4);
                List<Color> colors = new List<Color>();
                for (int i  = 0; i < 256; i += 4)
                {
                    Color c = new Color();
                    var color_b = file.ReadBytes(4); //BGRA
                    c.B = color_b[0];
                    c.G = color_b[1];
                    c.R = color_b[2];
                    c.A = color_b[3];
                    colors.Add(c);
                }
                BitmapPalette palette = new BitmapPalette(colors);
                //file.Position += 256 * 4;
                var data = file.ReadBytes(info.iWidth * info.iHeight);
                return ImageData.Create(info, PixelFormats.Indexed8, palette, data);
            }
            else
                throw new NotSupportedException(string.Format("Not BIN texture format."));
        }
        public override void Write(Stream stream, ImageData image)
        {
            //throw new System.NotImplementedException("BINFormat.Write not implemented");
            using (var file = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                if (image.Width > ushort.MaxValue || image.Height > ushort.MaxValue)
                {
                    throw new NotSupportedException(string.Format("Image width or height oversize."));
                }
                if (image.BPP != 32)
                {
                    throw new NotSupportedException(string.Format("Image bitdepth not supported, should be 32."));
                }
                file.Write((ushort)image.Width);
                file.Write((ushort)image.Height);
                file.Write(image.BPP);

                var bitmap = image.Bitmap;
                if (bitmap.Format != PixelFormats.Bgra32)
                {
                    bitmap = new FormatConvertedBitmap(image.Bitmap, PixelFormats.Bgra32, null, 0);
                }
                int stride = (int)image.Width * 4;
                byte[] row_data = new byte[stride];
                Int32Rect rect = new Int32Rect(0, 0, (int)image.Width, 1);
                for (uint row = 0; row < image.Height; ++row)
                {
                    bitmap.CopyPixels(rect, row_data, stride, 0);
                    for (uint col = 0; col < image.Width; ++col)
                    {
                        (row_data[(col * 4) + 3], row_data[col * 4]) = (row_data[col * 4], row_data[(col * 4) + 3]);
                        (row_data[(col * 4) + 2], row_data[(col * 4) + 1]) = (row_data[(col * 4) + 1], row_data[(col * 4) + 2]);
                    }
                    file.Write(row_data);
                    rect.Y++;
                }
            }
        }
    }
}
