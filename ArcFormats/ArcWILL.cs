//! \file       ArcWILL.cs
//! \date       Fri Oct 31 13:37:11 2014
//! \brief      ARC archive format implementation.
//
// Copyright (C) 2014 by morkt
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

using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.Will
{
    internal class ExtRecord
    {
        public string   Extension;
        public int      FileCount;
        public uint     DirOffset;
    }

    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC"; } }
        public override string Description { get { return "Will Co. game engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int ext_count = file.View.ReadInt32 (0);
            if (ext_count <= 0 || ext_count > 0xff)
                return null;

            uint dir_offset = 4;
            var ext_list = new List<ExtRecord> (ext_count);
            int file_count = 0;
            for (int i = 0; i < ext_count; ++i)
            {
                string ext = file.View.ReadString (dir_offset, 4).ToLowerInvariant();
                int count = file.View.ReadInt32 (dir_offset+4);
                uint offset = file.View.ReadUInt32 (dir_offset+8);
                if (count <= 0 || count > 0xffff || offset <= dir_offset)
                    return null;
                ext_list.Add (new ExtRecord { Extension = ext, FileCount = count, DirOffset = offset });
                file_count += count;
                dir_offset += 12;
            }
            var dir = ReadFileList (file, ext_list, 9);
            if (null == dir)
                dir = ReadFileList (file, ext_list, 13);
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }

        List<Entry> ReadFileList (ArcView file, IEnumerable<ExtRecord> ext_list, uint name_size)
        {
            var dir = new List<Entry>();
            foreach (var ext in ext_list)
            {
                uint dir_offset = ext.DirOffset;
                for (int i = 0; i < ext.FileCount; ++i)
                {
                    string name = file.View.ReadString (dir_offset, name_size).ToLowerInvariant()+'.'+ext.Extension;
                    var entry = FormatCatalog.Instance.CreateEntry (name);
                    entry.Size = file.View.ReadUInt32 (dir_offset+name_size);
                    entry.Offset = file.View.ReadUInt32 (dir_offset+name_size+4);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    dir_offset += name_size+8;
                }
            }
            return dir;
        }
    }
}
