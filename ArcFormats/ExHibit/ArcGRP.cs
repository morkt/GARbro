//! \file       ArcGRP.cs
//! \date       Thu Apr 06 16:41:59 2017
//! \brief      ExHIBIT audio resource archive.
//
// Copyright (C) 2017 by morkt
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
using System.Text;
using System.Text.RegularExpressions;

namespace GameRes.Formats.ExHibit
{
    [Export(typeof(ArchiveFormat))]
    public class GrpOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GRP/EXHIBIT"; } }
        public override string Description { get { return "ExHIBIT engine audio resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static readonly Regex s_ResNameRe = new Regex (@"res(\d+)\.grp$", RegexOptions.IgnoreCase);

        public override ArcFile TryOpen (ArcView file)
        {
            var match = s_ResNameRe.Match (file.Name);
            if (!match.Success || file.View.AsciiEqual (0, "AiFS"))
                return null;

            var digits = match.Groups[1];
            int arc_num = UInt16.Parse (digits.Value);
            int toc_num = arc_num - 1;
            int arc_index = 1; // number by which archive is referenced within toc file

            // look for toc file
            ArcView toc_file = null;
            var toc_name_sb = new StringBuilder (file.Name.Length);
            while (toc_num >= 0)
            {
                toc_name_sb.Clear();
                toc_name_sb.Append (file.Name);
                toc_name_sb.Remove (digits.Index, digits.Length);
                toc_name_sb.Insert (digits.Index, toc_num.ToString ("D4"));
                var toc_name = toc_name_sb.ToString();
                if (!VFS.FileExists (toc_name))
                    return null;
                toc_file = VFS.OpenView (toc_name);
                if (toc_file.View.AsciiEqual (0, "AiFS"))
                    break;
                toc_file.Dispose();
                toc_file = null;
                toc_num--;
                arc_index++;
            }
            if (null == toc_file)
                return null;
            using (toc_file)
            {
                int res_count = toc_file.View.ReadInt32 (0xC);
                if (res_count < arc_index)
                    return null;
                uint index_offset = 0x10;
                // find archive reference within toc file
                bool arc_found = false;
                for (int i = 0; i < res_count && index_offset < toc_file.MaxOffset; ++i)
                {
                    int num = toc_file.View.ReadInt32 (index_offset);
                    if (0x01000000 == num)
                    {
                        index_offset += 4;
                        num = toc_file.View.ReadInt32 (index_offset);
                    }
                    if (num == arc_index)
                    {
                        arc_found = true;
                        break;
                    }
                    uint entries = toc_file.View.ReadUInt32 (index_offset+0xC);
                    index_offset += 0x10 + entries * 8;
                }
                if (!arc_found)
                    return null;
                int count = toc_file.View.ReadInt32 (index_offset+0xC);
                if (!IsSaneCount (count))
                    return null;
                int start_index = toc_file.View.ReadInt32 (index_offset+4);
                index_offset += 0x10;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    uint size   = toc_file.View.ReadUInt32 (index_offset+4);
                    if (size != 0)
                    {
                        var entry = new Entry {
                            Name = string.Format ("{0:D5}.ogg", start_index+i),
                            Type = "audio",
                            Offset = toc_file.View.ReadUInt32 (index_offset),
                            Size = size,
                        };
                        if (!entry.CheckPlacement (file.MaxOffset))
                            return null;
                        dir.Add (entry);
                    }
                    index_offset += 8;
                }
                if (0 == dir.Count)
                    return null;
                return new ArcFile (file, this, dir);
            }
        }
    }
}
