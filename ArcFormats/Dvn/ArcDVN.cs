//! \file       ArcDVN.cs
//! \date       2026-09-14
//! \brief      Project DVN resource archive.
//
// Copyright (C) 2026 by morkt
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
using Newtonsoft.Json;

namespace GameRes.Formats.Dvn
{
    class Resource
    {
        [JsonProperty("path")]
        public string Path { get; set; }
    }

    [Export(typeof(ArchiveFormat))]
    public class DvnOpener : ArchiveFormat
    {
        public override string         Tag => "DVN";
        public override string Description => "Project DVN resource archive";
        public override uint     Signature => 0x424e5644; // 'DVNB'
        public override bool  IsHierarchic => true;
        public override bool      CanWrite => false;

        static readonly string[] ResourceLists = new[] {
            "resources/main.json",
            "game/backgrounds.json",
            "game/characters.json",
            "game/animations.json"
        };

        public override ArcFile TryOpen (ArcView file)
        {
            uint version = file.View.ReadUInt32 (4);
            if (version != 1)
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name).ToLowerInvariant();

            var resmap = new Dictionary<string, Resource> ();
            foreach (var list in ResourceLists)
            {
                try
                {
                    using (var input = VFS.OpenStream (list))
                    using (var reader = new StreamReader (input))
                    using (var jtr = new JsonTextReader (reader))
                    {
                        var serializer = new JsonSerializer();
                        var json = serializer.Deserialize<Dictionary<string, Resource>> (jtr);
                        resmap = resmap.Concat (json).ToDictionary (x => x.Key, x => x.Value);
                    }
                }
                catch
                {
                    continue;
                }
            }

            var dir = new List<Entry> ();
            uint offset = 8;
            while (offset < file.MaxOffset)
            {
                uint name_length = file.View.ReadUInt32 (offset);
                var name = file.View.ReadString (offset + 4, name_length);
                if (resmap.ContainsKey (name))
                    name = resmap[name].Path;
                offset += name_length + 4;
                var entry = Create<Entry> (name);
                entry.Size = file.View.ReadUInt32 (offset);
                entry.Offset = offset + 4;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if ("images" == base_name)
                    entry.Type = "image";
                dir.Add (entry);
                offset += entry.Size + 4;
            }

            return new ArcFile (file, this, dir);
        }
    }
}
