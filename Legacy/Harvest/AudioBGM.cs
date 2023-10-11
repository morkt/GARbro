//! \file       AudioBGM.cs
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
    public class BgmAudio : AudioFormat
    {
        public override string         Tag => "BGM/HARVEST";
        public override string Description => "MyHarvest audio resource";
        public override uint     Signature => 0x304D4742; // 'BMG0'
        public override bool      CanWrite => false;

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x1C);
            if (!header.AsciiEqual (0x14, "dar\0"))
                return null;
            var format = new WaveFormat {
                FormatTag               = header.ToUInt16 (4),
                Channels                = header.ToUInt16 (6),
                SamplesPerSecond        = header.ToUInt32 (8),
                AverageBytesPerSecond   = header.ToUInt32 (0xC),
                BlockAlign              = header.ToUInt16 (0x10),
                BitsPerSample           = header.ToUInt16 (0x12),
            };
            uint pcm_size = header.ToUInt32 (0x18);
            var region = new StreamRegion (file.AsStream, 0x1C, pcm_size);
            return new RawPcmInput (region, format);
        }
    }
}
