//! \file       ChainReactionCrypt.cs
//! \date       Mon Mar 07 15:59:47 2016
//! \brief      KiriKiri XP3 ecryption filter used in some games.
//
// Copyright (C) 2016-2018 by morkt
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
using System.ComponentModel;
using System.IO;
using System.Linq;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.KiriKiri
{
    // this encryption scheme encrypts first N bytes of file, where N varies depending on file's hash, and
    // those variations are stored within "plugin/list.bin" file.  by default N=512 (used when hash is not
    // found within "list.bin").
    //
    // this implementation looks for "list.bin" upon archive open, parses it and remembers encryption
    // threshold values in a dictionary.
    //
    // such implementation has some flaws, for one, it would fail if "list.bin" is stored within archive other
    // than one being opened.

    [Serializable]
    public class ChainReactionCrypt : ICrypt
    {
        readonly string     m_list_bin;

        public ChainReactionCrypt () : this ("plugin/list.bin")
        {
        }

        public ChainReactionCrypt (string list_file)
        {
            m_list_bin = list_file;
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            uint limit = GetEncryptionLimit (entry);
            if (offset >= limit)
                return;
            count = Math.Min ((int)(limit - offset), count);
            uint key = entry.Hash;
            int ofs = (int)offset;
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] ^= (byte)((ofs+i) ^ (byte)(key >> (((ofs+i) & 3) << 3)));
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            throw new NotImplementedException (Strings.arcStrings.MsgEncNotImplemented);
            // despite the fact that algorithm is symmetric, creating an archive without updating "list.bin"
            // wouldn't make much sense
//            Decrypt (entry, offset, values, pos, count);
        }

        protected virtual uint GetEncryptionLimit (Xp3Entry entry)
        {
            uint limit;
            if (EncryptionThresholdMap != null && EncryptionThresholdMap.TryGetValue (entry.Hash, out limit))
                return limit;
            else
                return 0x200;
        }

        [NonSerialized]
        Dictionary<uint, uint> EncryptionThresholdMap;

        public override void Init (ArcFile arc)
        {
            var bin = ReadListBin (arc);
            if (null == bin || bin.Length <= 0x30)
                return;

            if (!Binary.AsciiEqual (bin, "\"\x0D\x0A"))
            {
                for (int i = 0; i < 3; ++i)
                {
                    bin = DecodeListBin (bin);
                    if (null == bin)
                        return;
                }
            }
            if (null == EncryptionThresholdMap)
                EncryptionThresholdMap = new Dictionary<uint, uint>();
            else
                EncryptionThresholdMap.Clear();

            ParseListBin (bin);
        }

        internal byte[] ReadListBin (ArcFile arc)
        {
            var list_bin = arc.Dir.FirstOrDefault (e => e.Name == m_list_bin) as Xp3Entry;
            if (null == list_bin)
                return null;
            var bin = new byte[list_bin.UnpackedSize];
            using (var input = arc.OpenEntry (list_bin))
                input.Read (bin, 0, bin.Length);
            return bin;
        }

        void ParseListBin (byte[] data)
        {
            using (var mem = new MemoryStream (data))
            using (var input = new StreamReader (mem))
            {
                var converter = new UInt32Converter();
                string line;
                while ((line = input.ReadLine()) != null)
                {
                    if (0 == line.Length || '0' != line[0])
                        continue;
                    var pair = line.Split (',');
                    if (pair.Length > 1)
                    {
                        uint hash = (uint)converter.ConvertFromString (pair[0]);
                        uint threshold = (uint)converter.ConvertFromString (pair[1]);
                        EncryptionThresholdMap[hash] = threshold;
                    }
                }
            }
        }

        static byte[] DecodeListBin (byte[] data)
        {
            var header = new byte[0x30];
            DecodeDPD (data, 0, 0x30, header);
            int packed_size = LittleEndian.ToInt32 (header, 0x0C);
            int unpacked_size = LittleEndian.ToInt32 (header, 0x10);
            if (packed_size <= 0 || packed_size > data.Length-0x30)
                return null;
            if (Binary.AsciiEqual (header, 0, "DPDC"))
            {
                var decrypted = new byte[packed_size];
                DecodeDPD (data, 0x30, packed_size, decrypted);
                return decrypted;
            }
            if (Binary.AsciiEqual (header, 0, "SZLC")) // LZSS
            {
                using (var input = new MemoryStream (data, 0x30, packed_size))
                using (var lzss = new LzssReader (input, packed_size, unpacked_size))
                {
                    lzss.Unpack();
                    return lzss.Data;
                }
            }
            if (Binary.AsciiEqual (header, 0, "ELRC")) // RLE
            {
                var unpacked = new byte[unpacked_size];
                int min_repeat = LittleEndian.ToInt32 (header, 0x1C);
                DecodeRLE (data, 0x30, packed_size, unpacked, min_repeat);
                return unpacked;
            }
            return null;
        }

        static void DecodeRLE (byte[] input, int offset, int length, byte[] output, int min_repeat)
        {
            int src = offset;
            int src_end = offset+length;
            int dst = 0;
            while (src < src_end)
            {
                byte b = input[src++];
                int repeat = 1;
                while (repeat < min_repeat && src < src_end && input[src] == b)
                {
                    ++repeat;
                    ++src;
                }
                if (repeat == min_repeat)
                {
                    byte ctl = input[src++];
                    if (ctl > 0x7F)
                        repeat += input[src++] + ((ctl & 0x7F) << 8) + 0x80;
                    else
                        repeat += ctl;
                }
                for (int i = 0; i < repeat; ++i)
                    output[dst++] = b;
            }
        }

        unsafe static void DecodeDPD (byte[] src, int offset, int length, byte[] dst)
        {
            if (offset > src.Length || length > dst.Length || length > src.Length - offset)
                throw new IndexOutOfRangeException();
            if (length < 8)
                return;
            int tail = length & 3;
            if (tail != 0)
                Buffer.BlockCopy (src, offset+length-tail, dst, length-tail, tail);
            length /= 4;
            fixed (byte* src8 = &src[offset], dst8 = dst)
            {
                uint* src32 = (uint*)src8;
                uint* dst32 = (uint*)dst8;
                for (int i = 0; i < length-1; ++i)
                {
                    dst32[i] = src32[i] ^ src32[i+1];
                }
                dst32[length-1] = dst32[0] ^ src32[length-1];
            }
        }
    }

    [Serializable]
    public class HachukanoCrypt : ChainReactionCrypt
    {
        public HachukanoCrypt () : base ("plugins/list.txt")
        {
            StartupTjsNotEncrypted = true;
        }

        protected override uint GetEncryptionLimit (Xp3Entry entry)
        {
            uint limit = base.GetEncryptionLimit (entry);
            switch (limit)
            {
            case 0: return 0;
            case 1: return 0x100;
            case 2: return 0x200;
            case 3: return entry.UnpackedSize;
            default: return limit;
            }
        }
    }

    [Serializable]
    public class ChocolatCrypt : ChainReactionCrypt
    {
        public ChocolatCrypt () : base ("plugins/list.txt")
        {
            StartupTjsNotEncrypted = true;
        }

        protected override uint GetEncryptionLimit (Xp3Entry entry)
        {
            uint limit = base.GetEncryptionLimit (entry);
            switch (limit)
            {
            case 0: return 0;
            case 2: return entry.UnpackedSize;
            default: return 0x100;
            }
        }
    }
}
