//! \file       ArcALF.cs
//! \date       Sun Sep 20 13:58:52 2015
//! \brief      Eushully and its subsidiaries resource archives.
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
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            string ini_path = VFS.CombinePath (Path.GetDirectoryName (file.Name), "sys4ini.bin");
            if (!VFS.FileExists (ini_path))
                return null;

            var dir = ReadIndex (ini_path, Path.GetFileName (file.Name));
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }

        Tuple<string, Dictionary<string, List<Entry>>> LastAccessedIndex;

        List<Entry> ReadIndex (string ini_file, string arc_name)
        {
            if (null == LastAccessedIndex
                || !LastAccessedIndex.Item1.Equals (ini_file, StringComparison.InvariantCultureIgnoreCase))
            {
                LastAccessedIndex = null;
                using (var ini = VFS.OpenView (ini_file))
                {
                    if (!ini.View.AsciiEqual (0, "S4IC") && !ini.View.AsciiEqual (0, "S3IC"))
                        return null;
                    uint packed_size = ini.View.ReadUInt32 (0x134);
                    using (var packed = ini.CreateStream (0x138, packed_size))
                    using (var unpacked = new LzssStream (packed))
                    using (var index = new BinaryReader (unpacked))
                    {
                        int arc_count = index.ReadInt32();
                        var name_buf = new byte[0x100];
                        var file_table = new Dictionary<string, List<Entry>> (arc_count, StringComparer.InvariantCultureIgnoreCase);
                        var arc_list = new List<List<Entry>> (arc_count);
                        for (int i = 0; i < arc_count; ++i)
                        {
                            index.Read (name_buf, 0, name_buf.Length);
                            var file_list = new List<Entry>();
                            file_table.Add (Binary.GetCString (name_buf, 0, name_buf.Length), file_list);
                            arc_list.Add (file_list);
                        }
                        int file_count = index.ReadInt32();
                        for (int i = 0; i < file_count; ++i)
                        {
                            index.Read (name_buf, 0, 0x40);
                            int arc_id = index.ReadInt32();
                            if (arc_id < 0 || arc_id >= arc_list.Count)
                                return null;
                            index.ReadInt32(); // file number
                            uint offset = index.ReadUInt32();
                            uint size = index.ReadUInt32();
                            var name = Binary.GetCString (name_buf, 0, 0x40);
                            if ("@" == name)
                                continue;
                            var entry = FormatCatalog.Instance.Create<Entry> (name);
                            entry.Offset = offset;
                            entry.Size = size;
                            arc_list[arc_id].Add (entry);
                        }
                        LastAccessedIndex = Tuple.Create (ini_file, file_table);
                    }
                }
            }
            List<Entry> dir = null;
            LastAccessedIndex.Item2.TryGetValue (arc_name, out dir);
            return dir;
        }
    }
}
