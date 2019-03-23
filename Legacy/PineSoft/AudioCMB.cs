//! \file       AudioCMB.cs
//! \date       2019 Mar 21
//! \brief      PineSoft PCM audio.
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

namespace GameRes.Formats.PineSoft
{
    [Export(typeof(AudioFormat))]
    public class CmbAudio : AudioFormat
    {
        public override string         Tag { get { return "CMB/PCM"; } }
        public override string Description { get { return "PineSoft PCM audio"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public CmbAudio ()
        {
            Extensions = new string[] { "" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            int data_size = header.ToInt32 (0);
            int header_size = header.ToInt32 (4);
            if (data_size + header_size + 8 != file.Length || header[8] != 1)
                return null;
            var format = new WaveFormat {
                FormatTag = header.ToUInt16 (8),
                Channels = header.ToUInt16 (0xA),
                SamplesPerSecond = header.ToUInt32 (0xC),
                AverageBytesPerSecond = header.ToUInt32 (0x10),
                BlockAlign = header.ToUInt16 (0x14),
                BitsPerSample = header.ToUInt16 (0x16),
            };
            if (format.Channels != 1 && format.Channels != 2)
                return null;
            var pcm = new StreamRegion (file.AsStream, 8 + header_size);
            return new RawPcmInput (pcm, format);
        }
    }
}
