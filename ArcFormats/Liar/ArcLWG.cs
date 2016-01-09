//! \file       ArcLWG.cs
//! \date       Sun Jan 10 00:40:18 2016
//! \brief      Liar-soft LWG image archive implementation.
//
// Copyright (C) 2014-2016 by morkt
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

namespace GameRes.Formats.Liar
{
    public class LwgImageEntry : ImageEntry
    {
        public int PosX;
        public int PosY;
        public int BPP;
    }

    [Export(typeof(ArchiveFormat))]
    public class LwgOpener : ArchiveFormat
    {
        public override string         Tag { get { return "LWG"; } }
        public override string Description { get { return Strings.arcStrings.LWGDescription; } }
        public override uint     Signature { get { return 0x0001474C; } } // 'LG'
        public override bool  IsHierarchic { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint height = file.View.ReadUInt32 (4);
            uint width  = file.View.ReadUInt32 (8);
            int count   = file.View.ReadInt32 (12);
            if (!IsSaneCount (count))
                return null;
            uint dir_size = file.View.ReadUInt32 (20);
            uint cur_offset = 24;
            uint data_offset = cur_offset + dir_size;
            uint data_size = file.View.ReadUInt32 (data_offset);
            data_offset += 4;

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new LwgImageEntry();
                entry.PosX = file.View.ReadInt32 (cur_offset);
                entry.PosY = file.View.ReadInt32 (cur_offset+4);
                entry.BPP = file.View.ReadByte (cur_offset+8);
                entry.Offset = data_offset + file.View.ReadUInt32 (cur_offset+9);
                entry.Size = file.View.ReadUInt32 (cur_offset+13);

                uint name_length = file.View.ReadByte (cur_offset+17);
                string name = file.View.ReadString (cur_offset+18, name_length);
                entry.Name = name + ".wcg";
                cur_offset += 18+name_length;
                if (cur_offset > dir_size+24)
                    return null;
                if (entry.CheckPlacement (data_offset + data_size))
                    dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
