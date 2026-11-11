using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.VnMaker
{
    [Export(typeof(ImageFormat))]
    public class PngFormat : GameRes.PngFormat
    {
        public override string         Tag { get { return "PNG/VnMaker Encrypted PNG Image"; } }
        public override string Description { get { return "VnMaker Encrypted PNG Image"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return true; } }

        public PngFormat() : base()
        {
            Signatures = new uint[] { 0 };
            Extensions = new[] { "png" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            using (var input = DeobfuscateStream (file, GuessEncryptionKey (file)))
            {
                if (input.Signature != 0x474E5089)
                    throw new InvalidFormatException ();
                return base.ReadMetaData (input);
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var input = DeobfuscateStream (file, GuessEncryptionKey (file)))
            {
                if (input.Signature != 0x474E5089)
                    throw new InvalidFormatException ();
                return base.Read (input, info);
            }
        }

        byte[] GuessEncryptionKey (IBinaryStream file)
        {
            return new byte[] { 0x0A, 0x2B, 0x36, 0x6F, 0x0B };
        }

        IBinaryStream DeobfuscateStream (IBinaryStream file, byte[] key)
        {
            var png = new ByteStringEncryptedStream (file.AsStream, key, true);
            return new BinaryStream (png, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            var ms = new MemoryStream ();
            base.Write (ms, image);
            ms.Position = 0;
            var es = new ByteStringEncryptedStream (ms, GuessEncryptionKey (null));
            es.CopyTo (file);
            file.Flush ();
        }
    }
}
