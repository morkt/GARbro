using GameRes.Compression;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Kid
{
    [Export(typeof(ImageFormat))]
    public class SpcFormat: LbgFormat
    {
        public override string Tag { get { return "SPC/PS2"; } }
        public override string Description { get { return "PS2 CRI MiddleWare compressed texture format"; } }
        public override uint Signature { get { return 0; } }
        public SpcFormat()
        {
            Extensions = new string[] { "spc" };
        }

        public override ImageMetaData ReadMetaData(IBinaryStream stream)
        {
            uint unpacked_size = stream.Signature;
            if (unpacked_size <= 0x20 || unpacked_size > 0x5000000) // ~83MB
                return null;
            stream.Position = 4;
            using (var lzss = new LzssStream(stream.AsStream, LzssMode.Decompress, true))
            using (var input = new SeekableStream(lzss))
            using (var lbg = new BinaryStream(input, stream.Name))
                return base.ReadMetaData(lbg);
        }

        public override ImageData Read(IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 4;
            using (var lzss = new LzssStream(stream.AsStream, LzssMode.Decompress, true))
            using (var input = new SeekableStream(lzss))
            using (var lbg = new BinaryStream(input, stream.Name))
                return base.Read(lbg, info);
        }

        public override void Write(Stream file, ImageData image)
        {
            throw new System.NotImplementedException("SpcFormat.Write not implemented");
        }
    }
}
