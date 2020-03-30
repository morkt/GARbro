//! \file       ArcPCK.cs
//! \date       2020 Mar 29
//! \brief      Strikes resource archive.
//
// Copyright (C) 2020 by morkt
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Strikes
{
    internal class PckEntry : PackedEntry
    {
        public bool IsEncrypted { get; set; }
    }

    [Export(typeof(ArchiveFormat))]
    public class PckOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PCK/AVG"; } }
        public override string Description { get { return "Strikes resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public PckOpener ()
        {
            ContainedFormats = new[] { "LAG", "BMP", "OGG", "WAV", "TXT" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!VFS.IsPathEqualsToFileName (file.Name, "AVGDatas.pck"))
                return null;
            int seed = Binary.BigEndian (file.View.ReadInt32 (file.MaxOffset - 104));
            var header = file.View.ReadBytes (file.MaxOffset - 100, 100);
            var rnd = new RandomGenerator();
            rnd.Init (seed);
            rnd.Decrypt (header, 0, header.Length);

            uint checksum = (uint)(header[1] | header[2] << 8 | header[0] << 16 | header[3] << 24) ^ 0xDEFD32D3;
            uint length = BigEndian.ToUInt32 (header, 24);
            if (checksum != length)
                return null;

            uint idx_pos = BigEndian.ToUInt32 (header, 28);
            uint idx_size = Binary.BigEndian (file.View.ReadUInt32 (idx_pos));

            uint index_size;
            var index = ReadChunk (file, 8, idx_pos + 4, out index_size);
            if (index_size >= 0x80000000)
            {
                index_size &= 0x7FFFFFFF;
                var unpacked = new byte[idx_size];
                LzssUnpack (index, index.Length, unpacked);
                index = unpacked;
            }
            using (var input = new BinMemoryStream (index))
            {
                var dir = new List<Entry>();
                int dir_count = 0;
                while (input.PeekByte() != -1)
                {
                    input.ReadInt32();
                    input.ReadInt32();
                    input.ReadInt32();
                    int count = Binary.BigEndian (input.ReadInt32());
                    var dir_name = dir_count.ToString ("X4");
                    for (int i = 0; i < count; ++i)
                    {
                        var name = input.ReadCString (0x28);
                        name = Path.Combine (dir_name, name);
                        var entry = Create<PckEntry> (name);
                        entry.Offset = Binary.BigEndian (input.ReadUInt32());
                        entry.Size   = Binary.BigEndian (input.ReadUInt32());
                        entry.IsEncrypted = input.ReadInt32() != 0;
                        entry.UnpackedSize = input.ReadUInt32();
                        entry.IsPacked = entry.Size != entry.UnpackedSize;
                        if (!entry.CheckPlacement (file.MaxOffset))
                            return null;
                        dir.Add (entry);
                    }
                    ++dir_count;
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PckEntry;
            if (null == pent || !(pent.IsEncrypted || pent.IsPacked))
                return base.OpenEntry (arc, entry);
            arc.File.View.Reserve (pent.Offset, pent.Size);
            int skip_size = 8;
            do
            {
                uint test_size = Binary.BigEndian (arc.File.View.ReadUInt32 (pent.Offset + skip_size * 4));
                if (test_size + 4 == pent.Size)
                    break;
            }
            while (--skip_size > 0);
            byte[] data;
            uint data_size = pent.Size;
            if (0 == skip_size)
            {
                data = arc.File.View.ReadBytes (pent.Offset, pent.Size);
            }
            else
            {
                data = ReadChunk (arc.File, skip_size, pent.Offset, out data_size);
            }
            if (pent.IsEncrypted)
            {
                if (data_size >= 0x10)
                    DecryptData (data, 0x10, 0xC53A9A6C);
                else
                    DecryptData (data, data.Length, 0x6C9A3AC5);
            }
            Stream input = new BinMemoryStream (data, pent.Name);
            if (pent.IsPacked)
                input = new LzssStream (input);
            return input;
        }

        void DecryptData (byte[] data, int length, uint key)
        {
            for (int i = 0; i < length; ++i)
            {
                data[i] ^= (byte)(key >> ((i & 3) << 3));
            }
        }

        byte[] ReadChunk (ArcView file, int skipSize, long offset, out uint chunkSize)
        {
            skipSize *= 4;
            var header = file.View.ReadUInt32 (offset);
            chunkSize = Binary.BigEndian (file.View.ReadUInt32 (offset + skipSize));
            uint size = chunkSize & 0x7FFFFFFF;
            var chunk = file.View.ReadBytes (offset + 4, size);
            if (skipSize > 0)
            {
                System.Buffer.BlockCopy (chunk, 0, chunk, 4, skipSize - 4);
                LittleEndian.Pack (header, chunk, 0);
            }
            return chunk;
        }

        static internal int LzssUnpack (byte[] input, int in_length, byte[] output)
        {
            var frame = new byte[0x1000];
            int frame_pos = 0xFEE;
            int src = 0;
            int dst = 0;
            while (src < in_length)
            {
                int ctl = input[src++];
                for (int bit = 1; bit != 0x100; bit <<= 1)
                {
                    if (0 != (ctl & bit))
                    {
                        if (src >= in_length)
                            return dst;
                        byte b = input[src++];
                        frame[frame_pos++ & 0xFFF] = b;
                        output[dst++] = b;
                    }
                    else
                    {
                        if (src + 2 > in_length)
                            return dst;
                        int lo = input[src++];
                        int hi = input[src++];
                        int offset = (hi & 0xF0) << 4 | lo;
                        int count = Math.Min (3 + (hi & 0xF), output.Length - dst);
                        while (count --> 0)
                        {
                            byte b = frame[offset++ & 0xFFF];
                            frame[frame_pos++ & 0xFFF] = b;
                            output[dst++] = b;
                        }
                    }
                }
            }
            return dst;
        }
    }

    internal class RandomGenerator
    {
        int         m_count;
        int[]       m_state = new int[56];

        public void Init (int seed)
        {
            int n = 1;
            m_state[55] = seed;
            for (int i = 1; i <= 54; ++i)
            {
                int pos = 21 * i % 55;
                m_state[pos] = n;

                n = seed - n;
                if (n < 0)
                    n += 1000000000;
                seed = m_state[pos];
            }
            Shuffle();
            Shuffle();
            Shuffle();
            m_count = 55;
        }

        public uint Rand ()
        {
            if (++m_count > 55)
            {
                Shuffle();
                m_count = 1;
            }
            return (uint)m_state[m_count];
        }

        private void Shuffle ()
        {
            for (int i = 1; i <= 24; ++i)
            {
                m_state[i] = m_state[i] - m_state[i+31];
                if (m_state[i] < 0)
                    m_state[i] += 1000000000;
            }
            for (int i = 25; i <= 55; ++i)
            {
                m_state[i] = m_state[i] - m_state[i-24];
                if (m_state[i] < 0)
                    m_state[i] += 1000000000;
            }
        }

        public void Decrypt (byte[] input, int src, int length)
        {
            for (int i = src; i < length; i += 4)
            {
                uint key = Rand();
                input[i  ] ^= (byte)(key >> 24);
                input[i+1] ^= (byte)(key >> 16);
                input[i+2] ^= (byte)(key >> 8);
                input[i+3] ^= (byte)(key);
            }
        }
    }
}
