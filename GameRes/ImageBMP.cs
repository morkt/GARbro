//! \file       ImageBMP.cs
//! \date       Wed Jul 16 18:06:47 2014
//! \brief      BMP image implementation.
//

using System.IO;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.Windows.Media.Imaging;
using GameRes.Utility;
using System.Windows.Media;

namespace GameRes
{
    [Export(typeof(ImageFormat))]
    public class BmpFormat : ImageFormat
    {
        public override string Tag { get { return "BMP"; } }
        public override string Description { get { return "Windows device independent bitmap"; } }
        public override uint Signature { get { return 0; } }

        public override ImageData Read (Stream file, ImageMetaData info)
        {
            var decoder = new BmpBitmapDecoder (file,
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames.First();
            frame.Freeze();
            return new ImageData (frame, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            var encoder = new BmpBitmapEncoder();
            encoder.Frames.Add (BitmapFrame.Create (image.Bitmap));
            encoder.Save (file);
        }

        void SkipBytes (BinaryReader file, uint num)
        {
            if (file.BaseStream.CanSeek)
                file.BaseStream.Seek (num, SeekOrigin.Current);
            else
            {
                for (int i = 0; i < num / 4; ++i)
                    file.ReadInt32();
                for (int i = 0; i < num % 4; ++i)
                    file.ReadByte();
            }
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            int c1 = stream.ReadByte();
            int c2 = stream.ReadByte();
            if (0x42 != c1 || 0x4d != c2)
                return null;
            using (var file = new ArcView.Reader (stream))
            {
                uint size = file.ReadUInt32();
                if (size < 14+40)
                    return null;
                SkipBytes (file, 8);
                uint header_size = file.ReadUInt32();
                if (header_size < 40 || size-14 < header_size)
                    return null;
                uint width = file.ReadUInt32();
                uint height = file.ReadUInt32();
                file.ReadInt16();
                int bpp = file.ReadInt16();
                return new ImageMetaData {
                    Width = width,
                    Height = height,
                    OffsetX = 0,
                    OffsetY = 0,
                    BPP = bpp
                };
            }
        }
    }
}
