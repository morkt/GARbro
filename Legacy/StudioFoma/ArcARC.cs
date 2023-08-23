//! \file       ArcARC.cs
//! \date       2022 Jun 25
//! \brief      I's9 / Studio FOMA resource archive.
//
// Copyright (C) 2022 by morkt
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

// [991119][I’s9] Lovemation

namespace GameRes.Formats.Foma
{
    [Serializable]
    public class Is9Scheme : ResourceScheme
    {
        public IDictionary<string, IDictionary<string, uint>> KnownSchemes;
    }

    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/FOMA"; } }
        public override string Description { get { return "I's9/Studio FOMA resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            var arc_name = Path.GetFileName (file.Name).ToUpperInvariant();
            if (!KnownArcNames.Contains (arc_name))
                return null;
            var dir_name = VFS.GetDirectoryName (file.Name);
            foreach (var scheme in KnownSchemes)
            {
                var exe_name = VFS.CombinePath (dir_name, scheme.Key);
                if (VFS.FileExists (exe_name))
                {
                    uint table_offset;
                    if (scheme.Value.TryGetValue (arc_name, out table_offset))
                    {
                        var dir = GetEntryList (exe_name, table_offset, file.MaxOffset);
                        if (dir != null)
                            return new ArcFile (file, this, dir);
                    }
                }
            }
            return null;
        }

        internal List<Entry> GetEntryList (string exe_name, long table_offset, long max_offset)
        {
            using (var exe_file = VFS.OpenView (exe_name))
            {
                if (table_offset >= exe_file.MaxOffset)
                    return null;
                var exe = new ExeFile (exe_file);
                var dir = new List<Entry>();
                while (table_offset+12 <= exe_file.MaxOffset)
                {
                    uint name_addr = exe.View.ReadUInt32 (table_offset);
                    if (0 == name_addr)
                        break;
                    var name = exe.GetCString (name_addr);
                    if (string.IsNullOrEmpty (name))
                        return null;
                    var entry = Create<Entry> (name);
                    entry.Offset = exe.View.ReadUInt32 (table_offset + 4);
                    entry.Size   = exe.View.ReadUInt32 (table_offset + 8);
                    if (!entry.CheckPlacement (max_offset))
                        return null;
                    dir.Add (entry);
                    table_offset += 12;
                }
                return dir;
            }
        }

        Is9Scheme m_scheme = new Is9Scheme
        {
            KnownSchemes = new Dictionary<string, IDictionary<string, uint>>()
        };

        HashSet<string> m_known_arc_names = null;

        public override ResourceScheme Scheme
        {
            get { return m_scheme; }
            set { m_scheme = (Is9Scheme)value; m_known_arc_names = null; }
        }

        internal IDictionary<string, IDictionary<string, uint>> KnownSchemes
        {
            get { return m_scheme.KnownSchemes; }
        }

        internal HashSet<string> KnownArcNames
        {
            get { return m_known_arc_names ?? (m_known_arc_names = new HashSet<string> (KnownSchemes.Values.SelectMany (v => v.Keys))); }
        }
    }
}
