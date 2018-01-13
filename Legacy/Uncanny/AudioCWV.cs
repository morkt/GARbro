//! \file       AudioCWV.cs
//! \date       2018 Jan 13
//! \brief      Uncanny encrypted WAV file.
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

namespace GameRes.Formats.Uncanny
{
    [Export(typeof(AudioFormat))]
    public class CwvAudio : AudioFormat
    {
        public override string         Tag { get { return "CWV"; } }
        public override string Description { get { return "Uncanny encrypted WAV audio"; } }
        public override uint     Signature { get { return 0xA06F8BF7; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadBytes (0x10);
            Decrypt (header, 0, 0x10);
            if (!header.AsciiEqual (8, "WAVEfmt "))
                return null;
            file.Position = 0;
            var wave = file.ReadBytes ((int)file.Length);
            Decrypt (wave, 0, wave.Length);
            var input = new BinMemoryStream (wave);
            var sound = Wav.TryOpen (input);
            if (sound != null)
                file.Dispose();
            return sound;
        }

        static void Decrypt (byte[] data, int pos, int length)
        {
            uint key = 0x4B5AB4A5;
            for (int i = 0; i < length; ++i)
            {
                byte x = (byte)(key ^ data[pos+i]);
                data[pos+i] = x;
                key = ((key << 9) | (key >> 23) & 0x1F0) ^ x;
            }
        }
    }
}


