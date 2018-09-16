//! \file       ArcYSK.cs
//! \date       2018 Apr 22
//! \brief      GPlay engine resource archive.
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using GameRes.Utility;

namespace GameRes.Formats.GPlay
{
    [Export(typeof(ArchiveFormat))]
    public class YskOpener : ArchiveFormat
    {
        public override string         Tag { get { return "YSK"; } }
        public override string Description { get { return "GPlay engine resource archive"; } }
        public override uint     Signature { get { return 0x36314141; } } // 'AA1640124080'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public YskOpener ()
        {
            // "AA1640124080", "AA7790743350"
            Signatures = new uint[] { 0x36314141, 0x39324141, 0x37374141, 0 };
        }

        const ulong DefaultKey = 0x1234567812345678ul;

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "AA"))
                return null;
            int count = file.View.ReadInt32 (12);
            if (!IsSaneCount (count))
                return null;
            var ver_str = file.View.ReadString (2, 10);
            if (!ver_str.All (char.IsDigit))
                return null;

            uint index_offset = 0x10;
            long data_offset = index_offset + count * 0x18;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x14);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = data_offset;
                entry.Size = file.View.ReadUInt32 (index_offset+0x14);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x18;
                data_offset += entry.Size;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Name.HasAnyOfExtensions ("TXT", "DAT"))
            {
                var input = arc.File.CreateStream (entry.Offset, entry.Size);
                var dec = new DesTransform (DefaultKey);
                return new InputCryptoStream (input, dec);
            }
            else if (entry.Name.HasExtension ("JPG"))
            {
                using (var dec = new DesTransform (DefaultKey))
                {
                    var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
                    for (int i = 0; i < data.Length; i += 0x1000)
                    {
                        int count = Math.Min (8, data.Length-i);
                        dec.TransformBlock (data, i, count, data, i);
                    }
                    return new BinMemoryStream (data, entry.Name);
                }
            }
            else if (entry.Name.HasExtension ("BMP"))
            {
                var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
                int bpp = data.ToUInt16 (0x1C);
                if (4 == bpp)
                    data[0x1C] = 8;
                else if (16 == bpp)
                    data[0x1C] = 24;
                int pixels_src = 0x36;
                int pixels_length = data.Length - pixels_src;
                if (pixels_length > 0x493AA)
                {
                    using (var dec = new DesTransform (DefaultKey))
                    {
                        for (int src = pixels_src + 0x493AA; src + 8 <= data.Length; src += 0xA0)
                        {
                            dec.TransformBlock (data, src, 8, data, src);
                        }
                    }
                }
                return new BinMemoryStream (data, entry.Name);
            }
            else
            {
                return base.OpenEntry (arc, entry);
            }
        }
    }

    public class DesTransform : ICryptoTransform
    {
        const int BlockSize = 8;

        public bool          CanReuseTransform { get { return true; } }
        public bool CanTransformMultipleBlocks { get { return true; } }
        public int              InputBlockSize { get { return BlockSize; } }
        public int             OutputBlockSize { get { return BlockSize; } }

        public DesTransform (ulong key)
        {
            SetKey (key);
        }

        byte[]  m_state1 = new byte[512]; // dword_461820
        ulong[] m_state0 = new ulong[16]; // dword_462020

        internal void SetKey (ulong key)
        {
            uint p = 0;
            uint q = 0;
            for (int i = 0; i < 28; ++i)
            {
                p >>= 1;
                q >>= 1;
                if (((key >> pShift0[i]) & 1) != 0)
                    p |= 0x8000000u;
                if (((key >> pShift1[i]) & 1) != 0)
                    q |= 0x8000000u;
            }
            for (int i = 0; i < 16; ++i)
            {
                uint p0 = (p | (p << 28)) >> pShift2[i];
                q = ((q | (p << 28)) >> pShift2[i]) & 0xFFFFFFF;
                p = p0 & 0xFFFFFFF;
                uint q0 = p | (q << 28);
                ulong x = (ulong)(q >> 4) << 32 | q0;
                ulong s = 0;
                for (int j = 0; j < 48; ++j)
                {
                    s >>= 1;
                    if (((x >> pShift3[j]) & 1) != 0)
                        s |= 0x800000000000ul;
                }
                m_state0[i] = s;
            }
            int idx4 = 0;
            for (int i = 0; i < 4; i += 2)
            {
                int idx3 = idx4;
                for (int j = 0; j < 16; )
                {
                    int idx2 = idx3;
                    for (int k = 2; k > 0; --k)
                    {
                        int idx1 = idx2;
                        for (int m = 2; m > 0; --m)
                        {
                            int idx = idx1 / 4;
                            for (int n = 2; n > 0; --n)
                            {
                                int src = j + 16 * i;
                                m_state1[0   + idx] = pState[st0[src]];
                                m_state1[64  + idx] = pState[st1[src]];
                                m_state1[128 + idx] = pState[st2[src]];
                                m_state1[192 + idx] = pState[st3[src]];
                                m_state1[256 + idx] = pState[st4[src]];
                                m_state1[320 + idx] = pState[st5[src]];
                                m_state1[384 + idx] = pState[st6[src]];
                                m_state1[448 + idx] = pState[st7[src]];
                                src = j + 16 * (i + 1);
                                m_state1[32 + idx]  = pState[st0[src]];
                                m_state1[96 + idx]  = pState[st1[src]];
                                m_state1[160 + idx] = pState[st2[src]];
                                m_state1[224 + idx] = pState[st3[src]];
                                m_state1[288 + idx] = pState[st4[src]];
                                m_state1[352 + idx] = pState[st5[src]];
                                m_state1[416 + idx] = pState[st6[src]];
                                m_state1[480 + idx] = pState[st7[src]];
                                idx += 16;
                                ++j;
                            }
                            idx1 += 32;
                        }
                        idx2 += 16;
                    }
                    idx3 += 8;
                }
                idx4 += 4;
            }
        }

        internal ulong TransformQWord (ulong q)
        {
            uint hi = 0;
            uint lo = 0;
            for (int i = 0; i < 32; ++i)
            {
                hi >>= 1;
                lo >>= 1;
                if (((q >> tShift0[i]) & 1) != 0)
                    hi |= 0x80000000;
                if (((q >> tShift1[i]) & 1) != 0)
                    lo |= 0x80000000;
            }
            uint t = lo;
            for (int i = 15; i >= 0; --i)
            {
                ulong val = 0;
                for (int j = 0; j < 48; ++j)
                {
                    val >>= 1;
                    if ((tMask0[j] & lo) != 0)
                        val |= 0x800000000000ul;
                }
                val ^= m_state0[i];
                byte n0 = (byte)val;
                byte n1 = (byte)(val >> 6);
                byte n2 = (byte)(val >> 12);
                byte n3 = (byte)(val >> 18);
                byte n4 = (byte)(val >> 24);
                byte n5 = (byte)(val >> 30);
                byte n6 = (byte)(val >> 36);
                byte n7 = (byte)(val >> 42);
                uint m = (uint)m_state1[(n0 & 0x3F) + 384]
                    | 16u * (m_state1[(n1 & 0x3F) + 64]
                    | 16u * (m_state1[(n2 & 0x3F)]
                    | 16u * (m_state1[(n3 & 0x3F) + 256]
                    | 16u * (m_state1[(n4 & 0x3F) + 128]
                    | 16u * (m_state1[(n5 & 0x3F) + 192]
                    | 16u * (m_state1[(n6 & 0x3F) + 448]
                    | 16u *  m_state1[(n7 & 0x3F) + 320]))))));
                uint x = 0;
                for (int j = 0; j < 32; ++j)
                {
                    x >>= 1;
                    if ((tMask1[j] & m) != 0)
                        x |= 0x80000000;
                }
                t = lo;
                lo = hi ^ x;
                hi = t;
            }
            ulong s = (ulong)t << 32 | lo;
            ulong r = 0;
            for (int j = 0; j < 64; ++j)
            {
                r >>= 1;
                if (((s >> tShift2[j]) & 1) != 0)
                    r |= 0x8000000000000000ul;
            }
            return r;
        }

        public int TransformBlock (byte[] inputBuffer, int inputOffset, int inputCount,
                                   byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; i += 8)
            {
                ulong q = 0;
                int count = Math.Min (8, inputCount - i);
                for (int j = 0; j < count; ++j)
                {
                    q |= (ulong)inputBuffer[inputOffset+j] << (j << 3);
                }
                q = TransformQWord (q);
                for (int j = 0; j < count; ++j)
                {
                    outputBuffer[outputOffset+j] = (byte)q;
                    q >>= 8;
                }
                inputOffset += 8;
                outputOffset += 8;
            }
            return inputCount;
        }

        public byte[] TransformFinalBlock (byte[] inputBuffer, int inputOffset, int inputCount)
        {
            byte[] outputBuffer = new byte[inputCount];
            TransformBlock (inputBuffer, inputOffset, inputCount, outputBuffer, 0);
            return outputBuffer;
        }

        public void Dispose ()
        {
        }

        static readonly byte[] pShift0 = {
            56, 48, 40, 32, 24, 16, 8, 0, 57, 49, 41, 33, 25, 17,
            9, 1, 58, 50, 42, 34, 26, 18, 10, 2, 59, 51, 43, 35
        };
        static readonly byte[] pShift1 = {
            62, 54, 46, 38, 30, 22, 14, 6, 61, 53, 45, 37, 29, 21,
            13, 5, 60, 52, 44, 36, 28, 20, 12, 4, 27, 19, 11, 3
        };
        static readonly byte[] pShift2 = { 1, 1, 2, 2, 2, 2, 2, 2, 1, 2, 2, 2, 2, 2, 2, 1 };
        static readonly byte[] pShift3 = {
            13, 16, 10, 23, 0, 4, 2, 27, 14, 5, 20, 9, 22, 18,
            11, 3, 25, 7, 15, 6, 26, 19, 12, 1, 40, 51, 30, 36,
            46, 54, 29, 39, 50, 44, 32, 47, 43, 48, 38, 55, 33,
            52, 45, 41, 49, 35, 28, 31
        };
        static readonly byte[] pState = { 0, 8, 4, 12, 2, 10, 6, 14, 1, 9, 5, 13, 3, 11, 7, 15 };
        static readonly byte[] st6 = {
            14, 4, 13, 1, 2, 15, 11, 8, 3, 10, 6, 12, 5, 9, 0, 7,
            0, 15, 7, 4, 14, 2, 13, 1, 10, 6, 12, 11, 9, 5, 3, 8,
            4, 1, 14, 8, 13, 6, 2, 11, 15, 12, 9, 7, 3, 10, 5, 0,
            15, 12, 8, 2, 4, 9, 1, 7, 5, 11, 3, 14, 10, 0, 6, 13,
        };
        static readonly byte[] st1 = {
            15, 1, 8, 14, 6, 11, 3, 4, 9, 7, 2, 13, 12, 0, 5, 10,
            3, 13, 4, 7, 15, 2, 8, 14, 12, 0, 1, 10, 6, 9, 11, 5,
            0, 14, 7, 11, 10, 4, 13, 1, 5, 8, 12, 6, 9, 3, 2, 15,
            13, 8, 10, 1, 3, 15, 4, 2, 11, 6, 7, 12, 0, 5, 14, 9,
        };
        static readonly byte[] st0 = {
            10, 0, 9, 14, 6, 3, 15, 5, 1, 13, 12, 7, 11, 4, 2, 8,
            13, 7, 0, 9, 3, 4, 6, 10, 2, 8, 5, 14, 12, 11, 15, 1,
            13, 6, 4, 9, 8, 15, 3, 0, 11, 1, 2, 12, 5, 10, 14, 7,
            1, 10, 13, 0, 6, 9, 8, 7, 4, 15, 14, 3, 11, 5, 2, 12,
        };
        static readonly byte[] st4 = {
            7, 13, 14, 3, 0, 6, 9, 10, 1, 2, 8, 5, 11, 12, 4, 15,
            13, 8, 11, 5, 6, 15, 0, 3, 4, 7, 2, 12, 1, 10, 14, 9,
            10, 6, 9, 0, 12, 11, 7, 13, 15, 1, 3, 14, 5, 2, 8, 4,
            3, 15, 0, 6, 10, 1, 13, 8, 9, 4, 5, 11, 12, 7, 2, 14,
        };
        static readonly byte[] st2 = {
            2, 12, 4, 1, 7, 10, 11, 6, 8, 5, 3, 15, 13, 0, 14, 9,
            14, 11, 2, 12, 4, 7, 13, 1, 5, 0, 15, 10, 3, 9, 8, 6,
            4, 2, 1, 11, 10, 13, 7, 8, 15, 9, 12, 5, 6, 3, 0, 14,
            11, 8, 12, 7, 1, 14, 2, 13, 6, 15, 0, 9, 10, 4, 5, 3,
        };
        static readonly byte[] st3 = {
            12, 1, 10, 15, 9, 2, 6, 8, 0, 13, 3, 4, 14, 7, 5, 11,
            10, 15, 4, 2, 7, 12, 9, 5, 6, 1, 13, 14, 0, 11, 3, 8,
            9, 14, 15, 5, 2, 8, 12, 3, 7, 0, 4, 10, 1, 13, 11, 6,
            4, 3, 2, 12, 9, 5, 15, 10, 11, 14, 1, 7, 6, 0, 8, 13,
        };
        static readonly byte[] st7 = {
            4, 11, 2, 14, 15, 0, 8, 13, 3, 12, 9, 7, 5, 10, 6, 1,
            13, 0, 11, 7, 4, 9, 1, 10, 14, 3, 5, 12, 2, 15, 8, 6,
            1, 4, 11, 13, 12, 3, 7, 14, 10, 15, 6, 8, 0, 5, 9, 2,
            6, 11, 13, 8, 1, 4, 10, 7, 9, 5, 0, 15, 14, 2, 3, 12,
        };
        static readonly byte[] st5 = {
            13, 2, 8, 4, 6, 15, 11, 1, 10, 9, 3, 14, 5, 0, 12, 7,
            1, 15, 13, 8, 10, 3, 7, 4, 12, 5, 6, 11, 0, 14, 9, 2,
            7, 11, 4, 1, 9, 12, 14, 2, 0, 6, 10, 13, 15, 3, 5, 8,
            2, 1, 14, 7, 4, 10, 8, 13, 15, 12, 9, 0, 3, 5, 6, 11,
        };
        static readonly byte[] tShift0 = {
            57, 49, 41, 33, 25, 17, 9, 1, 59, 51, 43, 35, 27, 19,
            11, 3, 61, 53, 45, 37, 29, 21, 13, 5, 63, 55, 47, 39,
            31, 23, 15, 7
        };
        static readonly byte[] tShift1 = {
            56, 48, 40, 32, 24, 16, 8, 0, 58, 50, 42, 34, 26, 18,
            10, 2, 60, 52, 44, 36, 28, 20, 12, 4, 62, 54, 46, 38,
            30, 22, 14, 6
        };
        static readonly byte[] tShift2 = {
            39, 7, 47, 15, 55, 23, 63, 31, 38, 6, 46, 14, 54, 22,
            62, 30, 37, 5, 45, 13, 53, 21, 61, 29, 36, 4, 44, 12,
            52, 20, 60, 28, 35, 3, 43, 11, 51, 19, 59, 27, 34, 2,
            42, 10, 50, 18, 58, 26, 33, 1, 41, 9, 49, 17, 57, 25,
            32, 0, 40, 8, 48, 16, 56, 24,
        };
        static readonly uint[] tMask0 = {
            0x80000000, 1, 2, 4, 8, 0x10, 8, 0x10, 0x20, 0x40, 0x80, 0x100,
            0x80, 0x100, 0x200, 0x400, 0x800, 0x1000, 0x800, 0x1000, 0x2000,
            0x4000, 0x8000, 0x10000, 0x8000, 0x10000, 0x20000, 0x40000,
            0x80000, 0x100000, 0x80000, 0x100000, 0x200000, 0x400000,
            0x800000, 0x1000000, 0x800000, 0x1000000, 0x2000000, 0x4000000,
            0x8000000, 0x10000000, 0x8000000, 0x10000000, 0x20000000,
            0x40000000, 0x80000000, 1
        };
        static readonly uint[] tMask1 = {
            0x8000, 0x40, 0x80000, 0x100000, 0x10000000, 0x800, 0x8000000,
            0x10000, 1, 0x4000, 0x400000, 0x2000000, 0x10, 0x20000, 0x40000000,
            0x200, 2, 0x80, 0x800000, 0x2000, 0x80000000, 0x4000000, 4,
            0x100, 0x40000, 0x1000, 0x20000000, 0x20, 0x200000, 0x400,
            8, 0x1000000
        };
    }
}
