//! \file       ArcBIN.cs
//! \date       Tue Dec 08 21:45:54 2015
//! \brief      Favorite View Point v2 resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;

namespace GameRes.Formats.FVP
{
    [Export(typeof(ArchiveFormat))]
    public class Bin2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "BIN/FVP"; } }
        public override string Description { get { return "Favorite View Point resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Bin2Opener ()
        {
            Extensions = new string[] { "bin" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            uint index_size = (uint)count * 12;
            uint name_index_size = file.View.ReadUInt32 (4);
            if (8L + index_size + name_index_size >= file.MaxOffset)
                return null;

            uint index_offset = 8;
            file.View.Reserve (index_offset, index_size + name_index_size);
            uint names_base = index_offset + index_size;
            var dir = new List<Entry> (count);
            string entry_type = null;
            string arc_name = Path.GetFileNameWithoutExtension (file.Name).ToLowerInvariant();
            if ("voice" == arc_name || "bgm" == arc_name)
                entry_type = "audio";
            for (int i = 0; i < count; ++i)
            {
                uint filename_offset = file.View.ReadUInt32 (index_offset);
                if (filename_offset >= name_index_size)
                    return null;
                var name = file.View.ReadString (names_base+filename_offset, name_index_size-filename_offset);
                if (0 == name.Length)
                    return null;
                var entry = new Entry {
                    Name    = name,
                    Offset  = file.View.ReadUInt32 (index_offset+4),
                    Size    = file.View.ReadUInt32 (index_offset+8),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (entry_type != null)
                    entry.Type = entry_type;
                dir.Add (entry);
                index_offset += 12;
            }
            if (null == entry_type)
            {
                foreach (var entry in dir)
                {
                    var signature = file.View.ReadUInt32 (entry.Offset);
                    if (0 == signature)
                        continue;
                    var res = FormatCatalog.Instance.LookupSignature (signature).FirstOrDefault();
                    if (null == res)
                        continue;
                    entry.Type = res.Type;
                    var ext = res.Extensions.FirstOrDefault();
                    if (!string.IsNullOrEmpty (ext))
                        entry.Name += '.'+ext;
                }
            }
            return new ArcFile (file, this, dir);
        }
    }
}
