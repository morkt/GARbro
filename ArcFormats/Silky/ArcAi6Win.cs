//! \file       ArcAi6Win.cs
//! \date       Sat Nov 28 12:44:51 2015
//! \brief      Ai6Win engine resource archive
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
using System.Linq;
using GameRes.Compression;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.Silky
{
    [Export(typeof(ArchiveFormat))]
    public class Ai6Opener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/AI6WIN"; } }
        public override string Description { get { return "AI6WIN engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return true; } }

        public Ai6Opener ()
        {
            Extensions = new string[] { "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            long index_offset = 4;
            uint index_size = (uint)(count * (0x104 + 12));
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            var name_buffer = new byte[0x104];
            var dir = new List<Entry>();
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (index_offset, name_buffer, 0, (uint)name_buffer.Length);
                int name_length = Array.IndexOf<byte> (name_buffer, 0);
                if (0 == name_length)
                    return null;
                if (-1 == name_length)
                    name_length = name_buffer.Length;
                byte key = (byte)(name_length+1);
                for (int j = 0; j < name_length; ++j)
                {
                    name_buffer[j] -= key--;
                    char c = (char)name_buffer[j];
                    if (VFS.InvalidFileNameChars.Contains (c) && c != '/')
                        return null;
                }
                var name = Encodings.cp932.GetString (name_buffer, 0, name_length);
                index_offset += 0x104;
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Size          = Binary.BigEndian (file.View.ReadUInt32 (index_offset));
                entry.UnpackedSize  = Binary.BigEndian (file.View.ReadUInt32 (index_offset+4));
                entry.Offset        = Binary.BigEndian (file.View.ReadUInt32 (index_offset+8));
                if (entry.Offset < index_size+4 || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.IsPacked = entry.Size != entry.UnpackedSize;
                dir.Add (entry);
                index_offset += 12;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new LzssStream (input);
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options, EntryCallback callback)
        {
            int count = list.Count ();
            if (0 == count)
                throw new InvalidOperationException ("Archive is empty");

            uint index_size = (uint)(count * (0x104 + 12));
            uint data_offset = 4 + index_size;

            if (null != callback)
                callback (count + 1, null, null);

            var entries = new List<IndexEntry> (count);
            uint current_offset = data_offset;
            int callback_count = 0;

            foreach (var entry in list)
            {
                if (null != callback)
                    callback (callback_count++, entry, arcStrings.MsgAddingFile);

                string name = entry.Name;
                if (name.Contains ("\\"))
                    name = name.Replace ("\\", "/");

                try
                {
                    long file_size = 0;
                    using (var input = File.OpenRead (entry.Name))
                    {
                        file_size = input.Length;
                    }

                    if (file_size > uint.MaxValue)
                        throw new FileLoadException ("File is too large for this format.");

                    var index_entry = new IndexEntry
                    {
                        SourcePath = entry.Name,
                        ArchiveName = name,
                        Size = (uint)file_size,
                        UnpackedSize = (uint)file_size,
                        Offset = current_offset
                    };

                    entries.Add (index_entry);
                    current_offset += (uint)file_size;
                }
                catch (Exception X)
                {
                    // 修复点：此处改用硬编码字符串或尝试 MsgFileError
                    throw new InvalidFileName (entry.Name, "Error opening file", X);
                }
            }

            if (null != callback)
                callback (callback_count++, null, arcStrings.MsgWritingIndex);

            using (var writer = new BinaryWriter (output))
            {
                writer.Write ((uint)count);

                foreach (var entry in entries)
                {
                    var name_bytes = Encodings.cp932.GetBytes (entry.ArchiveName);
                    var name_buf = new byte[0x104];

                    int copyLength = Math.Min (name_bytes.Length, name_buf.Length);
                    Array.Copy (name_bytes, name_buf, copyLength);

                    byte key = (byte)(name_bytes.Length + 1);

                    for (int i = 0; i < copyLength; ++i)
                    {
                        name_buf[i] = (byte)((name_buf[i] + key) & 0xFF);
                        key--;
                    }

                    writer.Write (name_buf);
                    writer.Write (Binary.BigEndian (entry.Size));
                    writer.Write (Binary.BigEndian (entry.UnpackedSize));
                    writer.Write (Binary.BigEndian (entry.Offset));
                }

                foreach (var entry in entries)
                {
                    using (var input = File.OpenRead (entry.SourcePath))
                    {
                        input.CopyTo (output);
                    }
                }
            }
        }

        private class IndexEntry
        {
            public string SourcePath { get; set; }
            public string ArchiveName { get; set; }
            public uint Size { get; set; }
            public uint UnpackedSize { get; set; }
            public uint Offset { get; set; }
        }
    }
}
