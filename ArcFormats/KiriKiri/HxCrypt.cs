//! \file       HxCrypt.cs
//! \date       2022
//! \brief      Hx KiriKiri encryption schemes.
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

using GameRes.Compression;
using GameRes.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

#pragma warning disable IDE0019

namespace GameRes.Formats.KiriKiri
{
    [Serializable]
    public class HxCrypt : CxEncryption
    {
        public byte[]  IndexKey1; // 32 bytes
        public byte[]  IndexKey2; // 16 bytes
        public ulong   FilterKey;
        public int     RandomType;
        public string  NamesFile;

        public HxCrypt(CxScheme scheme) : base(scheme)
        {
        }

        [NonSerialized]
        uint[] _lookup32 = null;

        void CreateLookup32()
        {
            if (null != _lookup32)
                return;
            var result = new uint[256];
            for (int i = 0; i < result.Length; i++)
            {
                string s = i.ToString ("X2");
                result[i] = s[0] + ((uint)s[1] << 16);
            }
            _lookup32 = result;
        }

        string BinaryToString(byte[] data)
        {
            if (data.Length == 0)
                return string.Empty;
            var lookup32 = _lookup32;
            var result = new char[data.Length*2];
            for (int i = 0; i < data.Length; i++)
            {
                var val = lookup32[data[i]];
                result[2*i] = (char)val;
                result[2*i+1] = (char)(val >> 16);
            }
            return new string(result);
        }

        internal virtual Dictionary<string, HxEntry> ReadIndex(byte[] data)
        {
            if (data.Length <= 20) // 16 + 4
                return null;
            if (null == IndexKey1 || IndexKey1.Length != 32)
                return null;
            if (null == IndexKey2 || IndexKey2.Length != 16)
                return null;
            var seed = new uint[] { 1, 0 };
            var crypt = new HxChachaDecryptor (IndexKey1, IndexKey2, seed);
            var buf = new byte[data.Length-16];
            crypt.Decrypt (data, 16, buf, 0, buf.Length);
            Stream index_stream = null;
            using (var stream = new MemoryStream (buf))
            {
                stream.Position = 4;
                index_stream = ZLibCompressor.DeCompress (stream);
            }
            if (null == index_stream)
                return null;
            object index_obj = HxIndexDeserializer.Deserialize (index_stream);
            var root_obj = index_obj as object[];
            if (null == root_obj)
                return null;
            CreateLookup32 ();
            var path_map = new Dictionary<string, string>();
            var name_map = new Dictionary<string, string>();
            try
            {
                FormatCatalog.Instance.ReadFileList (NamesFile, line => {
                    var name = line.Split (':');  // "hash:name"
                    if (name.Length != 2)
                        return;
                    if (name[0].Length == 16)
                        path_map[name[0]] = name[1];
                    else if (name[0].Length == 64)
                        name_map[name[0]] = name[1];
                });
            }
            catch (Exception) { }
            var entry_info_map = new Dictionary<string, HxEntry>();
            for (var i = 0; i < root_obj.Length; i += 2)
            {
                var path_hash = root_obj[i] as byte[];
                if (null == path_hash)
                    continue;
                var dir_obj = root_obj[i+1] as object[];
                if (null == dir_obj)
                    continue;
                var path_hash_str = BinaryToString (path_hash);
                for (var j = 0; j < dir_obj.Length; j += 2)
                {
                    var entry_hash = dir_obj[j] as byte[];
                    if (null == entry_hash)
                        continue;
                    var entry_obj = dir_obj[j+1] as object[];
                    if (null == entry_obj)
                        continue;
                    if (entry_obj.Length < 2)
                        continue;
                    var entry_id = entry_obj[0] as long?;
                    if (null == entry_id)
                        continue;
                    var entry_key = entry_obj[1] as long?;
                    if (null == entry_key)
                        continue;
                    var entry_info = new HxEntry();
                    if (path_map.TryGetValue (path_hash_str, out string path_str))
                        entry_info.Path = path_str;
                    var name_hash_str = BinaryToString (entry_hash);
                    if (name_map.TryGetValue (name_hash_str, out string name_str))
                        entry_info.Name = name_str;
                    entry_info.Key = (long)entry_key;
                    var id = (uint)entry_id;
                    var uname = GetUnicodeName (id);
                    entry_info_map.Add (uname, entry_info);
                }
            }
            return entry_info_map;
        }

        internal virtual string GetUnicodeName(uint hash)
        {
            var buf = new char[4];
            var i = 0;
            for (;;)
            {
                buf[i++] = (char)((hash & 0x3FFF) + 0x5000);
                hash >>= 14;
                if (hash == 0)
                    break;
            }
            var str = new string (buf, 0, i);
            return str;
        }

        internal virtual HxFilterKey CreateFilterKey(ulong entry_key, ulong header_key_seed)
        {
            var result = new HxFilterKey
            {
                Key = new ulong[2],
                HeaderKey = new byte[16],
            };

            /* create file key */

            uint key0 = (uint)(entry_key & 0xffffffff);
            uint key1 = (uint)((entry_key >> 32) & 0xffffffff);

            var k0 = ExecuteXCode(key0);
            result.Key[0] = (ulong)k0.Item1 | ((ulong)k0.Item2 << 32);
            var k1 = ExecuteXCode(key1);
            result.Key[1] = (ulong)k1.Item1 | ((ulong)k1.Item2 << 32);

            result.SplitPosition = (long)((this.m_offset + ((entry_key >> 16) & this.m_mask)) & 0xffffffff);

            /* create header key */

            var k3 = ExecuteXCode((uint)header_key_seed);
            var v5 = (ulong)k3.Item1 | ((ulong)k3.Item2 << 32);
            v5 = ~v5;

            for (int i = 0, j = 56; i < 8; i += 1, j -= 8)
            {
                result.HeaderKey[i] = (byte)((v5 >> j) & 0xff);
            }

            k3 = ExecuteXCode((uint)v5);
            v5 = (ulong)k3.Item1 | ((ulong)k3.Item2 << 32);
            v5 = ~v5;

            for (int i = 0, j = 56; i < 8; i += 1, j -= 8)
            {
                result.HeaderKey[i+8] = (byte)((v5 >> j) & 0xff);
            }

            result.HasHeaderKey = true;
            result.Flag = false;

            return result;
        }

        internal virtual void CreateFilter(Xp3Entry entry)
        {
            var info = entry.Extra as HxEntry;
            if (null == info)
                return;
            if (null != info.Filter)
                return;
            var entry_key = (ulong)info.Key ^ FilterKey;
            var header_key = ~entry_key;
            var key = CreateFilterKey (entry_key, header_key);
            info.Filter = new HxFilter (key);
        }

        public override void Init(ArcFile arc)
        {
            return;
        }

        public override byte Decrypt(Xp3Entry entry, long offset, byte value)
        {
            if (entry.Extra == null)
                return value;
            var info = entry.Extra as HxEntry;
            if (null == info)
                return value;
            if (null == info.Filter)
                CreateFilter (entry);
            if (null == info.Filter)
                return value;

            var buf = new byte[1] { value };

            info.Filter.Decrypt (offset, buf, 0, 1);

            return buf[0];
        }

        public override void Decrypt(Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            if (entry.Extra == null)
                return;
            var info = entry.Extra as HxEntry;
            if (null == info)
                return;
            if (null == info.Filter)
                CreateFilter (entry);
            if (null == info.Filter)
                return;

            info.Filter.Decrypt (offset, buffer, pos, count);

            return;
        }

        public override void Encrypt(Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            throw new NotImplementedException();
        }

        internal override CxProgram NewProgram(uint seed)
        {
            return new HxProgram(seed, ControlBlock, RandomType);
        }
    }
    
    internal class HxEntry
    {
        public string   Path;
        public string   Name;
        public long     Key;
        public HxFilter Filter;
    }

    internal class HxFilterKey
    {
        public ulong[]  Key;
        public long     SplitPosition;
        public byte[]   HeaderKey;
        public bool     HasHeaderKey;
        public bool     Flag;
    }

    internal class HxHeaderKey
    {
        public long    Position;
        public byte[]  Key;
        public int     KeyPtr;
        public int     Length;
    }

    internal class HxBufferSpan
    {
        public byte[] Buffer;
        public int    BufferPtr;
        public int    Length;
    }

    internal class HxFilterSpan
    {
        public long          Position;
        public HxBufferSpan  Data;

        bool AdjustHeaderKey(HxHeaderKey key, HxHeaderKey new_key)
        {
            if (this.Data.Buffer == null)
                return false;

            if (this.Data.Length == 0)
                return false;

            if (key.Key == null)
                return false;

            if (key.Length == 0)
                return false;

            long dataStart = this.Position;
            long dataEnd = dataStart + this.Data.Length;

            if (dataStart <= key.Position)
                dataStart = key.Position;

            if (dataEnd >= key.Position + key.Length)
                dataEnd = key.Position + key.Length;

            if (dataStart >= dataEnd)
                return false;

            new_key.Position = dataStart;
            new_key.Key = key.Key;
            new_key.KeyPtr = (int)(dataStart - key.Position);
            new_key.Length = (int)(dataEnd - dataStart);

            return true;
        }

        public void DecryptHeader(HxHeaderKey key)
        {
            var key2 = new HxHeaderKey();

            if (AdjustHeaderKey(key, key2))
            {
                for (uint i = 0; i < key2.Length; i++)
                {
                    this.Data.Buffer[this.Data.BufferPtr + key2.Position - this.Position + i]
                        ^= key2.Key[key2.KeyPtr + i];
                }
            }
        }

        public int Split(long split_position, HxFilterSpan[] sub_span)
        {
            if (this.Data.Buffer == null)
                return 0;

            if (this.Data.Length == 0)
                return 0;

            if (split_position > Position)
            {
                if (split_position < Position + this.Data.Length)
                {
                    sub_span[0] = new HxFilterSpan
                    {
                        Position = this.Position,
                        Data = new HxBufferSpan
                        {
                            Buffer = this.Data.Buffer,
                            BufferPtr = this.Data.BufferPtr,
                            Length = (int)(split_position - Position),
                        }
                    };
                    sub_span[1] = new HxFilterSpan
                    {
                        Position = split_position,
                        Data = new HxBufferSpan
                        {
                            Buffer = this.Data.Buffer,
                            BufferPtr = this.Data.BufferPtr + sub_span[0].Data.Length,
                            Length = this.Data.Length - sub_span[0].Data.Length,
                        }
                    };
                    return 3;
                }
                else
                {
                    sub_span[0] = this;
                    sub_span[1] = new HxFilterSpan();
                    return 1;
                }
            }
            else
            {
                sub_span[0] = new HxFilterSpan();
                sub_span[1] = this;
                return 2;
            }
        }

        public void FirstDecrypt(uint key)
        {
            if (this.Data.Buffer == null)
                return;

            if (this.Data.Length == 0)
                return;

            var buf = BitConverter.GetBytes(key);

            for (int i = 0; i < this.Data.Length; i++)
            {
                var j = Position + i;
                this.Data.Buffer[this.Data.BufferPtr + i] ^= buf[j & 3];
            }
        }
    }

    internal class HxFilterSpanDecryptor
    {
        private long[]  SpanPosition;
        private uint    FirstDecryptKey;
        private uint    DecryptKey;

        public HxFilterSpanDecryptor(ulong key, bool flag)
        {
            this.DecryptKey = (uint)((key >> 8) & 0xFF);
            this.DecryptKey |= (uint)((key >> 8) & 0xFF00);

            this.SpanPosition = new long[2]
            {
                (long)((key >> 48) & 0xFFFF),
                (long)((key >> 32) & 0xFFFF),
            };

            this.FirstDecryptKey = (uint)(key & 0xFF);

            if (this.SpanPosition[0] == this.SpanPosition[1])
                this.SpanPosition[1] += 1;

            if (flag)
                this.DecryptKey = 0;

            if (!flag && this.FirstDecryptKey == 0)
                this.FirstDecryptKey = 0xA5;

            this.FirstDecryptKey *= 0x1010101;
        }

        public void Decrypt(HxFilterSpan span)
        {
            span.FirstDecrypt(FirstDecryptKey);

            byte key1 = (byte)(this.DecryptKey & 0xFF);
            byte key2 = (byte)((this.DecryptKey >> 8) & 0xFF);

            if (key1 != 0)
            {
                if (this.SpanPosition[0] >= span.Position &&
                    this.SpanPosition[0] < span.Position + span.Data.Length)
                {
                    span.Data.Buffer[span.Data.BufferPtr + this.SpanPosition[0] - span.Position] ^= key1;
                }
            }

            if (key2 != 0)
            {
                if (this.SpanPosition[1] >= span.Position &&
                    this.SpanPosition[1] < span.Position + span.Data.Length)
                {
                    span.Data.Buffer[span.Data.BufferPtr + this.SpanPosition[1] - span.Position] ^= key2;
                }
            }
        }
    }

    internal class HxFilter
    {
        private HxFilterSpanDecryptor[]  Span;
        private long                     SplitPosition;
        private HxHeaderKey              HeaderKey;

        public HxFilter(HxFilterKey key)
        {
            this.Span = new HxFilterSpanDecryptor[2]
            {
                new HxFilterSpanDecryptor(key.Key[0], key.Flag),
                new HxFilterSpanDecryptor(key.Key[1], key.Flag),
            };

            this.SplitPosition = key.SplitPosition;

            this.HeaderKey = new HxHeaderKey();

            if (key.HasHeaderKey)
            {
                this.HeaderKey.Key = key.HeaderKey;
                this.HeaderKey.Length = 16;
            }
        }

        public void Decrypt(long position, byte[] buffer, int buffer_ptr, int length)
        {
            var span = new HxFilterSpan
            {
                Position = position,

                Data = new HxBufferSpan
                {
                    Buffer = buffer,
                    BufferPtr = buffer_ptr,
                    Length = length,
                },
            };

            if (span.Position < this.HeaderKey.Position + this.HeaderKey.Length)
            {
                span.DecryptHeader(this.HeaderKey);
            }

            var sub_span = new HxFilterSpan[2];

            var flags = span.Split(this.SplitPosition, sub_span);

            if ((flags & 1) != 0)
            {
                this.Span[0].Decrypt(sub_span[0]);
            }

            if ((flags & 2) != 0)
            {
                this.Span[1].Decrypt(sub_span[1]);
            }
        }
    }

    internal class HxSplittableRandom
    {
        private ulong m_seed;

        public HxSplittableRandom(ulong seed)
        {
            m_seed = seed;
        }

        public ulong Next()
        {
            ulong z;

            m_seed += 0x9e3779b97f4a7c15;
            z = m_seed;

            z ^= z >> 30;
            z *= 0xbf58476d1ce4e5b9;
            z ^= z >> 27;
            z *= 0x94d049bb133111eb;
            z ^= z >> 31;

            return z;
        }
    }

    internal class HxProgram : CxProgram
    {
        [StructLayout(LayoutKind.Explicit)]
        struct M64
        {
            [FieldOffset(0)] public ulong    u64;
            [FieldOffset(0)] public  uint u32_lo;
            [FieldOffset(4)] public  uint u32_hi;
        }

        readonly int m_random_method;
        new readonly M64[] m_seed;

        public HxProgram(uint seed, uint[] control_block, int random_method) : base(seed, control_block)
        {
            m_random_method = random_method;
            m_seed = new M64[2];

            ulong s = seed;
            s = (s & 0xffffffff) | (~s << 32);

            var r = new HxSplittableRandom(s);

            m_seed[0].u64 = r.Next();
            m_seed[1].u64 = r.Next();
        }

        ulong GetOldRandom()
        {
            /* These codes only work correctly in little endian mode! */

            var a = new M64();
            var b = new M64();
            var c = new M64();
            var d = new M64();
            var e = new M64();

            ulong t;

            a.u64 = m_seed[0].u64;
            b.u64 = m_seed[1].u64;

            c.u32_lo = a.u32_hi ^ b.u32_hi;
            c.u32_hi = a.u32_lo ^ b.u32_lo;

            e.u32_lo = c.u32_hi;
            e.u32_hi = c.u32_lo;

            t = (ulong)(c.u32_hi) << 21;
            t ^= a.u64 >> 15;
            t ^= c.u32_hi;
            m_seed[0].u32_lo = (uint)t;

            t = a.u32_hi >> 15;
            t |= (ulong)(a.u32_lo) << 17;
            t ^= e.u64 >> 11;
            t ^= c.u32_lo;
            m_seed[0].u32_hi = (uint)t;

            m_seed[1].u32_hi = (uint)(e.u64 >> 4);
            m_seed[1].u32_lo = (uint)(c.u64 >> 4);

            d.u64 = a.u64 + b.u64;

            t = d.u64 << 17;
            t |= d.u32_hi >> 15;
            t += a.u64;

            return t;
        }

        ulong GetNewRandom()
        {
            /* These codes only work correctly in little endian mode! */

            var a = new M64();
            var b = new M64();
            var c = new M64();
            var d = new M64();

            ulong t;

            a.u64 = m_seed[0].u64;
            b.u64 = m_seed[1].u64;

            c.u32_lo = a.u32_lo ^ b.u32_lo;
            c.u32_hi = a.u32_hi ^ b.u32_hi;

            t = (ulong)(a.u32_lo) << 24;
            t |= a.u32_hi >> 8;
            t ^= (ulong)(c.u32_lo) << 16;
            t ^= c.u32_lo;
            m_seed[0].u32_lo = (uint)t;

            t = c.u64 >> 16;
            t ^= a.u64 >> 8;
            t ^= c.u32_hi;
            m_seed[0].u32_hi = (uint)t;

            t = c.u32_hi >> 27;
            t |= (ulong)(c.u32_lo) << 5;
            m_seed[1].u32_hi = (uint)t;

            m_seed[1].u32_lo = (uint)(c.u64 >> 27);

            d.u64 = 5 * a.u64;

            t = d.u32_hi >> 25;
            t |= d.u64 << 7;
            t *= 9;

            return t;
        }

        public override uint GetRandom()
        {
            if (0 == m_random_method)
                return (uint)GetOldRandom();
            else
                return (uint)GetNewRandom();
        }
    }

    internal class HxChachaDecryptor
    {
        internal class State
        {
            //public byte[] Constant;
            //public byte[] Key0;
            //public byte[] Key1;
            //public byte[] Nonce;
            public byte[] Data = new byte[64];
        }

        readonly State m_state;

        public HxChachaDecryptor(byte[] key, byte[] nonce, uint[] seed)
        {
            m_state = new State();

            var constant = Encoding.ASCII.GetBytes ("expand 32-byte k");

            Array.Copy (constant, 0, m_state.Data, 0, 16);
            Array.Copy (key, 0, m_state.Data, 16, 16);
            Array.Copy (key, 16, m_state.Data, 32, 16);
            LittleEndian.Pack (seed[0], m_state.Data, 48);
            LittleEndian.Pack (seed[1], m_state.Data, 52);
            Array.Copy (nonce, 0, m_state.Data, 56, 8);
        }

        void TransformState(State src, State dst)
        {
            uint z0, z1, z2, z3, z4, z5, z6, z7,
                z8, z9, za, zb, zc, zd, ze, zf;

            z0 = LittleEndian.ToUInt32(src.Data, 0);
            z1 = LittleEndian.ToUInt32(src.Data, 4);
            z2 = LittleEndian.ToUInt32(src.Data, 8);
            z3 = LittleEndian.ToUInt32(src.Data, 12);
            z4 = LittleEndian.ToUInt32(src.Data, 16);
            z5 = LittleEndian.ToUInt32(src.Data, 20);
            z6 = LittleEndian.ToUInt32(src.Data, 24);
            z7 = LittleEndian.ToUInt32(src.Data, 28);
            z8 = LittleEndian.ToUInt32(src.Data, 32);
            z9 = LittleEndian.ToUInt32(src.Data, 36);
            za = LittleEndian.ToUInt32(src.Data, 40);
            zb = LittleEndian.ToUInt32(src.Data, 44);
            zc = LittleEndian.ToUInt32(src.Data, 48);
            zd = LittleEndian.ToUInt32(src.Data, 52);
            ze = LittleEndian.ToUInt32(src.Data, 56);
            zf = LittleEndian.ToUInt32(src.Data, 60);

            for (int i = 0; i < 10; i++)
            {
                // QUARTER(z0, z4, z8, zc);
                z0 += z4; zc = Binary.RotL(zc ^ z0, 16);
                z8 += zc; z4 = Binary.RotL(z4 ^ z8, 12);
                z0 += z4; zc = Binary.RotL(zc ^ z0, 8);
                z8 += zc; z4 = Binary.RotL(z4 ^ z8, 7);
                // QUARTER(z1, z5, z9, zd);
                z1 += z5; zd = Binary.RotL(zd ^ z1, 16);
                z9 += zd; z5 = Binary.RotL(z5 ^ z9, 12);
                z1 += z5; zd = Binary.RotL(zd ^ z1, 8);
                z9 += zd; z5 = Binary.RotL(z5 ^ z9, 7);
                // QUARTER(z2, z6, za, ze);
                z2 += z6; ze = Binary.RotL(ze ^ z2, 16);
                za += ze; z6 = Binary.RotL(z6 ^ za, 12);
                z2 += z6; ze = Binary.RotL(ze ^ z2, 8);
                za += ze; z6 = Binary.RotL(z6 ^ za, 7);
                // QUARTER(z3, z7, zb, zf);
                z3 += z7; zf = Binary.RotL(zf ^ z3, 16);
                zb += zf; z7 = Binary.RotL(z7 ^ zb, 12);
                z3 += z7; zf = Binary.RotL(zf ^ z3, 8);
                zb += zf; z7 = Binary.RotL(z7 ^ zb, 7);
                // QUARTER(z0, z5, za, zf);
                z0 += z5; zf = Binary.RotL(zf ^ z0, 16);
                za += zf; z5 = Binary.RotL(z5 ^ za, 12);
                z0 += z5; zf = Binary.RotL(zf ^ z0, 8);
                za += zf; z5 = Binary.RotL(z5 ^ za, 7);
                // QUARTER(z1, z6, zb, zc);
                z1 += z6; zc = Binary.RotL(zc ^ z1, 16);
                zb += zc; z6 = Binary.RotL(z6 ^ zb, 12);
                z1 += z6; zc = Binary.RotL(zc ^ z1, 8);
                zb += zc; z6 = Binary.RotL(z6 ^ zb, 7);
                // QUARTER(z2, z7, z8, zd);
                z2 += z7; zd = Binary.RotL(zd ^ z2, 16);
                z8 += zd; z7 = Binary.RotL(z7 ^ z8, 12);
                z2 += z7; zd = Binary.RotL(zd ^ z2, 8);
                z8 += zd; z7 = Binary.RotL(z7 ^ z8, 7);
                // QUARTER(z3, z4, z9, ze);
                z3 += z4; ze = Binary.RotL(ze ^ z3, 16);
                z9 += ze; z4 = Binary.RotL(z4 ^ z9, 12);
                z3 += z4; ze = Binary.RotL(ze ^ z3, 8);
                z9 += ze; z4 = Binary.RotL(z4 ^ z9, 7);
            }

            LittleEndian.Pack(z0, dst.Data, 0);
            LittleEndian.Pack(z1, dst.Data, 4);
            LittleEndian.Pack(z2, dst.Data, 8);
            LittleEndian.Pack(z3, dst.Data, 12);
            LittleEndian.Pack(z4, dst.Data, 16);
            LittleEndian.Pack(z5, dst.Data, 20);
            LittleEndian.Pack(z6, dst.Data, 24);
            LittleEndian.Pack(z7, dst.Data, 28);
            LittleEndian.Pack(z8, dst.Data, 32);
            LittleEndian.Pack(z9, dst.Data, 36);
            LittleEndian.Pack(za, dst.Data, 40);
            LittleEndian.Pack(zb, dst.Data, 44);
            LittleEndian.Pack(zc, dst.Data, 48);
            LittleEndian.Pack(zd, dst.Data, 52);
            LittleEndian.Pack(ze, dst.Data, 56);
            LittleEndian.Pack(zf, dst.Data, 60);
        }

        public void Decrypt(byte[] input, int input_pos, byte[] output, int output_pos, int length)
        {
            var state = new State ();
            var num_block = length / 64;
            var input_ptr = input_pos;
            var output_ptr = output_pos;

            for (int i = 0; i < num_block; i++)
            {
                TransformState (m_state, state);

                for (int j = 0; j < 64; j += 4)
                {
                    var val = LittleEndian.ToUInt32 (input, input_ptr+j) ^ (LittleEndian.ToUInt32 (m_state.Data, j) + LittleEndian.ToUInt32 (state.Data, j));
                    LittleEndian.Pack (val, output, output_ptr+j);
                }

                input_ptr += 64;
                output_ptr += 64;

                for (int k = 0; ;)
                {
                    if (++m_state.Data[48+k] != 0)
                        break;
                    k++;
                    if (k == 8)
                        break;
                }
            }

            var num_bytes_remaining = length & 63;

            if (num_bytes_remaining > 0)
            {
                TransformState (m_state, state);

                var temp = new byte[64];

                for (int i = 0; i < 64; i += 4)
                {
                    var val = LittleEndian.ToUInt32 (m_state.Data, i) + LittleEndian.ToUInt32 (state.Data, i);
                    LittleEndian.Pack (val, temp, i);
                }

                for (int i = 0; i < num_bytes_remaining; i++)
                {
                    output[output_ptr+i] = (byte)(input[input_ptr+i] ^ temp[i]);
                }
            }
        }
    }

    internal class HxIndexDeserializer
    {
        public static object Deserialize(Stream stream)
        {
            using (var reader = new BinaryReader(stream, Encoding.ASCII, true))
            {
                var obj = ReadObject(reader);
                Debug.Assert(stream.Position == stream.Length);
                return obj;
            }
        }

        static object ReadObject(BinaryReader reader)
        {
            var type = reader.ReadByte();

            switch (type)
            {
                case 0x00:
                {
                    return null;
                }
                case 0x01:
                {
                    return null;
                }
                case 0x02:
                {
                    return ReadString(reader);
                }
                case 0x03:
                {
                    return ReadByteArray(reader);
                }
                case 0x04:
                {
                    return ReadInt64(reader);
                }
                case 0x05:
                {
                    return ReadInt64(reader);
                }
                case 0x81:
                {
                    return ReadArray(reader);
                }
                case 0xC1:
                {
                    return ReadDictionary(reader);
                }
                default:
                {
                    throw new Exception("unknown object type");
                }
            }
        }

        static object ReadByteArray(BinaryReader reader)
        {
            var count = ReadInt32(reader);
            var array = reader.ReadBytes(count);
            return array;
        }

        static object ReadArray(BinaryReader reader)
        {
            var count = ReadInt32(reader);

            var array = new List<object>(count);

            for (int i = 0; i < count; i++)
            {
                var obj = ReadObject(reader);
                array.Add(obj);
            }

            return array.ToArray();
        }

        static object ReadDictionary(BinaryReader reader)
        {
            var count = ReadInt32(reader);

            var dictionary = new Dictionary<string, object>(count);

            for (int i = 0; i < count; i++)
            {
                var name = ReadString(reader);
                var obj = ReadObject(reader);
                dictionary.Add(name, obj);
            }

            return dictionary;
        }

        static int ReadInt32(BinaryReader reader)
        {
            return Binary.BigEndian(reader.ReadInt32());
        }

        static long ReadInt64(BinaryReader reader)
        {
            return Binary.BigEndian(reader.ReadInt64());
        }

        static string ReadString(BinaryReader reader)
        {
            var length = ReadInt32(reader);
            var buffer = reader.ReadBytes(sizeof(short) * length);
            return Encoding.Unicode.GetString(buffer);
        }
    }
}
