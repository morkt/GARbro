//! \file       AudioWHC.cs
//! \date       2018 Dec 10
//! \brief      Basil Adventure System pcm audio.
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

namespace GameRes.Formats.Basil
{
    [Export(typeof(AudioFormat))]
    public class WhcAudio : AudioFormat
    {
        public override string         Tag { get { return "WHC"; } }
        public override string Description { get { return "Basil audio resource"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public WhcAudio ()
        {
            Signatures = new uint[] { 0x020001, 0x010001 };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".whc"))
                return null;
            var header = file.ReadHeader (0x12);
            var format = new WaveFormat {
                FormatTag = header.ToUInt16 (0),
                Channels = header.ToUInt16 (2),
                SamplesPerSecond = header.ToUInt32 (4),
                AverageBytesPerSecond = header.ToUInt32 (8),
                BlockAlign = header.ToUInt16 (0xC),
                BitsPerSample = header.ToUInt16 (0xE),
            };
            if (format.FormatTag != 1 || format.Channels < 1 || format.Channels > 2)
                return null;
            var pcm = new StreamRegion (file.AsStream, 0x12);
            return new RawPcmInput (pcm, format);
        }
    }
}
