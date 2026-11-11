//! \file       DxKey.cs
//! \date       2019 Feb 01
//! \brief      DxLib archive encryption classes.
//
// Copyright (C) 2019 by morkt
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
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using GameRes.Utility;

namespace GameRes.Formats.DxLib
{
    public interface IDxKey
    {
        byte[]  Password { get; }
        byte[]       Key { get; }

        byte[] GetEntryKey (string name = null);
    }

    [Serializable]
    public class DxKey : IDxKey
    {
        byte[]      m_pass;
        byte[]      m_key;

        public DxKey () : this (string.Empty)
        {
        }

        public DxKey (string plaintext) : this (Encodings.cp932.GetBytes (plaintext))
        {
        }

        public DxKey (byte[] encoded)
        {
            Password = encoded;
        }

        public byte[] Password
        {
            get { return m_pass; }
            set { m_pass = value; m_key = null; }
        }

        public byte[] Key
        {
            get { return m_key ?? (m_key = CreateKey (m_pass)); }
            set { m_key = value; m_pass = RestoreKey (m_key); }
        }

        public virtual byte[] GetEntryKey (string name)
        {
            return Key;
        }

        protected virtual byte[] CreateKey (byte[] keyword)
        {
            byte[] key;
            if (keyword == null || keyword.Length == 0)
            {
                key = Enumerable.Repeat<byte> (0xAA, 12).ToArray();
            }
            else
            {
                key = new byte[12];
                int byte_count = Math.Min (keyword.Length, 12);
                Buffer.BlockCopy (keyword, 0, key, 0, byte_count);
                if (byte_count < 12)
                    Binary.CopyOverlapped (key, 0, byte_count, 12-byte_count);
            }
            key[0] ^= 0xFF;
            key[1]  = Binary.RotByteR (key[1], 4);
            key[2] ^= 0x8A;
            key[3]  = (byte)~Binary.RotByteR (key[3], 4);
            key[4] ^= 0xFF;
            key[5] ^= 0xAC;
            key[6] ^= 0xFF;
            key[7]  = (byte)~Binary.RotByteR (key[7], 3);
            key[8]  = Binary.RotByteL (key[8], 3);
            key[9] ^= 0x7F;
            key[10] = (byte)(Binary.RotByteR (key[10], 4) ^ 0xD6);
            key[11] ^= 0xCC;
            return key;
        }

        protected virtual byte[] RestoreKey (byte[] key)
        {
            var bin = key.Clone() as byte[];
            bin[0] ^= 0xFF;
            bin[1]  = Binary.RotByteL (bin[1], 4);
            bin[2] ^= 0x8A;
            bin[3]  = Binary.RotByteL ((byte)~bin[3], 4);
            bin[4] ^= 0xFF;
            bin[5] ^= 0xAC;
            bin[6] ^= 0xFF;
            bin[7]  = Binary.RotByteL ((byte)~bin[7], 3);
            bin[8]  = Binary.RotByteR (bin[8], 3);
            bin[9] ^= 0x7F;
            bin[10] = Binary.RotByteL ((byte)(bin[10] ^ 0xD6), 4);
            bin[11] ^= 0xCC;
            return bin;
        }

        public static DxKey CreateInstanceFromKey (byte[] key)
        {
            var enc = new DxKey();
            enc.Key = key;
            return enc;
        }
    }

    [Serializable]
    public class DxKey7 : DxKey
    {
        public DxKey7 (string plaintext) : base (plaintext ?? "DXARC")
        {
        }

        public DxKey7 (byte[] encoded) : base (encoded)
        {
        }

        public override byte[] GetEntryKey (string name)
        {
            var password = this.Password;
            if (!string.IsNullOrEmpty (name))
            {
                var path = name.Split ('\\', '/');
                var append = string.Join ("", path.Reverse().Select (n => n.ToUpperInvariant()));
                var encoded = Encodings.cp932.GetBytes (append);
                password = password.Concat (encoded).ToArray();
            }
            return CreateKey (password);
        }

        protected override byte[] CreateKey (byte[] keyword)
        {
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash (keyword);
            }
        }

        protected override byte[] RestoreKey (byte[] key)
        {
            throw new NotSupportedException ("SHA-256 key cannot be restored.");
        }
    }

    [Serializable]
    public class DxKey8 : DxKey
    {
        int      m_codepage;

        public DxKey8 (string plaintext, int codepage)
            : this (Encoding.GetEncoding (codepage).GetBytes (plaintext ?? "DXLIBARC"), codepage)
        {
        }

        public DxKey8 (byte[] encoded, int codepage = 0) : base (encoded)
        {
            m_codepage = codepage;
        }

        public override byte[] GetEntryKey (string name)
        {
            var password = this.Password;
            if (!string.IsNullOrEmpty (name))
            {
                var path = name.Split ('\\', '/');
                var append = string.Join ("", path.Reverse().Select (n => n.ToUpperInvariant()));
                var encoded = Encoding.GetEncoding (m_codepage).GetBytes (append);
                password = password.Concat (encoded).ToArray();
            }
            return CreateKey (password);
        }

        protected override byte[] CreateKey (byte[] keyword)
        {
            // from DxArchive.cpp
            // check if the keyword is too short
            int keylen = keyword.Length;
            if (keylen < 4)
            {
                Array.Resize (ref keyword, keylen + 8);
                keyword[keylen  ] = (byte)'D';
                keyword[keylen+1] = (byte)'X';
                keyword[keylen+2] = (byte)'L';
                keyword[keylen+3] = (byte)'I';
                keyword[keylen+4] = (byte)'B';
                keyword[keylen+5] = (byte)'A';
                keyword[keylen+6] = (byte)'R';
                keyword[keylen+7] = (byte)'C';
            }

            byte[] oddBuffer = new byte[(keyword.Length / 2) + (keyword.Length % 2)];
            int oddCounter = 0;
            byte[] evenBuffer = new byte[keyword.Length / 2];
            int evenCounter = 0;
            for (int i = 0; i < keyword.Length; i += 2, oddCounter++)
            {
                oddBuffer[oddCounter] = keyword[i];
            }
            for (int i = 1; i < keyword.Length; i += 2, evenCounter++)
            {
                evenBuffer[evenCounter] = keyword[i];
            }
            UInt32 crc_0, crc_1;
            crc_0 = Crc32.Compute (oddBuffer, 0, oddCounter);
            crc_1 = Crc32.Compute (evenBuffer, 0, evenCounter);

            byte[] key = new byte[7];
            byte[] crc_0_Bytes = BitConverter.GetBytes (crc_0);
            byte[] crc_1_Bytes = BitConverter.GetBytes (crc_1);
            key[0] = crc_0_Bytes[0];
            key[1] = crc_0_Bytes[1];
            key[2] = crc_0_Bytes[2];
            key[3] = crc_0_Bytes[3];
            key[4] = crc_1_Bytes[0];
            key[5] = crc_1_Bytes[1];
            key[6] = crc_1_Bytes[2];
            return key;

            /*
            string oddString, evenString;
            oddString = string.Concat(keyword.Where((c, i) => i % 2 == 0));
            evenString = string.Concat(keyword.Where((c, i) => (i+1) % 2 == 0));
            UInt32 crc_0, crc_1;
            crc_0 = Crc32.Compute(Encoding.ASCII.GetBytes(oddString), 0, oddString.Length);
            crc_1 = Crc32.Compute(Encoding.ASCII.GetBytes(evenString), 0, evenString.Length);
            */
        }

        protected override byte[] RestoreKey (byte[] key)
        {
            throw new NotSupportedException ("CRC key cannot be restored.");
        }
    }
}
