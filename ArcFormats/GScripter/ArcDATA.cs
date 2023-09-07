//! \file       ArcDATA.cs
//! \date       2023 Sep 06
//! \brief      GScripter resource archive.
//
// Copyright (C) 2023 by morkt
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

namespace GameRes.Formats.GScripter
{
    [Export(typeof(ArchiveFormat))]
    public class DataOpener : ArchiveFormat
    {
        public override string         Tag { get => "DAT/GScripter"; }
        public override string Description { get => "GScripter engine resource archive"; }
        public override uint     Signature { get => 0; }
        public override bool  IsHierarchic { get => false; }
        public override bool      CanWrite { get => false; }

        public DataOpener ()
        {
            Extensions = new[] { "" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var info_name = file.Name + ".info";
            if (!VFS.FileExists (info_name))
                return null;
            using (var index = VFS.OpenView (info_name))
            {
                int count = (int)index.MaxOffset / 0x28;
                if (!IsSaneCount (count) || count * 0x28 != index.MaxOffset)
                    return null;
                var arc_name = Path.GetFileName (file.Name);
                bool is_cg    = arc_name.StartsWith ("CG");
                bool is_sound = !is_cg && arc_name.StartsWith ("SOUND");
                uint index_pos = 0;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = index.View.ReadString (index_pos, 0x20);
                    var entry = new Entry {
                        Name = name,
                        Type = is_cg ? "image" : is_sound ? "audio" : "",
                        Offset = index.View.ReadUInt32 (index_pos+0x20),
                        Size   = index.View.ReadUInt32 (index_pos+0x24),
                    };
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    index_pos += 0x28;
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
