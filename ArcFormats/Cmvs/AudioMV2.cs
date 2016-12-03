//! \file       AudioMV2.cs
//! \date       Sat Dec 03 05:42:52 2016
//! \brief      CVNS engine audio format.
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

namespace GameRes.Formats.Purple
{
    [Export(typeof(AudioFormat))]
    public class Mv2Audio : AudioFormat
    {
        public override string         Tag { get { return "MV2"; } }
        public override string Description { get { return "CVNS engine compressed audio format"; } }
        public override uint     Signature { get { return 0x5832564D; } } // 'MV2X'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            using (var decoder = new Mv2Decoder (file))
            {
                decoder.Unpack();
                var pcm = new MemoryStream (decoder.Data);
                var sound = new RawPcmInput (pcm, decoder.Format);
                file.Dispose();
                return sound;
            }
        }
    }

    internal sealed class Mv2Decoder : MvDecoderBase
    {
        int m_shift;
        int m_samples;

        public Mv2Decoder (IBinaryStream input) : base (input)
        {
            var header = input.ReadHeader (0x12);
            m_channel_size = header.ToInt32 (4);
            m_format.FormatTag          = 1;
            m_format.BitsPerSample      = 16;
            m_format.Channels           = header[0xC];
            m_format.SamplesPerSecond   = header.ToUInt16 (0xA);
            m_format.BlockAlign         = (ushort)(m_format.Channels*m_format.BitsPerSample/8);
            m_format.AverageBytesPerSecond = m_format.BlockAlign * m_format.SamplesPerSecond;
            m_output = new byte[m_format.BlockAlign * m_channel_size];
            m_shift = header[0xD];
            m_samples = header.ToInt32 (0xE);
        }

        int pre2_idx;
        int[] pre_sample1;
        int[] pre_sample2;
        int[] pre_sample3;

        public void Unpack ()
        {
            SetPosition (0x12);
            pre_sample1 = new int[0x400];
            pre_sample2 = new int[0x400 * m_format.Channels];
            pre_sample3 = new int[0x140 * m_format.Channels];
            pre2_idx = 0;
            int dst_pos = 0;
            for (int i = 0; i < m_samples && dst_pos < m_output.Length; ++i)
            {
                for (int c = 0; c < m_format.Channels; ++c)
                {
                    int n1 = GetBits (10);
                    if (-1 == n1)
                        return;
                    int n2 = GetBits (9);
                    if (-1 == n2)
                        return;
                    FillSample1 (n1, n2);
                    int t = 0; // within pre_sample1
                    for (int j = 0; j < 10; ++j)
                    {
                        FilterSamples (t, t, c);
                        t += 0x20;
                    }
                    int shift = 6 - m_shift;
                    int dst = dst_pos + 2 * c;
                    for (int j = 0; j < 0x140 && dst < m_output.Length; ++j)
                    {
                        short sample = Clamp (pre_sample3[j] >> shift);
                        LittleEndian.Pack (sample, m_output, dst);
                        dst += m_format.BlockAlign;
                    }
                }
                dst_pos += 2 * m_format.Channels * 0x140;
            }
        }

        void FillSample1 (int n1, int n2)
        {
            for (int i = 0; i < 0x140; ++i)
                pre_sample1[i] = 0;
            n2 = SampleTable[n2];
            for (int i = 0; i < n1; ++i)
            {
                int count = GetCount();
                if (count > 0)
                {
                    int coef = GetBits (count);
                    if (coef < 1 << (count-1))
                        coef += 1 - (1 << count);
                    pre_sample1[i] = n2 * coef;
                }
                else
                {
                    i += GetBits (3);
                }
            }
        }

        void FilterSamples (int dst, int src, int channel)
        {
            int idx2 = pre2_idx + (channel << 10);
            int coef1_idx = 0;
            for (int j = 0; j < 64; ++j)
            {
                int pre1_idx = src;
                int sample = 0;
                for (int k = 0; k < 8; ++k)
                {
                    for (int m = 0; m < 4; ++m)
                    {
                        sample += MvDecoder.Coef1Table[coef1_idx++] * pre_sample1[pre1_idx++] >> 10;
                    }
                }
                pre_sample2[idx2 + j] = sample;
            }
            for (int j = 0; j < 0x20; ++j)
            {
                int m = idx2 + j;
                int coef2_idx = j;
                int x = 0;
                for (int k = 0; k < 4; ++k)
                {
                    x += pre_sample2[m & 0x3FF] * Coef2Table[coef2_idx] >> 10;
                    coef2_idx += 32;
                    m += 96;
                    x += pre_sample2[m & 0x3FF] * Coef2Table[coef2_idx] >> 10;
                    coef2_idx += 32;
                    m += 32;
                    x -= pre_sample2[m & 0x3FF] * Coef2Table[coef2_idx] >> 10;
                    coef2_idx += 32;
                    m += 96;
                    x -= pre_sample2[m & 0x3FF] * Coef2Table[coef2_idx] >> 10;
                    coef2_idx += 32;
                    m += 32;
                }
                pre_sample3[dst++] = x;
            }
            pre2_idx = ((ushort)pre2_idx - 0x40) & 0x3FF;
        }

        static readonly int[] SampleTable = {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 4, 4, 5, 6, 6, 7, 8, 10, 11, 12, 14, 16,
            18, 20, 23, 26, 29, 33, 38, 42,
            48, 54, 61, 69, 78, 88, 100, 113,
            128, 144, 163, 184, 207, 234, 265, 299,
            337, 381, 430, 486, 548, 619, 699, 789,
            891, 1006, 1136, 1282, 1448, 1634, 1845, 2083,
            2352, 2655, 2998, 3385, 3821, 4314, 4870, 5499,
            6208, 7009, 7912, 8933, 10085, 11386, 12854, 14512,
            16384, 18496, 20882, 23575, 26615, 30048, 33923, 38298,
            43237, 48813, 55108, 62216, 70239, 79298, 89524, 101070,
            114104, 128820, 145433, 164189, 185363, 209269, 236257, 266726,
            301124, 339958, 383801, 433297, 489178, 552264, 623487, 703894,
            794672, 897156, 1012857, 1143480, 1290948, 1457434, 1645392, 1857589,
            -47447340, 47512876, -47447340, 47512876, -47447340, 47512876, -47447340, 47512876,
            -47447340, 47512876, -47447340, 47512876, -47447340, 47512876, -47447340, 47512876,
            -53869905, 60685810, -65076904, 67043178, -66256946, 63176952, -57475509, 49676897,
            -39846646, 28640110, -16188356, 3277812, 9894914, -22543391, 34536547, -45022410,
            -59178359, 66846423, -64094308, 51839458, -31523607, 6554579, 19528709, -42531961,
            59243895, -66780887, 64159844, -51773922, 31589143, -6489043, -19463173, 42597497,
            -63176095, 65142734, -44958222, 9831325, 28703756, -57539850, 67043080, -53805400,
            22545206, 16317442, -49675410, 66387531, -60555414, 34472623, 3341343, -39910460,
            -65797576, 55771335, -12976979, -37223444, 65863112, -55705799, 13042515, 37288980,
            -65797576, 55771335, -12976979, -37223444, 65863112, -55705799, 13042515, 37288980,
            -66977266, 39911861, 22608908, -65076561, 49676536, 9894972, -60619978, 57540658,
            -3212142, -53869667, 63242090, -16188150, -45022239, 66387624, -28574305, -34470914,
            -66780702, 19464841, 51903533, -59178908, -6552697, 64224489, -42467625, -31587333,
            66846238, -19399305, -51837997, 59244444, 6618233, -64158953, 42533161, 31652869,
            -65076811, -3275978, 66387210, -22479374, -57539644, 45023382, 39975938, -60620552,
            -16252003, 67042719, -9765551, -63175826, 34472280, 49740812, -53870542, -28638239,
            -61996665, -25623630, 62062201, 25689166, -61996665, -25623630, 62062201, 25689166,
            -61996665, -25623630, 62062201, 25689166, -61996665, -25623630, 62062201, 25689166,
            -57540264, -45022220, 39911474, 60685343, -16187829, -66976970, -9829642, 63241714,
            34536508, -49676138, -53869570, 28639480, 65141859, -3211873, -66321745, -22543506,
            -51838679, -59177989, 6554082, 64224631, 42597421, -31522916, -66780281, -19463401,
            51904215, 59243525, -6488546, -64159095, -42531885, 31588452, 66845817, 19528937,
            -45022984, -66321468, -28638410, 34537422, 67042450, 39976035, -22478998, -65076490,
            -49675295, 9830744, 60685727, 57605122, 3341810, -53870155, -63175692, -16252241,
            -37224249, -65797293, -55770132, -13041096, 37289785, 65862829, 55835668, 13106632,
            -37224249, -65797293, -55770132, -13041096, 37289785, 65862829, 55835668, 13106632,
            -28639082, -57539921, -66976799, -53869628, -22543775, 16252978, 49741298, 66387043,
            60685324, 34536714, -3211512, -39911080, -63175882, -65076226, -45022354, -9829963,
            -19464092, -42532382, -59178217, -66780205, -64158725, -51838073, -31587703, -6553303,
            19529628, 42597918, 59243753, 66845741, 64224261, 51903609, 31653239, 6618839,
            -9830350, -22544136, -34471499, -45022623, -53869834, -60619922, -65076284, -66976780,
            -66321410, -63175711, -57539683, -49675466, -39910737, -28638706, -16252584, -3276650,
        };

        static readonly short[] Coef2Table = {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, -1, -1, -1, -1, -1,
            -1, -1, -1, -1, -2, -2, -2, -2, -2, -2, -2, -2, -2, -3, -3, -3,
            -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -2, -2, -2,
            -2, -1, -1, -1, 0, 0, 0, 0, 1, 1, 2, 3, 3, 4, 5, 6,
            7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 20, 21, 22, 23,
            24, 25, 26, 27, 28, 29, 29, 30, 31, 31, 32, 32, 32, 32, 32, 32,
            31, 31, 30, 29, 28, 27, 25, 23, 22, 20, 17, 15, 12, 9, 6, 2,
            0, -4, -8, -12, -17, -21, -26, -31, -36, -41, -46, -52, -57, -63, -69, -74,
            -80, -86, -91, -97, -102, -108, -113, -118, -123, -128, -132, -136, -140, -144, -147, -149,
            -151, -153, -154, -155, -155, -155, -154, -152, -149, -146, -142, -138, -132, -126, -119, -111,
            -102, -93, -82, -71, -58, -45, -31, -16, -1, 15, 33, 51, 70, 90, 111, 133,
            155, 178, 202, 227, 252, 278, 304, 331, 358, 385, 413, 442, 470, 499, 527, 556,
            585, 614, 643, 671, 700, 728, 756, 783, 810, 836, 862, 887, 911, 934, 957, 979,
            1000, 1020, 1038, 1056, 1073, 1088, 1102, 1115, 1127, 1138, 1147, 1154, 1161, 1166, 1169, 1171,
            1172, 1171, 1169, 1166, 1161, 1154, 1147, 1138, 1127, 1115, 1102, 1088, 1073, 1056, 1038, 1020,
            1000, 979, 957, 934, 911, 887, 862, 836, 810, 783, 756, 728, 700, 671, 643, 614,
            585, 556, 527, 499, 470, 442, 413, 385, 358, 331, 304, 278, 252, 227, 202, 178,
            155, 133, 111, 90, 70, 51, 33, 15, -1, -16, -31, -45, -58, -71, -82, -93,
            -102, -111, -119, -126, -132, -138, -142, -146, -149, -152, -154, -155, -155, -155, -154, -153,
            -151, -149, -147, -144, -140, -136, -132, -128, -123, -118, -113, -108, -102, -97, -91, -86,
            -80, -74, -69, -63, -57, -52, -46, -41, -36, -31, -26, -21, -17, -12, -8, -4,
            0, 2, 6, 9, 12, 15, 17, 20, 22, 23, 25, 27, 28, 29, 30, 31,
            31, 32, 32, 32, 32, 32, 32, 31, 31, 30, 29, 29, 28, 27, 26, 25,
            24, 23, 22, 21, 20, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8,
            7, 6, 5, 4, 3, 3, 2, 1, 1, 0, 0, 0, 0, -1, -1, -1,
            -2, -2, -2, -2, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3,
            -3, -3, -3, -3, -2, -2, -2, -2, -2, -2, -2, -2, -2, -1, -1, -1,
            -1, -1, -1, -1, -1, -1, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };
    }
}
