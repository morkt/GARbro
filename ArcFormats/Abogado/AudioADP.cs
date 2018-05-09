//! \file       AudioADP.cs
//! \date       Thu Oct 20 09:29:26 2016
//! \brief      AbogadoPowers audio format.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Abogado
{
    [Export(typeof(AudioFormat))]
    public class AdpAudio : AudioFormat
    {
        public override string         Tag { get { return "ADP"; } }
        public override string Description { get { return "AbogadoPowers audio format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public AdpAudio ()
        {
            Signatures = new uint[] { 0x5622, 0xAC44, 0 };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0xC4);
            uint sample_rate = header.ToUInt32 (0);
            if (sample_rate < 8000 || sample_rate > 96000)
                return null;
            ushort channels = header.ToUInt16 (4);
            if (channels != 1 && channels != 2)
                return null;
            int samples = header.ToInt32 (0xBC) * channels;
            int start_pos = header.ToInt32 (0xC0);
            if (samples <= 0 || start_pos >= file.Length)
                return null;

            var output = new byte[2 * samples];
            var first = new AdpDecoder();
            var second = first;
            if (channels > 1)
                second = new AdpDecoder();
            file.Position = start_pos;
            int dst = 0;
            while (samples > 0)
            {
                byte v = file.ReadUInt8();
                LittleEndian.Pack (first.DecodeSample (v >> 4), output, dst);
                if (0 == --samples)
                    break;
                dst += 2;
                LittleEndian.Pack (second.DecodeSample (v), output, dst);
                dst += 2;
                --samples;
            }
            var format = new WaveFormat();
            format.FormatTag        = 1;
            format.Channels         = channels;
            format.SamplesPerSecond = sample_rate;
            format.AverageBytesPerSecond = 2u * channels * sample_rate;
            format.BlockAlign       = (ushort)(2 * channels);
            format.BitsPerSample    = 0x10;
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

        public short DecodeSample (int sample)
        {
            sample &= 0xF;
            var quant = QuantizeTable[quant_idx];
            quant_idx += IncrementTable[sample];
            if (quant_idx < 0)
                quant_idx = 0;
            else if (quant_idx > 0x58)
                quant_idx = 0x58;
            int step = (2 * (sample & 7) + 1) * quant >> 3;
            if (sample < 8)
            {
                sample = Math.Min (0x7FFF, prev_sample + step);
            }
            else
            {
                sample = Math.Max (-32768, prev_sample - step);
            }
            prev_sample = sample;
            return (short)sample;
        }

        internal static readonly ushort[] QuantizeTable = {
            0x0007, 0x0008, 0x0009, 0x000A, 0x000B, 0x000C, 0x000D, 0x000E,
            0x0010, 0x0011, 0x0013, 0x0015, 0x0017, 0x0019, 0x001C, 0x001F,
            0x0022, 0x0025, 0x0029, 0x002D, 0x0032, 0x0037, 0x003C, 0x0042,
            0x0049, 0x0050, 0x0058, 0x0061, 0x006B, 0x0076, 0x0082, 0x008F,
            0x009D, 0x00AD, 0x00BE, 0x00D1, 0x00E6, 0x00FD, 0x0117, 0x0133,
            0x0151, 0x0173, 0x0198, 0x01C1, 0x01EE, 0x0220, 0x0256, 0x0292,
            0x02D4, 0x031C, 0x036C, 0x03C3, 0x0424, 0x048E, 0x0502, 0x0583,
            0x0610, 0x06AB, 0x0756, 0x0812, 0x08E0, 0x09C3, 0x0ABD, 0x0BD0,
            0x0CFF, 0x0E4C, 0x0FBA, 0x114C, 0x1307, 0x14EE, 0x1706, 0x1954,
            0x1BDC, 0x1EA5, 0x21B6, 0x2515, 0x28CA, 0x2CDF, 0x315B, 0x364B,
            0x3BB9, 0x41B2, 0x4844, 0x4F7E, 0x5771, 0x602F, 0x69CE, 0x7462, 0x7FFF,
        };

        internal static readonly int[] IncrementTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };
    }
}
