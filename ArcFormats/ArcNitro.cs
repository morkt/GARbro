//! \file       ArcNitro.cs
//! \date       Wed Feb 25 20:28:14 2015
//! \brief      Nitro+ PAK archives implementation.
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
using System.Text;
using GameRes.Utility;
using ZLibNet;

namespace GameRes.Formats.NitroPlus
{
    internal class PakEntry : Entry
    {
        public uint Key;
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "NITRO+/PAK"; } }
        public override string Description { get { return "Nitro+ resource archive"; } }
        public override uint     Signature { get { return 0x03; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public PakOpener ()
        {
            Extensions = new string[] { "pak" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            byte[] name_buf = new byte[0x100];
            if (0x110 > file.View.Reserve (4, 0x110))
                return null;

            uint size_xor = file.View.ReadUInt32 (0x104);
            if (0x64 != size_xor)
                return null;
            file.View.Read (4, name_buf, 0, 0x100);
            int name_len = 0;
            for (int i = 0; i < name_buf.Length; ++i)
            {
                if (0 == name_buf[i])
                    break;
                if (name_buf[i] >= 0x80 || name_buf[i] < 0x20)
                    return null;
                ++name_len;
            }
            if (0 == name_len || name_len > 0x10)
                return null;

            uint header_key = GetKey (name_buf, name_len);
            uint unpacked = file.View.ReadUInt32 (0x108) ^ header_key;
            int count = (int)(file.View.ReadUInt32 (0x10c) ^ header_key);
            if (count <= 0 || count > 0xfffff)
                return null;

            var dir = new List<Entry> (count);
            uint header_size = file.View.ReadUInt32 (0x110) ^ size_xor;
            long base_offset = 0x114 + header_size;
            var input = file.CreateStream (0x114, header_size);
            using (var header_stream = new ZLibStream (input, CompressionMode.Decompress))
            using (var header = new BinaryReader (header_stream, Encoding.ASCII, true))
            {
                for (int i = 0; i < count; ++i)
                {
                    name_len = header.ReadInt32();
                    if (name_len <= 0 || name_len > name_buf.Length)
                        return null;
                    if (name_len != header.Read (name_buf, 0, name_len))
                        return null;
                    uint key = GetKey (name_buf, name_len);
                    uint offset = header.ReadUInt32() ^ key;
                    uint size = header.ReadUInt32() ^ key;
                    uint val1 = header.ReadUInt32() ^ key;
                    uint val2 = header.ReadUInt32() ^ key;
                    uint val3 = header.ReadUInt32() ^ key;

                    var entry = new PakEntry {
                        Name        = Encodings.cp932.GetString (name_buf, 0, name_len),
                        Offset      = base_offset+offset,
                        Size        = size,
                        Key         = key,
                    };
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name);
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        static uint GetKey (byte[] name, int length)
        {
            int key = 0;
            for (int i = 0; i < length; ++i)
            {
                key *= 0x89;
                key += (sbyte)name[i];
            }
            return (uint)key;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pak_entry = entry as PakEntry;
            if (null == pak_entry)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            uint enc_size = Math.Min (entry.Size, 0x10u);
            if (0 == enc_size)
                return Stream.Null;
            var buf = new byte[enc_size];
            arc.File.View.Read (entry.Offset, buf, 0, enc_size);
            uint key = pak_entry.Key;
            for (int i = 0; i < buf.Length; ++i)
            {
                buf[i] ^= (byte)key;
                key = key >> 8 | key << 24;
            }
            if (enc_size == entry.Size)
                return new MemoryStream (buf, false);
            return new PrefixStream (buf, arc.File.CreateStream (entry.Offset+enc_size, entry.Size-enc_size));
        }
    }
}
