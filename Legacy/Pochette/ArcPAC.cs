//! \file       ArcPAC.cs
//! \date       2019 Mar 26
//! \brief      Pochette resource archive.
//
// Copyright (C) 2019 by morkt
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

namespace GameRes.Formats.Pochette
{
    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/IDX"; } }
        public override string Description { get { return "Pochette resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PacOpener ()
        {
            ContainedFormats = new[] { "GDT", "WAV" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".pac"))
                return null;
            var idx_name = Path.ChangeExtension (file.Name, ".idx");
            if (!VFS.FileExists (idx_name))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            string type = "";
            if (base_name.StartsWith ("GDT", StringComparison.OrdinalIgnoreCase))
                type = "image";
            else if (base_name.StartsWith ("WAV", StringComparison.OrdinalIgnoreCase))
                type = "audio";
            List<Entry> dir = null;
            using (var idx = VFS.OpenView (idx_name))
            {
                if ((idx.MaxOffset & 0xF) != 0)
                    return null;
                int count = (int)idx.MaxOffset / 0x10;
                if (!IsSaneCount (count))
                    return null;
                uint idx_pos = 0;
                dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    uint name_length = idx.View.ReadByte (idx_pos);
                    var name = idx.View.ReadString (idx_pos+1, name_length);
                    if (string.IsNullOrWhiteSpace (name))
                        return null;
                    var entry = new Entry { Name = name, Type = type };
                    entry.Offset = idx.View.ReadUInt32 (idx_pos+8);
                    entry.Size   = idx.View.ReadUInt32 (idx_pos+12);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    idx_pos += 0x10;
                }
            }
            return new ArcFile (file, this, dir);
        }
    }
}
