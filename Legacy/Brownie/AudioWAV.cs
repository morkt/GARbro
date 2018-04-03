//! \file       AudioWAV.cs
//! \date       2018 Apr 02
//! \brief      Brownie obfuscated WAV file.
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

namespace GameRes.Formats.Brownie
{
    [Export(typeof(AudioFormat))]
    public class WavAudio : AudioFormat
    {
        public override string         Tag { get { return "WAV/BROWNIE"; } }
        public override string Description { get { return "Brownie obfuscated WAV file"; } }
        public override uint     Signature { get { return 0x58742367; } } // 'g#tX'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10).ToArray();
            header[0] = (byte)'R';
            header[1] = (byte)'I';
            header[2] = (byte)'F';
            header[3] = (byte)'F';
            for (int i = 4; i < 0x10; ++i)
                header[i] ^= 0x5C;
            if (!header.AsciiEqual (8, "WAVE"))
                return null;
            Stream input = new StreamRegion (file.AsStream, 0x10);
            input = new PrefixStream (header, input);
            return new WaveInput (input);
        }
    }
}
