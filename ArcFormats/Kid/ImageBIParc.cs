using GameRes.Compression;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Kid
{
    [Export(typeof(ImageFormat))]
    public class BipArkFormat: BipFormat
    {
        public override string Tag { get { return "BIP/PS2 compressed"; } }
        public override string Description { get { return "PS2 tiled bitmap format with lzss compress"; } }
        public override uint Signature { get { return 0; } }
        public BipArkFormat()
        {
            Extensions = new string[] { "bip" };
        }

        public override ImageMetaData ReadMetaData(IBinaryStream stream)
        {
            uint unpacked_size = stream.Signature;
            if (unpacked_size <= 0x20 || unpacked_size > 0x5000000) // ~83MB
                return null;
            stream.Position = 4;
            using (var lzss = new LzssStream(stream.AsStream, LzssMode.Decompress, true))
            using (var input = new SeekableStream(lzss))
            using (var bip = new BinaryStream(input, stream.Name))
                return base.ReadMetaData(bip);
        }
        public override ImageData Read(IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 4;
            using (var lzss = new LzssStream(stream.AsStream, LzssMode.Decompress, true))
            using (var input = new SeekableStream(lzss))
            using (var bip = new BinaryStream(input, stream.Name))
                return base.Read(bip, info);
        }
        public override void Write(Stream file, ImageData image)
        {
            throw new System.NotImplementedException("BipFormat.Write not implemented");
        }
    }
}
