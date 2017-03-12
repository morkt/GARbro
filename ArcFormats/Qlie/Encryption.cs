//! \file       Encryption.cs
//! \date       Sun Mar 12 04:51:11 2017
//! \brief      QLIE encryption-related classes.
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
using System.Text;

namespace GameRes.Formats.Qlie
{
    internal interface IEncryption
    {
        bool IsUnicode { get; }

        uint CalculateHash (byte[] data, int length);

        string DecryptName (byte[] name, int name_length);

        void DecryptEntry (byte[] data, int offset, int length, QlieEntry entry);
    }

    internal abstract class QlieEncryption : IEncryption
    {
        public virtual bool IsUnicode { get { return false; } }

        /// <summary>
        /// Hash generated from the key data contained within archive index.
        /// </summary>
        public uint ArcKey { get; protected set; }

        public static IEncryption Create (ArcView file, Version version, byte[] arc_key)
        {
            if (2 == version.Major || 1 == version.Major)
                return new EncryptionV2();
            else if (3 == version.Major && 1 == version.Minor)
                return new EncryptionV3_1 (file);
            else if (3 == version.Major && 0 == version.Minor)
                return new EncryptionV3 (file, arc_key);
            else
                throw new NotSupportedException ("Not supported QLIE archive version");
        }

        public abstract uint CalculateHash (byte[] data, int length);

        public abstract string DecryptName (byte[] name, int name_length);

        public abstract void DecryptEntry (byte[] data, int offset, int length, QlieEntry entry);
    }

    internal class EncryptionV2 : QlieEncryption
    {
        public EncryptionV2 ()
        {
            ArcKey = 0xC4;
        }

        public override uint CalculateHash (byte[] data, int length)
        {
            return 0; // not implemented
        }

        public override string DecryptName (byte[] name, int name_length)
        {
            int key = name_length + ((int)ArcKey ^ 0x3E);
            for (int k = 0; k < name_length; ++k)
                name[k] ^= (byte)(((k + 1) ^ key) + k + 1);

            return Encodings.cp932.GetString (name, 0, name_length);
        }

        public override void DecryptEntry (byte[] data, int offset, int length, QlieEntry entry)
        {
            if (offset < 0)
                throw new ArgumentOutOfRangeException ("offset");
            if (length > data.Length || offset > data.Length - length)
                throw new ArgumentOutOfRangeException ("length");
            uint arc_key = 0; // ArcKey?

            ulong hash = 0xA73C5F9DA73C5F9Dul;
            ulong xor = ((uint)length + arc_key) ^ 0xFEC9753Eu;
            xor |= xor << 32;
            unsafe
            {
                fixed (byte* raw = data)
                {
                    ulong* encoded = (ulong*)(raw + offset);
                    for (int i = 0; i < length / 8; ++i)
                    {
                        hash = MMX.PAddD (hash, 0xCE24F523CE24F523ul) ^ xor;
                        xor = *encoded ^ hash;
                        *encoded++ = xor;
                    }
                }
            }
        }
    }

    internal class EncryptionV3 : EncryptionV2
    {
        /// <summary>
        /// Internal game data used to decrypt encrypted entries.
        /// null if not used.
        /// </summary>
        public byte[] GameKeyData;

        public EncryptionV3 (ArcView file, byte[] game_key)
        {
            GameKeyData = game_key;
            var key_data = file.View.ReadBytes (file.MaxOffset-0x41C, 0x100);
            ArcKey = CalculateHash (key_data, key_data.Length) & 0x0FFFFFFFu;
        }

        public override uint CalculateHash (byte[] data, int length)
        {
            if (length > data.Length)
                throw new ArgumentOutOfRangeException ("length");
            unsafe
            {
                fixed (byte* data8 = data)
                {
                    ulong hash = 0;
                    ulong key  = 0;
                    ulong* data64 = (ulong*)data8;
                    for (int i = length / 8; i > 0; --i)
                    {
                        hash = MMX.PAddW (hash, 0x0307030703070307);
                        key  = MMX.PAddW (key, *data64++ ^ hash);
                    }
                    return (uint)(key ^ (key >> 32));
                }
            }
        }

        public override void DecryptEntry (byte[] data, int offset, int length, QlieEntry entry)
        {
            var key_file = entry.KeyFile;
            if (null == key_file)
            {
                base.DecryptEntry (data, offset, length, entry);
                return;
            }
            // play it safe with 'unsafe' sections
            if (offset < 0)
                throw new ArgumentOutOfRangeException ("offset");
            if (length > data.Length || offset > data.Length - length)
                throw new ArgumentOutOfRangeException ("length");

            if (length < 8)
                return;

            var file_name = entry.RawName;
            uint hash = 0x85F532;
            uint seed = 0x33F641;

            for (uint i = 0; i < file_name.Length; i++)
            {
                hash += (i & 0xFF) * file_name[i];
                seed ^= hash;
            }

            seed += ArcKey ^ (7 * ((uint)length & 0xFFFFFF) + (uint)length
                              + hash + (hash ^ (uint)length ^ 0x8F32DCu));
            seed = 9 * (seed & 0xFFFFFF);

            if (GameKeyData != null)
                seed ^= 0x453A;

            var mt = new QlieMersenneTwister (seed);
            if (key_file != null)
                mt.XorState (key_file);
            if (GameKeyData != null)
                mt.XorState (GameKeyData);

            // game code fills dword[41] table, but only the first 16 qwords are used
            ulong[] table = new ulong[16];
            for (int i = 0; i < table.Length; ++i)
                table[i] = mt.Rand64();

            // compensate for 9 discarded dwords
            for (int i = 0; i < 9; ++i)
                mt.Rand();

            ulong hash64 = mt.Rand64();
            uint t = mt.Rand() & 0xF;
            unsafe
            {
                fixed (byte* raw_data = &data[offset])
                {
                    ulong* data64 = (ulong*)raw_data;
                    int qword_length = length / 8;
                    for (int i = 0; i < qword_length; ++i)
                    {
                        hash64 = MMX.PAddD (hash64 ^ table[t], table[t]);

                        ulong d = data64[i] ^ hash64;
                        data64[i] = d;

                        hash64 = MMX.PAddB (hash64, d) ^ d;
                        hash64 = MMX.PAddW (MMX.PSllD (hash64, 1), d);

                        t++;
                        t &= 0xF;
                    }
                }
            }
        }
    }

    internal class EncryptionV3_1 : QlieEncryption
    {
        public override bool IsUnicode { get { return true; } }

        public EncryptionV3_1 (ArcView file)
        {
            var key_data = file.View.ReadBytes (file.MaxOffset-0x41C, 0x100);
            ArcKey = CalculateHash (key_data, key_data.Length) & 0x0FFFFFFFu;
        }

        public override uint CalculateHash (byte[] data, int length)
        {
            if (length > data.Length)
                throw new ArgumentOutOfRangeException ("length");
            unsafe
            {
                fixed (byte* data8 = data)
                {
                    ulong hash = 0;
                    ulong key  = 0;
                    ulong* data64 = (ulong*)data8;
                    for (int i = length / 8; i > 0; --i)
                    {
                        hash = MMX.PAddW (hash, 0xA35793A7A35793A7ul);
                        key  = MMX.PAddW (key, *data64++ ^ hash);
                        key  = MMX.PSllD (key, 3) | MMX.PSrlD (key, 29);
                    }
                    // MMX.PMAddWD (key, key >> 32);
                    return (uint)((short)key * (short)(key >> 32) + (short)(key >> 16) * (short)(key >> 48));
                }
            }
        }

        public override string DecryptName (byte[] name, int name_length)
        {
            int char_count = name_length / 2;
            int hash = (char_count * char_count) ^ char_count;
            hash ^= (int)(0x3E13 ^ (ArcKey >> 16) ^ ArcKey);
            hash &= 0xFFFF;
            int key = hash;
            for (int i = 0; i < char_count; ++i)
            {
                key = hash + i + 8 * key;
                name[i*2  ] ^= (byte)key;
                name[i*2+1] ^= (byte)(key >> 8);
            }
            return Encoding.Unicode.GetString (name, 0, name_length);
        }

        public override void DecryptEntry (byte[] data, int offset, int length, QlieEntry entry)
        {
            if (offset < 0)
                throw new ArgumentOutOfRangeException ("offset");
            if (length > data.Length || offset > data.Length - length)
                throw new ArgumentOutOfRangeException ("length");
            if (length < 8)
                return;

            var file_name = entry.Name;
            uint hash = 0x85F532;
            uint seed = 0x33F641;

            for (int i = 0; i < file_name.Length; i++)
            {
                hash += (uint)(file_name[i] << (i & 7));
                seed ^= hash;
            }

            seed += ArcKey ^ (7 * ((uint)length & 0xFFFFFF) + (uint)length
                              + hash + (hash ^ (uint)length ^ 0x8F32DCu));
            seed = 9 * (seed & 0xFFFFFF);
            var table = GenerateTable (0x20, seed); // originally 0x2C, 12 dwords not used
            unsafe
            {
                fixed (byte* raw_data = &data[offset])
                {
                    ulong* data64 = (ulong*)raw_data;
                    int qword_length = length / 8;
                    uint k = 2 * (table[0xD] & 0xF);
                    ulong hash64 = table[6] | (ulong)table[7] << 32;
                    for (int i = 0; i < qword_length; ++i)
                    {
                        ulong t = table[k] | (ulong)table[k+1] << 32;
                        hash64 = MMX.PAddD (hash64 ^ t, t);

                        ulong d = data64[i] ^ hash64;
                        data64[i] = d;

                        hash64 = MMX.PAddB (hash64, d) ^ d;
                        hash64 = MMX.PAddW (MMX.PSllD (hash64, 1), d);

                        k = (k + 2) & 0x1F;
                    }
                }
            }
        }

        static uint[] GenerateTable (int length, uint seed)
        {
            const uint key = 0x8DF21431u;
            var table = new uint[length];
            for (int i = 0; i < length; ++i)
            {
                ulong x = key * (ulong)(seed ^ key);
                seed = (uint)((x >> 32) + x);
                table[i] = seed;
            }
            return table;
        }
    }
}
