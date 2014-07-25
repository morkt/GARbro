//! \file       Image.cs
//! \date       Tue Jul 01 11:29:52 2014
//! \brief      image class.
//

using System.IO;
using System.Windows.Media.Imaging;

namespace GameRes
{
    public class ImageMetaData
    {
        public uint Width { get; set; }
        public uint Height { get; set; }
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
        public int BPP { get; set; }
    }

    public class ImageEntry : Entry
    {
        public override string Type { get { return "image"; } }
    }

    public class ImageData
    {
        private BitmapSource m_bitmap;

        public BitmapSource Bitmap { get { return m_bitmap; } }
        public uint Width { get { return (uint)m_bitmap.PixelWidth; } }
        public uint Height { get { return (uint)m_bitmap.PixelHeight; } }
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
        public int BPP { get { return m_bitmap.Format.BitsPerPixel; } }

        public ImageData (BitmapSource data, ImageMetaData meta)
        {
            m_bitmap = data;
            OffsetX = meta.OffsetX;
            OffsetY = meta.OffsetY;
        }

        public ImageData (BitmapSource data, int x = 0, int y = 0)
        {
            m_bitmap = data;
            OffsetX = x;
            OffsetY = y;
        }
    }

    public abstract class ImageFormat : IResource
    {
        public override string Type { get { return "image"; } }

        public abstract ImageMetaData ReadMetaData (Stream file);

        public abstract ImageData Read (Stream file, ImageMetaData info);
        public abstract void Write (Stream file, ImageData bitmap);

        public static ImageData Read (Stream file)
        {
            bool need_dispose = false;
            try
            {
                if (!file.CanSeek)
                {
                    var stream = new MemoryStream();
                    file.CopyTo (stream);
                    file = stream;
                    need_dispose = true;
                }
                var format = FindFormat (file);
                if (null == format)
                    return null;
                file.Position = 0;
                return format.Item1.Read (file, format.Item2);
            }
            finally
            {
                if (need_dispose)
                    file.Dispose();
            }
        }

        public static System.Tuple<ImageFormat, ImageMetaData> FindFormat (Stream file)
        {
            uint signature = FormatCatalog.ReadSignature (file);
            for (;;)
            {
                var range = FormatCatalog.Instance.LookupSignature<ImageFormat> (signature);
                foreach (var impl in range)
                {
                    try
                    {
                        file.Position = 0;
                        ImageMetaData metadata = impl.ReadMetaData (file);
                        if (null != metadata)
                            return new System.Tuple<ImageFormat, ImageMetaData> (impl, metadata);
                    }
                    catch { }
                }
                if (0 == signature)
                    break;
                signature = 0;
            }
            return null;
        }

        public override Entry CreateEntry ()
        {
            return new ImageEntry();
        }

        public bool IsBuiltin
        {
            get { return this.GetType().Assembly == typeof(ImageFormat).Assembly; }
        }
    }
}
