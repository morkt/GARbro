//! \file       AudioVWF.cs
//! \date       2018 May 09
//! \brief      AZSYSTEM/1.0 compressed audio format.
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
using GameRes.Formats.Abogado;
using GameRes.Compression;

namespace GameRes.Formats.Cadath
{
    [Export(typeof(AudioFormat))]
    public class VwfAudio : AudioFormat
    {
        public override string         Tag { get { return "VWF"; } }
        public override string Description { get { return "AZSYSTEM/1.0 audio format"; } }
        public override uint     Signature { get { return 0x1A465756; } } // 'VWF'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x23);
            int bits1_length = header.ToInt32 (0x13);
            int bits2_length = header.ToInt32 (0x17);
            var data = file.ReadBytes (bits1_length + bits2_length);
            CgfDecoder.Decrypt (data, bits1_length);

            int unpacked_length = header.ToInt32 (5);
            uint sample_rate = header.ToUInt32 (0xD);
            int sample_count = unpacked_length >> 1;
            var bits1 = new byte[(sample_count + 7) >> 3];
            var bits2 = new byte[sample_count >> 1];

            using (var mem = new MemoryStream (data, 4, bits1_length-4))
            using (var input = new ZLibStream (mem, CompressionMode.Decompress))
                input.Read (bits1, 0, bits1.Length);

            using (var mem = new MemoryStream (data, bits1_length+4, bits2_length-4))
            using (var input = new ZLibStream (mem, CompressionMode.Decompress))
                input.Read (bits2, 0, bits2.Length);

            short init = header.ToInt16 (0x11);
            var decoded = new short[sample_count];
            DecodeAdp (bits2, decoded, sample_count, init);

            var output = new byte[unpacked_length];
            byte bit = 0x80;
            int src = 0;
            int dst = 0;
            for (int i = 0; i < decoded.Length; ++i)
            {
                short sample = Math.Max (decoded[i], (short)0);
                if ((bit & bits1[src]) != 0)
                    sample = (short)-sample;
                LittleEndian.Pack (sample, output, dst);
                dst += 2;
                bit >>= 1;
                if (0 == bit)
                {
                    ++src;
                    bit = 0x80;
                }
            }
            var format = new WaveFormat {
                FormatTag = 1,
                Channels = 1,
                SamplesPerSecond = sample_rate,
                BlockAlign = 2,
                BitsPerSample = 16,
            };
            format.SetBPS();
            var pcm = new MemoryStream (output);
            return new RawPcmInput (pcm, format);
        }

        void DecodeAdp (byte[] input, short[] output, int count, short init)
        {
            int src = 0;
            int sample = init;
            int quant_idx = 0;
            byte s = 0;
            bool odd = false;
            ushort quant = AdpDecoder.QuantizeTable[0];
            for (int i = 0; i < count; )
            {
                int v;
                if (odd)
                {
                    v = s & 0xF;
                }
                else
                {
                    if (src >= input.Length)
                        break;
                    s = input[src++];
                    v = s >> 4;
                }
                quant_idx += AdpIncrementTable[v];
                if (quant_idx < 0)
                    quant_idx = 0;
                else if (quant_idx > 0x58)
                    quant_idx = 0x58;
                int step = quant >> 3;
                if ((v & 4) != 0)
                    step += quant;
                if ((v & 2) != 0)
                    step += quant >> 1;
                if ((v & 1) != 0)
                    step += quant >> 2;
                if (v < 8)
                    sample = Math.Min (0x7FFF, sample + step);
                else
                    sample = Math.Max (-32768, sample - step);
                quant = AdpDecoder.QuantizeTable[quant_idx];
                output[i++] = (short)sample;
                odd = !odd;
            }
        }

        static readonly sbyte[] AdpIncrementTable = { -1, -1, -1, 0, 2, 4, 6, 8, -1, -1, -1, 0, 2, 4, 6, 8 };
    }
}
