//! \file       ArcMBF.cs
//! \date       Sat Jan 28 18:07:18 2017
//! \brief      Graphic archive format by Tanaka Tatsuhiro.
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

namespace GameRes.Formats.Will
{
    [Export(typeof(ArchiveFormat))]
    public class MbfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MBF"; } }
        public override string Description { get { return "Tanaka Tatsuhiro's engine image archive"; } }
        public override uint     Signature { get { return 0x3046424D; } } // 'MBF0'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public MbfOpener ()
        {
            Signatures = new uint[] { 0x3046424D, 0x3146424D };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint data_offset = file.View.ReadUInt32 (8);
            uint index_offset = 0x20;
            if (0 != (file.View.ReadByte (0xC) & 1) && count > 1)
            {
                index_offset += file.View.ReadUInt16 (index_offset);
                --count;
            }
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint name_length = file.View.ReadUInt16 (index_offset);
                if (name_length < 3)
                    return null;
                var name = file.View.ReadString (index_offset+2, name_length-2);
                if (0 == name.Length)
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                dir.Add (entry);
                index_offset += name_length;
            }
            foreach (var entry in dir)
            {
                if (file.View.AsciiEqual (data_offset, "BC"))
                {
                    entry.Size = file.View.ReadUInt32 (data_offset+2);
                    entry.Type = "image";
                }
                else if (file.View.AsciiEqual (data_offset, "$SEQ"))
                    entry.Size = file.View.ReadUInt32 (data_offset+4);
                else
                    return null;
                entry.Offset = data_offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                data_offset += entry.Size;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
