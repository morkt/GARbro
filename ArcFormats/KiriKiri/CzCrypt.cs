//! \file       CzCrypt.cs
//! \date       2017 Sep 27
//! \brief      implementation of cZLIB extraction filter for KiriKiri engine.
//
// Copyright (C) 2017 by morkt
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
using System.IO;
using System.Security.Cryptography;
using GameRes.Compression;

namespace GameRes.Formats.KiriKiri
{
    /// <summary>
    /// cZLIB entry filter.
    /// </summary>
    [Serializable]
    public abstract class CzCrypt : ICrypt
    {
        const uint CzMagic  = 0xA590D7FDu;
        const uint CzIvSeed = 0xBFBFBFBFu;

        static readonly byte[] CzHeaderKey = { 0x9D, 0x1D, 0x9A, 0xF2 };
        static readonly byte[] CzDefaultKey = {
            0x91, 0x10, 0xFC, 0x75, 0x45, 0x8F, 0xB5, 0xE6, 0xFE, 0xAC, 0xBA, 0x44, 0x76, 0x58, 0xC2, 0x1A
        };

        public override Stream EntryReadFilter (Xp3Entry entry, Stream input)
        {
            if (entry.UnpackedSize <= 15 || "audio" == entry.Type)
                return input;

            var header = new byte[15];
            input.Read (header, 0, 15);
            if (CzMagic == header.ToUInt32 (0))
            {
                var type = new char[3] {
                    (char)(header[4] ^ 0x11),
                    (char)(header[5] ^ 0x7F),
                    (char)(header[6] ^ 0x9A)
                };
                byte key = (byte)type[0];
                int unpacked_size = CzDecryptInt (header, 7, key);
                int packed_size = CzDecryptInt (header, 11, key);
                if (packed_size < entry.UnpackedSize && 0 == ((packed_size-5) & 0xF))
                {
                    var data = new byte[packed_size];
                    input.Read (data, 0, packed_size);
                    input.Dispose();
                    data = CzDecryptData (data);
                    input = new BinMemoryStream (data);
                    if ('C' == type[0])
                        input = new ZLibStream (input, CompressionMode.Decompress);
                    return input;
                }
            }
            if (!input.CanSeek)
                return new PrefixStream (header, input);
            input.Position = 0;
            return input;
        }

        static int CzDecryptInt (byte[] data, int offset, byte key)
        {
            int v = data[offset] ^ key ^ CzHeaderKey[0];
            v |= (data[offset+1] ^ key ^ CzHeaderKey[1]) << 8;
            v |= (data[offset+2] ^ key ^ CzHeaderKey[2]) << 16;
            v |= (data[offset+3] ^ key ^ CzHeaderKey[3]) << 24;
            return v;
        }

        static byte[] CzDecryptData (byte[] data)
        {
            int padded_size = data.Length - 5;
            int original_size = padded_size - (data[padded_size+1] ^ data[padded_size]);
            uint iv_seed = data.ToUInt32 (padded_size+1) ^ CzIvSeed;
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.Zeros;
                aes.Key = CzDefaultKey;
                aes.IV = CzCreateIV (iv_seed);
                using (var enc = new MemoryStream (data, 0, padded_size))
                using (var dec = new InputCryptoStream (enc, aes.CreateDecryptor()))
                {
                    var original = new byte[original_size];
                    dec.Read (original, 0, original_size);
                    return original;
                }
            }
        }

        static byte[] CzCreateIV (uint seed)
        {
            var state = new uint[4];
            state[0] = 123456789; // field_0
            state[1] = 972436830; // field_4
            state[2] = 524018621; // field_8
            state[3] = seed;      // field_C
            var iv = new byte[16];
            for (int i = 0; i < 16; ++i)
            {
                uint a = state[3];
                uint b = state[0] ^ (state[0] << 11);
                state[0] = state[1];
                state[1] = state[2];
                state[2] = a;
                state[3] = b ^ a ^ ((b ^ (a >> 11)) >> 8);
                iv[i] = (byte)state[3];
            }
            return iv;
        }
    }
}
