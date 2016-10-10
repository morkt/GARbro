//! \file       ArcYOX.cs
//! \date       Wed Aug 17 10:54:39 2016
//! \brief      Yox ADV+++ resource archives.
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

namespace GameRes.Formats.Yox
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/YOX"; } }
        public override string Description { get { return "YOX ADV+++ engine resource archive"; } }
        public override uint     Signature { get { return 0x584F59; } } // 'YOX'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_offset = file.View.ReadUInt32 (8);
            int count = file.View.ReadInt32 (0xC);
            if (!IsSaneCount (count) || index_offset >= file.MaxOffset)
                return null;

            var dir = new List<Entry> (count);
            Func<uint, bool> ReadIndex = entry_size => {
                uint current_offset = index_offset;
                for (int i = 0; i < count; ++i)
                {
                    var entry = new PackedEntry { Name = i.ToString ("D5") };
                    entry.Offset = file.View.ReadUInt32 (current_offset);
                    entry.Size   = file.View.ReadUInt32 (current_offset+4);
                    if (0 == entry.Size || !entry.CheckPlacement (file.MaxOffset))
                        return false;
                    dir.Add (entry);
                    current_offset += entry_size;
                }
                return true;
            };
            if (!ReadIndex (8))
            {
                dir.Clear();
                if (!ReadIndex (0x10))
                    return null;
            }
            using (var stream = file.CreateStream())
                DetectFileTypes (stream, dir);
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = base.OpenEntry (arc, entry);
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return input;
            return new ZLibStream (input, CompressionMode.Decompress);
        }

        void DetectFileTypes (Stream file, IList<Entry> dir)
        {
            foreach (PackedEntry entry in dir)
            {
                file.Position = entry.Offset;
                uint signature = ReadUInt32 (file);
                IResource res = null;
                if (0x584F59 == signature) // 'YOX'
                {
                    if (0 != (2 & ReadUInt32 (file)))
                    {
                        entry.IsPacked = true;
                        entry.UnpackedSize = ReadUInt32 (file);
                        entry.Offset += 0x10;
                        entry.Size   -= 0x10;
                        file.Position = entry.Offset;
                        using (var input = new ZLibStream (file, CompressionMode.Decompress, true))
                            signature = ReadUInt32 (input);
                        res = AutoEntry.DetectFileType (signature);
                    }
                }
                else
                    res = AutoEntry.DetectFileType (signature);
                if (res != null)
                {
                    entry.Name = Path.ChangeExtension (entry.Name, res.Extensions.FirstOrDefault());
                    entry.Type = res.Type;
                }
            }
        }

        static uint ReadUInt32 (Stream input)
        {
            uint v = (uint)input.ReadByte();
            v |= (uint)input.ReadByte() << 8;
            v |= (uint)input.ReadByte() << 16;
            v |= (uint)input.ReadByte() << 24;
            return v;
        }
    }
}
