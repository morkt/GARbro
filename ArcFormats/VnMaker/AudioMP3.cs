using System.ComponentModel.Composition;

namespace GameRes.Formats.VnMaker
{
    [Export(typeof(AudioFormat))]
    public class AudioMP3 : Mp3Audio
    {
        public override string         Tag { get { return "MP3/VnMaker Encrypted MP3 Audio"; } }
        public override string Description { get { return "VnMaker Encrypted MP3 Audio"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public AudioMP3() : base()
        {
            Signatures = new uint[] { 0 };
            Extensions = new[] { "mp3" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            using (var input = DeobfuscateStream (file, GuessEncryptionKey (file)))
            {
                return base.TryOpen (input);
            }
        }

        byte[] GuessEncryptionKey (IBinaryStream file)
        {
            return new byte[] { 0x0A, 0x2B, 0x36, 0x6F, 0x0B };
        }

        IBinaryStream DeobfuscateStream (IBinaryStream file, byte[] key)
        {
            var mp3 = new ByteStringEncryptedStream (file.AsStream, key, true);
            return new BinaryStream (mp3, file.Name);
        }
    }
}
