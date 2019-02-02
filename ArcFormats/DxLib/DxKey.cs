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
using System.Linq;
using System.Security.Cryptography;
using GameRes.Utility;

namespace GameRes.Formats.DxLib
{
    public interface IDxKey
    {
        string  Password { get; }
        byte[]       Key { get; }

        byte[] GetEntryKey (string name);
    }

    [Serializable]
    public class DxKey : IDxKey
    {
        string      m_password;
        byte[]      m_key;

        public DxKey () : this (string.Empty)
        {
        }

        public DxKey (string password)
        {
            Password = password;
        }

        public DxKey (byte[] key)
        {
            Key = key;
        }

        public string Password
        {
            get { return m_password; }
            set { m_password = value; m_key = null; }
        }

        public byte[] Key
        {
            get { return m_key ?? (m_key = CreateKey (m_password)); }
            set { m_key = value; m_password = RestoreKey (m_key); }
        }

        public virtual byte[] GetEntryKey (string name)
        {
            return Key;
        }

        protected virtual byte[] CreateKey (string keyword)
        {
            byte[] key;
            if (string.IsNullOrEmpty (keyword))
            {
                key = Enumerable.Repeat<byte> (0xAA, 12).ToArray();
            }
            else
            {
                key = new byte[12];
                int char_count = Math.Min (keyword.Length, 12);
                int length = Encodings.cp932.GetBytes (keyword, 0, char_count, key, 0);
                if (length < 12)
                    Binary.CopyOverlapped (key, 0, length, 12-length);
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

        protected virtual string RestoreKey (byte[] key)
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
            return Encodings.cp932.GetString (bin);
        }
    }

    [Serializable]
    public class DxKey7 : DxKey
    {
        public DxKey7 (string password) : base (password ?? "DXARC")
        {
        }

        public override byte[] GetEntryKey (string name)
        {
            var password = this.Password;
            var path = name.Split ('\\', '/');
            password += string.Join ("", path.Reverse().Select (n => n.ToUpperInvariant()));
            return CreateKey (password);
        }

        protected override byte[] CreateKey (string keyword)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encodings.cp932.GetBytes (keyword);
                return sha.ComputeHash (bytes);
            }
        }

        protected override string RestoreKey (byte[] key)
        {
            throw new NotSupportedException ("SHA-256 key cannot be restored.");
        }
    }
}
