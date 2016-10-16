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
using System.Text;

namespace GameRes.Formats.Will
{
    [Export(typeof(ArchiveFormat))]
    public class Arc2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/WillV2"; } }
        public override string Description { get { return "Will Co. game engine resource archive v2"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Arc2Opener ()
        {
            Extensions = new string[] { "arc", "ar2" };
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
                var entry = FormatCatalog.Instance.Create<Entry> (name);
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
            if (!entry.Name.EndsWith (".ws2", StringComparison.InvariantCultureIgnoreCase))
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] = Binary.RotByteR (data[i], 2);
            }
            return new BinMemoryStream (data, entry.Name);
        }
    }
}
