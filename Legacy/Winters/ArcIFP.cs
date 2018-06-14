//! \file       ArcIFP.cs
//! \date       2018 May 23
//! \brief      Winters resource archive.
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
using System.IO;

// [020426][Winters] Kiss x 200 To Aru Bunkou no Hanashi
// [040528][Winters] Kiss x 300 Konna Sekai
// [051216][Winters] Kiss x 400 Natsukashiki Hibi no Renzoku

namespace GameRes.Formats.Winters
{
    [Export(typeof(ArchiveFormat))]
    public class IfpOpener : ArchiveFormat
    {
        public override string         Tag { get { return "IFP"; } }
        public override string Description { get { return "Winters resource archive"; } }
        public override uint     Signature { get { return 0x53474149; } } // 'IAGS_IFP_01'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "_IFP_01     ")
                || file.View.ReadInt32 (0x10) != 1)
                return null;
            int count = file.View.ReadInt32 (0x18) / 0x10 - 1;
            if (!IsSaneCount (count))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry>();
            for (int i = 0, index_offset = 0x20; i < count; ++i, index_offset += 0x10)
            {
                int type = file.View.ReadUInt16 (index_offset);
                if (0 == type)
                    continue;
                int mask_type = file.View.ReadUInt16 (index_offset+2);
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D5}", base_name, i),
                    Offset = file.View.ReadUInt32 (index_offset+4),
                    Size = file.View.ReadUInt32 (index_offset+8),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                switch (type)
                {
                case 0x0B: entry.ChangeType (ImageFormat.Bmp); break;
                case 0x0C: entry.ChangeType (ImageFormat.Png); break;
                case 0x0D: entry.ChangeType (ImageFormat.Jpeg); break;
                case 0x15: entry.Type = "script"; break;
                }
                dir.Add (entry);
                uint mask_size = file.View.ReadUInt32 (index_offset+12);
                if (0x0B == mask_type && 0 != mask_size)
                {
                    entry = new Entry {
                        Name = string.Format ("{0}#{1:D5}M.bmp", base_name, i),
                        Type = "image",
                        Offset = entry.Offset + entry.Size,
                        Size = mask_size,
                    };
                    dir.Add (entry);
                }
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
