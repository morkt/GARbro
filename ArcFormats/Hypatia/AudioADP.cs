//! \file       AudioADP.cs
//! \date       2018 Sep 21
//! \brief      Hypatia compressed audio format.
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
using GameRes.Utility;

namespace GameRes.Formats.Hypatia
{
    [Export(typeof(AudioFormat))]
    public class AdpAudio : AudioFormat
    {
        public override string         Tag { get { return "ADP/HYPATIA"; } }
        public override string Description { get { return "Hypatia compressed audio format"; } }
        public override uint     Signature { get { return 0x31504441; } } // 'ADP1'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            int samples = header.ToInt32 (4);
            uint sample_rate = header.ToUInt32 (8);
            if (sample_rate < 8000 || sample_rate > 96000)
                return null;
            ushort channels = header.ToUInt16 (12);
            if (channels != 1 && channels != 2)
                return null;
            samples *= channels;

            var output = new byte[2 * samples];
            var first = new AdpDecoder();
            var second = first;
            if (channels > 1)
                second = new AdpDecoder();
            int dst = 0;
            while (samples > 0)
            {
                int v = file.ReadByte();
                if (-1 == v)
                    break;
                LittleEndian.Pack (first.DecodeSample (v), output, dst);
                if (0 == --samples)
                    break;
                dst += 2;
                LittleEndian.Pack (second.DecodeSample (v >> 4), output, dst);
                dst += 2;
                --samples;
            }

            var format = new WaveFormat {
                FormatTag        = 1,
                Channels         = channels,
                SamplesPerSecond = sample_rate,
                AverageBytesPerSecond = 2u * channels * sample_rate,
                BlockAlign       = (ushort)(2 * channels),
                BitsPerSample    = 16,
            };
            var pcm = new MemoryStream (output);
            var sound = new RawPcmInput (pcm, format);
            file.Dispose();
            return sound;
        }
    }

    internal class AdpDecoder
    {
        int prev_sample;
        int quant_idx = 0;

        public AdpDecoder (int init_sample = 0)
        {
            prev_sample = init_sample;
        }

        public void Reset (short s, int q)
        {
            prev_sample = s;
            quant_idx = q;
        }

        public short DecodeSample (int src)
        {
            src &= 0xF;
            int sample = ScaleTable[src] * QuantizeTable[quant_idx] + prev_sample;
            if (sample < -32768)
                sample = -32768;
            else if (sample > 0x7FFF)
                sample = 0x7FFF;
            prev_sample = sample;

            quant_idx += IncrementTable[src];
            if (quant_idx < 0)
                quant_idx = 0;
            else if (quant_idx > 48)
                quant_idx = 48;
            return (short)sample;
        }

        static readonly ushort[] QuantizeTable = {
            0x0010, 0x0011, 0x0013, 0x0015, 0x0017, 0x0019, 0x001C, 0x001F, 0x0022, 0x0025, 0x0029, 0x002D,
            0x0032, 0x0037, 0x003C, 0x0042, 0x0049, 0x0050, 0x0058, 0x0061, 0x006B, 0x0076, 0x0082, 0x008F,
            0x009D, 0x00AD, 0x00BE, 0x00D1, 0x00E6, 0x00FD, 0x0117, 0x0133, 0x0151, 0x0173, 0x0198, 0x01C1,
            0x01EE, 0x0220, 0x0256, 0x0292, 0x02D4, 0x031C, 0x036C, 0x03C3, 0x0424, 0x048E, 0x0502, 0x0583,
            0x0610,
        };
        static readonly short[] ScaleTable = {
             2,  6,  0xA,  0xE,  0x12,  0x16,  0x1A,  0x1E,
            -2, -6, -0xA, -0xE, -0x12, -0x16, -0x1A, -0x1E,
        };
        static readonly sbyte[] IncrementTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };
    }
}
