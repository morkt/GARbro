//! \file       ArcHED.cs
//! \date       Fri Jul 31 19:51:15 2015
//! \brief      elf AV king archive.
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
using System.Text;
using System.Text.RegularExpressions;
using GameRes.Utility;

namespace GameRes.Formats.Elf
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BIN/HED"; } }
        public override string Description { get { return "elf AV King resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public PakOpener ()
        {
            Extensions = new string[] { "bin" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            string pak_name = Path.ChangeExtension (file.Name, "pak");
            if (!File.Exists (pak_name))
                return null;
            var file_map = GetFileMap (pak_name);
            if (null == file_map)
                return null;
            string base_name = Path.GetFileNameWithoutExtension (pak_name);

            using (var pak = new ArcView (pak_name))
            {
                if (0x00646568 != pak.View.ReadUInt32 (0))
                    return null;
                int count = pak.View.ReadInt32 (4);
                if (count != file_map.Count)
                    return null;
                List<Entry> dir;
                if ("cg" == base_name)
                    dir = ReadCgPak (pak, file_map);
                else
                    dir = ReadVoicePak (pak, file_map);
                if (null == dir)
                    return null;
                return new ArcFile (file, this, dir);
            }
        }

        List<Entry> ReadCgPak (ArcView pak, List<string> file_map)
        {
            uint index_offset = 8;
            uint index_size = (uint)file_map.Count * 8u;
            if (index_size > pak.View.Reserve (index_offset, index_size))
                return null;
            var dir = new List<Entry> (file_map.Count);
            for (int i = 0; i < file_map.Count; ++i)
            {
                var entry = FormatCatalog.Instance.CreateEntry (file_map[i]);
                entry.Offset = pak.View.ReadUInt32 (index_offset);
                entry.Size   = pak.View.ReadUInt32 (index_offset + 4);
                dir.Add (entry);
                index_offset += 8;
            }
            return dir;
        }

        List<Entry> ReadVoicePak (ArcView pak, List<string> file_map)
        {
            uint index_offset = 8;
            uint index_size = (uint)file_map.Count * 0x18u;
            if (index_size > pak.View.Reserve (index_offset, index_size))
                return null;
            var dir = new List<Entry> (file_map.Count);
            for (int i = 0; i < file_map.Count; ++i)
            {
                var entry = FormatCatalog.Instance.CreateEntry (file_map[i]);
                entry.Offset = pak.View.ReadUInt32 (index_offset);
                entry.Size   = pak.View.ReadUInt32 (index_offset + 4);
                dir.Add (entry);
                index_offset += 0x18;
            }
            return dir;
        }

        private List<string>    CgMap { get; set; }
        private List<string> VoiceMap { get; set; }
        private string CurrentMapName { get; set; }

        private List<string> GetFileMap (string pak_name, string map_name = "avking.map")
        {
            string base_name = Path.GetFileNameWithoutExtension (pak_name);
            List<string> map;
            if ("cg" == base_name)
                map = CgMap;
            else if ("voice" == base_name)
                map = VoiceMap;
            else
                return null;
            if (null != map && File.Exists (CurrentMapName))
                return map;
            CgMap = null;
            VoiceMap = null;
            string dir_name = Path.GetDirectoryName (pak_name);
            if (string.IsNullOrEmpty (dir_name))
                dir_name = ".";
            while (!string.IsNullOrEmpty (dir_name))
            {
                string map_file = Path.Combine (dir_name, map_name);
                if (File.Exists (map_file))
                {
                    if (!ReadMap (map_file))
                        return null;
                    CurrentMapName = map_file;
                    if ("cg" == base_name)
                        return CgMap;
                    if ("voice" == base_name)
                        return VoiceMap;
                }
                dir_name = Path.GetDirectoryName (dir_name);
            }
            return null;
        }

        static readonly Regex FilesTypeRe = new Regex (@"^//([A-Z]+) FILES = (\d+)");

        private bool ReadMap (string map_file)
        {
            try
            {
                using (var map = File.OpenRead (map_file))
                using (var input = new StreamReader (map, Encoding.ASCII))
                {
                    var cg = new List<string>();
                    var voice = new List<string>();
                    List<string> current_list = null;
                    for (;;)
                    {
                        string line = input.ReadLine();
                        if (null == line)
                            break;
                        var match = FilesTypeRe.Match (line);
                        if (!match.Success)
                            return false;
                        string type = match.Groups[1].Value;
                        if ("BG" == type || "CHR" == type)
                            current_list = cg;
                        else if ("VOICE" == type)
                            current_list = voice;
                        else
                            current_list = null;
                        int count = UInt16.Parse (match.Groups[2].Value);
                        for (int i = 0; i < count; ++i)
                        {
                            line = input.ReadLine();
                            if (null == line)
                                break;
                            if (null != current_list)
                                current_list.Add (line.TrimEnd ('\0'));
                        }
                    }
                    CgMap = cg;
                    VoiceMap = voice;
                    return cg.Count > 0 || voice.Count > 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}


