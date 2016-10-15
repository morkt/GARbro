//! \file       ArcNitro.cs
//! \date       Wed Feb 25 20:28:14 2015
//! \brief      Nitro+ PAK archives implementation.
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.NitroPlus
{
    internal class PakEntry : PackedEntry
    {
        public uint Key;
    }

    internal class NitroPak : ArcFile
    {
        public int Version;

        public NitroPak (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, int version)
            : base (arc, impl, dir)
        {
            Version = version;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/NITRO+"; } }
        public override string Description { get { return "Nitro+ resource archive"; } }
        public override uint     Signature { get { return 0x03; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public PakOpener ()
        {
            Extensions = new string[] { "pak" };
            Signatures = new uint[] { 2, 3 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadInt32 (0);
            List<Entry> dir = null;
            if (2 == version)
                dir = OpenPakV2 (file);
            else if (3 == version)
                dir = OpenPakV3 (file);
            if (null == dir)
                return null;
            return new NitroPak (file, this, dir, version);
        }

        private List<Entry> OpenPakV2 (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            int unpacked_size = file.View.ReadInt32 (8);
            uint packed_size = file.View.ReadUInt32 (0xC);
            using (var input = file.CreateStream (0x114, packed_size))
            using (var header_stream = new ZLibStream (input, CompressionMode.Decompress))
            using (var header = new BinaryReader (header_stream, Encoding.ASCII, true))
            {
                long base_offset = 0x114 + packed_size;
                var name_buf = new byte[0x40];
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    int name_length = header.ReadInt32();
                    if (name_length <= 0)
                        return null;
                    if (name_length > name_buf.Length)
                        name_buf = new byte[name_length];
                    if (name_length != header.Read (name_buf, 0, name_length))
                        return null;
                    var name = Encodings.cp932.GetString (name_buf, 0, name_length);
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (name);

                    entry.Offset        = base_offset + header.ReadUInt32();
                    entry.UnpackedSize  = header.ReadUInt32();
                    entry.Size          = header.ReadUInt32();
                    entry.IsPacked      = header.ReadInt32() != 0;
                    uint psize          = header.ReadUInt32();
                    if (entry.IsPacked)
                        entry.Size = psize;

                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return dir;
            }
        }

        private List<Entry> OpenPakV3 (ArcView file)
        {
            if (0x110 > file.View.Reserve (4, 0x110))
                return null;

            uint size_xor = file.View.ReadUInt32 (0x104);
            if (0x64 != size_xor)
                return null;
            byte[] name_buf = file.View.ReadBytes (4, 0x100);
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
            if (!IsSaneCount (count))
                return null;

            var dir = new List<Entry> (count);
            uint header_size = file.View.ReadUInt32 (0x110) ^ size_xor;
            long base_offset = 0x114 + header_size;
            using (var input = file.CreateStream (0x114, header_size))
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
                    var name = Encodings.cp932.GetString (name_buf, 0, name_len);
                    var entry = FormatCatalog.Instance.Create<PakEntry> (name);
                    entry.Offset        = (header.ReadUInt32() ^ key) + base_offset;
                    entry.UnpackedSize  = (header.ReadUInt32() ^ key);
                    uint ignored        = (header.ReadUInt32() ^ key);
                    entry.IsPacked      = (header.ReadUInt32() ^ key) != 0;
                    uint packed_size    = (header.ReadUInt32() ^ key);
                    entry.Key           = key;
                    entry.Size          = entry.IsPacked ? packed_size : entry.UnpackedSize;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return dir;
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
            if (pak_entry != null && !pak_entry.IsPacked)
                return OpenV3Entry (arc, pak_entry);

            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            var packed_entry = entry as PackedEntry;
            if (packed_entry != null && packed_entry.IsPacked)
                input = new ZLibStream (input, CompressionMode.Decompress);
            return input;
        }

        private Stream OpenV3Entry (ArcFile arc, PakEntry entry)
        {
            uint enc_size = Math.Min (entry.Size, 0x10u);
            if (0 == enc_size)
                return Stream.Null;
            var buf = arc.File.View.ReadBytes (entry.Offset, enc_size);
            uint key = entry.Key;
            for (int i = 0; i < buf.Length; ++i)
            {
                buf[i] ^= (byte)key;
                key = Binary.RotR (key, 8);
            }
            if (enc_size == entry.Size)
                return new MemoryStream (buf);
            return new PrefixStream (buf, arc.File.CreateStream (entry.Offset+enc_size, entry.Size-enc_size));
        }
    }
}
