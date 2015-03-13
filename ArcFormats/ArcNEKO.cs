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
        public override bool  IsHierarchic { get { return false; } }
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
            return new MemoryStream (data, false);
        }

        static ulong KeyFromHash (uint hash)
        {
            uint v2 = hash ^ (hash + 1566083941u);
            uint v3 = v2 ^ (hash - 899497514u);
            ulong result = v3 ^ (v2 - 1894007588u);
            return result | (result ^ (v3 + 1812433253u)) << 32;
        }

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
                        key = _m_paddw (key, v);
                        *first++ = v;
                    }
                    return key;
                }
            }
        }

        static ulong _m_paddw (ulong x, ulong y)
        {
            ulong mask = 0xffff;
            ulong r = ((x & mask) + (y & mask)) & mask;
            mask <<= 16;
            r |= ((x & mask) + (y & mask)) & mask;
            mask <<= 16;
            r |= ((x & mask) + (y & mask)) & mask;
            mask <<= 16;
            r |= ((x & mask) + (y & mask)) & mask;
            return r;
        }

        static byte[] ReadBlock (ArcView.Frame view, uint id, long offset, out int length)
        {
            uint hash = view.ReadUInt32 (offset);
            length = view.ReadInt32 (offset+4);

            int aligned_size = (length+7) & ~7;
            byte[] buffer = new byte[aligned_size];
            view.Read (offset+8, buffer, 0, (uint)length);
            if (0 != hash)
            {
                ulong key = KeyFromHash (hash);
                Decrypt (key, buffer, 0, aligned_size);
            }
            return buffer;
        }
    }
}
