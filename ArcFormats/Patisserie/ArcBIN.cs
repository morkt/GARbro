//! \file       ArcBIN.cs
//! \date       Thu Jul 21 21:10:31 2016
//! \brief      Patisserie resource archive.
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
using System.Linq;
using GameRes.Compression;

namespace GameRes.Formats.Patisserie
{
    [Export(typeof(ArchiveFormat))]
    public class BinOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BIN/OZ"; } }
        public override string Description { get { return "Patisserie resource archive"; } }
        public override uint     Signature { get { return 0x01005A4F; } } // 'OZ'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public BinOpener ()
        {
            Extensions = new string[] { "bin" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "OFST"))
                return null;
            int index_size = file.View.ReadInt32 (8);
            int count = index_size / 4;
            if (!IsSaneCount (count))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            string content_ext = "", content_type = "";
            if (base_name.EndsWith ("flac", StringComparison.OrdinalIgnoreCase))
            {
                content_ext = "flac";
                content_type = "audio";
                base_name = base_name.Substring (0, base_name.Length-4);
            }
            else if (base_name.EndsWith ("ogg", StringComparison.OrdinalIgnoreCase))
            {
                content_ext = "ogg";
                content_type = "audio";
                base_name = base_name.Substring (0, base_name.Length-3);
            }

            var filenames = GetFileNames (file.Name);
            if (null == filenames)
                filenames = new List<string> (count);
            for (int i = filenames.Count; i < count; ++i)
                filenames.Add (string.Format ("{0}#{1:D5}", base_name, i));

            uint index_offset = 0xC;
            var dir = new List<Entry> (count);
            uint next_offset = file.View.ReadUInt32 (index_offset);
            for (int i = 0; i < count; ++i)
            {
                index_offset += 4;
                var entry = new PackedEntry { Name = filenames[i] };
                entry.Offset = next_offset;
                next_offset = i+1 < count ? file.View.ReadUInt32 (index_offset) : (uint)file.MaxOffset;
                entry.Size = next_offset - (uint)entry.Offset;
                if (entry.Size > 0 && !entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (!string.IsNullOrEmpty (content_type))
                {
                    entry.Type = content_type;
                    entry.Name = Path.ChangeExtension (entry.Name, content_ext);
                }
                dir.Add (entry);
            }
            foreach (PackedEntry entry in dir.Where (e => e.Size > 4))
            {
                entry.IsPacked = file.View.AsciiEqual (entry.Offset, "DFLT");
                if (entry.IsPacked)
                {
                    entry.Size = file.View.ReadUInt32 (entry.Offset+4);
                    entry.UnpackedSize = file.View.ReadUInt32 (entry.Offset+8);
                    entry.Offset += 12;
                }
                else if (file.View.AsciiEqual (entry.Offset, "DATA"))
                {
                    entry.Size = file.View.ReadUInt32 (entry.Offset+4);
                    entry.UnpackedSize = entry.Size;
                    entry.Offset += 8;
                    if (string.IsNullOrEmpty (entry.Type))
                    {
                        uint signature = file.View.ReadUInt32 (entry.Offset);
                        if (0x43614C66 == signature) // 'fLaC'
                        {
                            entry.Type = "audio";
                            entry.Name = Path.ChangeExtension (entry.Name, "flac");
                        }
                        else
                        {
                            var res = AutoEntry.DetectFileType (signature);
                            if (null != res)
                                entry.ChangeType (res);
                        }
                    }
                }
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return input;
            return new ZLibStream (input, CompressionMode.Decompress);
        }

        IList<string> ReadListFile (string lst_name)
        {
            return File.ReadAllLines (lst_name, Encodings.cp932);
        }

        IList<string> GetFileNames (string arc_name)
        {
            var dir_name = VFS.GetDirectoryName (arc_name);
            var lst_name = Path.ChangeExtension (arc_name, ".lst");
            if (VFS.FileExists (lst_name))
                return ReadListFile (lst_name);

            var lists_lst_name = VFS.CombinePath (dir_name, "lists.lst");
            if (!VFS.FileExists (lists_lst_name))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (arc_name);
            var arcs = ReadListFile (lists_lst_name);
            var arc_no = arcs.IndexOf (base_name);
            if (-1 == arc_no)
                return null;
            var lists_bin_name = VFS.CombinePath (dir_name, "lists.bin");
            using (var lists_bin = VFS.OpenView (lists_bin_name))
                return ReadFileNames (lists_bin, arc_no);
        }

        IList<string> ReadFileNames (ArcView index, int arc_no)
        {
            if (index.View.ReadUInt32 (0) != Signature)
                return null;
            if (!index.View.AsciiEqual (4, "OFST"))
                return null;
            int index_size = index.View.ReadInt32 (8);
            int arc_count = index_size / 4;
            if (arc_no >= arc_count)
                return null;
            uint index_offset = index.View.ReadUInt32 (0xC + arc_no * 4);
            if (index_offset >= index.MaxOffset)
                return null;
            Stream input;
            if (index.View.AsciiEqual (index_offset, "DFLT"))
            {
                uint packed_size = index.View.ReadUInt32 (index_offset+4);
                input = index.CreateStream (index_offset+12, packed_size);
                input = new ZLibStream (input, CompressionMode.Decompress);
            }
            else if (index.View.AsciiEqual (index_offset, "DATA"))
            {
                uint data_size = index.View.ReadUInt32 (index_offset+4);
                input = index.CreateStream (index_offset+8, data_size);
            }
            else
                return null;
            using (input)
            using (var reader = new StreamReader (input, Encodings.cp932))
            {
                var list = new List<string>();
                string line;
                while ((line = reader.ReadLine()) != null)
                    list.Add (line.Trim());
                return list;
            }
        }
    }
}
