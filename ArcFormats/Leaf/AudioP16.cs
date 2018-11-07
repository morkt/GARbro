//! \file       AudioP16.cs
//! \date       2018 Nov 06
//! \brief      Leaf PCM audio format.
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

namespace GameRes.Formats.Leaf
{
    [Export(typeof(AudioFormat))]
    public sealed class P16Audio : AudioFormat
    {
        public override string         Tag { get { return "P16"; } }
        public override string Description { get { return "Leaf PCM audio format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            if (!file.Name.HasExtension ("P16"))
                return null;
            var format = new WaveFormat {
                FormatTag   = 1,
                Channels    = 1,
                SamplesPerSecond = 44100,
                AverageBytesPerSecond = 88200,
                BlockAlign  = 2,
                BitsPerSample = 16,
            };
            return new RawPcmInput (file.AsStream, format);
        }
    }
}
