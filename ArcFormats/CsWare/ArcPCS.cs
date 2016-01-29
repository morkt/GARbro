//! \file       ArcPCS.cs
//! \date       Mon Jan 25 01:36:53 2016
//! \brief      C's ware resource archive.
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
using GameRes.Utility;

namespace GameRes.Formats.CsWare
{
    internal class PcsEntry : Entry
    {
        public byte Key;
    }

    [Export(typeof(ArchiveFormat))]
    public class PcsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PCS"; } }
        public override string Description { get { return "C's ware resource archive"; } }
        public override uint     Signature { get { return 0x53434350; } } // 'PCCS'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadUInt16 (4);
            if (version < 1 || version > 4)
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            uint data_offset = file.View.ReadUInt32 (12);
            int index_size = (int)data_offset - 0x10;

            int index_offset = 0x10;
            var index_buffer = new byte[0x40];
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int name_length;
                if (version > 1)
                {
                    name_length = file.View.ReadInt32 (index_offset);
                    if (0 == name_length)
                        break;
                    if (name_length > index_size)
                        return null;
                    index_offset += 5 + name_length;
                }
                name_length = file.View.ReadInt32 (index_offset);
                if (0 == name_length)
                    break;
                if (name_length > index_size)
                    return null;
                if (name_length > index_buffer.Length)
                    index_buffer = new byte[name_length];
                file.View.Read (index_offset+5, index_buffer, 0, (uint)name_length);
                index_offset += 5 + name_length;
                byte checksum;
                var name = DecryptName (index_buffer, name_length, out checksum);
                Entry entry;
                if (4 == version)
                {
                    entry = FormatCatalog.Instance.Create<PcsEntry> (name);
                    (entry as PcsEntry).Key = checksum;
                    int c = -1 - checksum;
                    file.View.Read (index_offset, index_buffer, 0, 8);
                    for (int j = 0; j < 4; ++j)
                    {
                        byte key = (byte)((checksum + (17 << j)) & 0x33);
                        index_buffer[j]   = (byte)(c + key - index_buffer[j]);
                        index_buffer[j+4] = (byte)(c + key - index_buffer[j+4]);
                    }
                    entry.Offset = LittleEndian.ToUInt32 (index_buffer, 0);
                    entry.Size   = LittleEndian.ToUInt32 (index_buffer, 4);
                }
                else
                {
                    entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = file.View.ReadUInt32 (index_offset);
                    entry.Size   = file.View.ReadUInt32 (index_offset+4);
                }
                index_offset += 0x10;
                if (index_offset > data_offset)
                    return null;
                entry.Offset += data_offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PcsEntry;
            if (null == pent)
                return base.OpenEntry (arc, entry);
            uint header_size = Math.Min (entry.Size, 512u);
            var header = arc.File.View.ReadBytes (entry.Offset, header_size);
            for (int i = 0; i < header.Length; ++i)
            {
                header[i] = (byte)(pent.Key - header[i] - 1);
            }
            if (header_size == entry.Size)
                return new MemoryStream (header);
            var rest = arc.File.CreateStream (entry.Offset+512, entry.Size-512);
            return new PrefixStream (header, rest);
        }

        string DecryptName (byte[] name_buffer, int length, out byte checksum)
        {
            int count;
            checksum = 0;
            for (count = 0; count < length; ++count)
            {
                if (0 == name_buffer[count])
                    break;
                name_buffer[count] = Binary.RotByteL (name_buffer[count], 4);
                checksum += name_buffer[count];
            }
            return Encodings.cp932.GetString (name_buffer, 0, count);
        }
    }
}
