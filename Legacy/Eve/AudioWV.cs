//! \file       AudioWV.cs
//! \date       2018 Apr 06
//! \brief      Eve compressed audio.
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

namespace GameRes.Formats.Eve
{
    [Export(typeof(AudioFormat))]
    public sealed class Wv3Audio : AudioFormat
    {
        public override string         Tag { get { return "WV3"; } }
        public override string Description { get { return "Eve compressed audio format"; } }
        public override uint     Signature { get { return 0x2E335657; } } // 'WV3.0'
        public override bool      CanWrite { get { return false; } }

        public Wv3Audio ()
        {
            Extensions = new string[] { "wv3", "wav" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x26);
            if (header[4] != '0')
                return null;
            var decoder = new Wv3Decoder (file);
            var data = decoder.Decode();
            var pcm = new MemoryStream (data);
            return new RawPcmInput (pcm, decoder.Format);
        }
    }

    internal class Wv3Decoder
    {
        IBinaryStream   m_input;
        uint            m_data_offset;
        int             m_sample_count;
        byte[]          m_output;
        WaveFormat      m_format;

        public WaveFormat Format { get { return m_format; } }

        public Wv3Decoder (IBinaryStream file)
        {
            m_input = file;
            var header = m_input.ReadHeader (0x26);
            m_data_offset = header.ToUInt32 (6);
            m_sample_count = header.ToInt32 (0x1A);
            m_output = new byte[m_sample_count * 280];

            m_format = new WaveFormat {
                FormatTag = 1,
                Channels = 2,
                SamplesPerSecond = header.ToUInt32 (0xE),
                BitsPerSample = 16,
                BlockAlign = 4
            };
            m_format.SetBPS();
        }

        public byte[] Decode ()
        {
            m_input.Position = m_data_offset;
            var input  = new byte[72];
            var output = new short[140];
            var back = new int[2,2];
            int out_pos = 0;
            for (int i = 0; i < m_sample_count; ++i)
            {
                if (72 != m_input.Read (input, 0, 72))
                    break;
                for (int k = 0; k < 2; ++k)
                {
                    byte v = input[k];
                    int shift = v & 0xF;
                    v >>= 4;
                    short scale0 = ScaleMap[v+8];
                    short scale1 = ScaleMap[v];
                    int src = 35 * k + 2;
                    for (int j = 0; j < 35; ++j)
                    {
                        int dst = k + (j << 2);
                        v = input[src++];
                        int x = v & 0xF;
                        if ((x & 8) != 0)
                            x |= -0x10;
                        int sample = ((scale0 * back[0,k] + scale1 * back[1,k]) >> 8) + (x << shift);
                        back[0,k] = back[1,k];
                        back[1,k] = sample;
                        output[dst] = (short)sample;

                        x = v >> 4;
                        if ((x & 8) != 0)
                            x |= -0x10;
                        sample = ((scale0 * back[0,k] + scale1 * back[1,k]) >> 8) + (x << shift);
                        back[0,k] = back[1,k];
                        back[1,k] = sample;
                        output[dst + 2] = (short)sample;
                    }
                }
                Buffer.BlockCopy (output, 0, m_output, out_pos, 280);
                out_pos += 280;
            }
            return m_output;
        }

        static readonly short[] ScaleMap = {
            0, 240, 128, 192, 320, 460, 392, 488,
            0, 0, -12, -56, -88, -208, -220, -240,
        };
    }
}
