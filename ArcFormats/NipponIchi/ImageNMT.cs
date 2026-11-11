using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.NipponIchi
{
    [Export(typeof(ImageFormat))]
    public class NmtFormat : ImageFormat {
        public override string Tag { get { return "NMT"; } }
        public override string Description { get { return "NIS Multy Texform image format"; } }
        public override uint Signature { get { return 0x6D73696E; } } // 'nism'

        public override ImageMetaData ReadMetaData(IBinaryStream file)
        {
            var header = file.ReadHeader(0x30);
            var signature = file.ReadHeader(15);
            if (System.Text.Encoding.ASCII.GetString(signature.ToArray()) != "nismultitexform")
                return null;
            return new ImageMetaData
            {
                Width = header.ToUInt16(0x26),
                Height = header.ToUInt16(0x28)
            };
        }
        public override ImageData Read(IBinaryStream file, ImageMetaData info)
        {
            //uint imagedatasize = info.Width * info.Height * 4;
            if (info == null) throw new NotSupportedException(string.Format("Not NMT texture format."));
            file.Position = 0x30;
            var data = file.ReadBytes(info.iWidth * info.iHeight * 4);
            return ImageData.Create(info, PixelFormats.Bgra32, null, data);
        }
        public override void Write(Stream file, ImageData image)
        {
            throw new System.NotImplementedException("NmtFormat.Write not implemented");
        }
    }
}
