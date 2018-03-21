//! \file       AudioSTR.cs
//! \date       2018 Mar 18
//! \brief      UmeSoft audio file.
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
    public sealed class WstrAudio : AudioFormat
    {
        public override string         Tag { get { return "STR"; } }
        public override string Description { get { return "U-Me Soft audio file"; } }
        public override uint     Signature { get { return 0x52545357; } } // 'WSTR'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            var format = new WaveFormat {
                FormatTag = 1,
                Channels = header.ToUInt16 (4),
                SamplesPerSecond = header.ToUInt32 (8),
                BitsPerSample = header.ToUInt16 (6),
            };
            format.BlockAlign = (ushort)(format.Channels * format.BitsPerSample / 8);
            format.SetBPS();
            var input = new StreamRegion (file.AsStream, 0x20);
            return new RawPcmInput (input, format);
        }
    }
}
