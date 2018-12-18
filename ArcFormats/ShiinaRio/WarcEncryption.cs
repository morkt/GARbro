//! \file       WarcEncryption.cs
//! \date       Mon Aug 15 08:56:04 2016
//! \brief      ShiinaRio archives encryption.
//
// Copyright (C) 2015-2016 by morkt
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
using System.Runtime.InteropServices;
using GameRes.Utility;

namespace GameRes.Formats.ShiinaRio
{
    [Serializable]
    public class EncryptionScheme
    {
        public string Name          { get; set; }
        public string OriginalTitle { get; set; }
        public    int Version       { get; set; }
        public    int EntryNameSize;
        public byte[] CryptKey;
        public uint[] HelperKey;
        public byte[] Region;
        public byte[] DecodeBin;
        public IByteArray       ShiinaImage;
        public IDecryptExtra    ExtraCrypt;

        public static readonly EncryptionScheme Warc110 = new EncryptionScheme { EntryNameSize = 0x10 };
    }

    public interface IByteArray
    {
        int       Length { get; }
        byte this[int i] { get; }
    }

    public interface IDecryptExtra
    {
        void Decrypt (byte[] data, int index, uint length, uint flags);
        void Encrypt (byte[] data, int index, uint length, uint flags);
    }

    internal class Decoder
    {
        EncryptionScheme    m_scheme;

        public int   SchemeVersion { get { return m_scheme.Version; } }
        public int     WarcVersion { get; private set; }
        public uint MaxIndexLength { get; private set; }
        public int   EntryNameSize { get { return m_scheme.EntryNameSize; } }
        public IDecryptExtra ExtraCrypt { get { return m_scheme.ExtraCrypt; } }

        private uint          Rand { get; set; }

        public Decoder (int version, EncryptionScheme scheme)
        {
            m_scheme = scheme;
            WarcVersion = version;
            MaxIndexLength = GetMaxIndexLength (version);
        }

        public void Decrypt (byte[] data, int index, uint data_length)
        {
            DoEncryption (data, index, data_length, DecryptContent);
        }

        public void Encrypt (byte[] data, int index, uint data_length)
        {
            DoEncryption (data, index, data_length, EncryptContent);
        }

        public void DecryptIndex (uint index_offset, byte[] index)
        {
            Decrypt (index, 0, (uint)index.Length);
            XorIndex (index_offset, index);
        }

        public void EncryptIndex (uint index_offset, byte[] index)
        {
            XorIndex (index_offset, index);
            Encrypt (index, 0, (uint)index.Length);
        }

        void DecryptContent (int x, byte[] data, int index, uint length)
        {
            int n = 0;
            for (int i = 2; i < length; ++i)
            {
                byte d = data[index+i];
                if (WarcVersion > 120)
                    d ^= (byte)((double)NextRand() / 16777216.0);
                d = Binary.RotByteR (d, 1);
                d ^= (byte)(m_scheme.CryptKey[n++] ^ m_scheme.CryptKey[x]);
                data[index+i] = d;
                x = d % m_scheme.CryptKey.Length;
                if (n >= m_scheme.CryptKey.Length)
                    n = 0;
            }
        }

        void EncryptContent (int x, byte[] data, int index, uint length)
        {
            int n = 0;
            for (int i = 2; i < length; ++i)
            {
                byte k = (byte)(m_scheme.CryptKey[n++] ^ m_scheme.CryptKey[x]);
                byte d = data[index+i];
                x = d % m_scheme.CryptKey.Length;
                d ^= k;
                d = Binary.RotByteL (d, 1);
                if (WarcVersion > 120)
                    d ^= (byte)((double)NextRand() / 16777216.0);
                data[index+i] = d;
                if (n >= m_scheme.CryptKey.Length)
                    n = 0;
            }
        }

        delegate void ContentEncryptor (int start_key, byte[] data, int index, uint length);

        void DoEncryption (byte[] data, int index, uint data_length, ContentEncryptor encryptor)
        {
            if (data_length < 3 || WarcVersion < 120)
                return;
            uint effective_length = Math.Min (data_length, 1024u);
            int a, b;
            uint fac = 0;
            Rand = data_length;
            if (WarcVersion > 120)
            {
                a = (sbyte)data[index]   ^ (sbyte)data_length;
                b = (sbyte)data[index+1] ^ (sbyte)(data_length / 2);
                if (data_length != MaxIndexLength && (WarcVersion > 130 || m_scheme.Version > 2150))
                {
                    // ... regular entry decryption
                    int idx = (int)((double)NextRand() * (m_scheme.ShiinaImage.Length / 4294967296.0));
                    if (WarcVersion >= 160)
                    {
                        fac = Rand + m_scheme.ShiinaImage[idx];
                        fac = DecryptHelper3 (fac) & 0xfffffff;
                        if (effective_length > 0x80 && SchemeVersion > 2350)
                        {
                            DecryptHelper4 (data, index+4, m_scheme.HelperKey);
                            index += 0x80;
                            effective_length -= 0x80;
                        }
                    }
                    else if (150 == WarcVersion)
                    {
                        fac = Rand + m_scheme.ShiinaImage[idx];
                        fac ^= (fac & 0xfff) * (fac & 0xfff);
                        uint v = 0;
                        for (int i = 0; i < 32; ++i)
                        {
                            uint bit = fac & 1;
                            fac >>= 1;
                            if (0 != bit)
                                v += fac;
                        }
                        fac = v;
                    }
                    else if (140 == WarcVersion)
                    {
                        fac = m_scheme.ShiinaImage[idx];
                    }
                    else if (130 == WarcVersion)
                    {
                        fac = m_scheme.ShiinaImage[idx & 0xff];
                    }
                }
            }
            else
            {
                a = data[index];
                b = data[index+1];
            }
            Rand ^= (uint)(DecryptHelper1 (a) * 100000000.0);

            double token = 0.0;
            if (0 != (a|b))
            {
                token = Math.Acos ((double)a / Math.Sqrt ((double)(a*a + b*b)));
                token = token / Math.PI * 180.0;
            }
            if (b < 0)
                token = 360.0 - token;

            int x = (int)((fac + (byte)DecryptHelper2 (token)) % (uint)m_scheme.CryptKey.Length);
            encryptor (x, data, index, effective_length);
        }

        unsafe void XorIndex (uint index_offset, byte[] index)
        {
            fixed (byte* buf_raw = index)
            {
                uint* encoded = (uint*)buf_raw;
                for (int i = 0; i < index.Length/4; ++i)
                    encoded[i] ^= index_offset;
                if (WarcVersion >= 170)
                {
                    byte key = (byte)~WarcVersion;
                    for (int i = 0; i < index.Length; ++i)
                        buf_raw[i] ^= key;
                }
            }
        }

        public void Decrypt2 (byte[] data, int index, uint length)
        {
            if (length < 0x400 || null == m_scheme.DecodeBin)
                return;
            uint crc = Crc32Normal.UpdateCrc (0xFFFFFFFF, data, index, 0x100);
            index += 0x100;
            for (int i = 0; i < 0x40; ++i)
            {
                uint src = LittleEndian.ToUInt32 (data, index) & 0x1ffcu;
                src = LittleEndian.ToUInt32 (m_scheme.DecodeBin, (int)src);
                uint key = src ^ crc;
                data[index++ + 0x100] ^= (byte)key;
                data[index++ + 0x100] ^= (byte)(key >> 8);
                data[index++ + 0x100] ^= (byte)(key >> 16);
                data[index++ + 0x100] ^= (byte)(key >> 24);
            }
        }

        double DecryptHelper1 (double a)
        {
            if (a < 0)
                return -DecryptHelper1 (-a);

            double v0;
            double v1;
            if (a < 18.0)
            {
                v0 = a;
                v1 = a;
                double v2 = -(a * a);

                for (int j = 3; j < 1000; j += 2)
                {
                    v1 *= v2 / (j * (j - 1));
                    v0 += v1 / j;
                    if (v0 == v2)
                        break;
                }
                return v0;
            }

            int flags = 0;
            double v0_l = 0;
            v1 = 0;
            double div = 1 / a;
            double v1_h = 2.0;
            double v0_h = 2.0;
            double v1_l = 0;
            v0 = 0;
            int i = 0;
            
            do
            {
                v0 += div;
                div *= ++i / a;
                if (v0 < v0_h)
                    v0_h = v0;
                else
                    flags |= 1;

                v1 += div;
                div *= ++i / a;
                if (v1 < v1_h)
                    v1_h = v1;
                else
                    flags |= 2;

                v0 -= div;
                div *= ++i / a;
                if (v0 > v0_l)
                    v0_l = v0;
                else
                    flags |= 4;

                v1 -= div;
                div *= ++i / a;
                if (v1 > v1_l)
                    v1_l = v1;
                else
                    flags |= 8;
            }
            while (flags != 0xf);

            return ((Math.PI - Math.Cos(a) * (v0_l + v0_h)) - (Math.Sin(a) * (v1_l + v1_h))) / 2.0;
        }

        uint DecryptHelper2 (double a)
        {
            double v0, v1, v2, v3;

            if (a > 1.0)
            {
                v0 = Math.Sqrt (a * 2 - 1);	
                for (;;)
                {
                    v1 = 1 - (double)NextRand() / 4294967296.0;
                    v2 = 2.0 * (double)NextRand() / 4294967296.0 - 1.0;
                    if (v1 * v1 + v2 * v2 > 1.0)
                        continue;

                    v2 /= v1;
                    v3 = v2 * v0 + a - 1.0;
                    if (v3 <= 0)
                        continue;

                    v1 = (a - 1.0) * Math.Log (v3 / (a - 1.0)) - v2 * v0;
                    if (v1 < -50.0)
                        continue;

                    if (((double)NextRand() / 4294967296.0) <= (Math.Exp(v1) * (v2 * v2 + 1.0)))
                        break;
                }
            }
            else
            {
                v0 = Math.Exp(1.0) / (a + Math.Exp(1.0));
                do
                {
                    v1 = (double)NextRand() / 4294967296.0;
                    v2 = (double)NextRand() / 4294967296.0;
                    if (v1 < v0)
                    {
                        v3 = Math.Pow(v2, 1.0 / a);
                        v1 = Math.Exp(-v3);
                    } else
                    {
                        v3 = 1.0 - Math.Log(v2);
                        v1 = Math.Pow(v3, a - 1.0);
                    }
                }
                while ((double)NextRand() / 4294967296.0 >= v1);
            }

            if (WarcVersion > 120)
                return (uint)(v3 * 256.0);
            else
                return (byte)((double)NextRand() / 4294967296.0);	
        }

        [StructLayout(LayoutKind.Explicit)]
        struct Union
        {
            [FieldOffset(0)]
            public int i;
            [FieldOffset (0)]
            public uint u;
            [FieldOffset(0)]
            public float f;
            [FieldOffset(0)]
            public byte b0;
            [FieldOffset(1)]
            public byte b1;
            [FieldOffset(2)]
            public byte b2;
            [FieldOffset(3)]
            public byte b3;
        }

        uint DecryptHelper3 (uint key)
        {
            var p = new Union();
            p.u = key;
            var fv = new Union();
            fv.f = (float)(1.5 * (double)p.b0 + 0.1);
            uint v0 = Binary.BigEndian (fv.u);
            fv.f = (float)(1.5 * (double)p.b1 + 0.1);
            uint v1 = (uint)fv.f;
            fv.f = (float)(1.5 * (double)p.b2 + 0.1);
            uint v2 = (uint)-fv.i;
            fv.f = (float)(1.5 * (double)p.b3 + 0.1);
            uint v3 = ~fv.u;

            return ((v0 + v1) | (v2 - v3));
        }

        void DecryptHelper4 (byte[] data, int index, uint[] key_src)
        {
            uint[] buf = new uint[0x50];
            int i;
            for (i = 0; i < 0x10; ++i)
            {
                buf[i] = BigEndian.ToUInt32 (data, index+40+4*i);
            }
            for (; i < 0x50; ++i)
            {
                uint v = buf[i-16];
                v ^= buf[i-14];
                v ^= buf[i-8];
                v ^= buf[i-3];
                buf[i] = Binary.RotL (v, 1);
            }
            uint[] key = new uint[10];
            Array.Copy (key_src, key, 5);
            uint k0 = key[0];
            uint k1 = key[1];
            uint k2 = key[2];
            uint k3 = key[3];
            uint k4 = key[4];

            for (int buf_idx = 0; buf_idx < 0x50; ++buf_idx)
            {
                uint f, c;
                if (buf_idx < 0x10)
                {
                    f = k1 ^ k2 ^ k3;
                    c = 0;
                }
                else if (buf_idx < 0x20)
                {
                    f = k1 & k2 | k3 & ~k1;
                    c = 0x5A827999;
                }
                else if (buf_idx < 0x30)
                {
                    f = k3 ^ (k1 | ~k2);
                    c = 0x6ED9EBA1;
                }
                else if (buf_idx < 0x40)
                {
                    f = k1 & k3 | k2 & ~k3;
                    c = 0x8F1BBCDC;
                }
                else
                {
                    f = k1 ^ (k2 | ~k3);
                    c = 0xA953FD4E;
                }
                uint new_k0 = buf[buf_idx] + k4 + f + c + Binary.RotL (k0, 5);
                uint new_k2 = Binary.RotR (k1, 2);
                k1 = k0;
                k4 = k3;
                k3 = k2;
                k2 = new_k2;
                k0 = new_k0;
            }
            key[0] += k0;
            key[1] += k1;
            key[2] += k2;
            key[3] += k3;
            key[4] += k4;
            var ft = new FILETIME {
                DateTimeLow = key[1],
                DateTimeHigh = key[0] & 0x7FFFFFFF
            };
            var sys_time = new SYSTEMTIME (ft);
            key[5] = (uint)(sys_time.Year | sys_time.Month << 16);
            key[7] = (uint)(sys_time.Hour | sys_time.Minute << 16);
            key[8] = (uint)(sys_time.Second | sys_time.Milliseconds << 16);

            uint flags = LittleEndian.ToUInt32 (data, index+40) | 0x80000000;
            uint rgb = buf[1] >> 8; // BigEndian.ToUInt32 (data, index+44) >> 8;
            if (0 == (flags & 0x78000000))
                flags |= 0x98000000;
            key[6] = RegionCrc32 (m_scheme.Region, flags, rgb);
            key[9] = (uint)(((int)key[2] * (long)(int)key[3]) >> 8);
            if (m_scheme.Version >= 2390)
                key[6] += key[9];
            unsafe
            {
                fixed (byte* data_fixed = data)
                {
                    uint* encoded = (uint*)(data_fixed+index);
                    for (i = 0; i < 10; ++i)
                    {
                        encoded[i] ^= key[i];
                    }
                }
            }
        }

        static readonly uint[] CustomCrcTable = InitCrcTable();

        static uint[] InitCrcTable ()
        {
            var table = new uint[0x100];
            for (uint i = 0; i != 256; ++i)
            {
                uint poly = i;
                for (int j = 0; j < 8; ++j)
                {
                    uint bit = poly & 1;
                    poly = Binary.RotR (poly, 1);
                    if (0 == bit)
                        poly ^= 0x6DB88320;
                }
                table[i] = poly;
            }
            return table;
        }

        uint RegionCrc32 (byte[] src, uint flags, uint rgb)
        {
            int src_alpha = (int)flags & 0x1ff;
            int dst_alpha = (int)(flags >> 12) & 0x1ff;
            flags >>= 24;
            if (0 == (flags & 0x10))
                dst_alpha = 0;
            if (0 == (flags & 8))
                src_alpha = 0x100;
            int y_step = 0;
            int x_step = 4;
            int width = 48;
            int pos = 0;
            if (0 != (flags & 0x40)) // horizontal flip
            {
                y_step += width;
                pos += (width-1)*4;
                x_step = -x_step;
            }
            if (0 != (flags & 0x20)) // vertical flip
            {
                y_step -= width;
                pos += width*0x2f*4; // width*(height-1)*4;
            }
            y_step <<= 3;
            uint checksum = 0;
            for (int y = 0; y < 48; ++y)
            {
                for (int x = 0; x < 48; ++x)
                {
                    int alpha = src[pos+3] * src_alpha;
                    alpha >>= 8;
                    uint color = rgb;
                    for (int i = 0; i < 3; ++i)
                    {
                        int v = src[pos+i];
                        int c = (int)(color & 0xff); // rgb[i];
                        c -= v;
                        c = (c * dst_alpha) >> 8;
                        c = (c + v) & 0xff;
                        c = (c * alpha) >> 8;
                        checksum = (checksum >> 8) ^ CustomCrcTable[(c ^ checksum) & 0xff];
                        color >>= 8;
                    }
                    pos += x_step;
                }
                pos += y_step;
            }
            return checksum;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint DateTimeLow;
            public uint DateTimeHigh;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            [MarshalAs(UnmanagedType.U2)] public ushort Year;
            [MarshalAs(UnmanagedType.U2)] public ushort Month;
            [MarshalAs(UnmanagedType.U2)] public ushort DayOfWeek;
            [MarshalAs(UnmanagedType.U2)] public ushort Day;
            [MarshalAs(UnmanagedType.U2)] public ushort Hour;
            [MarshalAs(UnmanagedType.U2)] public ushort Minute;
            [MarshalAs(UnmanagedType.U2)] public ushort Second;
            [MarshalAs(UnmanagedType.U2)] public ushort Milliseconds;

            public SYSTEMTIME (FILETIME ft)
            {
                FileTimeToSystemTime (ref ft, out this);
            }

            [DllImport ("kernel32.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
            static extern bool FileTimeToSystemTime (ref FILETIME lpFileTime, out SYSTEMTIME lpSystemTime);
        }

        uint NextRand ()
        {
            Rand = 1566083941u * Rand + 1u;
            return Rand;
        }

        uint GetMaxIndexLength (int version)
        {
            int max_index_entries = version < 150 || SchemeVersion < 2310 ? 8192 : 16384;
            return (uint)((m_scheme.EntryNameSize + 0x18) * max_index_entries);
        }

        public static EncryptionScheme[] KnownSchemes = new EncryptionScheme[0];
    }

    [Serializable]
    public class ImageArray : IByteArray
    {
        private byte[]  m_common;
        private byte[]  m_extra;
        private int     m_common_length;

        public ImageArray (byte[] common) : this (common, common.Length, Array.Empty<byte>())
        {
        }

        public ImageArray (byte[] common, byte[] extra) : this (common, common.Length, extra)
        {
        }

        public ImageArray (byte[] common, int common_length, byte[] extra)
        {
            if (common_length > common.Length)
                throw new IndexOutOfRangeException();
            m_common = common;
            m_extra = extra;
            m_common_length = common_length;
        }

        public int Length { get { return m_common_length + m_extra.Length; } }

        public byte this[int i]
        {
            get
            {
                if (i < m_common_length)
                    return m_common[i];
                else
                    return m_extra[i - m_common_length];
            }
        }
    }

    [Serializable]
    public abstract class KeyDecryptBase : IDecryptExtra
    {
        protected readonly uint     Seed;
        protected readonly byte[]   DecodeTable;
        protected uint MinLength = 0x400;
        protected int PostDataOffset = 0x200;

        public KeyDecryptBase (uint seed, byte[] decode_bin)
        {
            Seed = seed;
            DecodeTable = decode_bin;
        }

        public void Decrypt (byte[] data, int index, uint length, uint flags)
        {
            if (length < MinLength)
                return;
            if ((flags & 0x202) == 0x202)
                DecryptPre (data, index, length);
            if ((flags & 0x204) == 0x204)
                DecryptPost (data, index, length);
        }

        public void Encrypt (byte[] data, int index, uint length, uint flags)
        {
            if (length < MinLength)
                return;
            if ((flags & 0x104) == 0x104)
                DecryptPost (data, index, length);
            if ((flags & 0x102) == 0x102)
                DecryptPre (data, index, length);
        }

        protected abstract void DecryptPre (byte[] data, int index, uint length);

        protected virtual void DecryptPost (byte[] data, int index, uint length)
        {
            int pos = index + PostDataOffset;
            data[pos  ] ^= (byte)Seed;
            data[pos+1] ^= (byte)(Seed >> 8);
            data[pos+2] ^= (byte)(Seed >> 16);
            data[pos+3] ^= (byte)(Seed >> 24);
        }
    }

    [Serializable]
    public abstract class KeyDecryptExtra : KeyDecryptBase
    {
        protected int EncryptedSize = 0xFF;

        public KeyDecryptExtra (uint seed, byte[] decode_bin) : base (seed, decode_bin)
        {
        }

        protected override void DecryptPre (byte[] data, int index, uint length)
        {
            var k = new uint[4];
            InitKey (Seed, k);
            for (int i = 0; i < EncryptedSize; ++i)
            {
                uint j = k[3] ^ (k[3] << 11) ^ k[0] ^ ((k[3] ^ (k[3] << 11) ^ (k[0] >> 11)) >> 8);
                k[3] = k[2];
                k[2] = k[1];
                k[1] = k[0];
                k[0] = j;
                data[index + i] ^= DecodeTable[j % DecodeTable.Length];
            }
        }

        protected abstract void InitKey (uint key, uint[] k);
    }

    [Serializable]
    public class ShojoMamaCrypt : KeyDecryptExtra
    {
        public ShojoMamaCrypt (uint key, byte[] bin) : base (key, bin)
        {
        }

        protected override void InitKey (uint key, uint[] k)
        {
            k[0] = key + 1;
            k[1] = key + 4;
            k[2] = key + 2;
            k[3] = key + 3;
        }
    }

    [Serializable]
    public class YuruPlusCrypt : KeyDecryptExtra
    {
        public YuruPlusCrypt (uint key, byte[] bin) : base (key, bin)
        {
            EncryptedSize = 0x100;
            PostDataOffset = 0x204;
        }

        protected override void InitKey (uint key, uint[] k)
        {
            k[0] = key + 4;
            k[1] = key + 3;
            k[2] = key + 2;
            k[3] = key + 1;
        }
    }

    [Serializable]
    public class TestamentCrypt : KeyDecryptExtra // Shinigami no Testament
    {
        public TestamentCrypt (uint key, byte[] bin) : base (key, bin)
        {
        }

        protected override void InitKey (uint key, uint[] k)
        {
            k[0] = key + 3;
            k[1] = key + 2;
            k[2] = key + 1;
            k[3] = key;
        }
    }

    [Serializable]
    public class MakiFesCrypt : KeyDecryptBase
    {
        public MakiFesCrypt (uint seed, byte[] key) : base (seed, key)
        {
        }

        protected override void DecryptPre (byte[] data, int index, uint length)
        {
            uint k = Seed;
            for (int i = 0; i < 0x100; ++i)
            {
                k = 0x343FD * k + 0x269EC3;
                data[index+i] ^= DecodeTable[((int)(k >> 16) & 0x7FFF) % DecodeTable.Length];
            }
        }
    }

    [Serializable]
    public class MajimeCrypt : IDecryptExtra
    {
        public void Decrypt (byte[] data, int index, uint length, uint flags)
        {
            if (length < 0x200)
                return;
            if ((flags & 0x202) == 0x202)
            {
                int sum = RotateBytesRight (data, index, 0x100);
                data[index + 0x104] ^= (byte)sum;
                data[index + 0x105] ^= (byte)(sum >> 8);
            }
        }

        public void Encrypt (byte[] data, int index, uint length, uint flags)
        {
            if (length < 0x200)
                return;
            if ((flags & 0x102) == 0x102)
            {
                int sum = RotateBytesLeft (data, index, 0x100);
                data[index + 0x104] ^= (byte)sum;
                data[index + 0x105] ^= (byte)(sum >> 8);
            }
        }

        internal int RotateBytesRight (byte[] data, int index, int length)
        {
            int sum = 0;
            int bit = 0;
            for (int i = 0; i < length; ++i)
            {
                byte v = data[index+i];
                sum += v >> 1;
                data[index+i] = (byte)(v >> 1 | bit);
                bit = v << 7;
            }
            data[index] |= (byte)bit;
            return sum;
        }

        internal int RotateBytesLeft (byte[] data, int index, int length)
        {
            int sum = 0;
            int bit = 0;
            for (int i = length-1; i >= 0; --i)
            {
                byte v = data[index+i];
                sum += v & 0x7F;
                data[index+i] = (byte)(v << 1 | bit);
                bit = v >> 7;
            }
            data[index + length-1] |= (byte)bit;
            return sum;
        }
    }

    [Serializable]
    public class NyaruCrypt : MajimeCrypt, IDecryptExtra
    {
        new public void Decrypt (byte[] data, int index, uint length, uint flags)
        {
            if (length < 0x200)
                return;
            if ((flags & 0x204) == 0x204)
            {
                int sum = RotateBytesRight (data, index, 0x100);
                data[index + 0x100] ^= (byte)sum;
                data[index + 0x101] ^= (byte)(sum >> 8);
            }
        }

        new public void Encrypt (byte[] data, int index, uint length, uint flags)
        {
            if (length < 0x200)
                return;
            if ((flags & 0x104) == 0x104)
            {
                int sum = RotateBytesLeft (data, index, 0x100);
                data[index + 0x100] ^= (byte)sum;
                data[index + 0x101] ^= (byte)(sum >> 8);
            }
        }
    }

    [Serializable]
    public class AlcotCrypt : IDecryptExtra
    {
        public void Decrypt (byte[] data, int index, uint length, uint flags)
        {
            if (length >= 0x400 && (flags & 0x204) == 0x204)
                Crc16Crypt (data, index, (int)length);
        }

        public void Encrypt (byte[] data, int index, uint length, uint flags)
        {
            if (length >= 0x400 && (flags & 0x104) == 0x104)
                Crc16Crypt (data, index, (int)length);
        }

        void Crc16Crypt (byte[] data, int index, int length)
        {
            var crc16 = new Crc16();
            crc16.Update (data, index, length & 0x7E | 1);
            var sum = crc16.Value ^ 0xFFFF;
            data[index + 0x104] ^= (byte)sum;
            data[index + 0x105] ^= (byte)(sum >> 8);
        }
    }

    [Serializable]
    public class DodakureCrypt : IDecryptExtra
    {
        public void Decrypt (byte[] data, int index, uint length, uint flags)
        {
            if (length < 0x200)
                return;
            if ((flags & 0x204) == 0x204)
            {
                if (0x718E958D == LittleEndian.ToUInt32 (data, index))
                {
                    var input = new byte[0x200];
                    Buffer.BlockCopy (data, index, input, 0, 0x200);
                    int remaining = LittleEndian.ToInt32 (input, 8);
                    int src = 12;
                    int dst = index;
                    bool rle = false;
                    while (remaining > 0)
                    {
                        int count = input[src++];
                        if (rle)
                        {
                            byte v = data[dst-1];
                            for (int i = 0; i < count; ++i)
                                data[dst++] = v;
                        }
                        else
                        {
                            Buffer.BlockCopy (input, src, data, dst, count);
                            src += count;
                            dst += count;
                        }
                        remaining -= count;
                        if (count < 0xFF)
                            rle = !rle;
                    }
                }
                if (length > 0x200)
                    data[index + 0x200] ^= (byte)length;
                if (length > 0x201)
                    data[index + 0x201] ^= (byte)(length >> 8);
                if (length > 0x202)
                    data[index + 0x202] ^= (byte)(length >> 16);
                if (length > 0x203)
                    data[index + 0x203] ^= (byte)(length >> 24);
            }
        }

        public void Encrypt (byte[] data, int index, uint length, uint flags)
        {
            if (length < 0x200)
                return;
            if ((flags & 0x104) == 0x104)
            {
                throw new NotImplementedException();
            }
        }
    }

    [Serializable]
    public class JokersCrypt : IDecryptExtra
    {
        public void Decrypt (byte[] data, int index, uint length, uint flags)
        {
            if (length < 0x400)
                return;
            if ((flags & 0x204) == 0x204)
            {
                if (0x718E958D == LittleEndian.ToUInt32 (data, index))
                {
                    var input = new byte[0x200];
                    Buffer.BlockCopy (data, index, input, 0, 0x200);
                    int remaining = LittleEndian.ToInt32 (input, 8);
                    int src = 12;
                    int dst = index;
                    var ranges_hi = new uint[0x100];
                    var ranges_lo = new uint[0x101];
                    for (int i = 0; i < 0x100; ++i)
                    {
                        uint v = input[src++];
                        ranges_hi[i] = v;
                        ranges_lo[i+1] = v + ranges_lo[i];
                    }
                    uint denominator = ranges_lo[0x100];
                    var symbol_table = new byte[denominator];
                    uint low, high;
                    for (int i = 0; i < 0x100; ++i)
                    {
                        low  = ranges_lo[i];
                        high = ranges_lo[i + 1];
                        int count = (int)(high - low);
                        for (int j = 0; j < count; ++j)
                            symbol_table[low + j] = (byte)i;
                    }
                    low  = 0;
                    high = 0xFFFFFFFF;
                    uint current = BigEndian.ToUInt32 (input, src);
                    src += 4;
                    for (int i = 0; i < remaining; ++i)
                    {
                        uint range = high / denominator;
                        byte symbol = symbol_table[(current - low) / range];
                        data[index+i] = symbol;
                        low += ranges_lo[symbol] * range;
                        high = ranges_hi[symbol] * range;
                        while (0 == ((low ^ (high + low)) & 0xFF000000u))
                        {
                            low <<= 8;
                            high <<= 8;
                            current <<= 8;
                            current |= input[src++];
                        }
                        while (high < 0x10000)
                        {
                            low <<= 8;
                            high = 0x1000000 - (low & 0xFFFF00);
                            current <<= 8;
                            current |= input[src++];
                        }
                    }
                }
                data[index + 0x200] ^= (byte)length;
                data[index + 0x201] ^= (byte)(length >> 8);
                data[index + 0x202] ^= (byte)(length >> 16);
                data[index + 0x203] ^= (byte)(length >> 24);
            }
        }

        public void Encrypt (byte[] data, int index, uint length, uint flags)
        {
            if (length < 0x400)
                return;
            if ((flags & 0x104) == 0x104)
            {
                throw new NotImplementedException();
            }
        }
    }

    [Serializable]
    public class KeyAdlerCrypt : KeyDecryptBase
    {
        public KeyAdlerCrypt (uint key) : base (key, null)
        {
        }

        protected override void DecryptPre (byte[] data, int index, uint length)
        {
            uint key = Adler32.Compute (data, index, 0x100);
            data[index + 0x204] ^= (byte)key;
            data[index + 0x205] ^= (byte)(key >> 8);
            data[index + 0x206] ^= (byte)(key >> 16);
            data[index + 0x207] ^= (byte)(key >> 24);
        }
    }

    [Serializable]
    public class AdlerCrypt
    {
        internal void Transform (byte[] data, int index, int length)
        {
            uint key = Adler32.Compute (data, index, length);
            data[index + 0x200] ^= (byte)key;
            data[index + 0x201] ^= (byte)(key >> 8);
            data[index + 0x202] ^= (byte)(key >> 16);
            data[index + 0x203] ^= (byte)(key >> 24);
        }
    }

    [Serializable]
    public class PostAdlerCrypt : AdlerCrypt, IDecryptExtra
    {
        public void Decrypt (byte[] data, int index, uint length, uint flags)
        {
            if (length >= 0x400 && (flags & 0x204) == 0x204)
                Transform (data, index, 0xFF);
        }

        public void Encrypt (byte[] data, int index, uint length, uint flags)
        {
            if (length >= 0x400 && (flags & 0x104) == 0x104)
                Transform (data, index, 0xFF);
        }
    }

    [Serializable]
    public class PreAdlerCrypt : AdlerCrypt, IDecryptExtra
    {
        public void Decrypt (byte[] data, int index, uint length, uint flags)
        {
            if (length >= 0x400 && (flags & 0x202) == 0x202)
                Transform (data, index, 0xFF);
        }

        public void Encrypt (byte[] data, int index, uint length, uint flags)
        {
            if (length >= 0x400 && (flags & 0x102) == 0x102)
                Transform (data, index, 0xFF);
        }
    }

    [Serializable]
    public class BinboCrypt : IDecryptExtra
    {
        public void Decrypt (byte[] data, int index, uint length, uint flags)
        {
            if (length < 0x200)
                return;
            if ((flags & 0x204) == 0x204)
            {
                if (0x718E958D == LittleEndian.ToUInt32 (data, index))
                {
                    var input = new byte[0x200];
                    Buffer.BlockCopy (data, index, input, 0, 0x200);
                    var reader = new LzComp (input, 8);
                    reader.Unpack (data, index);
                }
                if (length > 0x200)
                    data[index + 0x200] ^= (byte)length;
                if (length > 0x201)
                    data[index + 0x201] ^= (byte)(length >> 8);
                if (length > 0x202)
                    data[index + 0x202] ^= (byte)(length >> 16);
                if (length > 0x203)
                    data[index + 0x203] ^= (byte)(length >> 24);
            }
        }

        public void Encrypt (byte[] data, int index, uint length, uint flags)
        {
            if (length < 0x200)
                return;
            if ((flags & 0x104) == 0x104)
            {
                throw new NotImplementedException();
            }
        }

        sealed class LzComp
        {
            byte[]  m_input;
            int     m_src;

            uint    m_bits;
            int     m_bits_count;

            public LzComp (byte[] input, int index)
            {
                m_input = input;
                m_src = index;
            }

            public void Unpack (byte[] output, int dst)
            {
                FillBitCache();
                while (m_src < m_input.Length)
                {
                    if (GetBit() != 0)
                    {
                        output[dst++] = m_input[m_src++];
                        continue;
                    }
                    int count, offset;
                    if (GetBit() != 0)
                    {
                        count = LittleEndian.ToUInt16 (m_input, m_src);
                        m_src += 2;
                        offset = count >> 3 | -0x2000;
                        count &= 7;
                        if (count > 0)
                        {
                            count += 2;
                        }
                        else
                        {
                            count = m_input[m_src++];
                            if (0 == count)
                                break;
                            count += 9;
                        }
                    }
                    else
                    {
                        count = GetBit() << 1;
                        count |= GetBit();
                        count += 2;
                        offset = m_input[m_src++] | -0x100;
                    }
                    Binary.CopyOverlapped (output, dst+offset, dst, count);
                    dst += count;
                }
            }

            int GetBit ()
            {
                uint v = m_bits >> --m_bits_count;
                if (m_bits_count <= 0)
                {
                    FillBitCache();
                }
                return (int)(v & 1);
            }

            void FillBitCache ()
            {
                m_bits = LittleEndian.ToUInt32 (m_input, m_src);
                m_src += 4;
                m_bits_count = 32;
            }
        }
    }

    [Serializable]
    public class CountCrypt : IDecryptExtra
    {
        public void Decrypt (byte[] data, int index, uint length, uint flags)
        {
            if ((flags & 0x204) == 0x204)
                DoCountCrypt (data, index, (int)length);
        }

        public void Encrypt (byte[] data, int index, uint length, uint flags)
        {
            if ((flags & 0x104) == 0x104)
                DoCountCrypt (data, index, (int)length);
        }

        void DoCountCrypt (byte[] data, int index, int length)
        {
            if (length < 0x200)
                return;
            length = (length & 0x7E) | 1;
            byte count_00 = 0, count_FF = 0;
            for (int i = 0; i < length; ++i)
            {
                if (0xFF == data[index+i])
                    count_FF++;
                else if (0 == data[index+i])
                    count_00++;
            }
            data[index + 0x100] ^= count_00;
            data[index + 0x104] ^= count_FF;
        }
    }

    [Serializable]
    public class AltCountCrypt : IDecryptExtra
    {
        public void Decrypt (byte[] data, int index, uint length, uint flags)
        {
            if ((flags & 0x204) == 0x204)
                DoCountCrypt (data, index, (int)length);
        }

        public void Encrypt (byte[] data, int index, uint length, uint flags)
        {
            if ((flags & 0x104) == 0x104)
                DoCountCrypt (data, index, (int)length);
        }

        void DoCountCrypt (byte[] data, int index, int length)
        {
            if (length < 0x400)
                return;
            length = (length & 0x7E) | 1;
            byte count_00 = 0, count_FF = 0;
            for (int i = 0; i < length; ++i)
            {
                if (0xFF == data[index+i])
                    count_FF++;
                else if (0 == data[index+i])
                    count_00++;
            }
            data[index + 0x100] ^= count_FF;
            data[index + 0x104] ^= count_00;
        }
    }

    [Serializable]
    public class UshimitsuCrypt : IDecryptExtra
    {
        protected readonly uint m_key;

        public UshimitsuCrypt (uint key)
        {
            m_key = key;
        }

        public void Decrypt (byte[] data, int index, uint length, uint flags)
        {
            if ((flags & 0x204) == 0x204)
                DoCrypt (data, index, length);
        }

        public void Encrypt (byte[] data, int index, uint length, uint flags)
        {
            if ((flags & 0x104) == 0x104)
                DoCrypt (data, index, length);
        }

        unsafe void DoCrypt (byte[] data, int index, uint length)
        {
            if (length < 0x100)
                return;
            fixed (byte* data8 = &data[index])
            {
                uint* data32 = (uint*)data8;
                for (int i = 0; i < 0x40; ++i)
                {
                    data32[i] ^= m_key;
                }
            }
        }
    }
}
