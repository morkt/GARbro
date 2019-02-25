//! \file       ArcPulltop.cs
//! \date       Sun Nov 29 04:43:52 2015
//! \brief      Pulltop archives implementation.
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

using GameRes.Utility;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Formats.Strings;
using GameRes.Compression;

namespace GameRes.Formats.Will
{
    [Export(typeof(ArchiveFormat))]
    public class Arc2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/WillV2"; } }
        public override string Description { get { return "Will Co. game engine resource archive v2"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return true; } }

        public Arc2Opener ()
        {
            Extensions = new string[] { "arc", "ar2" };
            ContainedFormats = new[] { "PNG", "PNA", "PSB", "OGG", "SCR" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 8;
            uint index_size = file.View.ReadUInt32 (4);
            uint base_offset = index_offset + index_size;
            if (index_size > base_offset || base_offset >= file.MaxOffset)
                return null;
            file.View.Reserve (index_offset, index_size);

            var dir = new List<Entry> (count);
            var name_buffer = new StringBuilder (0x40);
            for (int i = 0; i < count; ++i)
            {
                if (index_offset >= base_offset)
                    return null;
                uint size = file.View.ReadUInt32 (index_offset);
                long offset = (long)base_offset + file.View.ReadUInt32 (index_offset+4);
                index_offset += 8;
                name_buffer.Clear();
                for (;;)
                {
                    if (index_offset >= base_offset)
                        return null;
                    char c = (char)file.View.ReadUInt16 (index_offset);
                    index_offset += 2;
                    if (0 == c)
                        break;
                    name_buffer.Append (c);
                }
                if (0 == name_buffer.Length)
                    return null;
                var name = name_buffer.ToString();
                var entry = Create<Entry> (name);
                entry.Offset = offset;
                entry.Size = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            if (index_offset != base_offset)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!IsScriptFile (entry.Name) || Path.GetFileName (arc.File.Name).Contains ("Model"))
            {
                if (entry.Name.HasExtension (".PSP"))
                    return OpenPsp (arc, entry);
                return base.OpenEntry (arc, entry);
            }
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] = Binary.RotByteR (data[i], 2);
            }
            return new BinMemoryStream (data, entry.Name);
        }

        Stream OpenPsp (ArcFile arc, Entry entry)
        {
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
            {
                int unpacked_size = input.ReadInt32();
                var output = new byte[unpacked_size];
                int dst = 0;
                var frame = new byte[0x1000];
                int frame_pos = 1;
                while (dst < unpacked_size)
                {
                    int ctl = input.ReadByte();
                    for (int bit = 1; dst < unpacked_size && bit != 0x100; bit <<= 1)
                    {
                        if (0 != (ctl & bit))
                        {
                            byte b = input.ReadUInt8();
                            output[dst++] = frame[frame_pos++ & 0xFFF] = b;
                        }
                        else
                        {
                            int hi = input.ReadByte();
                            int lo = input.ReadByte();
                            int offset = hi << 4 | lo >> 4;
                            for (int count = 2 + (lo & 0xF); count != 0; --count)
                            {
                                byte v = frame[offset++ & 0xFFF];
                                output[dst++] = frame[frame_pos++ & 0xFFF] = v;
                            }
                        }
                    }
                }
                return new BinMemoryStream (output, entry.Name);
            }
        }

        static bool IsScriptFile (string name)
        {
            return name.HasAnyOfExtensions ("ws2", "json");
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            int file_count = list.Count();
            var names = new List<byte[]> (file_count);
            int index_size = 0;
            foreach (var entry in list)
            {
                var utf16_name = Encoding.Unicode.GetBytes (Path.GetFileName (entry.Name));
                names.Add (utf16_name);
                index_size += 8 + utf16_name.Length + 2;
            }
            int callback_count = 0;
            uint current_offset = 0;
            output.Position = 8 + index_size;
            foreach (var entry in list)
            {
                if (null != callback)
                    callback (callback_count++, entry, arcStrings.MsgAddingFile);
                entry.Offset = current_offset;
                using (var input = File.OpenRead (entry.Name))
                {
                    var size = input.Length;
                    if (size > uint.MaxValue || current_offset + size > uint.MaxValue)
                        throw new FileSizeException();
                    if (IsScriptFile (entry.Name))
                        CopyScript (input, output);
                    else
                        input.CopyTo (output);
                    current_offset += (uint)size;
                    entry.Size = (uint)size;
                }
            }
            if (null != callback)
                callback (callback_count++, null, arcStrings.MsgWritingIndex);
            output.Position = 0;
            using (var writer = new BinaryWriter (output, Encoding.Unicode, true))
            {
                writer.Write (file_count);
                writer.Write (index_size);
                int i = 0;
                foreach (var entry in list)
                {
                    writer.Write (entry.Size);
                    writer.Write ((uint)entry.Offset);
                    writer.Write (names[i++]);
                    writer.Write ((short)0);
                }
            }
        }

        void CopyScript (Stream input, Stream output)
        {
            var buffer = new byte[81920];
            for (;;)
            {
                int read = input.Read (buffer, 0, buffer.Length);
                if (0 == read)
                    break;
                for (int i = 0; i < read; ++i)
                {
                    buffer[i] = Binary.RotByteL (buffer[i], 2);
                }
                output.Write (buffer, 0, read);
            }
        }
    }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "PSP")]
    [ExportMetadata("Target", "PSB")]
    public class PspFormat : ResourceAlias { }
}
