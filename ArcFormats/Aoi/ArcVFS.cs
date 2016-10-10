//! \file       ArcVFS.cs
//! \date       Sun Jan 17 07:41:37 2016
//! \brief      SofthouseChara resource archive implementation.
//
// Copyright (C) 2016 by morkt
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

namespace GameRes.Formats.Aoi
{
    [Export(typeof(ArchiveFormat))]
    public class VfsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "VFS/AOI"; } }
        public override string Description { get { return "Aoi engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public VfsOpener ()
        {
            Extensions = new string[] { "vfs" };
            Signatures = new uint[] { 0x01014656, 0x02004656, 0 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int signature = file.View.ReadInt16 (0);
            if (0x4656 != signature && 0x4C56 != signature)
                return null;
            int version = file.View.ReadInt16 (2);
            int count = file.View.ReadInt16 (4);
            if (!IsSaneCount (count))
                return null;
            int entry_size = file.View.ReadInt16 (6);
            int index_size = file.View.ReadInt32 (8);
            if (entry_size <= 0 || index_size <= 0 || file.MaxOffset != file.View.ReadUInt32 (0xC))
                return null;
            if (version >= 0x0200)
                return OpenV2 (file, count, entry_size, index_size);

            int index_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x13);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x13);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x17);
                entry.UnpackedSize = file.View.ReadUInt32 (index_offset+0x1B);
                entry.IsPacked     = 0 != file.View.ReadByte (index_offset+0x1F);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += entry_size;
            }
            return new ArcFile (file, this, dir);
        }

        ArcFile OpenV2 (ArcView file, int count, int entry_size, int index_size)
        {
            int index_offset = 0x10;
            int filenames_offset = index_offset + entry_size * count;
            int filenames_length = file.View.ReadInt32 (filenames_offset);
            char[] filenames;
            using (var fn_stream = file.CreateStream (filenames_offset+8, (uint)filenames_length*2))
            using (var fn_reader = new BinaryReader (fn_stream, Encoding.Unicode))
                filenames = fn_reader.ReadChars (filenames_length);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int name_offset = file.View.ReadInt32 (index_offset);
                if (name_offset < 0 || name_offset >= filenames.Length)
                    return null;
                var name = GetName (filenames, name_offset);
                if (0 == name.Length)
                    return null;
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0xA);
                entry.Size   = file.View.ReadUInt32 (index_offset+0xE);
                entry.UnpackedSize = file.View.ReadUInt32 (index_offset+0x12);
                entry.IsPacked     = 0 != file.View.ReadByte (index_offset+0x16);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += entry_size;
            }
            return new ArcFile (file, this, dir);
        }

        static string GetName (char[] names, int begin)
        {
            int end = Array.IndexOf (names, '\0', begin);
            if (-1 == end)
                end = names.Length;
            return new string (names, begin, end-begin);
        }
    }
}
