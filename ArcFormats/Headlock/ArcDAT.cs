//! \file       ArcDAT.cs
//! \date       2026-09-30
//! \brief      HEADLOCK engine resource archive.
//
// Copyright (C) 2026 by morkt
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
// Galaxy Angel EX / Princess Concerto (PC) "dataXX.dat" archives.
//
// A file is a sequence of blocks; block 0 is the index.  Every block starts with
// a 12-byte header -- unpacked size ^ 0x1f84c9af, packed size ^ 0x9ed835ab (zero
// when the payload is not LZSS-packed) and a checksum -- followed by the payload,
// which is LZSS-packed with a key derived from the checksum, XORed with that key,
// or stored as is.
//
// Index records hold the cp932 entry name and the address of the entry block
// relative to the end of the index block; two record layouts differ in the
// header and address field widths.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Headlock
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/HEADLOCK"; } }
        public override string Description { get { return "HEADLOCK engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        const uint XorUncomp = 0x1f84c9af;
        const uint XorComp   = 0x9ed835ab;

        static readonly Encoding Cp932 = Encoding.GetEncoding (932);
        static readonly Regex DatNameRe = new Regex (@"^data[0-9]+\.dat$", RegexOptions.IgnoreCase);

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!DatNameRe.IsMatch (Path.GetFileName (file.Name)))
                return null;
            var index = ReadBlock (file, 0, out long index_size);
            if (null == index)
                return null;
            long data_start = index_size;
            var dir = ParseLayout (index, file, data_start, 8, 4)
                   ?? ParseLayout (index, file, data_start, 12, 8);
            if (null == dir || 0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var data = ReadBlock (arc.File, entry.Offset, out _);
            if (null == data)
                return base.OpenEntry (arc, entry);
            return new BinMemoryStream (data, entry.Name);
        }

        // The index is a list of records holding the entry name length, the block
        // address and the cp932 name; two layouts exist that differ in the header
        // and address field widths.
        List<Entry> ParseLayout (byte[] index, ArcView file, long data_start, int header_size, int addr_offset)
        {
            var list = new List<Entry> ();
            int pos = 0;
            while (pos + header_size <= index.Length)
            {
                uint name_len = LittleEndian.ToUInt32 (index, pos);
                long addr     = LittleEndian.ToUInt32 (index, pos + addr_offset);
                int name_pos  = pos + header_size;
                if (0 == name_len || name_pos + name_len + 1 > index.Length)
                    return null;
                long block_offset = data_start + addr;
                uint unpacked, packed;
                if (!ReadBlockHeader (file, block_offset, out unpacked, out packed))
                    return null;
                long payload_size = 0 != packed ? packed : unpacked;
                if (block_offset + 12 + payload_size > file.MaxOffset)
                    return null;
                var name_bytes = new byte[name_len];
                Buffer.BlockCopy (index, name_pos, name_bytes, 0, (int)name_len);
                var name = Cp932.GetString (name_bytes).Replace ('\\', '/');
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                var entry = Create<Entry> (name);
                entry.Offset = block_offset;
                entry.Size   = unpacked;
                list.Add (entry);
                pos = name_pos + (int)name_len + 1;
            }
            return list;
        }

        // Block header: the unpacked and packed sizes are XOR-obfuscated; a nonzero
        // packed size with a zero checksum cannot describe a valid block.
        static bool ReadBlockHeader (ArcView file, long offset, out uint unpacked, out uint packed)
        {
            unpacked = packed = 0;
            if (offset + 12 > file.MaxOffset)
                return false;
            unpacked = file.View.ReadUInt32 (offset)     ^ XorUncomp;
            packed   = file.View.ReadUInt32 (offset + 4) ^ XorComp;
            if (unpacked > int.MaxValue || packed > int.MaxValue)
                return false;
            if (0 != packed && 0 == file.View.ReadUInt32 (offset + 8))
                return false;
            return true;
        }

        // Decode a block into an array; block_size receives the physical size of the
        // block (header + payload).  Returns null when the block is malformed.
        byte[] ReadBlock (ArcView file, long offset, out long block_size)
        {
            block_size = 0;
            uint unpacked, packed;
            if (!ReadBlockHeader (file, offset, out unpacked, out packed))
                return null;
            long payload_size = 0 != packed ? packed : unpacked;
            if (offset + 12 + payload_size > file.MaxOffset)
                return null;
            block_size = 12 + payload_size;
            uint checksum = file.View.ReadUInt32 (offset + 8);
            if (0 != packed)
            {
                byte key = CalcKey (checksum);
                var input = file.View.ReadBytes (offset + 12, packed);
                if (input.Length != packed)
                    return null;
                for (int i = 0; i < input.Length; ++i)
                    input[i] ^= key;
                using (var mem = new MemoryStream (input))
                using (var lzss = new LzssReader (mem, input.Length, (int)unpacked))
                {
                    lzss.Unpack ();
                    return lzss.Data;
                }
            }
            var data = file.View.ReadBytes (offset + 12, (uint)payload_size);
            if (data.Length != payload_size)
                return null;
            if (0 != checksum)
            {
                byte key = CalcKey (checksum);
                for (int i = 0; i < data.Length; ++i)
                    data[i] ^= key;
            }
            return data;
        }

        static byte CalcKey (uint checksum)
        {
            int sum = (int)(checksum & 0xFF)
                    + (int)((checksum >> 8)  & 0xFF)
                    + (int)((checksum >> 16) & 0xFF)
                    + (int)((checksum >> 24) & 0xFF);
            byte key = (byte)(sum & 0xFF);
            return 0 == key ? (byte)0xAA : key;
        }
    }
}