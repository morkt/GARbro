//! \file       AudioW.cs
//! \date       Thu Aug 18 06:04:50 2016
//! \brief      Leaf PCM audio.
//
// Copyright (C) 2016 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Leaf
{
    [Export(typeof(AudioFormat))]
    public class WAudio : AudioFormat
    {
        public override string         Tag { get { return "W/Leaf"; } }
        public override string Description { get { return "Leaf PCM audio"; } }
        public override uint     Signature { get { return 0; } }

        public WAudio ()
        {
            Extensions = new string[] { "w" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x12);
            var format = new WaveFormat
            {
                FormatTag           = 1,
                Channels            = header[0],
                SamplesPerSecond    = header.ToUInt16 (2),
                AverageBytesPerSecond = header.ToUInt32 (6),
                BlockAlign          = header[1],
                BitsPerSample       = header.ToUInt16 (4),
            };
            uint pcm_size = header.ToUInt32 (0xA);
            if (0 == pcm_size || 0 == format.AverageBytesPerSecond || format.BitsPerSample < 8
                || 0 == format.Channels || format.Channels > 8
                || (format.BlockAlign * format.SamplesPerSecond != format.AverageBytesPerSecond)
                || (pcm_size + 0x12 != file.Length))
                return null;
            var pcm = new StreamRegion (file.AsStream, 0x12, pcm_size);
            return new RawPcmInput (pcm, format);
        }
    }
}
