//! \file       ArcDSK.cs
//! \date       Tue Oct 18 20:19:49 2016
//! \brief      AbogadoPowers resource archive.
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

namespace GameRes.Formats.Abogado
{
    [Export(typeof(ArchiveFormat))]
    public class DskOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DSK/PFT"; } }
        public override string Description { get { return "AbogadoPowers resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static readonly IDictionary<string, string> ExtensionMap = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase)
        {
            { "BACK",   "KG"  },
            { "BUST",   "KG"  },
            { "EVENT",  "KG"  },
            { "SYSTEM", "KG"  },
            { "VISUAL", "KG"  },
            { "SETTEI", "KG"  },
            { "THUMB",  "KG"  },
            { "SCENE",  "SCF" },
            { "SOUND",  "ADP" },
            { "PCM1",   "ADP" },
            { "PCM2",   "ADP" },
            { "PCM",    "ADP" },
            { "GRAPHIC", "KG" },
            { "SCENARIO", "SCF" },
        };

        public override ArcFile TryOpen (ArcView file)
        {
            var pft_name = Path.ChangeExtension (file.Name, "pft");
            if (file.Name.Equals (pft_name, StringComparison.InvariantCultureIgnoreCase)
                || !VFS.FileExists (pft_name))
                return null;
            using (var pft_view = VFS.OpenView (pft_name))
            using (var pft = pft_view.CreateStream())
            {
                var arc_name = Path.GetFileNameWithoutExtension (file.Name);
                string ext = "";
                ExtensionMap.TryGetValue (arc_name, out ext);
                uint header_size = pft.ReadUInt16();
                uint cluster_size = pft.ReadUInt16();
                int count = pft.ReadInt32();
                if (!IsSaneCount (count))
                    return null;

                pft.Position = header_size;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = pft.ReadCString (8);
                    if (name.Length > 0)
                    {
                        if (!string.IsNullOrEmpty (ext))
                            name = Path.ChangeExtension (name, ext);
                        var entry = FormatCatalog.Instance.Create<Entry> (name);
                        entry.Offset = cluster_size * (long)pft.ReadUInt32();
                        entry.Size   = pft.ReadUInt32();
                        if (!entry.CheckPlacement (file.MaxOffset))
                            return null;
                        dir.Add (entry);
                    }
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
