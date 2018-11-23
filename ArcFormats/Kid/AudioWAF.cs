//! \file       AudioWAF.cs
//! \date       2018 Nov 18
//! \brief      KID ADPCM audio format.
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
using System.Linq;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Kid
{
    [Export(typeof(AudioFormat))]
    public class WafAudio : AudioFormat
    {
        public override string         Tag { get { return "WAF"; } }
        public override string Description { get { return "KID ADPCM audio file"; } }
        public override uint     Signature { get { return 0x464157; } } // 'WAF'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x38);
            var format = new WaveFormat {
                FormatTag = 2,
                Channels = header.ToUInt16 (6),
                SamplesPerSecond = header.ToUInt32 (8),
                AverageBytesPerSecond = header.ToUInt32 (0xC),
                BlockAlign = header.ToUInt16 (0x10),
                BitsPerSample = header.ToUInt16 (0x12),
                ExtraSize = 0x20,
            };
            var codec_data = header.Skip (0x14).Take (format.ExtraSize).ToArray();
            int adpcm_length = header.ToInt32 (0x34);
            byte[] wav_header;
            using (var wav = new MemoryStream())
            using (var buffer = new BinaryWriter (wav, Encoding.ASCII, true))
            {
                buffer.Write (Wav.Signature);
                buffer.Write (adpcm_length + 0x46);
                buffer.Write (0x45564157); // 'WAVE'
                buffer.Write (0x20746d66); // 'fmt '
                buffer.Write (0x32);
                buffer.Write (format.FormatTag);
                buffer.Write (format.Channels);
                buffer.Write (format.SamplesPerSecond);
                buffer.Write (format.AverageBytesPerSecond);
                buffer.Write (format.BlockAlign);
                buffer.Write (format.BitsPerSample);
                buffer.Write (format.ExtraSize);
                buffer.Write (codec_data);
                buffer.Write (0x61746164); // 'data'
                buffer.Write (adpcm_length);
                buffer.Flush();
                wav_header = wav.ToArray();
            }
            Stream input = new StreamRegion (file.AsStream, file.Position);
            input = new PrefixStream (wav_header, input);
            return new WaveInput (input);
        }
    }
}
