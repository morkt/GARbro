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
using GameRes.Compression;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;

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

        static internal string GetAAIName(string alf_name)
        {
            const string pattern = @"^(APPEND(?:[0-9]+)?)(?:_[0-9]+)?\.ALF$";
            var match = Regex.Match(alf_name, pattern);
            if (match.Success)
                return match.Groups[1].Value;
            return alf_name;
        }

        internal IEnumerable<string> GetIndexNames (string alf_name)
        {
            yield return "sys5ini.bin";
            yield return "sys4ini.bin";
            yield return "sys3ini.bin";
            yield return Path.ChangeExtension (GetAAIName(alf_name), "AAI");
        }

        Tuple<string, Dictionary<string, List<Entry>>> LastAccessedIndex;


        internal class AGEArchiveInfo
        {
            public readonly byte[] signature;
            public readonly int offset;
            public readonly bool isNameUnicode;
            public readonly bool isLZSSCompressed;

            public AGEArchiveInfo(byte[] Signature, int Offset, bool IsNameUnicode, bool IsLZSSCompressed)
            {
                signature = Signature;
                offset = Offset;
                isNameUnicode = IsNameUnicode;
                isLZSSCompressed = IsLZSSCompressed;
            }
        }

        static AGEArchiveInfo[] infos =
        {
            new AGEArchiveInfo(Encoding.ASCII.GetBytes("S3IN"), 0x12C, false, false),
            new AGEArchiveInfo(Encoding.ASCII.GetBytes("S3IC"), 0x134, false, true),
            new AGEArchiveInfo(Encoding.ASCII.GetBytes("S3AC"), 0x114, false, true),
            new AGEArchiveInfo(Encoding.ASCII.GetBytes("S4IC"), 0x134, false, true),
            new AGEArchiveInfo(Encoding.ASCII.GetBytes("S4AC"), 0x114, false, true),
            new AGEArchiveInfo(Encoding.Unicode.GetBytes("S5IC"), 0x224, true, true),
            new AGEArchiveInfo(Encoding.Unicode.GetBytes("S5AC"), 0x21C, true, true)
        };

        static internal AGEArchiveInfo GetAGEArcInfo(ArcView view)
        {
            byte[] sig = view.View.ReadBytes(0, 8);
            var siglow = sig.Take(4);
            var res = infos.Where(i => Enumerable.SequenceEqual(i.signature, siglow));
            if (res.Any()) return res.First();
            res = infos.Where(i => Enumerable.SequenceEqual(i.signature, sig));
            if (res.Any()) return res.First();
            return null;
        }

        List<Entry> ReadIndex (string ini_file, string arc_name)
        {
            if (null == LastAccessedIndex
                || !LastAccessedIndex.Item1.Equals (ini_file, StringComparison.OrdinalIgnoreCase))
            {
                LastAccessedIndex = null;
                using (var ini = VFS.OpenView (ini_file))
                {
                    IBinaryStream index;

                    AGEArchiveInfo info = GetAGEArcInfo (ini);
                    if (info == null) return null;

                    if (info.isLZSSCompressed)
                    {
                        index = new BinaryStream(new LzssStream(ini.CreateStream(info.offset + 4, (uint)ini.View.ReadInt32(info.offset))), ini_file);
                    }
                    else
                    {
                        index = ini.CreateStream(info.offset);
                    }
                    using (index)
                    {
                        var file_table = ReadSysIni (index, info);
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

        internal Dictionary<string, List<Entry>> ReadSysIni (IBinaryStream index, AGEArchiveInfo info)
        {
            int arc_count = index.ReadInt32();
            if (!IsSaneCount (arc_count))
                return null;
            var file_table = new Dictionary<string, List<Entry>> (arc_count, StringComparer.OrdinalIgnoreCase);
            var arc_list = new List<Entry>[arc_count];
            for (int i = 0; i < arc_count; ++i)
            {
                string name = info.isNameUnicode ? index.ReadCString(0x200, Encoding.Unicode) : index.ReadCString(0x100);

                var file_list = new List<Entry>();
                file_table.Add (name, file_list);
                arc_list[i] = file_list;
            }
            int file_count = index.ReadInt32();
            if (!IsSaneCount (file_count))
                return null;

            for (int i = 0; i < file_count; ++i)
            {
                string name = info.isNameUnicode ? index.ReadCString(0x80, Encoding.Unicode) : index.ReadCString(0x40);
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
