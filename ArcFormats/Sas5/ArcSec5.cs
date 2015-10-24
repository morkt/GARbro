//! \file       ArcSec5.cs
//! \date       Fri Oct 23 18:10:06 2015
//! \brief      Sas5 engine resource index file.
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
using GameRes.Utility;
using System.Text;

namespace GameRes.Formats.Sas5
{
    [Export(typeof(ArchiveFormat))]
    public class Sec5Opener : ArchiveFormat
    {
        public override string         Tag { get { return "SEC5"; } }
        public override string Description { get { return "SAS5 engine resource index file"; } }
        public override uint     Signature { get { return 0x35434553; } } // 'SEC5'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint offset = 8;
            var dir = new List<Entry>();
            while (offset < file.MaxOffset)
            {
                string name = file.View.ReadString (offset, 4, Encoding.ASCII);
                if ("ENDS" == name)
                    break;
                uint section_size = file.View.ReadUInt32 (offset+4);
                offset += 8;
                var entry = new Entry {
                    Name = name,
                    Offset = offset,
                    Size = section_size,
                };
                dir.Add (entry);
                offset += section_size;
            }
            if (dir.Count > 0)
                return new ArcFile (file, this, dir);
            return null;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if ("CODE" != entry.Name)
                return arc.File.CreateStream (entry.Offset, entry.Size);

            var code = new byte[entry.Size];
            arc.File.View.Read (entry.Offset, code, 0, entry.Size);
            DecryptCodeSection (code);
            return new MemoryStream (code);
        }

        static void DecryptCodeSection (byte[] code)
        {
            byte key = 0;
            for (int i = 0; i < code.Length; ++i)
            {
                int x = code[i] + 18;
                code[i] ^= key;
                key += (byte)x;
            }
        }

        static internal Dictionary<string, Dictionary<int, Entry>> CurrentIndex;

        static internal Dictionary<int, Entry> LookupIndex (string filename)
        {
            if (null == CurrentIndex)
                CurrentIndex = FindSec5Resr (filename);
            if (null == CurrentIndex)
                return null;
            Dictionary<int, Entry> arc_map = null;
            CurrentIndex.TryGetValue (Path.GetFileName (filename), out arc_map);
            return arc_map;
        }

        static internal Dictionary<string, Dictionary<int, Entry>> FindSec5Resr (string arc_name)
        {
            string dir_name = Path.GetDirectoryName (arc_name);
            var match = Directory.GetFiles (dir_name, "*.sec5");
            if (0 == match.Length)
            {
                string parent = Path.GetDirectoryName (dir_name);
                if (!string.IsNullOrEmpty (parent))
                    match = Directory.GetFiles (parent, "*.sec5");
            }
            if (0 == match.Length)
                return null;
            using (var sec5 = new ArcView (match[0]))
            {
                if (!sec5.View.AsciiEqual (0, "SEC5"))
                    return null;
                uint offset = 8;
                while (offset < sec5.MaxOffset)
                {
                    string id = sec5.View.ReadString (offset, 4, Encoding.ASCII);
                    if ("ENDS" == id)
                        break;
                    uint section_size = sec5.View.ReadUInt32 (offset+4);
                    offset += 8;
                    if ("RESR" == id)
                    {
                        using (var resr = sec5.CreateStream (offset, section_size))
                            return ReadResrSection (resr);
                    }
                    offset += section_size;
                }
            }
            return null;
        }

        static internal Dictionary<string, Dictionary<int, Entry>> ReadResrSection (Stream input)
        {
            using (var resr = new BinaryReader (input, Encodings.cp932, true))
            {
                int count = resr.ReadInt32();
                if (0 == count)
                    return null;
                var map = new Dictionary<string, Dictionary<int, Entry>> (StringComparer.InvariantCultureIgnoreCase);
                for (int i = 0; i < count; ++i)
                {
                    string name = resr.BaseStream.ReadCString();
                    string type = resr.BaseStream.ReadCString();
                    string arc_type = resr.BaseStream.ReadCString();
                    int res_length = resr.ReadInt32();
                    var next_pos = resr.BaseStream.Position + res_length;
                    if (arc_type == "file-war" || arc_type == "file-iar")
                    {
                        string arc_name = resr.BaseStream.ReadCString();
                        int id = resr.ReadInt32();
                        var base_arc_name = Path.GetFileName (arc_name);
                        if (!map.ContainsKey (base_arc_name))
                            map[base_arc_name] = new Dictionary<int, Entry>();
                        var entry = new Entry
                        {
                            Name = name,
                            Type = type,
                        };
                        map[base_arc_name][id] = entry;
                    }
                    resr.BaseStream.Position = next_pos;
                }
                return map.Count > 0 ? map : null;
            }
        }
    }
}
