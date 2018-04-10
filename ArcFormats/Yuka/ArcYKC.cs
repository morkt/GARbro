//! \file       ArcYKC.cs
//! \date       Thu Aug 13 21:52:01 2015
//! \brief      Yuka engine resource archives.
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
using System.Text;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.Yuka
{
    internal class YukaEntry : Entry
    {
        public uint NameOffset;
        public uint NameLength;
    }

    [Export(typeof(ArchiveFormat))]
    public class YkcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "YKC"; } }
        public override string Description { get { return "Yuka engine resource archive"; } }
        public override uint     Signature { get { return 0x30434B59; } } // 'YKC0'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return true; } }

        public YkcOpener ()
        {
            Extensions = new string[] { "ykc", "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var version = file.View.ReadUInt32 (4);
            if (version != 0x3130 && version != 0x3230)
                return null;
            uint index_offset = file.View.ReadUInt32 (0x10);
            uint index_length = file.View.ReadUInt32 (0x14);
            int count = (int)(index_length / 0x14);
            if (index_offset >= file.MaxOffset || !IsSaneCount (count))
                return null;
            if (index_length > file.View.Reserve (index_offset, index_length))
                return null;

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new YukaEntry();
                entry.NameOffset = file.View.ReadUInt32 (index_offset);
                entry.NameLength = file.View.ReadUInt32 (index_offset+4);
                entry.Offset = file.View.ReadUInt32 (index_offset+8);
                entry.Size   = file.View.ReadUInt32 (index_offset+0xC);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x14;
            }
            Encoding encoding = 0x3130 == version ? Encodings.cp932 : Encoding.UTF8;
            // read in two cycles to avoid memory mapped file page switching when accessing names
            foreach (YukaEntry entry in dir)
            {
                entry.Name = file.View.ReadString (entry.NameOffset, entry.NameLength, encoding);
                entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Size < 0x24
                || !entry.Name.HasExtension (".yks")
                || !arc.File.View.AsciiEqual (entry.Offset, "YKS001")
                || 1 != arc.File.View.ReadUInt16 (entry.Offset+6))
                return base.OpenEntry (arc, entry);
            // decrypt script contents
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            uint text_offset = LittleEndian.ToUInt32 (data, 0x20);
            for (uint i = text_offset; i < data.Length; ++i)
                data[i] ^= 0xAA;
            data[6] = 0;
            return new BinMemoryStream (data, entry.Name);
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            output.Position = 0x18;
            int callback_count = 0;
            foreach (var entry in list)
            {
                using (var file = File.OpenRead (entry.Name))
                {
                    var file_size = file.Length;
                    if (file_size > uint.MaxValue)
                        throw new FileSizeException();
                    long file_offset = output.Position;
                    if (file_offset+file_size > uint.MaxValue)
                        throw new FileSizeException();
                    entry.Offset = file_offset;
                    entry.Size = (uint)file_size;
                    if (null != callback)
                        callback (callback_count++, entry, arcStrings.MsgAddingFile);

                    file.CopyTo (output);
                }
            }

            if (null != callback)
                callback (callback_count++, null, arcStrings.MsgWritingIndex);

            byte[] name_buf = new byte[0x40];
            var encoding = Encodings.cp932.WithFatalFallback();
            int count = list.Count();
            var name_offsets = new uint[count];
            var name_sizes   = new  int[count];
            int i = 0;
            foreach (var entry in list)
            {
                int length = encoding.GetByteCount (entry.Name);
                if (length+1 > name_buf.Length)
                    name_buf = new byte[length+2];
                length = encoding.GetBytes (entry.Name, 0, entry.Name.Length, name_buf, 0);
                name_buf[length++] = 0;

                name_offsets[i] = (uint)output.Position;
                output.Write (name_buf, 0, length);
                name_sizes[i] = length;
                ++i;
                if (output.Position > uint.MaxValue)
                    throw new FileSizeException();
            }
            uint index_offset = (uint)output.Position;
            using (var writer = new BinaryWriter (output, encoding, true))
            {
                i = 0;
                foreach (var entry in list)
                {
                    writer.Write (name_offsets[i]);
                    writer.Write (name_sizes[i]);
                    writer.Write ((uint)entry.Offset);
                    writer.Write (entry.Size);
                    writer.Write (0);
                    ++i;
                }
                uint index_length = (uint)(output.Position - index_offset);
                output.Position = 0;
                encoding.GetBytes ("YKC001\0\0", 0, 8, name_buf, 0);
                writer.Write (name_buf, 0, 8);
                writer.Write (0x18);
                output.Position = 0x10;
                writer.Write (index_offset);
                writer.Write (index_length);
            }
        }
    }
}
