//! \file       ArcDAT.cs
//! \date       2018 Sep 20
//! \brief      Lazycrew resource archive.
//
// Copyright (C) 2018 by morkt
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
using System.Text.RegularExpressions;
using GameRes.Utility;

// [031024][Lazycrew] Sister Mermaid
// [020927][Ga-Bang] Sorairo Memories

namespace GameRes.Formats.Lazycrew
{
    internal class DirEntry
    {
        public string   Name;
        public int      Offset;
        public int      Count;
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/LAZYCREW"; } }
        public override string Description { get { return "Lazycrew resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat", "" };
            Signatures = new uint[] { 2, 0 };
        }

        static readonly Regex NamePattern = new Regex (@"^(?:(?<num>0\d\d\d)\.dat|data(?<num>\d+)?)$", RegexOptions.IgnoreCase);

        const uint DefaultPcmKey = 0x4B5AB4A5;

        public override ArcFile TryOpen (ArcView file)
        {
            var name = Path.GetFileName (file.Name);
            if (!NamePattern.IsMatch (name))
                return null;
            var match = NamePattern.Match (name);
            int name_id = 1;
            var num_str = match.Groups["num"].Value;
            if (!string.IsNullOrEmpty (num_str))
                name_id = Int32.Parse (num_str);
            if (name_id < 1)
                return null;
            ArcView index = file;
            try
            {
                if (name_id != 1)
                {
                    string index_name;
                    if (file.Name.HasExtension (".dat"))
                        index_name = VFS.ChangeFileName (file.Name, "0001.dat");
                    else
                        index_name = VFS.ChangeFileName (file.Name, "data");
                    if (!VFS.FileExists (index_name))
                        return null;
                    index = VFS.OpenView (index_name);
                }
                var dir = ReadIndex (index, name_id, file.MaxOffset);
                if (null == dir || 0 == dir.Count)
                    return null;
                return new ArcFile (file, this, dir);
            }
            finally
            {
                if (index != file)
                    index.Dispose();
            }
        }

        List<Entry> ReadIndex (ArcView index, int arc_id, long arc_length)
        {
            int dir_count = index.View.ReadInt32 (0);
            if (dir_count <= 0 || dir_count > 20)
                return null;
            var dir_list = new List<DirEntry> (dir_count);
            int dir_offset = 4;
            int first_offset = dir_count * 0x10 + 4;
            for (int i = 0; i < dir_count; ++i)
            {
                var dir_entry = new DirEntry {
                    Name   = index.View.ReadString (dir_offset, 8),
                    Offset = index.View.ReadInt32 (dir_offset+8),
                    Count  = index.View.ReadInt32 (dir_offset+12),
                };
                if (dir_entry.Offset < first_offset || dir_entry.Offset >= index.MaxOffset
                    || !IsSaneCount (dir_entry.Count))
                    return null;
                dir_list.Add (dir_entry);
                dir_offset += 16;
            }
            var file_list = new List<Entry>();
            foreach (var dir in dir_list)
            {
                dir_offset = dir.Offset;
                string type = "";
                if (dir.Name.Equals ("image", StringComparison.OrdinalIgnoreCase))
                    type = "image";
                else if (dir.Name.Equals ("sound", StringComparison.OrdinalIgnoreCase))
                    type = "audio";
                for (int i = 0; i < dir.Count; ++i)
                {
                    int id = index.View.ReadUInt16 (dir_offset);
                    if (id == arc_id)
                    {
                        var entry = new Entry {
                            Name = Path.Combine (dir.Name, i.ToString ("D5")),
                            Type = type,
                            Offset = index.View.ReadUInt32 (dir_offset+2),
                            Size   = index.View.ReadUInt32 (dir_offset+6),
                        };
                        if (!entry.CheckPlacement (arc_length))
                            return null;
                        file_list.Add (entry);
                    }
                    dir_offset += 10;
                }
            }
            return file_list;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Type == "audio")
            {
                uint signature = arc.File.View.ReadUInt32 (entry.Offset);
                if (signature == 0x10000 || signature == 0)
                    return OpenAudio (arc, entry);
                else if (signature == 1 || signature == 0x10001)
                    return arc.File.CreateStream (entry.Offset+4, entry.Size-4);
            }
            return base.OpenEntry (arc, entry);
        }

        Stream OpenAudio (ArcFile arc, Entry entry)
        {
            var riff_header = new byte[0x2C];
            LittleEndian.Pack (AudioFormat.Wav.Signature, riff_header, 0);
            LittleEndian.Pack (0x45564157, riff_header, 8); // 'WAVE'
            LittleEndian.Pack (0x20746d66, riff_header, 12); // 'fmt '
            LittleEndian.Pack (0x10, riff_header, 16);
            arc.File.View.Read (entry.Offset+4, riff_header, 20, 0x10);
            LittleEndian.Pack (0x61746164, riff_header, 0x24); // 'data'
            uint data_size = arc.File.View.ReadUInt32 (entry.Offset+0x16);
            LittleEndian.Pack (data_size + 0x24u, riff_header, 4);
            LittleEndian.Pack (data_size, riff_header, 0x28);
            var pcm = arc.File.View.ReadBytes (entry.Offset+0x1A, data_size);
            DecryptData (pcm, DefaultPcmKey);
            var riff_data = new MemoryStream (pcm);
            return new PrefixStream (riff_header, riff_data);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.OpenBinaryEntry (entry);
            int fmt = (int)input.Signature & 0xFFFF;
            if (fmt > 4)
                return ImageFormatDecoder.Create (input);
            var header = input.ReadHeader (14);
            input.Position = 0;
            int hpos = 2;
            if (1 == fmt || 3 == fmt)
                hpos = 8;
            int cmp = header.ToUInt16 (hpos);
            int bpp = 0;
            switch (cmp & 0xFF)
            {
            case 2: bpp = 4; break;
            case 3: bpp = 8; break;
            case 5: bpp = 24; break;
            default:
                return ImageFormatDecoder.Create (input);
            }
            var info = new LcImageMetaData {
                Width   = header.ToUInt16 (hpos+2),
                Height  = header.ToUInt16 (hpos+4),
                BPP     = bpp,
                Format  = fmt,
                Compression = cmp,
                DataOffset = hpos + 6,
            };
            return new LcImageDecoder (input, info);
        }

        void DecryptData (byte[] data, uint key)
        {
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] ^= (byte)key;
                key = data[i] ^ ((key << 9) | (key >> 23) & 0x1F0);
            }
        }
    }
}
