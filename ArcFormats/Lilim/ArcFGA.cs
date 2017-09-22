//! \file       ArcFGA.cs
//! \date       2017 Sep 16
//! \brief      SFA engine resource archive.
//
// Copyright (C) 2017 by morkt
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Utility;

namespace GameRes.Formats.Lilim
{
    [Export(typeof(ArchiveFormat))]
    public class FgaOpener : AosOpener
    {
        public override string         Tag { get { return "FGA"; } }
        public override string Description { get { return "SFA engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".fga"))
                return null;
            uint index_offset = 0;
            var index_buffer = file.View.ReadBytes (index_offset, 0x318);
            if (0x318 != index_buffer.Length)
                return null;
            var dir = new List<Entry> (0x20);
            int pos = 0;
            while (pos < index_buffer.Length)
            {
                if (0 == index_buffer[pos])
                    break;
                if (0xFF == index_buffer[pos])
                {
                    uint next_offset = index_buffer.ToUInt32 (pos+0xC);
                    if (next_offset <= index_offset || next_offset >= file.MaxOffset)
                        return null;
                    index_offset = next_offset;
                    if (0x318 != file.View.Read (index_offset, index_buffer, 0, 0x318))
                        return null;
                    pos = 0;
                }
                else
                {
                    var name = Binary.GetCString (index_buffer, pos, 0xC);
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                    entry.Offset = index_buffer.ToUInt32 (pos+0xC);
                    entry.Size   = index_buffer.ToUInt32 (pos+0x10);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    entry.IsPacked = name.HasExtension (".scr");
                    dir.Add (entry);
                    pos += 0x18;
                }
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
