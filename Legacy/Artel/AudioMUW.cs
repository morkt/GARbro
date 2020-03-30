//! \file       AudioMUW.cs
//! \date       2019 May 22
//! \brief      ADVG Script Interpreter System audio file.
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

namespace GameRes.Formats.Artel
{
    [Export(typeof(AudioFormat))]
    public class MuwAudio : AudioFormat
    {
        public override string         Tag { get { return "MUW"; } }
        public override string Description { get { return "Artel ADVG engine audio file"; } }
        public override uint     Signature { get { return 0x46464952; } } // 'RIFF'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x24);
            if (!header.AsciiEqual (0, "RIFF") || !header.AsciiEqual (8, "PCMWFMT "))
                return null;
            uint data_pos = header.ToUInt32 (0x10) + 0x14;
            file.Position = data_pos;
            if (file.ReadUInt32() != 0x61746164) // 'data'
                return null;
            uint data_size = file.ReadUInt32();
            var format = new WaveFormat {
                FormatTag = header.ToUInt16 (0x14),
                Channels = header.ToUInt16 (0x16),
                SamplesPerSecond = header.ToUInt32 (0x18),
                AverageBytesPerSecond = header.ToUInt32 (0x1C),
                BlockAlign = header.ToUInt16 (0x20),
                BitsPerSample = header.ToUInt16 (0x22),
            };
            var pcm = new StreamRegion (file.AsStream, data_pos+8, data_size);
            return new RawPcmInput (pcm, format);
        }
    }
}
