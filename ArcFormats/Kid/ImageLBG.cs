using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.IO;

namespace GameRes.Formats.Kid
{
    [Export(typeof(ImageFormat))]
    public class LbgFormat : ImageFormat
    {
        public override string Tag { get { return "LBG/PS2-SPC"; } }
        public override string Description { get { return "KID PS2 SPC Image Format"; } }
        public override uint Signature { get { return 0; } } //real signature may exist below

        public LbgFormat()
        {
            Extensions = new string[] { "", "lbg" };
            // Actually LBG format can contain more than one image as lip for chara, well, "not implemented".
        }

        public override ImageMetaData ReadMetaData(IBinaryStream file)
        {
            uint header = file.ReadUInt32();
            uint overheader = 0;

            if (header != 0x2047424C) //LBG\x20
            {
                overheader = header;
                if (overheader > 0x50) //usually 10 or 30, never seen any larger
                    return null;
                file.Seek(overheader, SeekOrigin.Begin);
                header = file.ReadUInt32();
                if (header != 0x2047424C) //LBG\x20
                    return null;
            }

            int width = file.ReadInt16();
            int height = file.ReadInt16();
            if (width <= 0 || height <= 0)
                return null;

            return new LbgImageMetaData
            {
                Width = (uint)width,
                Height = (uint)height,
                OverHeader = overheader,
            };
        }
        public override ImageData Read(IBinaryStream file, ImageMetaData info)
        {
            if (info == null)
                throw new NotSupportedException(string.Format("Not LBG texture format."));

            /*if (info.Width != 640)
            {
                throw new NotSupportedException(string.Format("Not 640 pixels width, may not work."));
            }*/
            var lbgheader = (LbgImageMetaData)info;
            uint oversize = 0;
            uint blocknum = info.Width / 126;
            if (lbgheader.OverHeader != 0)
            {
                file.Position = 4;
                uint filesize = file.ReadUInt32();
                if (filesize == 0)
                    oversize = 8;
                else
                {
                    filesize -= 16;
                    oversize = (filesize - info.Width * info.Height * 4) / info.Height / (blocknum + 1);
                }
            }
            file.Position = lbgheader.OverHeader + 0x10;
            //List<byte> pixels = new List<byte>();
            byte[] pixels = new byte[info.Width * info.Height * 4];
            for (int blockcount = 0; blockcount <= blocknum; blockcount++)
            {
                for (int y = 0; y < info.Height; y++)
                {
                    for (int x = 0; (blockcount != blocknum & x < 126) | (blockcount == blocknum & x < info.Width - blockcount * 126); x++)
                    {
                        var pixel = file.ReadBytes(4); //RGBA with wrong A
                        long target = (x + blockcount * 126) * 4 + y * info.Width * 4;
                        //BGRA
                        pixels[target] = pixel[2];
                        pixels[target + 1] = pixel[1];
                        pixels[target + 2] = pixel[0];
                        if (pixel[3] >= byte.MaxValue / 2)
                            pixels[target + 3] = byte.MaxValue;
                        else
                            pixels[target + 3] = (byte)(pixel[3] << 1); //needs a test
                    }
                    if (oversize != 0)
                        file.ReadBytes((int)oversize);
                }
            }
            return ImageData.Create(info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write(Stream file, ImageData bitmap)
        {
            throw new NotImplementedException("LbgFormat.Write not implemented");
        }
    }
    class LbgImageMetaData : ImageMetaData
    {
        public uint OverHeader { get; set; }
    }
}
