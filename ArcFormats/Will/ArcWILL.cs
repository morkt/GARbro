//! \file       ArcWILL.cs
//! \date       Fri Oct 31 13:37:11 2014
//! \brief      Will ARC archive format implementation.
//
// Copyright (C) 2014-2017 by morkt
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
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.Will
{
    internal class ExtRecord
    {
        public string   Extension;
        public int      FileCount;
        public uint     DirOffset;
    }

    public class ArcOptions : ResourceOptions
    {
        public int      NameLength { get; set; }
    }

    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/Will"; } }
        public override string Description { get { return "Will Co. game engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return true; } }

        ArcOpener ()
        {
            Extensions = new string[] { "arc" };
            Signatures = new uint[] { 1, 0, 5, 6 };
            ContainedFormats = new[] { "WIP", "PNA", "OGG", "SCR" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int ext_count = file.View.ReadInt32 (0);
            if (ext_count <= 0 || ext_count > 0xff)
                return null;

            uint dir_offset = 4;
            var ext_list = new List<ExtRecord> (ext_count);
            for (int i = 0; i < ext_count; ++i)
            {
                string ext = file.View.ReadString (dir_offset, 4).ToLowerInvariant();
                int count = file.View.ReadInt32 (dir_offset+4);
                uint offset = file.View.ReadUInt32 (dir_offset+8);
                if (count <= 0 || count > 0xffff || offset <= dir_offset || offset > file.MaxOffset)
                    return null;
                ext_list.Add (new ExtRecord { Extension = ext, FileCount = count, DirOffset = offset });
                dir_offset += 12;
            }
            List<Entry> dir = null;
            try
            {
                dir = ReadFileList (file, ext_list, 9);
            }
            catch { /* ignore parse errors */ }
            if (null == dir)
                dir = ReadFileList (file, ext_list, 13);
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }

        List<Entry> ReadFileList (ArcView file, IEnumerable<ExtRecord> ext_list, uint name_size)
        {
            var dir = new List<Entry> (ext_list.Sum (ext => ext.FileCount));
            foreach (var ext in ext_list)
            {
                uint dir_offset = ext.DirOffset;
                for (int i = 0; i < ext.FileCount; ++i)
                {
                    string name = file.View.ReadString (dir_offset, name_size);
                    if (string.IsNullOrEmpty (name))
                        return null;
                    name = name.ToLowerInvariant();
                    if (ext.Extension.Length > 0)
                        name = Path.ChangeExtension (name, ext.Extension);
                    var entry = Create<Entry> (name);
                    entry.Size = file.View.ReadUInt32 (dir_offset+name_size);
                    entry.Offset = file.View.ReadUInt32 (dir_offset+name_size+4);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    dir_offset += name_size+8;
                }
            }
            return dir;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!IsScriptFile (entry.Name))
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            DecodeScript (data);
            return new BinMemoryStream (data, entry.Name);
        }

        private static void DecodeScript (byte[] data)
        {
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] = Binary.RotByteR (data[i], 2);
            }
        }

        private static void EncodeScript (byte[] data)
        {
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] = Binary.RotByteL (data[i], 2);
            }
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new ArcOptions { NameLength = Properties.Settings.Default.ARCNameLength };
        }

        public override object GetCreationWidget ()
        {
            return new GUI.CreateARCWidget();
        }

        internal class ArcEntry : Entry
        {
            public byte[]   RawName;
        }

        internal class ArcDirectory
        {
            public byte[]   Extension;
            public uint     DirOffset;
            public List<ArcEntry> Files;
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            var arc_options = GetOptions<ArcOptions> (options);
            var encoding = Encodings.cp932.WithFatalFallback();

            int file_count = 0;
            var file_table = new SortedDictionary<string, ArcDirectory>();
            foreach (var entry in list)
            {
                string ext = Path.GetExtension (entry.Name).TrimStart ('.').ToUpperInvariant();
                if (ext.Length > 3)
                    throw new InvalidFileName (entry.Name, arcStrings.MsgExtensionTooLong);
                string name = Path.GetFileNameWithoutExtension (entry.Name).ToUpperInvariant();
                byte[] raw_name = encoding.GetBytes (name);
                if (raw_name.Length > arc_options.NameLength)
                    throw new InvalidFileName (entry.Name, arcStrings.MsgFileNameTooLong);

                ArcDirectory dir;
                if (!file_table.TryGetValue (ext, out dir))
                {
                    byte[] raw_ext = encoding.GetBytes (ext);
                    if (raw_ext.Length > 3)
                        throw new InvalidFileName (entry.Name, arcStrings.MsgExtensionTooLong);
                    dir = new ArcDirectory { Extension = raw_ext, Files = new List<ArcEntry>() };
                    file_table[ext] = dir;
                }
                dir.Files.Add (new ArcEntry { Name = entry.Name, RawName = raw_name });
                ++file_count;
            }
            if (null != callback)
                callback (file_count+1, null, null);

            int callback_count = 0;
            long dir_offset = 4 + file_table.Count * 12;
            long data_offset = dir_offset + (arc_options.NameLength + 9) * file_count;
            output.Position = data_offset;
            foreach (var ext in file_table.Keys)
            {
                var dir = file_table[ext];
                dir.DirOffset = (uint)dir_offset;
                dir_offset += (arc_options.NameLength + 9) * dir.Files.Count;
                foreach (var entry in dir.Files)
                {
                    if (null != callback)
                        callback (callback_count++, entry, arcStrings.MsgAddingFile);
                    entry.Offset = data_offset;
                    entry.Size = WriteEntry (entry.Name, output);
                    data_offset += entry.Size;
                    if (data_offset > uint.MaxValue)
                        throw new FileSizeException();
                }
            }
            if (null != callback)
                callback (callback_count++, null, arcStrings.MsgWritingIndex);

            output.Position = 0;
            using (var header = new BinaryWriter (output, encoding, true))
            {
                byte[] buffer = new byte[arc_options.NameLength+1];
                header.Write (file_table.Count);
                foreach (var ext in file_table)
                {
                    Buffer.BlockCopy (ext.Value.Extension, 0, buffer, 0, ext.Value.Extension.Length);
                    for (int i = ext.Value.Extension.Length; i < 4; ++i)
                        buffer[i] = 0;
                    header.Write (buffer, 0, 4);
                    header.Write (ext.Value.Files.Count);
                    header.Write (ext.Value.DirOffset);
                }
                foreach (var ext in file_table)
                {
                    foreach (var entry in ext.Value.Files)
                    {
                        Buffer.BlockCopy (entry.RawName, 0, buffer, 0, entry.RawName.Length);
                        for (int i = entry.RawName.Length; i < buffer.Length; ++i)
                            buffer[i] = 0;
                        header.Write (buffer);
                        header.Write (entry.Size);
                        header.Write ((uint)entry.Offset);
                    }
                }
            }
        }

        private uint WriteEntry (string filename, Stream output)
        {
            if (!IsScriptFile (filename))
            {
                using (var input = File.OpenRead (filename))
                {
                    var size = input.Length;
                    if (size > uint.MaxValue)
                        throw new FileSizeException();
                    input.CopyTo (output);
                    return (uint)size;
                }
            }
            else
            {
                var input = File.ReadAllBytes (filename);
                EncodeScript (input);
                output.Write (input, 0, input.Length);
                return (uint)input.Length;
            }
        }

        private static bool IsScriptFile (string filename)
        {
            return filename.HasAnyOfExtensions ("scr", "wsc");
        }
    }
}
