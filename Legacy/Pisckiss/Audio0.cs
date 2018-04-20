//! \file       Audio0.cs
//! \date       2018 Apr 20
//! \brief      Pisckiss encrypted audio.
//
// Copyright (C) 2018 by morkt
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to
// deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
// sell copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
//

using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Pisckiss
{
    [Export(typeof(AudioFormat))]
    public sealed class Audio1 : AudioFormat
    {
        public override string         Tag { get { return "WAV/0"; } }
        public override string Description { get { return "Pisckiss encrypted audio"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            int length = (int)file.Length;
            if (length < 4)
                return null;
            uint signature = file.ReadUInt8();
            switch (signature & 0x42)
            {
            case 0x42:
                length -= 3;
                break;
            case 0x02:
            case 0x40:
                length -= 1;
                break;
            default:
                return null;
            }
            uint key = (signature & 0xBD) ^ 0x6A8CD4E7u;
            var header = file.ReadBytes (4);
            file.Position = 1;
            Decrypt (header, key);

            SoundInput sound = null;
            if (header.AsciiEqual ("RIFF"))
                sound = new WaveInput (DecryptedStream (file, length, key));
            else if (header.AsciiEqual ("OggS"))
                sound = new OggInput (DecryptedStream (file, length, key));
            if (sound != null)
                file.Dispose();
            return sound;
        }

        static void Decrypt (byte[] data, uint key)
        {
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] ^= (byte)key;
                key = key << 1 | (((key & 0x10000) ^ (key >> 15)) >> 16);
            }
        }

        Stream DecryptedStream (IBinaryStream input, int length, uint key)
        {
            var data = input.ReadBytes (length);
            Decrypt (data, key);
            return new BinMemoryStream (data, input.Name);
        }
    }
}
