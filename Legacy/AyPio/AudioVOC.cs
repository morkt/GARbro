//! \file       AudioVOC.cs
//! \date       2023 Oct 19
//! \brief      AyPio ADPCM-compressed audio.
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

using GameRes.Utility;
using System.ComponentModel.Composition;

namespace GameRes.Formats.AyPio
{
    [Export(typeof(AudioFormat))]
    public class VocAudio : AudioFormat
    {
        public override string         Tag => "VOC/UK2";
        public override string Description => "UK2 engine compressed audio";
        public override uint     Signature => 0x81564157; // 'WAV\x81'
        public override bool      CanWrite => false;

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x3C);
            if (!header.AsciiEqual (0x38, "RIFF"))
                return null;
            var decoder = new VocDecoder (file);
            var samples = decoder.Decode();
            var stream = new BinMemoryStream (samples, file.Name);
            file.Dispose();
            return new RawPcmInput (stream, decoder.Format);
        }
    }

    internal sealed class VocDecoder
    {
        IBinaryStream   m_input;

        int     m_sample_count;
        byte    m_channels;
        byte    m_bits_per_sample;
        byte[]  m_prev_sample = new byte[2];
        long    m_start_pos;

        public WaveFormat Format { get; private set; }

        public VocDecoder (IBinaryStream input)
        {
            m_input = input;
            var header = input.ReadHeader (0x38);
            Format = new WaveFormat {
                FormatTag = header.ToUInt16 (0x21),
                Channels = header.ToUInt16 (0x23),
                SamplesPerSecond = header.ToUInt32 (0x25),
                AverageBytesPerSecond = header.ToUInt32 (0x29),
                BlockAlign = header.ToUInt16 (0x2D),
                BitsPerSample = header.ToUInt16 (0x2F),
            };
            m_sample_count = header.ToInt32 (0x18);
            m_channels = header[8];
            m_prev_sample[0] = header[0xC];
            m_prev_sample[1] = header[0x10];
            m_bits_per_sample = header[0x20];
            m_output = new byte[m_sample_count << 1];
            m_output[0] = header[0xA];
            m_output[1] = header[0xB];
            m_output[2] = header[0xE];
            m_output[3] = header[0xF];
            m_start_pos = header.ToUInt32 (4) + header.ToUInt32 (0x14);
        }

        byte[] m_output;
        int[] m_samples;
        int m_src;

        public byte[] Decode ()
        {
            m_input.Position = m_start_pos;
            m_src = 0;
            BuildSamples();
            int count = m_sample_count - m_channels;
            int src = 0;
            int pos = 0;
            while (src < count)
            {
                byte sample = GetSample();
                int v7 = sample & 7;
                int channel;
                if (m_channels == 1 || (src & 1) == 0)
                    channel = 0;
                else
                    channel = 1;
                byte prev = m_prev_sample[channel];
                int s = m_samples[89 * v7 + prev];
                if ((sample & 8) != 0)
                    s = -s;
                s += m_output.ToInt16 (pos);
                pos += 2;
                LittleEndian.Pack (Clamp (s), m_output, pos);
                int p = IndexTable[v7] + prev;
                if (p < 0)
                    p = 0;
                else if (p > 88)
                    p = 88;
                m_prev_sample[channel] = (byte)p;
                ++src;
            }
            return m_output;
        }

        byte m_current_sample;

        byte GetSample ()
        {
            if (0 == (m_src & 1))
                m_current_sample = m_input.ReadUInt8();
            ++m_src;
            byte sample = m_current_sample;
            m_current_sample >>= 4;
            return sample &= 0xF;
        }

        short Clamp (int sample)
        {
            if (sample > 0x7FFF)
                sample = 0x7FFF;
            else if (sample < -0x8000)
                sample = -0x8000;
            return (short)sample;
        }

        void BuildSamples ()
        {
            int b = 1 << (m_bits_per_sample - 1);
            int i = 0;
            m_samples = new int[89 * b];
            while (i < 89)
            {
                int ii = i;
                int j = 0;
                while (j < b)
                {
                    double d = 0.0;
                    int a = 1;
                    int c = b;
                    do
                    {
                        if (j % a >= a / 2)
                        {
                            d += (double)StepTable[ii] / (double)c;
                        }
                        a <<= 1;
                        c >>= 1;
                    }
                    while (a <= b);
                    ++j;
                    m_samples[i] = (int)d;
                    i += 89;
                }
                i = ii + 1;
            }
        }

        static readonly short[] StepTable = {
            0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x10, 0x11, 0x13, 0x15, 0x17, 0x19, 0x1C, 0x1F,
            0x22, 0x25, 0x29, 0x2D, 0x32, 0x37, 0x3C, 0x42, 0x49, 0x50, 0x58, 0x61, 0x6B, 0x76, 0x82, 0x8F,
            0x9D, 0x0AD, 0x0BE, 0x0D1, 0x0E6, 0x0FD, 0x117, 0x133, 0x151, 0x173, 0x198, 0x1C1, 0x1EE, 0x220,
            0x256, 0x292, 0x2D4, 0x31C, 0x36C, 0x3C3, 0x424, 0x48E, 0x502, 0x583, 0x610, 0x6AB, 0x756, 0x812,
            0x8E0, 0x9C3, 0x0ABD, 0x0BD0, 0x0CFF, 0x0E4C, 0x0FBA, 0x114C, 0x1307, 0x14EE, 0x1706, 0x1954,
            0x1BDC, 0x1EA5, 0x21B6, 0x2515, 0x28CA, 0x2CDF, 0x315B, 0x364B, 0x3BB9, 0x41B2, 0x4844, 0x4F7E,
            0x5771, 0x602F, 0x69CE, 0x7462, 0x7FFF,
        };
        static readonly sbyte[] IndexTable = { -1, -1, -1, -1, 1, 2, 3, 4 };
    }
}
