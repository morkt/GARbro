//! \file       ArcNEKO.cs
//! \date       Fri Mar 13 02:27:53 2015
//! \brief      Nekopack archive format implementation.
//
// Copyright (C) 2015 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Neko
{
    public class NekoArchive : ArcFile
    {
        public uint Key { get; private set; }

        public NekoArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, uint key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "NEKO"; } }
        public override string Description { get { return "NekoPack resource archive"; } }
        public override uint     Signature { get { return 0x4f4b454e; } } // "NEKO"
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public PakOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "PACK"))
                return null;
            uint id = file.View.ReadUInt32 (8);
            int length;
            byte[] index = ReadBlock (file.View, id, 0x10, out length);
            int total = 0;
            long offset = 0x18 + length;
            var dir = new List<Entry>();
            int index_pos = 0;
            for (int remaining = length; remaining > 0;)
            {
                int count = LittleEndian.ToInt32 (index, index_pos+4);
                if (count <= 0 || count > remaining/8-1)
                    return null;
                uint hash = LittleEndian.ToUInt32 (index, index_pos);
                total += count;
                dir.Capacity = total;
                index_pos += 8;
                for (int j = 0; j < count; ++j)
                {
                    var entry = new Entry
                        { Offset = offset, Size = LittleEndian.ToUInt32 (index, index_pos+4) };
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    index_pos += 8;
                    offset += entry.Size + 8;
                    dir.Add (entry);
                }
                remaining -= 8 + count * 8;
            }
            int i = 0;
            byte[] buffer = new byte[0x10];
            foreach (var entry in dir)
            {
                file.View.Read (entry.Offset, buffer, 0, 0x10);
                uint hash = LittleEndian.ToUInt32 (buffer, 0);
                if (0 != hash)
                {
                    ulong key = KeyFromHash (hash);
                    Decrypt (key, buffer, 8, 8);
                }
                uint signature = LittleEndian.ToUInt32 (buffer, 8);
                var res = FormatCatalog.Instance.LookupSignature (signature);
                string ext = "";
                if (res.Any())
                {
                    ext = res.First().Extensions.First();
                    entry.Type = res.First().Type;
                }
                else if (0x474e4d8a == signature)
                    ext = "mng";
                if (!string.IsNullOrEmpty (ext))
                    entry.Name = string.Format ("{0:D4}.{1}", i, ext);
                else
                    entry.Name = i.ToString ("D4");
                ++i;
            }
            return new NekoArchive (file, this, dir, id);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pak = arc as NekoArchive;
            if (null == pak)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            int length;
            var data = ReadBlock (arc.File.View, pak.Key, entry.Offset, out length);
            return new MemoryStream (data, 0, length, false);
        }

        static ulong KeyFromHash (uint hash)
        {
            uint v2 = hash ^ (hash + 1566083941u);
            uint v3 = v2 ^ (hash - 899497514u);
            ulong result = v3 ^ (v2 - 1894007588u);
            return result | (result ^ (v3 + 1812433253u)) << 32;
        }

        static uint HashFromString (uint seed, byte[] str, int offset, int length)
        {
            // 0x00000000, "NEKOPACK" -> 0xAC0BF0B1
            // 0xAC0BF0B1, "RBS0011"  -> 0x9B75ADA7
            uint result = seed;
            for (int i = 0; i < length; ++i)
            {
                byte c = str[offset+i];
                result += c + result * 37;
            }
            return result;
        }

        static uint HashFromName (uint seed, byte[] str, int offset, int length)
        {
            // 0x9B75ADA7, "script"    -> 0x0DDB021E
            // 0x9B75ADA7, "start.bin" -> 0xCB8FB53B
            uint result = seed;
            for (int i = 0; i < length; ++i)
            {
                byte c = str[offset+i];
                result = 81 * (ShiftMap[c] ^ result);
            }
            return result;
        }

        static readonly byte[] ShiftMap = new byte[]
        {
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x38, 0x2F, 0x33, 0x3C, 0x40, 0x3B, 0x2A, 0x2E, 0x31, 0x30, 0x26, 0x44, 0x35, 0x28, 0x3E, 0x12,
            0x02, 0x22, 0x06, 0x20, 0x1A, 0x1C, 0x0F, 0x11, 0x18, 0x17, 0x42, 0x2B, 0x3A, 0x37, 0x34, 0x0C,
            0x41, 0x08, 0x1D, 0x07, 0x15, 0x21, 0x05, 0x1E, 0x0A, 0x14, 0x0E, 0x10, 0x09, 0x27, 0x1F, 0x0B,
            0x23, 0x16, 0x0D, 0x01, 0x25, 0x04, 0x1B, 0x03, 0x13, 0x24, 0x19, 0x2D, 0x12, 0x29, 0x32, 0x3F,
            0x3D, 0x08, 0x1D, 0x07, 0x15, 0x21, 0x05, 0x1E, 0x0A, 0x14, 0x0E, 0x10, 0x09, 0x27, 0x1F, 0x0B,
            0x23, 0x16, 0x0D, 0x01, 0x25, 0x04, 0x1B, 0x03, 0x13, 0x24, 0x19, 0x2C, 0x39, 0x43, 0x36, 0x00,
            0x4B, 0xA9, 0xA7, 0xAF, 0x50, 0x52, 0x91, 0x9F, 0x47, 0x6B, 0x96, 0xAB, 0x87, 0xB5, 0x9B, 0xBB,
            0x99, 0xA4, 0xBF, 0x5C, 0xC6, 0x9C, 0xC2, 0xC4, 0xB6, 0x4F, 0xB8, 0xC1, 0x85, 0xA8, 0x51, 0x7E,
            0x5F, 0x82, 0x73, 0xC7, 0x90, 0x4E, 0x45, 0xA5, 0x7A, 0x63, 0x70, 0xB3, 0x79, 0x83, 0x60, 0x55,
            0x5B, 0x5E, 0x68, 0xBA, 0x53, 0xA1, 0x67, 0x97, 0xAC, 0x71, 0x81, 0x59, 0x64, 0x7C, 0x9D, 0xBD,
            0x9D, 0xBD, 0x95, 0xA0, 0xB2, 0xC0, 0x6F, 0x6A, 0x54, 0xB9, 0x6D, 0x88, 0x77, 0x48, 0x5D, 0x72,
            0x49, 0x93, 0x57, 0x65, 0xBE, 0x4A, 0x80, 0xA2, 0x5A, 0x98, 0xA6, 0x62, 0x7F, 0x84, 0x75, 0xBC,
            0xAD, 0xB1, 0x6E, 0x76, 0x8B, 0x9E, 0x8C, 0x61, 0x69, 0x8D, 0xB4, 0x78, 0xAA, 0xAE, 0x8F, 0xC3,
            0x58, 0xC5, 0x74, 0xB7, 0x8E, 0x7D, 0x89, 0x8A, 0x56, 0x4D, 0x86, 0x94, 0x9A, 0x4C, 0x92, 0xB0,
        };

        static uint CalcParity (uint a1, uint a2)
        {
            uint v1 = (a2 ^ ((a2 ^ ((a2 ^ ((a2 ^ a1) + 1566083941u)) - 899497514u)) - 1894007588u)) + 1812433253u;
            int v2 = (int)(((a2 ^ ((a2 ^ a1) + 1566083941u)) - 899497514u) >> 27);
            return v1 << v2 | v1 >> (32-v2);
        }

        static ulong Decrypt (ulong key, byte[] buf, int offset, int length)
        {
            unsafe
            {
                fixed (byte* data = buf)
                {
                    ulong* first = (ulong*)(data + offset);
                    ulong* last = first + length/8;
                    while (first != last)
                    {
                        ulong v = *first ^ key;
                        key = MMX.PAddW (key, v);
                        *first++ = v;
                    }
                    return key;
                }
            }
        }

        static byte[] ReadBlock (ArcView.Frame view, uint id, long offset, out int length)
        {
            uint hash = view.ReadUInt32 (offset);
            length = view.ReadInt32 (offset+4);
            // parity check
//            if (CalcParity (id, (uint)length) != hash)
//                throw new InvalidFormatException();

            int aligned_size = (length+7) & ~7;
            byte[] buffer = new byte[aligned_size];
            length = view.Read (offset+8, buffer, 0, (uint)length);
            if (0 != hash)
            {
                ulong key = KeyFromHash (hash);
                Decrypt (key, buffer, 0, aligned_size);
            }
            return buffer;
        }
    }
}
