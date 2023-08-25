//! \file       ArcNEKO2.cs
//! \date       2022 Jun 17
//! \brief      Nekopack archive format implementation.
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

using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;

namespace GameRes.Formats.Neko
{
    [Export(typeof(ArchiveFormat))]
    public class Pak2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "NEKOPACK/2"; } }
        public override string Description { get { return "NekoPack resource archive"; } }
        public override uint     Signature { get { return 0x4F4B454E; } } // "NEKO"
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public Pak2Opener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "PACK"))
                return null;

            uint init_key = file.View.ReadUInt32 (0xC);
            var xdec = new NekoXCode (init_key);
            uint seed = file.View.ReadUInt32 (0x10);
            var buffer = file.View.ReadBytes (0x14, 8);
            xdec.Decrypt (seed, buffer, 0, 8);

            uint index_size = LittleEndian.ToUInt32 (buffer, 0);
            if (index_size < 0x14 || index_size != LittleEndian.ToUInt32 (buffer, 4))
                return null;
            var index = new byte[(index_size + 7u) & ~7u];
            if (file.View.Read (0x1C, index, 0, index_size) < index_size)
                return null;
            xdec.Decrypt (seed, index, 0, index.Length);

            using (var reader = new IndexReader (file, xdec, index, (int)index_size))
            {
                var dir = reader.Parse (0x1C+index.Length);
                if (null == dir)
                    return null;
                reader.DetectTypes (dir, entry => {
                    uint key = file.View.ReadUInt32 (entry.Offset);
                    file.View.Read (entry.Offset+12, buffer, 0, 8);
                    xdec.Decrypt (key, buffer, 0, 8);
                    return LittleEndian.ToUInt32 (buffer, 0);
                });
                return new NekoArchive (file, this, dir, xdec);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var narc = arc as NekoArchive;
            if (null == narc || entry.Size <= 12)
                return base.OpenEntry (arc, entry);
            uint key = arc.File.View.ReadUInt32 (entry.Offset);
            var data = new byte[entry.Size];
            arc.File.View.Read (entry.Offset+4, data, 0, 8);
            narc.Decoder.Decrypt (key, data, 0, 8);
            int size = LittleEndian.ToInt32 (data, 0);
            if (size != LittleEndian.ToInt32 (data, 4))
            {
                Trace.WriteLine ("entry decryption failed", "[NEKOPACK]");
                return base.OpenEntry (arc, entry);
            }
            int aligned_size = (size + 7) & ~7;
            if (aligned_size > data.Length)
                data = new byte[aligned_size];
            arc.File.View.Read (entry.Offset+12, data, 0, (uint)size);
            narc.Decoder.Decrypt (key, data, 0, aligned_size);
            return new BinMemoryStream (data, 0, size, entry.Name);
        }
    }

    internal class NekoXCode : INekoFormat
    {
        uint            m_seed;
        uint[]          m_random;
        SimdProgram     m_program;

        public NekoXCode (uint init_key)
        {
            m_seed = init_key;
            m_random = InitTable (init_key);
            m_program = new SimdProgram (init_key);
        }

        public void Decrypt (uint key, byte[] input, int offset, int length)
        {
            for (int i = 1; i < 7; ++i)
            {
                uint src = key % 0x28 * 2;
                m_program.mm[i] = m_random[src] | (ulong)m_random[src+1] << 32;
                key /= 0x28;
            }
            m_program.Execute (input, offset, length);
        }

        public uint HashFromName (byte[] str, int offset, int length)
        {
            uint hash = m_seed;
            for (int i = 0; i < length; ++i)
            {
                hash = 0x100002A * (ShiftMap[str[offset+i] & 0xFF] ^ hash);
            }
            return hash;
        }

        public DirRecord ReadDir (IBinaryStream input)
        {
            uint hash = input.ReadUInt32();
            int count = input.ReadInt32();
            if (count != input.ReadInt32())
                throw new InvalidFormatException();
            return new DirRecord { Hash = hash, FileCount = count };
        }

        public long NextOffset (Entry entry)
        {
            return entry.Offset + entry.Size;
        }

        static readonly byte[] ShiftMap = {
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xc9, 0xca, 0x00, 0xcb, 0xcc, 0xcd, 0xce, 0xcf, 0xd0, 0xd1, 0x00, 0xd2, 0xd3, 0x27, 0x25, 0xc8,
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x00, 0xd4, 0x00, 0xd5, 0x00, 0x00,
            0xd6, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19,
            0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20, 0x21, 0x22, 0x23, 0x24, 0xd7, 0xc8, 0xd8, 0xd9, 0x26,
            0xda, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19,
            0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20, 0x21, 0x22, 0x23, 0x24, 0xdb, 0x00, 0xdc, 0xdd, 0x00,
            0x29, 0x2a, 0x2b, 0x2c, 0x2d, 0x2e, 0x2f, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38,
            0x39, 0x3a, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f, 0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
            0x49, 0x4a, 0x4b, 0x4c, 0x4d, 0x4e, 0x4f, 0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58,
            0x59, 0x5a, 0x5b, 0x5c, 0x5d, 0x5e, 0x5f, 0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
            0x69, 0x6a, 0x6b, 0x6c, 0x6d, 0x6e, 0x6f, 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78,
            0x79, 0x7a, 0x7b, 0x7c, 0x7d, 0x7e, 0x7f, 0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88,
            0x89, 0x8a, 0x8b, 0x8c, 0x8d, 0x8e, 0x8f, 0x90, 0x91, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98,
            0x99, 0x9a, 0x9b, 0x9c, 0x9d, 0x9e, 0x9f, 0xa0, 0xa1, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7, 0xa8,
        };

        class SimdProgram
        {
            public ulong[] mm = new ulong[7];

            Action[]    m_transform = new Action[4];
            Action[]    m_shuffle = new Action[6];

            Action<int>[]   TransformList;
            Action[]        ShuffleList;

            public SimdProgram (uint key)
            {
                TransformList = new Action<int>[] {
                    pxor, paddb, paddw, paddd, psubb, psubw, psubd,
                    pxor, psubb, psubw, psubd, paddb, paddw, paddd,
                };
                ShuffleList = new Action[] {
                    paddq_1_2, paddq_2_3, paddq_3_4, paddq_4_5, paddq_5_6, paddq_6_1,
                };

                GenerateProgram (key);
            }

            void pxor (int i) { mm[0] ^= mm[i]; }
            void paddb (int i) { mm[0] = MMX.PAddB (mm[0], mm[i]); }
            void paddw (int i) { mm[0] = MMX.PAddW (mm[0], mm[i]); }
            void paddd (int i) { mm[0] = MMX.PAddD (mm[0], mm[i]); }
            void psubb (int i) { mm[0] = MMX.PSubB (mm[0], mm[i]); }
            void psubw (int i) { mm[0] = MMX.PSubW (mm[0], mm[i]); }
            void psubd (int i) { mm[0] = MMX.PSubD (mm[0], mm[i]); }

            void paddq_1_2 () { mm[1] += mm[2]; }
            void paddq_2_3 () { mm[2] += mm[3]; }
            void paddq_3_4 () { mm[3] += mm[4]; }
            void paddq_4_5 () { mm[4] += mm[5]; }
            void paddq_5_6 () { mm[5] += mm[6]; }
            void paddq_6_1 () { mm[6] += mm[1]; }

            void GenerateProgram (uint key)
            {
                int t1 = 7 + (int)(key >> 28);
                int cmd_base = (int)key & 0xffff;
                int arg_base = (int)(key >> 16) & 0xfff;
                for (int i = 3; i >= 0; --i)
                {
                    int cmd = ((cmd_base >> (4 * i)) + t1) % TransformList.Length;
                    int arg = (arg_base >> (3 * i)) % 6 + 1;
                    m_transform[3-i] = () => TransformList[cmd] (arg);
                }
                for (uint i = 0; i < 6; ++i)
                {
                    m_shuffle[i] = ShuffleList[(i + key) % (uint)ShuffleList.Length];
                }
            }

            public unsafe void Execute (byte[] input, int offset, int length)
            {
                if (offset < 0 || offset > input.Length)
                    throw new ArgumentException ("offset");
                int count = Math.Min (length, input.Length-offset) / 8;
                if (0 == count)
                    return;
                fixed (byte* data = &input[offset])
                {
                    ulong* data64 = (ulong*)data;
                    for (;;)
                    {
                        mm[0] = *data64;
                        foreach (var cmd in m_transform)
                            cmd();
                        *data64++ = mm[0];
                        if (1 == count--)
                            break;
                        foreach (var cmd in m_shuffle)
                            cmd();
                    }
                }
            }
        }

        static uint[] InitTable (uint key)
        {
            uint a = 0;
            uint b = 0;
            do
            {
                a <<= 1;
                b ^= 1;
                a = ((a | b) << (int)(key & 1)) | b;
                key >>= 1;
            }
            while (0 == (a & 0x80000000));
            key = a << 1;
            a = key + Binary.BigEndian (key);
            byte count = (byte)key;
            do
            {
                b = key ^ a;
                a = (b << 4) ^ (b >> 4) ^ (b << 3) ^ (b >> 3) ^ b;
            }
            while (--count != 0);

            var table = new uint[154];
            for (int i = 0; i < table.Length; ++i)
            {
                b = key ^ a;
                a = (b << 4) ^ (b >> 4) ^ (b << 3) ^ (b >> 3) ^ b;
                table[i] = a;
            }
            return table;
        }
    }
}
