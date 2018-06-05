//! \file       AudioNSF.cs
//! \date       2018 Jun 05
//! \brief      Pan engine PCM audio format.
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

using System;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Pan
{
    [Export(typeof(AudioFormat))]
    public class NsfAudio : AudioFormat
    {
        public override string         Tag { get { return "NSF"; } }
        public override string Description { get { return "Pan engine PCM audio"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public NsfAudio ()
        {
            Signatures = new uint[] { 0x010001, 0x020001, 0 };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            if (!file.Name.HasExtension ("NSF"))
                return null;
            var header = file.ReadHeader (0x10);
            var format = new WaveFormat {
                FormatTag           = header.ToUInt16 (0),
                Channels            = header.ToUInt16 (2),
                SamplesPerSecond    = header.ToUInt32 (4),
                AverageBytesPerSecond = header.ToUInt16 (8),
                BlockAlign          = header.ToUInt16 (12),
                BitsPerSample       = header.ToUInt16 (14),
            };
            if (format.FormatTag != 1 ||
                format.SamplesPerSecond * format.Channels * format.BitsPerSample / 8 != format.AverageBytesPerSecond)
                return null;
            var pcm = new StreamRegion (file.AsStream, 0x10);
            return new RawPcmInput (pcm, format);
        }
    }
}
