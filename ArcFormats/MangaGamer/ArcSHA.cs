//! \file       ArcSHA.cs
//! \date       2018 Jul 23
//! \brief      
//
// Copyright (C) 2018 by morkt
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
using System.Text;

namespace GameRes.Formats.Mg
{
    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/SHA"; } }
        public override string Description { get { return "MG resource archive"; } }
        public override uint     Signature { get { return 0x5F414853; } } // 'SHA_'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            long cur_offset = 0x0C;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint name_length = file.View.ReadByte (cur_offset);
                string name = file.View.ReadString (cur_offset+1, name_length, Encoding.UTF8);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (cur_offset+0x40);
                entry.Size = file.View.ReadUInt32 (cur_offset+0x44);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                cur_offset += 0x50;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
