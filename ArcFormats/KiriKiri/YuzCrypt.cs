//! \file       YuzCrypt.cs
//! \date       2018 Apr 01
//! \brief      YuzuSoft KiriKiri encryption schemes.
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
using System.IO;
using System.Text;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.KiriKiri
{
    [Serializable]
    public class SenrenCxCrypt : CxEncryption
    {
        public SenrenCxCrypt (CxScheme scheme) : base (scheme)
        {
        }

        public virtual string NamesSectionId { get { return "sen:"; } }

        internal virtual void ReadYuzNames (byte[] yuz, FilenameMap filename_map)
        {
            using (var ystream = new MemoryStream (yuz))
            using (var zstream = ZLibCompressor.DeCompress (ystream))
            using (var input = new BinaryReader (zstream, Encoding.Unicode))
            {
                long dir_offset = 0;
                while (-1 != input.PeekChar())
                {
                    uint entry_signature = input.ReadUInt32();
                    long entry_size = input.ReadInt64();
                    if (entry_size < 0)
                        return;
                    dir_offset += 12 + entry_size;
                    uint hash = input.ReadUInt32();
                    int name_size = input.ReadInt16();
                    if (name_size > 0)
                    {
                        entry_size -= 6;
                        if (name_size * 2 <= entry_size)
                        {
                            var filename = new string (input.ReadChars (name_size));
                            filename_map.Add (hash, filename);
                        }
                    }
                    input.BaseStream.Position = dir_offset;
                }
                filename_map.AddShortcut ("$", "startup.tjs");
            }
        }
    }

    [Serializable]
    public class CabbageCxCrypt : SenrenCxCrypt
    {
        uint m_random_seed;

        public CabbageCxCrypt (CxScheme scheme, uint seed) : base (scheme)
        {
            m_random_seed = seed;
        }

        public override string NamesSectionId { get { return "cbg:"; } }

        internal override CxProgram NewProgram (uint seed)
        {
            return new CxProgramNana (seed, m_random_seed, ControlBlock);
        }
    }

    [Serializable]
    public class NanaCxCrypt : CabbageCxCrypt
    {
        public uint[] YuzKey;

        public NanaCxCrypt (CxScheme scheme, uint seed) : base (scheme, seed)
        {
        }

        public override string NamesSectionId { get { return "dls:"; } }

        internal override void ReadYuzNames (byte[] yuz, FilenameMap filename_map)
        {
            if (null == YuzKey)
                throw new InvalidEncryptionScheme();
            var decryptor = CreateNameListDecryptor();
            decryptor.Decrypt (yuz, Math.Min (yuz.Length, 0x100));
            base.ReadYuzNames (yuz, filename_map);
        }

        internal virtual INameListDecryptor CreateNameListDecryptor ()
        {
            return new NanaDecryptor (YuzKey, YuzKey[4], YuzKey[5]);
        }
    }

    [Serializable]
    public class RiddleCxCrypt : NanaCxCrypt
    {
        public RiddleCxCrypt (CxScheme scheme, uint seed) : base (scheme, seed)
        {
        }

        public override string NamesSectionId { get { return "yuz:"; } }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            ProcessFirstBytes (entry, offset, buffer, pos, count);
            base.Decrypt (entry, offset, buffer, pos, count);
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            base.Encrypt (entry, offset, buffer, pos, count);
            ProcessFirstBytes (entry, offset, buffer, pos, count);
        }

        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            if (offset < 8)
            {
                var buffer = new byte[1] { value };
                this.Decrypt (entry, offset, buffer, 0, 1);
                return buffer[0];
            }
            else
            {
                return base.Decrypt (entry, offset, value);
            }
        }

        internal void ProcessFirstBytes (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            if (offset < 8 && count > 0)
            {
                ulong key = GetKeyFromHash (entry.Hash);
                key >>= (int)offset << 3;
                int first_chunk = Math.Min (count, 8 - (int)offset);
                for (int i = 0; i < first_chunk; ++i)
                {
                    buffer[pos+i] ^= (byte)key;
                    key >>= 8;
                }
            }
        }

        internal ulong GetKeyFromHash (uint hash)
        {
            uint lo = hash ^ 0x55555555;
            uint hi = (hash << 13) ^ hash;
            hi ^= hi >> 17;
            hi ^= (hi << 5) ^ 0xAAAAAAAA;
            return (ulong)hi << 32 | lo;
        }

        internal override INameListDecryptor CreateNameListDecryptor ()
        {
            return new YuzDecryptor (ControlBlock, YuzKey, YuzKey[4], YuzKey[5]);
        }
    }

    internal interface INameListDecryptor
    {
        void Decrypt (byte[] data, int length);
    }

    internal class YuzDecryptor : INameListDecryptor
    {
        byte[]  m_state;

        public YuzDecryptor (uint[] key1, uint[] key2, uint seed1, uint seed2)
        {
            m_state = new byte[64];
            Buffer.BlockCopy (key2, 0, m_state, 0, 16);
            Buffer.BlockCopy (key1, 0, m_state, 16, 32);
            LittleEndian.Pack (~0, m_state, 48);
            LittleEndian.Pack (~0, m_state, 52);
            LittleEndian.Pack (~seed1, m_state, 56);
            LittleEndian.Pack (~seed2, m_state, 60);
        }

        public void Decrypt (byte[] data, int length)
        {
            var state1 = new byte[64];
            var state2 = new byte[64];
            int i = 0;
            ulong offset = 0;
            while (length > 0)
            {
                Buffer.BlockCopy (m_state, 0, state1, 0, 64);
                LittleEndian.Pack (~offset++, state1, 48);
                TransformState (state1, state2, 8);
                int count = Math.Min (0x40, length);
                for (int j = 0; j < count; ++j)
                {
                    data[i++] ^= state2[j];
                }
                length -= count;
            }
        }

        uint[] tmp = new uint[16];

        void TransformState (byte[] state1, byte[] target, int length)
        {
            for (int i = 0; i < 16; ++i)
            {
                tmp[i] = ~LittleEndian.ToUInt32 (state1, i * 4);
            }
            if (length > 0)
            {
                for (int count = ((length - 1) >> 1) + 1; count > 0; --count)
                {
                    uint t1 = tmp[4] + tmp[0];
                    uint t2 = Binary.RotL (t1 ^ tmp[12], 16);
                    uint t3 = t2 + tmp[8];
                    uint t4 = Binary.RotL (tmp[4] ^ t3, 12);
                    uint t5 = t4 + t1;
                    uint t6 = Binary.RotL (t5 ^ t2, 8);
                    tmp[12] = t6;
                    t6 += t3;
                    tmp[4] = Binary.RotL (t4 ^ t6, 7);
                    t4 = Binary.RotL ((tmp[5] + tmp[1]) ^ tmp[13], 16);
                    t3 = Binary.RotL (tmp[5] ^ (t4 + tmp[9]), 12);
                    t2 = t3 + tmp[5] + tmp[1];
                    tmp[13] = Binary.RotL (t2 ^ t4, 8);
                    tmp[9] += tmp[13] + t4;
                    tmp[5] = Binary.RotL (t3 ^ tmp[9], 7);
                    t4 = Binary.RotL ((tmp[6] + tmp[2]) ^ tmp[14], 16);
                    tmp[10] += t4;
                    t1 = Binary.RotL (tmp[6] ^ tmp[10], 12);
                    t3 = t1 + tmp[6] + tmp[2];
                    tmp[14] = Binary.RotL (t3 ^ t4, 8);
                    tmp[6] = Binary.RotL (t1 ^ (tmp[14] + tmp[10]), 7);
                    tmp[10] += tmp[14];
                    t4 = (tmp[7] + tmp[3]) ^ tmp[15];
                    tmp[3] += tmp[7];
                    t4 = Binary.RotL (t4, 16);
                    tmp[11] += t4;
                    t1 = Binary.RotL (tmp[7] ^ tmp[11], 12);
                    t4 ^= t1 + tmp[3];
                    tmp[3] += t1;
                    t4 = Binary.RotL (t4, 8);
                    tmp[11] += t4;
                    t1 = Binary.RotL (t1 ^ tmp[11], 7);
                    t5 += tmp[5];
                    t2 += tmp[6];
                    t4 = Binary.RotL (t5 ^ t4, 16);
                    tmp[10] += t4;
                    tmp[5] = Binary.RotL (tmp[5] ^ tmp[10], 12);
                    tmp[0] = tmp[5] + t5;
                    t4 = Binary.RotL (tmp[0] ^ t4, 8);
                    tmp[15] = t4;
                    tmp[10] += t4;
                    tmp[5] = Binary.RotL (tmp[5] ^ tmp[10], 7);
                    tmp[12] = Binary.RotL (tmp[12] ^ t2, 16);
                    tmp[11] += tmp[12];
                    t4 = Binary.RotL (tmp[11] ^ tmp[6], 12);
                    tmp[1] = t4 + t2;
                    tmp[12] = Binary.RotL (tmp[12] ^ tmp[1], 8);
                    tmp[11] += tmp[12];
                    tmp[6] = Binary.RotL (t4 ^ tmp[11], 7);
                    t3 += t1;
                    t4 = Binary.RotL (tmp[13] ^ t3, 16);
                    t2 = t4 + t6;
                    t1 = Binary.RotL (t2 ^ t1, 12);
                    tmp[2] = t1 + t3;
                    tmp[13] = Binary.RotL (t4 ^ tmp[2], 8);
                    tmp[8] = tmp[13] + t2;
                    tmp[7] = Binary.RotL (tmp[8] ^ t1, 7);
                    t6 = Binary.RotL (tmp[14] ^ (tmp[4] + tmp[3]), 16);
                    t1 = Binary.RotL (tmp[4] ^ (t6 + tmp[9]), 12);
                    tmp[3] += t1 + tmp[4];
                    t3 = Binary.RotL (t6 ^ tmp[3], 8);
                    tmp[9] += t3 + t6;
                    tmp[4] = Binary.RotL (t1 ^ tmp[9], 7);
                    tmp[14] = t3;
                }
            }
            int pos = 0;
            for (int i = 0; i < 16; ++i)
            {
                uint x = tmp[i] + ~LittleEndian.ToUInt32 (state1, pos);
                LittleEndian.Pack (x, target, pos);
                pos += 4;
            }
        }
    }

    internal class NanaDecryptor : INameListDecryptor
    {
        uint[]  m_state;
        ulong   m_seed;

        public NanaDecryptor (uint[] key, uint seed1, uint seed2)
        {
            m_state = new uint[27];
            m_seed = (ulong)seed2 << 32 | seed1;
            var s = new uint[3];
            uint k = key[0];
            s[0] = key[1];
            s[1] = key[2];
            s[2] = key[3];
            m_state[0] = k;
            int dst = 1;
            for (uint i = 0; i < 26; ++i)
            {
                int src = (int)i % 3;
                uint m = Binary.RotR (s[src], 8);
                uint n = i ^ (k + m);
                k = n ^ Binary.RotL (k, 3);
                m_state[dst++] = k;
                s[src] = n;
            }
        }

        public void Decrypt (byte[] data, int length)
        {
            int i = 0;
            ulong offset = 0;
            while (length > 0)
            {
                ulong key = ++offset ^ m_seed;
                key = TransformKey (key);
                int count = Math.Min (8, length);
                for (int j = 0; j < count; ++j)
                {
                    data[i++] ^= (byte)key;
                    key >>= 8;
                }
                length -= count;
            }
        }

        ulong TransformKey (ulong key)
        {
            uint lo = (uint)key;
            uint hi = (uint)(key >> 32);
            for (int i = 0; i < 27; ++i)
            {
                hi = Binary.RotR (hi, 8);
                hi += lo;
                hi ^= m_state[i];
                lo = Binary.RotL (lo, 3);
                lo ^= hi;
            }
            return (ulong)hi << 32 | lo;
        }
    }
}
