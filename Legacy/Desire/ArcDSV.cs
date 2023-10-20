//! \file       ArcDSV.cs
//! \date       2023 Oct 15
//! \brief      Desire resource archive (PC-98).
//
// Copyright (C) 2023 by morkt
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

namespace GameRes.Formats.Desire
{
    [Export(typeof(ArchiveFormat))]
    public class D000Opener : ArchiveFormat
    {
        public override string         Tag => "000/DESIRE";
        public override string Description => "Desire resource archive";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasAnyOfExtensions (".000", ".001", ".002", ".003"))
                return null;
            if (!IsAscii (file.View.ReadByte (0)))
                return null;
            uint index_pos = 0;
            var dir = new List<Entry>();
            while (index_pos < file.MaxOffset)
            {
                byte b = file.View.ReadByte (index_pos);
                if (0 == b)
                    break;
                if (!IsAscii (b))
                    return null;
                var name = file.View.ReadString (index_pos, 0xC);
                var entry = Create<Entry> (name);
                entry.Size = file.View.ReadUInt32 (index_pos+0xC);
                if (entry.Size >= file.MaxOffset || 0 == entry.Size)
                    return null;
                dir.Add (entry);
                index_pos += 0x10;
            }
            if (index_pos >= file.MaxOffset || file.View.ReadUInt32 (index_pos+0xC) != 0)
                return null;
            uint offset = index_pos + 0x10;
            foreach (var entry in dir)
            {
                entry.Offset = offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                offset += entry.Size;
            }
            if (offset != file.MaxOffset)
                return null;
            return new ArcFile (file, this, dir);
        }

        static internal bool IsAscii (byte b)
        {
            return b >= 0x20 && b < 0x7F;
        }
    }
}
