using System.ComponentModel.Composition;

namespace GameRes.Formats.VnMaker
{
    [Export(typeof(AudioFormat))]
    public class AudioOGG : AudioFormat
    {
        public override string         Tag { get { return "OGG/VnMaker Encrypted OGG Audio"; } }
        public override string Description { get { return "VnMaker Encrypted OGG Audio"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public AudioOGG() : base()
        {
            Signatures = new uint[] { 0 };
            Extensions = new[] { "ogg" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            using (var input = DeobfuscateStream (file, GuessEncryptionKey (file)))
            {
                if (input.Signature != 0x5367674F)
                    throw new InvalidFormatException ();
                return new OggInput (input.AsStream);
            }
        }

        byte[] GuessEncryptionKey (IBinaryStream file)
        {
            return new byte[] { 0x0A, 0x2B, 0x36, 0x6F, 0x0B };
        }

        IBinaryStream DeobfuscateStream (IBinaryStream file, byte[] key)
        {
            var ogg = new ByteStringEncryptedStream (file.AsStream, key, true);
            return new BinaryStream (ogg, file.Name);
        }
    }
}
