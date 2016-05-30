//! \file       ArcMED.cs
//! \date       Mon May 30 12:35:24 2016
//! \brief      DxLib resource archive.
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

namespace GameRes.Formats.DxLib
{
    [Export(typeof(ArchiveFormat))]
    public class MedOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MED"; } }
        public override string Description { get { return "DxLib engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        static readonly Lazy<ImageFormat> PrsFormat = new Lazy<ImageFormat> (() => ImageFormat.FindByTag ("PRS"));

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "MD"))
                return null;
            uint entry_length = file.View.ReadUInt16 (4);
            int count = file.View.ReadUInt16 (6);
            if (entry_length <= 8 || !IsSaneCount (count))
                return null;

            uint name_length = entry_length - 8;
            uint index_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, name_length);
                index_offset += name_length;
                uint offset = file.View.ReadUInt32 (index_offset+4);

                var entry = new AutoEntry (name, () => {
                    uint signature = file.View.ReadUInt32 (offset);
                    if (0x4259 == (signature & 0xFFFF)) // 'YB'
                        return PrsFormat.Value;
                    return AutoEntry.DetectFileType (signature);
                });
                entry.Size   = file.View.ReadUInt32 (index_offset);
                entry.Offset = offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
