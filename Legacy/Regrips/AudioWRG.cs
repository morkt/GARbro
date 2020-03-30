//! \file       AudioWRG.cs
//! \date       2019 May 05
//! \brief      Regrips obfuscated audio.
//
// Copyright (C) 2019 by morkt
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

namespace GameRes.Formats.Regrips
{
    [Export(typeof(AudioFormat))]
    public class WrgAudio : AudioFormat
    {
        public override string         Tag { get { return "WRG"; } }
        public override string Description { get { return "Regrips encrypted WAVE file"; } }
        public override uint     Signature { get { return 0xB9B9B6AD; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var input = new XoredStream (file.AsStream, 0xFF);
            return Wav.TryOpen (new BinaryStream (input, file.Name));
        }
    }

    [Export(typeof(AudioFormat))]
    public class MrgAudio : AudioFormat
    {
        public override string         Tag { get { return "MRG"; } }
        public override string Description { get { return "Regrips encrypted MP3 file"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        static readonly ResourceInstance<AudioFormat> Mp3Format = new ResourceInstance<AudioFormat> ("MP3");

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (2);
            if (header[0] != 0)
                return null;
            file.Position = 0;
            var input = new XoredStream (file.AsStream, 0xFF);
            return Mp3Format.Value.TryOpen (new BinaryStream (input, file.Name));
        }
    }
}
