//! \file       AudioSED.cs
//! \date       2023 Sep 26
//! \brief      MyHarvest audio format.
//
// Copyright (C) 2023 by morkt
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

namespace GameRes.Formats.MyHarvest
{
    [Export(typeof(AudioFormat))]
    public class SedAudio : AudioFormat
    {
        public override string         Tag => "SED/HARVEST";
        public override string Description => "MyHarvest audio resource";
        public override uint     Signature => 0x14553; // 'SE'
        public override bool      CanWrite => false;

        public SedAudio ()
        {
            Signatures = new[] { 0x14553u, 0u };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            if (!header.AsciiEqual (0, "SE") || !header.AsciiEqual (0x12, "da"))
                return null;
            var format = new WaveFormat {
                FormatTag               = header.ToUInt16 (2),
                Channels                = header.ToUInt16 (4),
                SamplesPerSecond        = header.ToUInt32 (6),
                AverageBytesPerSecond   = header.ToUInt32 (0xA),
                BlockAlign              = header.ToUInt16 (0xE),
                BitsPerSample           = header.ToUInt16 (0x10),
            };
            uint pcm_size = header.ToUInt32 (0x14);
            var region = new StreamRegion (file.AsStream, 0x18, pcm_size);
            return new RawPcmInput (region, format);
        }
    }
}
