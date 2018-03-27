//! \file       AudioIKE.cs
//! \date       2018 Mar 22
//! \brief      ike-compressed wave file.
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

namespace GameRes.Formats.UMeSoft
{
    [Export(typeof(AudioFormat))]
    public sealed class IkeAudio : AudioFormat
    {
        public override string         Tag { get { return "WAV/IKE"; } }
        public override string Description { get { return "ike-compressed WAVE audio"; } }
        public override uint     Signature { get { return 0x6B69899D; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x13);
            if (!header.AsciiEqual (2, "ike") || !header.AsciiEqual (0xF, "RIFF"))
                return null;
            int unpacked_size = IkeReader.DecodeSize (header[10], header[11], header[12]);
            var wav = IkeReader.CreateStream (file, unpacked_size);
            var sound = Wav.TryOpen (wav);
            if (sound != null)
                file.Dispose();
            else
                wav.Dispose();
            return sound;
        }
    }
}


