//! \file       AudioKWF.cs
//! \date       2017 Dec 11
//! \brief      DiceSystem audio file.
//
// Copyright (C) 2017 by morkt
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

using System;
using System.ComponentModel.Composition;

namespace GameRes.Formats.Dice
{
    [Export(typeof(AudioFormat))]
    public class KwfAudio : AudioFormat
    {
        public override string         Tag { get { return "KWF"; } }
        public override string Description { get { return "DiceSystem audio format"; } }
        public override uint     Signature { get { return 0x3046574B; } } // 'KWF0'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x40);
            int method = header.ToInt32 (4);
            if (method != 3)
                throw new NotImplementedException();
            var format = new WaveFormat {
                FormatTag       = header.ToUInt16 (0x28),
                Channels        = header.ToUInt16 (0x2A),
                SamplesPerSecond = header.ToUInt32 (0x2C),
                AverageBytesPerSecond = header.ToUInt32 (0x30),
                BlockAlign      = header.ToUInt16 (0x34),
                BitsPerSample   = header.ToUInt16 (0x36),
            };
            var pcm = new StreamRegion (file.AsStream, 0x40);
            return new RawPcmInput (pcm, format);
        }
    }
}
