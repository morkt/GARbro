//! \file       AudioMSF.cs
//! \date       2018 Jun 19
//! \brief      'Unknown' PCM audio format.
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
using System.IO;

namespace GameRes.Formats.Unknown
{
    [Export(typeof(AudioFormat))]
    public class MsfAudio : AudioFormat
    {
        public override string         Tag { get { return "MSF"; } }
        public override string Description { get { return "'Unknown' PCM audio format"; } }
        public override uint     Signature { get { return 0x2046534D; } } // 'MSF '
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadBytes (0x30);
            ushort key = (ushort)(101 * header.ToUInt16 (4) + 778);
            for (int i = 0; i < header.Length; i += 2)
            {
                header[i  ] ^= (byte)key;
                header[i+1] ^= (byte)(key >> 8);
            }
            var format = new WaveFormat {
                FormatTag       = header.ToUInt16 (0x1C),
                Channels        = header.ToUInt16 (0x1E),
                SamplesPerSecond = header.ToUInt32 (0x20),
                AverageBytesPerSecond = header.ToUInt32 (0x24),
                BlockAlign      = header.ToUInt16 (0x28),
                BitsPerSample   = header.ToUInt16 (0x2A),
            };
            uint pcm_size = header.ToUInt32 (0x18);
            var pcm = new StreamRegion (file.AsStream, 0x30, pcm_size);
            return new RawPcmInput (pcm, format);
        }
    }
}
