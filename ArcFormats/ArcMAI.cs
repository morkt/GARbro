//! \file       ArcMAI.cs
//! \date       Sun May 03 09:26:58 2015
//! \brief      MAI archive format implementation.
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

namespace GameRes.Formats.MAI
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MAI"; } }
        public override string Description { get { return "MAI resource archive"; } }
        public override uint     Signature { get { return 0x0a49414d; } } // 'MAI\x0a'
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "arc" };
        }

        internal class DirEntry
        {
            public string Name;
            public int    Index;
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint file_size = file.View.ReadUInt32 (4);
            if (file_size != file.MaxOffset)
                return null;
            int count = file.View.ReadInt32 (8);
            if (count <= 0 || count > 0xfffff)
                return null;
            int dir_level = file.View.ReadByte (0x0d);
            int dir_entries = file.View.ReadUInt16 (0x0e);
            uint index_offset = 0x10;
            uint index_size = (uint)(count * 0x18 + dir_entries * 8);
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            List<DirEntry> folders = null;
            if (0 != dir_entries && 2 == dir_level)
            {
                folders = new List<DirEntry> (dir_entries);
                uint dir_offset = index_offset + (uint)count*0x18;
                for (int i = 0; i < dir_entries; ++i)
                {
                    folders.Add (new DirEntry {
                        Name = file.View.ReadString (dir_offset, 4),
                        Index = file.View.ReadInt32 (dir_offset+4)
                    });
                    dir_offset += 8;
                }
            }
            bool is_mask_arc = "mask.arc" == Path.GetFileName (file.Name).ToLowerInvariant();
            var dir = new List<Entry> (count);
            int next_folder = null == folders ? count : folders[0].Index;
            int folder = 0;
            string current_folder = "";
            for (int i = 0; i < count; ++i)
            {
                while (i >= next_folder && folder < folders.Count)
                {
                    current_folder = folders[folder++].Name;
                    if (folders.Count == folder)
                        next_folder = count;
                    else
                        next_folder = folders[folder].Index;
                }
                string name = file.View.ReadString (index_offset, 0x10);
                if (0 == name.Length)
                    return null;
                var offset = file.View.ReadUInt32 (index_offset+0x10);
                var entry = new AutoEntry (Path.Combine (current_folder, name), () => {
                    uint signature = file.View.ReadUInt32 (offset);
                    IEnumerable<IResource> res;
                    if (is_mask_arc)
                        res = FormatCatalog.Instance.ImageFormats.Where (x => x.Tag == "MSK/MAI");
                    else if (0x4d43 == (signature & 0xffff)) // 'CM'
                        res = FormatCatalog.Instance.ImageFormats.Where (x => x.Tag == "CMP/MAI");
                    else if (0x4d41 == (signature & 0xffff)) // 'AM'
                        res = FormatCatalog.Instance.ImageFormats.Where (x => x.Tag == "AM/MAI");
                    else if (0x4d42 == (signature & 0xffff)) // 'BM'
                        res = FormatCatalog.Instance.ImageFormats.Where (x => x.Tag == "BMP");
                    else
                        res = FormatCatalog.Instance.LookupSignature (signature);
                    return res.FirstOrDefault();
                });
                entry.Offset = offset;
                entry.Size = file.View.ReadUInt32 (index_offset+0x14);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x18;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
