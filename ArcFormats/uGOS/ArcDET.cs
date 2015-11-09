//! \file       ArcDET.cs
//! \date       Mon Nov 09 00:16:53 2015
//! \brief      μ-GameOperationSystem resource archive.
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
using GameRes.Utility;

namespace GameRes.Formats.uGOS
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DET"; } }
        public override string Description { get { return "μ-GameOperationSystem resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.EndsWith (".det", StringComparison.InvariantCultureIgnoreCase))
                return null;
            var name_file = Path.ChangeExtension (file.Name, "nme");
            var index_file = Path.ChangeExtension (file.Name, "atm");
            if (!VFS.FileExists (name_file) || !VFS.FileExists (index_file))
                return null;
            using (var nme = VFS.OpenView (name_file))
            using (var idx = VFS.OpenView (index_file))
            {
                var name_table = new byte[nme.MaxOffset];
                nme.View.Read (0, name_table, 0, (uint)name_table.Length);
                uint idx_offset = 0;
                int count = (int)(idx.MaxOffset / 0x14);
                if (!IsSaneCount (count))
                    return null;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    int name_offset = idx.View.ReadInt32 (idx_offset);
                    if (name_offset < 0 || name_offset >= name_table.Length)
                        return null;
                    var name = Binary.GetCString (name_table, name_offset, name_table.Length - name_offset);
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                    entry.Offset = idx.View.ReadUInt32 (idx_offset + 4);
                    entry.Size = idx.View.ReadUInt32 (idx_offset + 8);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    entry.UnpackedSize = idx.View.ReadUInt32 (idx_offset + 0x10);
                    entry.IsPacked = true;
                    dir.Add (entry);
                    idx_offset += 0x14;
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);

            var output = new byte[pent.UnpackedSize];
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
            {
                if (Unpack (input, output))
                    return new MemoryStream (output);
                else
                    return base.OpenEntry (arc, entry);
            }
        }

        bool Unpack (Stream input, byte[] output)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                int ctl = input.ReadByte();
                if (-1 == ctl)
                    return false;
                if (0xFF != ctl)
                {
                    output[dst++] = (byte)ctl;
                }
                else
                {
                    ctl = input.ReadByte();
                    if (-1 == ctl)
                        return false;
                    if (0xFF == ctl)
                    {
                        output[dst++] = 0xFF;
                    }
                    else
                    {
                        int offset = (ctl >> 2) + 1;
                        int count = (ctl & 3) + 3;
                        Binary.CopyOverlapped (output, dst-offset, dst, count);
                        dst += count;
                    }
                }
            }
            return true;
        }
    }
}
