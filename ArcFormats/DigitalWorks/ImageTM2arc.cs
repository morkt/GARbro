using GameRes.Compression;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.DigitalWorks
{
    [Export(typeof(ImageFormat))]
    public class TM2ArkFormat : Tim2Format 
    {
        public override string Tag { get { return "TIM2/PS2 compressed"; } }
        public override string Description { get { return "PlayStation/2 image format with LZSS compress"; } }
        public override uint Signature { get { return 0x535A4C; } } // 'LZS'
        public TM2ArkFormat()
        {
            Extensions = new string[] { "tm2" };
            Settings = null;
        }

        public override ImageMetaData ReadMetaData(IBinaryStream stream)
        {
            stream.Position = 9;
            uint real_sign = stream.ReadUInt32();
            //Tim2Format tm2raw = new Tim2Format();
            if (real_sign != base.Signature)
            {
                return null;
            }
            stream.Position = 4;
            uint unpacked_size = stream.ReadUInt32();
            if (unpacked_size <= 0x20 || unpacked_size > 0x5000000) // ~83MB
                return null;
            stream.Position = 8;
            using (var lzss = new LzssStream(stream.AsStream, LzssMode.Decompress, true))
            using (var input = new SeekableStream(lzss))
            using (var tm2 = new BinaryStream(input, stream.Name))
                return base.ReadMetaData(tm2);
        }
        public override ImageData Read(IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 8;
            using (var lzss = new LzssStream(stream.AsStream, LzssMode.Decompress, true))
            using (var input = new SeekableStream(lzss))
            using (var tm2 = new BinaryStream(input, stream.Name))
                return base.Read(tm2, info);
        }
        public override void Write(Stream file, ImageData image)
        {
            throw new System.NotImplementedException("TM2ArkFormat.Write not implemented");
        }
    }
}
