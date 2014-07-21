//! \file       ImageTGA.cs
//! \date       Fri Jul 04 07:24:38 2014
//! \brief      Targa image implementation.
//

using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.ComponentModel.Composition;

namespace GameRes
{
    public class TgaMetaData : ImageMetaData
    {
        public short    ImageType;
        public short    ColormapType;
        public uint     ColormapOffset;
        public ushort   ColormapFirst;
        public ushort   ColormapLength;
        public short    ColormapDepth;
        public short    Descriptor;
    }

    [Export(typeof(ImageFormat))]
    public class TgaFormat : ImageFormat
    {
        public override string Tag { get { return "TGA"; } }
        public override string Description { get { return "Truevision TGA image"; } }
        public override uint Signature { get { return 0; } }

        public override ImageData Read (Stream stream, ImageMetaData metadata)
        {
            var meta = metadata as TgaMetaData;
            if (null == meta)
                throw new System.ArgumentException ("TgaFormat.Read should be supplied with TgaMetaData", "metadata");
            int colormap_size = meta.ColormapLength * meta.ColormapDepth / 8;
            int width  = (int)meta.Width;
            int height = (int)meta.Height;
            int bpp    = meta.BPP;
            long image_offset = meta.ColormapOffset;
            if (1 == meta.ColormapType)
                image_offset += colormap_size;
            switch (meta.ImageType)
            {
            case 1:  // Uncompressed, color-mapped images.
            case 3:  // Uncompressed, black and white images.
            case 9:  // Runlength encoded color-mapped images.
            case 10: // Runlength encoded RGB images.
            case 11: // Compressed, black and white images.
            case 32: // Compressed color-mapped data, using Huffman, Delta, and
                    // runlength encoding.
            case 33: // Compressed color-mapped data, using Huffman, Delta, and
                    // runlength encoding.  4-pass quadtree-type process.
                throw new System.NotImplementedException();
            default:
                throw new InvalidFormatException();
            case 2:  // Uncompressed, RGB images.
                {
                    PixelFormat pixel_format;
                    switch (bpp)
                    {
                    default: throw new InvalidFormatException();
                    case 24: pixel_format = PixelFormats.Bgr24; break;
                    case 32: pixel_format = PixelFormats.Bgra32; break;
                    case 15: pixel_format = PixelFormats.Bgr555; break;
                    case 16: pixel_format = PixelFormats.Bgr565; break;
                    }
                    stream.Position = image_offset;
                    int stride = width*((bpp+7)/8);
                    byte[] data = new byte[stride*height];
                    if (0 != (meta.Descriptor & 0x20))
                    {
                        if (data.Length != stream.Read (data, 0, data.Length))
                            throw new InvalidFormatException();
                    }
                    else
                    {
                        for (int row = height-1; row >= 0; --row)
                        {
                            if (stride != stream.Read (data, row*stride, stride))
                                throw new InvalidFormatException();
                        }
                    }
                    var bitmap = BitmapSource.Create (width, height, 96, 96, pixel_format, null,
                                                      data, stride);
                    bitmap.Freeze();
                    return new ImageData (bitmap, meta);
                }
                throw new InvalidFormatException();
            }
        }

        public override void Write (Stream stream, ImageData image)
        {
            using (var file = new BinaryWriter (stream, System.Text.Encoding.ASCII, true))
            {
                file.Write ((byte)0);   // idlength
                file.Write ((byte)0);   // colourmaptype
                file.Write ((byte)2);   // datatypecode
                file.Write ((short)0);  // colourmaporigin
                file.Write ((short)0);  // colourmaplength
                file.Write ((byte)0);   // colourmapdepth
                file.Write ((short)image.OffsetX);
                file.Write ((short)image.OffsetY);
                file.Write ((ushort)image.Width);
                file.Write ((ushort)image.Height);

                var bitmap = image.Bitmap;
                int bpp = 0;
                int stride = 0;
                byte descriptor = 0;
                if (PixelFormats.Bgr24 == bitmap.Format)
                {
                    bpp = 24;
                    stride = (int)image.Width*3;
                }
                else if (PixelFormats.Bgr32 == bitmap.Format)
                {
                    bpp = 32;
                    stride = (int)image.Width*4;
                }
                else
                {
                    bpp = 32;
                    stride = (int)image.Width*4;
                    if (PixelFormats.Bgra32 != bitmap.Format)
                    {
                        var converted_bitmap = new FormatConvertedBitmap();
                        converted_bitmap.BeginInit();
                        converted_bitmap.Source = image.Bitmap;
                        converted_bitmap.DestinationFormat = PixelFormats.Bgra32;
                        converted_bitmap.EndInit();
                        bitmap = converted_bitmap;
                    }
                }
                file.Write ((byte)bpp);
                file.Write (descriptor);
                byte[] row_data = new byte[stride];
                Int32Rect rect = new Int32Rect (0, (int)image.Height, (int)image.Width, 1);
                for (uint row = 0; row < image.Height; ++row)
                {
                    --rect.Y;
                    bitmap.CopyPixels (rect, row_data, stride, 0);
                    file.Write (row_data);
                }
            }
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            using (var file = new ArcView.Reader (stream))
            {
                short id_length     = file.ReadByte();
                short colormap_type = file.ReadByte();
                if (colormap_type > 1)
                    return null;
                short image_type    = file.ReadByte();
                ushort colormap_first  = file.ReadUInt16();
                ushort colormap_length = file.ReadUInt16();
                short colormap_depth  = file.ReadByte();
                int pos_x           = file.ReadInt16();
                int pos_y           = file.ReadInt16();
                uint width          = file.ReadUInt16();
                uint height         = file.ReadUInt16();
                int bpp             = file.ReadByte();
                if (bpp != 32 && bpp != 24 && bpp != 16 && bpp != 15 && bpp != 8)
                    return null;
                short descriptor    = file.ReadByte();
                uint colormap_offset = (uint)(18 + id_length);
                switch (image_type)
                {
                default: return null;
                case 1:  // Uncompressed, color-mapped images.
                case 2:  // Uncompressed, RGB images.
                case 3:  // Uncompressed, black and white images.
                case 9:  // Runlength encoded color-mapped images.
                case 10: // Runlength encoded RGB images.
                case 11: // Compressed, black and white images.
                case 32: // Compressed color-mapped data, using Huffman, Delta, and
                        // runlength encoding.
                case 33: // Compressed color-mapped data, using Huffman, Delta, and
                        // runlength encoding.  4-pass quadtree-type process.
                    break;
                }
                return new TgaMetaData {
                    OffsetX = pos_x,
                    OffsetY = pos_y,
                    Width   = width,
                    Height  = height,
                    BPP     = bpp,
                    ImageType       = image_type,
                    ColormapType    = colormap_type,
                    ColormapOffset  = colormap_offset,
                    ColormapFirst   = colormap_first,
                    ColormapLength  = colormap_length,
                    ColormapDepth   = colormap_depth,
                    Descriptor      = descriptor,
                };
            }
        }
    }
}
