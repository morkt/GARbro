//! \file       AudioPAD.cs
//! \date       Sun Jun 21 23:44:18 2015
//! \brief      ShiinaRio compressed audio format.
//
// Copyright (C) 2015 by morkt
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

namespace GameRes.Formats.ShiinaRio
{
    [Export(typeof(AudioFormat))]
    public class PadAudio : AudioFormat
    {
        public override string         Tag { get { return "PAD"; } }
        public override string Description { get { return "ShiinaRio compressed audio format"; } }
        public override uint     Signature { get { return 0x444150; } } // 'PAD'
        
        public override SoundInput TryOpen (IBinaryStream file)
        {
            var wav_header = file.ReadHeader (0x2c).ToArray();
            int pcm_size = LittleEndian.ToInt32 (wav_header, 0x28);
            int channels = LittleEndian.ToUInt16 (wav_header, 0x16);
            wav_header[0] = (byte)'R';
            wav_header[1] = (byte)'I';
            wav_header[2] = (byte)'F';
            wav_header[3] = (byte)'F';
            LittleEndian.Pack (pcm_size+0x24, wav_header, 4);

            var decoder = new PadDecoder (file.AsStream, pcm_size, channels);
            decoder.Unpack();
            var data = new MemoryStream (decoder.Data, 0, pcm_size);
            var wav = new PrefixStream (wav_header, data);
            try
            {
                return new WaveInput (wav);
            }
            catch
            {
                wav.Dispose();
                throw;
            }
        }
    }

    internal class PadDecoder
    {
        byte[]      m_input;
        byte[]      m_output;
        int         m_pcm_size;
        int         m_packed_size;
        int         m_channels;

        public byte[] Data { get { return m_output; } }

        public PadDecoder (Stream input, int pcm_size, int channels)
        {
            m_packed_size = (int)(input.Length - input.Position);
            m_pcm_size = pcm_size;
            m_channels = channels;
            m_output = new byte[pcm_size + 0x9c];
            m_input = new byte[m_packed_size];
            if (m_packed_size != input.Read (m_input, 0, m_packed_size))
                throw new InvalidFormatException ("Unexpected end of file");
        }

        public byte[] Unpack ()
        {
            int v10;
            double v3 = 0;
            double v27 = 0;
            double v28 = 0;
            int v30 = 0;

            var table = new double[69];
            table[4] = 0.9375;  // 0x3FEE000000000000
            table[6] = 1.796875; // 0x3FFCC00000000000
            table[7] = -0.8125; // 0xBFEA000000000000
            table[8] = 1.53125; // 0x3FF8800000000000
            table[9] = -0.859375; // 0xBFEB800000000000
            table[10] = 1.90625; // 0x3FFE800000000000
            table[11] = -0.9375; // 0xBFEE000000000000

            int dst = 0;
            int src = 0;
            int v5 = m_input[src++];
            while (v5 != 0xff)
            {
                int v7 = m_input[src++];
                int v29 = v7 >> 4;
                int v9 = v7 & 0xF;
                if (2 == m_channels)
                {
                    int next = m_input[src+1];
                    v10 = next & 0xF;
                    v30 = next >> 4;
                    long v = BitConverter.DoubleToInt64Bits (table[12]) & 0xffffffffL;
                    table[12] = BitConverter.Int64BitsToDouble (v | (long)v10 << 32);
                    src += 2;
                }
                else
                {
                    v10 = (int)(BitConverter.DoubleToInt64Bits (table[12]) >> 32);
                }
                int v12 = 14; // within table
                for (int i = 0; i < 14; ++i)
                {
                    int v13 = m_input[src++];
                    int v14 = (v13 & 0xF) << 12;
                    if (0 != (v14 & 0x8000))
                        v14 |= ~0xFFFF;
                    int v15 = (v13 & 0xF0) << 8;
                    table[v12 - 1] = (double)(v14 >> v9);
                    if (0 != (v15 & 0x8000))
                        v15 |= ~0xFFFF;
                    table[v12] = (double)(v15 >> v9);
                    v12 += 2;
                }
                if (2 == m_channels)
                {
                    v12 = 42; // within table
                    for (int i = 0; i < 14; ++i)
                    {
                        int v18 = m_input[src++];
                        int v19 = (v18 & 0xF) << 12;
                        if (0 != (v19 & 0x8000))
                            v19 |= ~0xFFFF;
                        int v20 = (byte)(v18 & 0xF0) << 8;
                        table[v12 - 1] = (double)(v19 >> v10);
                        if (0 != (v20 & 0x8000))
                            v20 |= ~0xFFFF;
                        table[v12] = (double)(v20 >> v10);
                        v12 += 2;
                    }
                }
                v12 = 41; // within table
                for (int i = 0; i < 28; ++i)
                {
                    double v22 = v27 * table[2 * v29 + 3];
                    v27 = table[0];
                    table[v12 - 28] += v22 + v27 * table[2 * v29 + 2];
                    table[0] = table[v12 - 28];
                    LittleEndian.Pack ((short)(table[v12 - 28] + 0.5), m_output, dst);
                    dst += 2;
                    if (2 == m_channels)
                    {
                        table[v12] += v28 * table[2 * v30 + 3] + v3 * table[2 * v30 + 2];
                        v28 = v3;
                        v3 = table[v12];
                        LittleEndian.Pack ((short)(table[v12] + 0.5), m_output, dst);
                        dst += 2;
                    }
                    ++v12;
                }
                v5 = m_input[src++];
            }
            return m_output;
        }
    }
}
