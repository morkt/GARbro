//! \file       ArcMajiro.cs
//! \date       Thu Jul 31 20:27:26 2014
//! \brief      Majiro engine resource archive.
//
// Copyright (C) 2014 by morkt
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
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.Majiro
{
    public class MajiroOptions : ResourceOptions
    {
        public uint    Key { get; set; }
        public int Version { get; set; }
    }

    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MAJIRO"; } }
        public override string Description { get { return "Majiro game engine resource archive"; } }
        public override uint     Signature { get { return 0x696a614d; } } // 'Maji'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return true; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "roArcV"))
                return null;
            int version = file.View.ReadByte (0xA) - '0';
            if (version < 1 || version > 3 || !file.View.AsciiEqual (0xB, ".000\0"))
                return null;
            int count = file.View.ReadInt32 (16);
            uint names_offset = file.View.ReadUInt32 (20);
            uint data_offset = file.View.ReadUInt32 (24);
            if (data_offset <= names_offset || data_offset >= file.MaxOffset || !IsSaneCount (count))
                return null;
            int table_size = count + (1 == version ? 1 : 0);
            int entry_size = 4 * (version + 1);
            table_size *= entry_size;
            if (table_size + 0x1c != names_offset)
                return null;
            if (data_offset > file.View.Reserve (0, data_offset))
                return null;
            int names_size = (int)(data_offset - names_offset);
            var names = new byte[names_size];
            file.View.Read (names_offset, names, 0, (uint)names_size);
            int names_pos = 0;
            int table_pos = 0x1c;
            int hash_size = version < 3 ? 4 : 8;
            uint offset_next = file.View.ReadUInt32 (table_pos+hash_size);

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var zero = Array.IndexOf (names, (byte)0, names_pos, names_size);
                if (-1 == zero)
                    break;
                int name_len = zero-names_pos;
                string name = Encodings.cp932.GetString (names, names_pos, name_len);
                names_size -= name_len+1;
                names_pos = zero+1;
                uint offset = offset_next;
                offset_next = file.View.ReadUInt32 (table_pos + entry_size + hash_size);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = offset;
                if (1 == version)
                    entry.Size = offset_next >= offset ? offset_next - offset : 0;
                else
                    entry.Size = file.View.ReadUInt32 (table_pos + hash_size + 4);
                table_pos += entry_size;
                if (offset < data_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            if (!dir.Any())
                return null;
            return new ArcFile (file, this, dir);
        }

        internal class MajiroEntry : Entry
        {
            public byte[]   IndexName;
            public uint     NameHash;
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            int version = 1;
            int file_count = list.Count();
            int callback_count = 0;
            var encoding = Encodings.cp932.WithFatalFallback();
            if (null != callback)
                callback (file_count+2, null, null);

            using (var writer = new BinaryWriter (output, encoding, true))
            {
                if (null != callback)
                    callback (callback_count++, null, arcStrings.MsgWritingIndex);

                string signature = string.Format ("MajiroArcV{0}.000\0", version);
                writer.Write (encoding.GetBytes (signature));

                writer.Write (file_count);
                int index_offset = 0x1c + (file_count+1) * 8;
                writer.Write (index_offset);

                // calculate name hashes
                var real_entry_list = new List<MajiroEntry> (file_count);
                foreach (var entry in list)
                {
                    try
                    {
                        string name = Path.GetFileName (entry.Name);
                        byte[] name_buf = encoding.GetBytes (name);
                        uint crc32 = Crc32.Compute (name_buf, 0, name_buf.Length);
                        var maj_entry = new MajiroEntry
                        {
                            Name        = entry.Name,
                            IndexName   = name_buf,
                            NameHash    = crc32,
                        };
                        real_entry_list.Add (maj_entry);
                    }
                    catch (EncoderFallbackException X)
                    {
                        throw new InvalidFileName (entry.Name, arcStrings.MsgIllegalCharacters, X);
                    }
                }
                real_entry_list.Sort ((a, b) => a.NameHash.CompareTo (b.NameHash));

                // write names
                writer.BaseStream.Seek (index_offset, SeekOrigin.Begin);
                foreach (var entry in real_entry_list)
                {
                    writer.Write (entry.IndexName);
                    writer.Write ((byte)0);
                }

                // write files
                uint start_offset = (uint)output.Position;
                uint current_offset = start_offset;
                foreach (var entry in real_entry_list)
                {
                    if (null != callback)
                        callback (callback_count++, entry, arcStrings.MsgAddingFile);

                    entry.Offset = current_offset;
                    using (var input = File.OpenRead (entry.Name))
                    {
                        var size = input.Length;
                        if (size > uint.MaxValue || current_offset + size > uint.MaxValue)
                            throw new FileSizeException();
                        current_offset += (uint)size;
                        entry.Size = (uint)size;
                        input.CopyTo (output);
                    }
                }
                uint end_offset = (uint)output.Position;

                if (null != callback)
                    callback (callback_count++, null, arcStrings.MsgUpdatingIndex);

                // at last, go back to index and write hashes/offsets
                writer.BaseStream.Position = 0x18;
                writer.Write (start_offset);
                foreach (var entry in real_entry_list)
                {
                    writer.Write (entry.NameHash);
                    writer.Write ((uint)entry.Offset);
                }
                writer.Write ((int)0);
                writer.Write (end_offset);
            }
        }
    }
}
