//! \file       ArcALF.cs
//! \date       Sun Sep 20 13:58:52 2015
//! \brief      Eushully and its subsidiaries resource archives.
//
// Copyright (C) 2015-2018 by morkt
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
using GameRes.Compression;

namespace GameRes.Formats.Eushully
{
    [Export(typeof(ArchiveFormat))]
    public class AlfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ALF"; } }
        public override string Description { get { return "Eushully resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public AlfOpener ()
        {
            ContainedFormats = new[] { "AGF", "WAV", "AOG/SYS3", "SCR" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            string dir_name = Path.GetDirectoryName (file.Name);
            string file_name = Path.GetFileName (file.Name);
            foreach (var ini_name in GetIndexNames (file_name))
            {
                string ini_path = VFS.CombinePath (dir_name, ini_name);
                if (VFS.FileExists (ini_path))
                {
                    var dir = ReadIndex (ini_path, file_name);
                    if (null != dir)
                        return new ArcFile (file, this, dir);
                }
            }
            return null;
        }

        internal IEnumerable<string> GetIndexNames (string alf_name)
        {
            yield return "sys4ini.bin";
            yield return "sys3ini.bin";
            yield return Path.ChangeExtension (alf_name, "AAI");
        }

        Tuple<string, Dictionary<string, List<Entry>>> LastAccessedIndex;

        List<Entry> ReadIndex (string ini_file, string arc_name)
        {
            if (null == LastAccessedIndex
                || !LastAccessedIndex.Item1.Equals (ini_file, StringComparison.OrdinalIgnoreCase))
            {
                LastAccessedIndex = null;
                using (var ini = VFS.OpenView (ini_file))
                {
                    IBinaryStream index;
                    bool is_append = ini.View.AsciiEqual (0, "S4AC");
                    if (is_append || ini.View.AsciiEqual (0, "S4IC") || ini.View.AsciiEqual (0, "S3IC"))
                    {
                        uint offset = is_append ? 0x114u : 0x134u;
                        uint packed_size = ini.View.ReadUInt32 (offset);
                        var packed = ini.CreateStream (offset+4, packed_size);
                        var unpacked = new LzssStream (packed);
                        index = new BinaryStream (unpacked, ini_file);
                    }
                    else if (ini.View.AsciiEqual (0, "S3IN"))
                    {
                        index = ini.CreateStream (0x12C);
                    }
                    else
                        return null;
                    using (index)
                    {
                        var file_table = ReadSysIni (index);
                        if (null == file_table)
                            return null;
                        LastAccessedIndex = Tuple.Create (ini_file, file_table);
                    }
                }
            }
            List<Entry> dir = null;
            LastAccessedIndex.Item2.TryGetValue (arc_name, out dir);
            return dir;
        }

        internal Dictionary<string, List<Entry>> ReadSysIni (IBinaryStream index)
        {
            int arc_count = index.ReadInt32();
            if (!IsSaneCount (arc_count))
                return null;
            var file_table = new Dictionary<string, List<Entry>> (arc_count, StringComparer.OrdinalIgnoreCase);
            var arc_list = new List<Entry>[arc_count];
            for (int i = 0; i < arc_count; ++i)
            {
                var name = index.ReadCString (0x100);
                var file_list = new List<Entry>();
                file_table.Add (name, file_list);
                arc_list[i] = file_list;
            }
            int file_count = index.ReadInt32();
            if (!IsSaneCount (file_count))
                return null;
            for (int i = 0; i < file_count; ++i)
            {
                var name = index.ReadCString (0x40);
                int arc_id = index.ReadInt32();
                if (arc_id < 0 || arc_id >= arc_list.Length)
                    return null;
                index.ReadInt32(); // file number
                uint offset = index.ReadUInt32();
                uint size = index.ReadUInt32();
                if ("@" == name)
                    continue;
                var entry = Create<Entry> (name);
                entry.Offset = offset;
                entry.Size = size;
                arc_list[arc_id].Add (entry);
            }
            return file_table;
        }
    }
}
