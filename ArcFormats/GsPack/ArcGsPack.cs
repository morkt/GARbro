//! \file       ArcGsPack.cs
//! \date       Thu Apr 16 14:55:01 2015
//! \brief      GsPack resource archives implementation.
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Gs
{
    internal class GsPackArchive : ArcFile
    {
        public GsPackArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir)
            : base (arc, impl, dir)
        {
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GsPack"; } }
        public override string Description { get { return "GsPack resource archive"; } }
        public override uint     Signature { get { return 0x61746144; } } // 'Data'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PakOpener ()
        {
            Extensions = new string[] { "pak", "dat", "pa_" };
            Signatures = new   uint[] { 0x61746144, 0x61507347 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!(file.View.AsciiEqual (0, "DataPack5") || 
                  file.View.AsciiEqual (0, "GsPack5") ||
                  file.View.AsciiEqual (0, "GsPack4")))
                return null;
            int version_minor = file.View.ReadUInt16 (0x30);
            int version_major = file.View.ReadUInt16 (0x32);
            uint index_size = file.View.ReadUInt32 (0x34);
            int count = file.View.ReadInt32 (0x3c);
            if (!IsSaneCount (count) || index_size > 0xffffff)
                return null;
            uint is_encrypted = file.View.ReadUInt32 (0x38);
            long data_offset = file.View.ReadUInt32 (0x40);
            int index_offset = file.View.ReadInt32 (0x44);
            int entry_size = version_major < 5 ? 0x48 : 0x68;
            int unpacked_size = count * entry_size;
            byte[] index;
            if (index_size != 0)
            {
                byte[] packed_index = file.View.ReadBytes (index_offset, index_size);
                if (index_size != packed_index.Length)
                    return null;
                if (0 != (is_encrypted & 1))
                    for (int i = 0; i != packed_index.Length; ++i)
                        packed_index[i] ^= (byte)i;
                using (var stream = new MemoryStream (packed_index))
                using (var reader = new LzssReader (stream, packed_index.Length, unpacked_size))
                {
                    reader.Unpack();
                    index = reader.Data;
                }
            }
            else
            {
                index = file.View.ReadBytes (index_offset, (uint)unpacked_size);
            }
            index_offset = 0;
            string default_type = "";
            var arc_name = Path.GetFileNameWithoutExtension (file.Name);
            if (arc_name.StartsWith ("image", StringComparison.OrdinalIgnoreCase))
                default_type = "image";
            else if (arc_name.StartsWith ("voice", StringComparison.OrdinalIgnoreCase))
                default_type = "audio";
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                string name = Binary.GetCString (index, index_offset, 0x40);
                if (0 != name.Length)
                {
                    var entry = new Entry {
                        Name = name,
                        Type = default_type,
                        Offset = data_offset + LittleEndian.ToUInt32 (index, index_offset+0x40),
                        Size = LittleEndian.ToUInt32 (index, index_offset+0x44),
                    };
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                index_offset += entry_size;
            }
            if (0 != (is_encrypted & 2))
                return new GsPackArchive (file, this, dir);

            if (string.IsNullOrEmpty (default_type))
            {
                foreach (var entry in dir)
                {
                    uint signature = file.View.ReadUInt32 (entry.Offset);
                    var res = AutoEntry.DetectFileType (signature);
                    entry.ChangeType (res);
                }
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var garc = arc as GsPackArchive;
            if (null == garc)
                return base.OpenEntry (arc, entry);
            var data = garc.File.View.ReadBytes (entry.Offset, entry.Size);
            DecryptData (data, entry.Name);
            return new BinMemoryStream (data, entry.Name);
        }

        void DecryptData (byte[] data, string key)
        {
            int numkey = 0;
            for (int i = 0; i < key.Length; ++i)
                numkey = numkey * 37 + (key[i] | 0x20);
            unsafe
            {
                fixed (byte* data8 = data)
                {
                    int* data32 = (int*)data8;
                    for (int count = data.Length / 4; count > 0; --count)
                        *data32++ ^= numkey;
                }
            }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GsData"; } }
        public override string Description { get { return "GsPack resource archive"; } }
        public override uint     Signature { get { return 0x59537347; } } // 'GsSY'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "GsSYMBOL5BINDATA"))
                return null;
            uint header_size = file.View.ReadUInt32 (0xa4);
            if (header_size < 0xd0)
                return null;
            int count = file.View.ReadInt32 (0xa8);
            uint index_offset = file.View.ReadUInt32 (0xb8);
            uint index_size = file.View.ReadUInt32 (0xbc);
            uint crypt_key = file.View.ReadUInt32 (0xc0);
            uint unpacked_index_size = file.View.ReadUInt32 (0xc4);
            if (count * 0x18 != unpacked_index_size)
                return null;
            uint data_offset = file.View.ReadUInt32 (0xc8);
            byte[] packed_index = new byte[index_size];
            if (index_size != file.View.Read (index_offset, packed_index, 0, index_size))
                return null;
            if (0 != crypt_key)
                for (int i = 0; i != packed_index.Length; ++i)
                    packed_index[i] ^= (byte)(i & crypt_key);
            using (var stream = new MemoryStream (packed_index))
            using (var reader = new LzssReader (stream, packed_index.Length, (int)unpacked_index_size))
            {
                reader.Unpack();
                var index = reader.Data;
                index_offset = 0;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var entry = new PackedEntry
                    {
                        Name = i.ToString ("D5"),
                        Offset = data_offset + LittleEndian.ToUInt32 (index, (int)index_offset),
                        Size = LittleEndian.ToUInt32 (index, (int)index_offset + 4),
                        UnpackedSize = LittleEndian.ToUInt32 (index, (int)index_offset + 8),
                    };
                    dir.Add (entry);
                    index_offset += 0x18;
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (0 == entry.Size)
                return Stream.Null;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (entry is PackedEntry)
                return new LzssStream (input);
            return input;
        }
    }

    [Export(typeof(ScriptFormat))]
    public class GsScriptFormat : GenericScriptFormat
    {
        public override string         Tag { get { return "SCW"; } }
        public override string Description { get { return "GsWin script file"; } }
        public override uint     Signature { get { return 0x20574353; } } // 'SCW '

        public GsScriptFormat ()
        {
            Signatures = new uint[] { 0x20574353, 0x35776353, 0x34776353 };
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class GswSysOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GswSys"; } }
        public override string Description { get { return "GswSys resource archive"; } }
        public override uint     Signature { get { return 0x53777347; } } // 'GswS'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public GswSysOpener ()
        {
            Extensions = new string[] { "pak" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "GswSys PACK 2.0"))
                return null;
            int index_size = file.View.ReadInt32 (0x10);
            int count = file.View.ReadInt32 (0x14);
            uint data_offset = file.View.ReadUInt32 (0x18);
            if (!IsSaneCount (count) || index_size <= 0)
                return null;
            long index_pos = 0x1C;
            if (index_pos + index_size > file.MaxOffset)
                return null;
            var packed_index = file.View.ReadBytes (index_pos, (uint)index_size);
            if (packed_index.Length != index_size)
                return null;
            for (int i = 0; i != packed_index.Length; ++i)
                packed_index[i] ^= (byte)i;
            byte[] index;
            using (var stream = new MemoryStream (packed_index))
            using (var reader = new LzssReader (stream, packed_index.Length, count * 0x28))
            {
                reader.Unpack ();
                index = reader.Data;
            }
            var dir = new List<Entry> (count);
            int offset = 0;
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = Binary.GetCString (index, offset, 0x20),
                    Offset = data_offset + LittleEndian.ToUInt32 (index, offset+0x20),
                    Size = LittleEndian.ToUInt32 (index, offset+0x24),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                offset += 0x28;
            }
            foreach (var entry in dir)
            {
                var res = AutoEntry.DetectFileType (file.View.ReadUInt32 (entry.Offset));
                if (null != res)
                    entry.ChangeType (res);
                else if (IsGswinImage (file, entry))
                    entry.ChangeType (ImageFormat.FindByTag ("GSWIN2"));
            }
            return new ArcFile (file, this, dir);
        }

        // GswSys 2.0 images carry no fixed signature; check the header fields instead.
        bool IsGswinImage (ArcView file, Entry entry)
        {
            if (entry.Size < 0x28)
                return false;
            uint packed  = file.View.ReadUInt32 (entry.Offset);
            uint width   = file.View.ReadUInt32 (entry.Offset+0x10);
            uint height  = file.View.ReadUInt32 (entry.Offset+0x14);
            int bpp      = file.View.ReadInt32 (entry.Offset+0x18);
            if (0 == width || 0 == height || width > 0x4000 || height > 0x4000)
                return false;
            if (8 != bpp && 24 != bpp && 32 != bpp)
                return false;
            long need = (long)width*height*(bpp/8) + (8 == bpp ? 0x400 : 0);
            long body = entry.Size - 0x28;
            if (0 == packed)
                return body == need;
            return packed <= body;
        }
    }
}
