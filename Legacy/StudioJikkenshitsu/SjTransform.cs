//! \file       SjTransform.cs
//! \date       2018 Oct 01
//! \brief      Encryption method used by Studio Jikkenshitsu.
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
using System.Security.Cryptography;

namespace GameRes.Formats.Jikkenshitsu
{
    internal sealed class SjTransform : ICryptoTransform
    {
        const int BlockSize = 8;

        public bool          CanReuseTransform { get { return true; } }
        public bool CanTransformMultipleBlocks { get { return true; } }
        public int              InputBlockSize { get { return BlockSize; } }
        public int             OutputBlockSize { get { return BlockSize; } }

        byte[]  m_key000 = new byte[0x80];
        byte[]  m_key080 = new byte[0x80];

        public SjTransform (byte[] key)
        {
            var key_bits = GetKeyBits (key);
            InitKey (key_bits);
        }

        byte[] GetKeyBits (byte[] key)
        {
            var key_bits = new byte[0x40];
            int length = Math.Min (key.Length, 16);
            int dst = 0;
            for (int src = 0; src < length && key[src] != 0; ++src)
            {
                byte k = key[src];
                key_bits[dst++] = (byte)((k >> 3) & 1);
                key_bits[dst++] = (byte)((k >> 2) & 1);
                key_bits[dst++] = (byte)((k >> 1) & 1);
                key_bits[dst++] = (byte)(k & 1);
            }
            return key_bits;
        }

        void InitKey (byte[] key_bits)
        {
            var key_buf = new int[0xA8];
            key_buf[56] = key_bits[56];
            key_buf[57] = key_bits[48];
            key_buf[58] = key_bits[40];
            key_buf[59] = key_bits[32];
            key_buf[60] = key_bits[24];
            key_buf[61] = key_bits[16];
            key_buf[62] = key_bits[8];
            key_buf[63] = key_bits[0];
            key_buf[64] = key_bits[57];
            key_buf[65] = key_bits[49];
            key_buf[66] = key_bits[41];
            key_buf[67] = key_bits[33];
            key_buf[68] = key_bits[25];
            key_buf[69] = key_bits[17];
            key_buf[70] = key_bits[9];
            key_buf[71] = key_bits[1];
            key_buf[72] = key_bits[58];
            key_buf[73] = key_bits[50];
            key_buf[74] = key_bits[42];
            key_buf[75] = key_bits[34];
            key_buf[76] = key_bits[26];
            key_buf[77] = key_bits[18];
            key_buf[78] = key_bits[10];
            key_buf[79] = key_bits[2];
            key_buf[80] = key_bits[59];
            key_buf[81] = key_bits[51];
            key_buf[82] = key_bits[43];
            key_buf[83] = key_bits[35];
            key_buf[112] = key_bits[62];
            key_buf[113] = key_bits[54];
            key_buf[114] = key_bits[46];
            key_buf[115] = key_bits[38];
            key_buf[116] = key_bits[30];
            key_buf[117] = key_bits[22];
            key_buf[118] = key_bits[14];
            key_buf[119] = key_bits[6];
            key_buf[120] = key_bits[61];
            key_buf[121] = key_bits[53];
            key_buf[122] = key_bits[45];
            key_buf[123] = key_bits[37];
            key_buf[124] = key_bits[29];
            key_buf[125] = key_bits[21];
            key_buf[126] = key_bits[13];
            key_buf[127] = key_bits[5];
            key_buf[128] = key_bits[60];
            key_buf[129] = key_bits[52];
            key_buf[130] = key_bits[44];
            key_buf[131] = key_bits[36];
            key_buf[132] = key_bits[28];
            key_buf[133] = key_bits[20];
            key_buf[134] = key_bits[12];
            key_buf[135] = key_bits[4];
            key_buf[136] = key_bits[27];
            key_buf[137] = key_bits[19];
            key_buf[138] = key_bits[11];
            key_buf[139] = key_bits[3];

            Array.Copy (key_buf, 0x38, key_buf, 0x54, 0x1C);
            Array.Copy (key_buf, 0x70, key_buf, 0x8C, 0x1C);
            int src1 = 0; //off_45AA20;
            int src2 = 0; //off_45AA60;
            int k8 = 0; // g_key080;
            int k0 = 0; // g_key000;
            for (int i = 0; i < 16; ++i)
            {
                Array.Copy (key_buf, off_45AA20[src1++], key_buf, off_45AAA8[0], 0x1C);
                Array.Copy (key_buf, off_45AA60[src2++], key_buf, off_45AB08[0], 0x1C);
                m_key080[k8++] = (byte)(key_buf[off_45AAA8[0]]  + 2 * (key_buf[off_45AAA8[20]] + 2 * (key_buf[off_45AAA8[9]]  + 2 * key_buf[off_45AAA8[15]])));
                m_key080[k8++] = (byte)(key_buf[off_45AAA8[18]] + 2 * (key_buf[off_45AAA8[5]]  + 2 * (key_buf[off_45AAA8[13]] + 2 * key_buf[off_45AAA8[23]])));
                m_key080[k8++] = (byte)(key_buf[off_45AAA8[21]] + 2 * (key_buf[off_45AAA8[3]]  + 2 * (key_buf[off_45AAA8[10]] + 2 * key_buf[off_45AAA8[16]])));
                m_key080[k8++] = (byte)(key_buf[off_45AAA8[11]] + 2 * (key_buf[off_45AAA8[17]] + 2 * (key_buf[off_45AAA8[22]] + 2 * key_buf[off_45AAA8[6]])));
                m_key080[k8++] = (byte)(key_buf[off_45AB08[15]] + 2 * (key_buf[off_45AB08[7]]  + 2 * (key_buf[off_45AB08[2]]  + 2 * key_buf[off_45AB08[20]])));
                m_key080[k8++] = (byte)(key_buf[off_45AB08[4]]  + 2 * (key_buf[off_45AB08[13]] + 2 * (key_buf[off_45AB08[19]] + 2 * key_buf[off_45AB08[9]])));
                m_key080[k8++] = (byte)(key_buf[off_45AB08[5]]  + 2 * (key_buf[off_45AB08[23]] + 2 * (key_buf[off_45AB08[8]]  + 2 * key_buf[off_45AB08[17]])));
                m_key080[k8++] = (byte)(key_buf[off_45AB08[0]]  + 2 * (key_buf[off_45AB08[6]]  + 2 * (key_buf[off_45AB08[18]] + 2 * key_buf[off_45AB08[11]])));
                m_key000[k0++] = (byte)(key_buf[off_45AAA8[4]]  + 2 * key_buf[off_45AAA8[12]]);
                m_key000[k0++] = (byte)(key_buf[off_45AAA8[8]]  + 2 * key_buf[off_45AAA8[2]]);
                m_key000[k0++] = (byte)(key_buf[off_45AAA8[7]]  + 2 * key_buf[off_45AAA8[19]]);
                m_key000[k0++] = (byte)(key_buf[off_45AAA8[1]]  + 2 * key_buf[off_45AAA8[14]]);
                m_key000[k0++] = (byte)(key_buf[off_45AB08[22]] + 2 * key_buf[off_45AB08[10]]);
                m_key000[k0++] = (byte)(key_buf[off_45AB08[16]] + 2 * key_buf[off_45AB08[1]]);
                m_key000[k0++] = (byte)(key_buf[off_45AB08[21]] + 2 * key_buf[off_45AB08[12]]);
                m_key000[k0++] = (byte)(key_buf[off_45AB08[3]]  + 2 * key_buf[off_45AB08[14]]);
            }
        }

        public int TransformBlock (byte[] inputBuffer, int inputOffset, int inputCount,
                                   byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; i += 8)
            {
                DoTransform (inputBuffer, inputOffset, outputBuffer, outputOffset);
                inputOffset += 8;
                outputOffset += 8;
            }
            return inputCount;
        }

        public byte[] TransformFinalBlock (byte[] inputBuffer, int inputOffset, int inputCount)
        {
            byte[] outputBuffer = new byte[inputCount];
            int src = inputOffset;
            int dst = 0;
            if (inputCount >= 8)
            {
                int count = inputCount & ~7;
                TransformBlock (inputBuffer, inputOffset, count, outputBuffer, dst);
                dst += count;
                inputCount -= count;
            }
            if (inputCount > 0)
                Buffer.BlockCopy (inputBuffer, inputOffset, outputBuffer, dst, inputCount);
            return outputBuffer;
        }

        void DoTransform (byte[] input, int src, byte[] output, int dst)
        {
            int b07 = (input[src] >> 7) & 1;
            int b06 = (input[src] >> 6) & 1;
            int b05 = (input[src] >> 5) & 1;
            int b04 = (input[src] >> 4) & 1;
            int b03 = (input[src] >> 3) & 1;
            int b02 = (input[src] >> 2) & 1;
            int b01 = (input[src] >> 1) & 1;
            int b00 = input[src] & 1;
            int b17 = (input[src+1] >> 7) & 1;
            int b16 = (input[src+1] >> 6) & 1;
            int b15 = (input[src+1] >> 5) & 1;
            int b14 = (input[src+1] >> 4) & 1;
            int b13 = (input[src+1] >> 3) & 1;
            int b12 = (input[src+1] >> 2) & 1;
            int b11 = (input[src+1] >> 1) & 1;
            int b10 = input[src+1] & 1;
            int b27 = (input[src+2] >> 7) & 1;
            int b26 = (input[src+2] >> 6) & 1;
            int b25 = (input[src+2] >> 5) & 1;
            int b24 = (input[src+2] >> 4) & 1;
            int b23 = (input[src+2] >> 3) & 1;
            int b22 = (input[src+2] >> 2) & 1;
            int b21 = (input[src+2] >> 1) & 1;
            int b20 = input[src+2] & 1;
            int b37 = (input[src+3] >> 7) & 1;
            int b36 = (input[src+3] >> 6) & 1;
            int b35 = (input[src+3] >> 5) & 1;
            int b34 = (input[src+3] >> 4) & 1;
            int b33 = (input[src+3] >> 3) & 1;
            int b32 = (input[src+3] >> 2) & 1;
            int b31 = (input[src+3] >> 1) & 1;
            int b30 = input[src+3] & 1;
            int b47 = (input[src+4] >> 7) & 1;
            int b46 = (input[src+4] >> 6) & 1;
            int b45 = (input[src+4] >> 5) & 1;
            int b44 = (input[src+4] >> 4) & 1;
            int b43 = (input[src+4] >> 3) & 1;
            int b42 = (input[src+4] >> 2) & 1;
            int b41 = (input[src+4] >> 1) & 1;
            int b40 = input[src+4] & 1;
            int b57 = (input[src+5] >> 7) & 1;
            int b56 = (input[src+5] >> 6) & 1;
            int b55 = (input[src+5] >> 5) & 1;
            int b54 = (input[src+5] >> 4) & 1;
            int b53 = (input[src+5] >> 3) & 1;
            int b52 = (input[src+5] >> 2) & 1;
            int b51 = (input[src+5] >> 1) & 1;
            int b50 = input[src+5] & 1;
            int b67 = (input[src+6] >> 7) & 1;
            int b66 = (input[src+6] >> 6) & 1;
            int b65 = (input[src+6] >> 5) & 1;
            int b64 = (input[src+6] >> 4) & 1;
            int b63 = (input[src+6] >> 3) & 1;
            int b62 = (input[src+6] >> 2) & 1;
            int b61 = (input[src+6] >> 1) & 1;
            int b60 = input[src+6] & 1;
            int b77 = (input[src+7] >> 7) & 1;
            int b76 = (input[src+7] >> 6) & 1;
            int b75 = (input[src+7] >> 5) & 1;
            int b74 = (input[src+7] >> 4) & 1;
            int b73 = (input[src+7] >> 3) & 1;
            int b72 = (input[src+7] >> 2) & 1;
            int b71 = (input[src+7] >> 1) & 1;
            int b70 = input[src+7] & 1;
            int t0 = 0; // m_key000;
            int t1 = 0; // m_key080;
            for (int i = 0; i < 8; ++i)
            {
                byte[] bits;
                bits = BitsOrder0[(m_key000[t0++] ^ (b37 | b01 << 1)) << 4 | (m_key080[t1++] ^ (b47 | b57 << 1 | b67 << 2 | b77 << 3))];
                b74 ^= bits[0];
                b72 ^= bits[1];
                b12 ^= bits[2];
                b10 ^= bits[3];
                bits = BitsOrder1[(m_key000[t0++] ^ (b75 | b47 << 1)) << 4 | (m_key080[t1++] ^ (b07 | b17 << 1 | b27 << 2 | b37 << 3))];
                b34 ^= bits[0];
                b40 ^= bits[1];
                b66 ^= bits[2];
                b62 ^= bits[3];
                bits = BitsOrder2[(m_key000[t0++] ^ (b35 | b07 << 1)) << 4 | (m_key080[t1++] ^ (b45 | b55 << 1 | b65 << 2 | b75 << 3))];
                b02 ^= bits[0];
                b04 ^= bits[1];
                b20 ^= bits[2];
                b26 ^= bits[3];
                bits = BitsOrder3[(m_key000[t0++] ^ (b73 | b45 << 1)) << 4 | (m_key080[t1++] ^ (b05 | b15 << 1 | b25 << 2 | b35 << 3))];
                b60 ^= bits[0];
                b42 ^= bits[1];
                b64 ^= bits[2];
                b76 ^= bits[3];
                bits = BitsOrder4[(m_key000[t0++] ^ (b33 | b05 << 1)) << 4 | (m_key080[t1++] ^ (b43 | b53 << 1 | b63 << 2 | b73 << 3))];
                b06 ^= bits[0];
                b24 ^= bits[1];
                b70 ^= bits[2];
                b56 ^= bits[3];
                bits = BitsOrder5[(m_key000[t0++] ^ (b71 | b43 << 1)) << 4 | (m_key080[t1++] ^ (b03 | b13 << 1 | b23 << 2 | b33 << 3))];
                b46 ^= bits[0];
                b30 ^= bits[1];
                b54 ^= bits[2];
                b52 ^= bits[3];
                bits = BitsOrder6[(m_key000[t0++] ^ (b31 | b03 << 1)) << 4 | (m_key080[t1++] ^ (b41 | b51 << 1 | b61 << 2 | b71 << 3))];
                b00 ^= bits[0];
                b44 ^= bits[1];
                b22 ^= bits[2];
                b16 ^= bits[3];
                bits = BitsOrder7[(m_key000[t0++] ^ (b77 | b41 << 1)) << 4 | (m_key080[t1++] ^ (b01 | b11 << 1 | b21 << 2 | b31 << 3))];
                b36 ^= bits[0];
                b50 ^= bits[1];
                b14 ^= bits[2];
                b32 ^= bits[3];
                bits = BitsOrder0[(m_key000[t0++] ^ (b36 | b00 << 1)) << 4 | (m_key080[t1++] ^ (b46 | b56 << 1 | b66 << 2 | b76 << 3))];
                b75 ^= bits[0];
                b73 ^= bits[1];
                b13 ^= bits[2];
                b11 ^= bits[3];
                bits = BitsOrder1[(m_key000[t0++] ^ (b74 | b46 << 1)) << 4 | (m_key080[t1++] ^ (b06 | b16 << 1 | b26 << 2 | b36 << 3))];
                b35 ^= bits[0];
                b41 ^= bits[1];
                b67 ^= bits[2];
                b63 ^= bits[3];
                bits = BitsOrder2[(m_key000[t0++] ^ (b34 | b06 << 1)) << 4 | (m_key080[t1++] ^ (b44 | b54 << 1 | b64 << 2 | b74 << 3))];
                b03 ^= bits[0];
                b05 ^= bits[1];
                b21 ^= bits[2];
                b27 ^= bits[3];
                bits = BitsOrder3[(m_key000[t0++] ^ (b72 | b44 << 1)) << 4 | (m_key080[t1++] ^ (b04 | b14 << 1 | b24 << 2 | b34 << 3))];
                b61 ^= bits[0];
                b43 ^= bits[1];
                b65 ^= bits[2];
                b77 ^= bits[3];
                bits = BitsOrder4[(m_key000[t0++] ^ (b32 | b04 << 1)) << 4 | (m_key080[t1++] ^ (b42 | b52 << 1 | b62 << 2 | b72 << 3))];
                b07 ^= bits[0];
                b25 ^= bits[1];
                b71 ^= bits[2];
                b57 ^= bits[3];
                bits = BitsOrder5[(m_key000[t0++] ^ (b70 | b42 << 1)) << 4 | (m_key080[t1++] ^ (b02 | b12 << 1 | b22 << 2 | b32 << 3))];
                b47 ^= bits[0];
                b31 ^= bits[1];
                b55 ^= bits[2];
                b53 ^= bits[3];
                bits = BitsOrder6[(m_key000[t0++] ^ (b30 | b02 << 1)) << 4 | (m_key080[t1++] ^ (b40 | b50 << 1 | b60 << 2 | b70 << 3))];
                b01 ^= bits[0];
                b45 ^= bits[1];
                b23 ^= bits[2];
                b17 ^= bits[3];
                bits = BitsOrder7[(m_key000[t0++] ^ (b76 | b40 << 1)) << 4 | (m_key080[t1++] ^ (b00 | b10 << 1 | b20 << 2 | b30 << 3))];
                b37 ^= bits[0];
                b51 ^= bits[1];
                b15 ^= bits[2];
                b33 ^= bits[3];
            }
            output[dst  ] = (byte)(b01 + 2 * (b00 + 2 * (b03 + 2 * (b02 + 2 * (b05 + 2 * (b04 + 2 * (b07 - (b06 << 1))))))));
            output[dst+1] = (byte)(b11 + 2 * (b10 + 2 * (b13 + 2 * (b12 + 2 * (b15 + 2 * (b14 + 2 * (b17 - (b16 << 1))))))));
            output[dst+2] = (byte)(b21 + 2 * (b20 + 2 * (b23 + 2 * (b22 + 2 * (b25 + 2 * (b24 + 2 * (b27 - (b26 << 1))))))));
            output[dst+3] = (byte)(b31 + 2 * (b30 + 2 * (b33 + 2 * (b32 + 2 * (b35 + 2 * (b34 + 2 * (b37 - (b36 << 1))))))));
            output[dst+4] = (byte)(b41 + 2 * (b40 + 2 * (b43 + 2 * (b42 + 2 * (b45 + 2 * (b44 + 2 * (b47 - (b46 << 1))))))));
            output[dst+5] = (byte)(b51 + 2 * (b50 + 2 * (b53 + 2 * (b52 + 2 * (b55 + 2 * (b54 + 2 * (b57 - (b56 << 1))))))));
            output[dst+6] = (byte)(b61 + 2 * (b60 + 2 * (b63 + 2 * (b62 + 2 * (b65 + 2 * (b64 + 2 * (b67 - (b66 << 1))))))));
            output[dst+7] = (byte)(b71 + 2 * (b70 + 2 * (b73 + 2 * (b72 + 2 * (b75 + 2 * (b74 + 2 * (b77 - (b76 << 1))))))));

        }

        public void Dispose ()
        {
        }

        static readonly byte[] off_45AA20 = {
            0x54, 0x53, 0x51, 0x4F, 0x4D, 0x4B, 0x49, 0x47,
            0x46, 0x44, 0x42, 0x40, 0x3E, 0x3C, 0x3A, 0x39,
        };
        static readonly byte[] off_45AA60 = {
            0x8C, 0x8B, 0x89, 0x87, 0x85, 0x83, 0x81, 0x7F,
            0x7E, 0x7C, 0x7A, 0x78, 0x76, 0x74, 0x72, 0x71,
        };

        static readonly byte[] off_45AAA8 = { // key_buf
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
            0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
            0x12, 0x13, 0x14, 0x16, 0x17, 0x19, 0x1A, 0x1B,
        };

        static readonly byte[] off_45AB08 = { // key_buf
            0x1C, 0x1D, 0x1E, 0x1F, 0x20, 0x21, 0x23, 0x24,
            0x26, 0x27, 0x28, 0x29, 0x2B, 0x2C, 0x2D, 0x2E,
            0x2F, 0x30, 0x31, 0x32, 0x33, 0x34, 0x36, 0x37,
        };

        static readonly byte[] bits_0000 = { 0, 0, 0, 0 };
        static readonly byte[] bits_0001 = { 0, 0, 0, 1 };
        static readonly byte[] bits_0010 = { 0, 0, 1, 0 };
        static readonly byte[] bits_0011 = { 0, 0, 1, 1 };
        static readonly byte[] bits_0100 = { 0, 1, 0, 0 };
        static readonly byte[] bits_0101 = { 0, 1, 0, 1 };
        static readonly byte[] bits_0110 = { 0, 1, 1, 0 };
        static readonly byte[] bits_0111 = { 0, 1, 1, 1 };
        static readonly byte[] bits_1000 = { 1, 0, 0, 0 };
        static readonly byte[] bits_1001 = { 1, 0, 0, 1 };
        static readonly byte[] bits_1010 = { 1, 0, 1, 0 };
        static readonly byte[] bits_1011 = { 1, 0, 1, 1 };
        static readonly byte[] bits_1100 = { 1, 1, 0, 0 };
        static readonly byte[] bits_1101 = { 1, 1, 0, 1 };
        static readonly byte[] bits_1110 = { 1, 1, 1, 0 };
        static readonly byte[] bits_1111 = { 1, 1, 1, 1 };

        static readonly byte[][] BitsOrder0 = {
            bits_1110, bits_0100, bits_1101, bits_0001, bits_0010, bits_1111, bits_1011, bits_1000,
            bits_0011, bits_1010, bits_0110, bits_1100, bits_0101, bits_1001, bits_0000, bits_0111,
            bits_0000, bits_1111, bits_0111, bits_0100, bits_1110, bits_0010, bits_1101, bits_0001,
            bits_1010, bits_0110, bits_1100, bits_1011, bits_1001, bits_0101, bits_0011, bits_1000,
            bits_0100, bits_0001, bits_1110, bits_1000, bits_1101, bits_0110, bits_0010, bits_1011,
            bits_1111, bits_1100, bits_1001, bits_0111, bits_0011, bits_1010, bits_0101, bits_0000,
            bits_1111, bits_1100, bits_1000, bits_0010, bits_0100, bits_1001, bits_0001, bits_0111,
            bits_0101, bits_1011, bits_0011, bits_1110, bits_1010, bits_0000, bits_0110, bits_1101,
        };
        static readonly byte[][] BitsOrder1 = {
            bits_1111, bits_0001, bits_1000, bits_1110, bits_0110, bits_1011, bits_0011, bits_0100,
            bits_1001, bits_0111, bits_0010, bits_1101, bits_1100, bits_0000, bits_0101, bits_1010,
            bits_0011, bits_1101, bits_0100, bits_0111, bits_1111, bits_0010, bits_1000, bits_1110,
            bits_1100, bits_0000, bits_0001, bits_1010, bits_0110, bits_1001, bits_1011, bits_0101,
            bits_0000, bits_1110, bits_0111, bits_1011, bits_1010, bits_0100, bits_1101, bits_0001,
            bits_0101, bits_1000, bits_1100, bits_0110, bits_1001, bits_0011, bits_0010, bits_1111,
            bits_1101, bits_1000, bits_1010, bits_0001, bits_0011, bits_1111, bits_0100, bits_0010,
            bits_1011, bits_0110, bits_0111, bits_1100, bits_0000, bits_0101, bits_1110, bits_1001,
        };
        static readonly byte[][] BitsOrder2 = {
            bits_1010, bits_0000, bits_1001, bits_1110, bits_0110, bits_0011, bits_1111, bits_0101,
            bits_0001, bits_1101, bits_1100, bits_0111, bits_1011, bits_0100, bits_0010, bits_1000,
            bits_1101, bits_0111, bits_0000, bits_1001, bits_0011, bits_0100, bits_0110, bits_1010,
            bits_0010, bits_1000, bits_0101, bits_1110, bits_1100, bits_1011, bits_1111, bits_0001,
            bits_1101, bits_0110, bits_0100, bits_1001, bits_1000, bits_1111, bits_0011, bits_0000,
            bits_1011, bits_0001, bits_0010, bits_1100, bits_0101, bits_1010, bits_1110, bits_0111,
            bits_0001, bits_1010, bits_1101, bits_0000, bits_0110, bits_1001, bits_1000, bits_0111,
            bits_0100, bits_1111, bits_1110, bits_0011, bits_1011, bits_0101, bits_0010, bits_1100,
        };
        static readonly byte[][] BitsOrder3 = {
            bits_0111, bits_1101, bits_1110, bits_0011, bits_0000, bits_0110, bits_1001, bits_1010,
            bits_0001, bits_0010, bits_1000, bits_0101, bits_1011, bits_1100, bits_0100, bits_1111,
            bits_1101, bits_1000, bits_1011, bits_0101, bits_0110, bits_1111, bits_0000, bits_0011,
            bits_0100, bits_0111, bits_0010, bits_1100, bits_0001, bits_1010, bits_1110, bits_1001,
            bits_1010, bits_0110, bits_1001, bits_0000, bits_1100, bits_1011, bits_0111, bits_1101,
            bits_1111, bits_0001, bits_0011, bits_1110, bits_0101, bits_0010, bits_1000, bits_0100,
            bits_0011, bits_1111, bits_0000, bits_0110, bits_1010, bits_0001, bits_1101, bits_1000,
            bits_1001, bits_0100, bits_0101, bits_1011, bits_1100, bits_0111, bits_0010, bits_1110,
        };
        static readonly byte[][] BitsOrder4 = {
            bits_0010, bits_1100, bits_0100, bits_0001, bits_0111, bits_1010, bits_1011, bits_0110,
            bits_1000, bits_0101, bits_0011, bits_1111, bits_1101, bits_0000, bits_1110, bits_1001,
            bits_1110, bits_1011, bits_0010, bits_1100, bits_0100, bits_0111, bits_1101, bits_0001,
            bits_0101, bits_0000, bits_1111, bits_1010, bits_0011, bits_1001, bits_1000, bits_0110,
            bits_0100, bits_0010, bits_0001, bits_1011, bits_1010, bits_1101, bits_0111, bits_1000,
            bits_1111, bits_1001, bits_1100, bits_0101, bits_0110, bits_0011, bits_0000, bits_1110,
            bits_1011, bits_1000, bits_1100, bits_0111, bits_0001, bits_1110, bits_0010, bits_1101,
            bits_0110, bits_1111, bits_0000, bits_1001, bits_1010, bits_0100, bits_0101, bits_0011,
        };
        static readonly byte[][] BitsOrder5 = {
            bits_1100, bits_0001, bits_1010, bits_1111, bits_1001, bits_0010, bits_0110, bits_1000,
            bits_0000, bits_1101, bits_0011, bits_0100, bits_1110, bits_0111, bits_0101, bits_1011,
            bits_1010, bits_1111, bits_0100, bits_0010, bits_0111, bits_1100, bits_1001, bits_0101,
            bits_0110, bits_0001, bits_1101, bits_1110, bits_0000, bits_1011, bits_0011, bits_1000,
            bits_1001, bits_1110, bits_1111, bits_0101, bits_0010, bits_1000, bits_1100, bits_0011,
            bits_0111, bits_0000, bits_0100, bits_1010, bits_0001, bits_1101, bits_1011, bits_0110,
            bits_0100, bits_0011, bits_0010, bits_1100, bits_1001, bits_0101, bits_1111, bits_1010,
            bits_1011, bits_1110, bits_0001, bits_0111, bits_0110, bits_0000, bits_1000, bits_1101,
        };
        static readonly byte[][] BitsOrder6 = {
            bits_0100, bits_1011, bits_0010, bits_1110, bits_1111, bits_0000, bits_1000, bits_1101,
            bits_0011, bits_1100, bits_1001, bits_0111, bits_0101, bits_1010, bits_0110, bits_0001,
            bits_1101, bits_0000, bits_1011, bits_0111, bits_0100, bits_1001, bits_0001, bits_1010,
            bits_1110, bits_0011, bits_0101, bits_1100, bits_0010, bits_1111, bits_1000, bits_0110,
            bits_0001, bits_0100, bits_1011, bits_1101, bits_1100, bits_0011, bits_0111, bits_1110,
            bits_1010, bits_1111, bits_0110, bits_1000, bits_0000, bits_0101, bits_1001, bits_0010,
            bits_0110, bits_1011, bits_1101, bits_1000, bits_0001, bits_0100, bits_1010, bits_0111,
            bits_1001, bits_0101, bits_0000, bits_1111, bits_1110, bits_0010, bits_0011, bits_1100,
        };
        static readonly byte[][] BitsOrder7 = {
            bits_1101, bits_0010, bits_1000, bits_0100, bits_0110, bits_1111, bits_1011, bits_0001,
            bits_1010, bits_1001, bits_0011, bits_1110, bits_0101, bits_0000, bits_1100, bits_0111,
            bits_0001, bits_1111, bits_1101, bits_1000, bits_1010, bits_0011, bits_0111, bits_0100,
            bits_1100, bits_0101, bits_0110, bits_1011, bits_0000, bits_1110, bits_1001, bits_0010,
            bits_0111, bits_1011, bits_0100, bits_0001, bits_1001, bits_1100, bits_1110, bits_0010,
            bits_0000, bits_0110, bits_1010, bits_1101, bits_1111, bits_0011, bits_0101, bits_1000,
            bits_0010, bits_0001, bits_1110, bits_0111, bits_0100, bits_1010, bits_1000, bits_1101,
            bits_1111, bits_1100, bits_1001, bits_0000, bits_0011, bits_0101, bits_0110, bits_1011,
        };
    }
}
