//! \file       ArcWAG.cs
//! \date       Tue Aug 11 08:28:28 2015
//! \brief      Xuse/Eternal resource archive.
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

namespace GameRes.Formats.Xuse
{
    [Export(typeof(ArchiveFormat))]
    public class WagOpener : ArchiveFormat
    {
        public override string         Tag { get { return "WAG"; } }
        public override string Description { get { return "Xuse/Eternal resource archive"; } }
        public override uint     Signature { get { return 0x40474157; } } // 'WAG@'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public WagOpener ()
        {
            Extensions = new string[] { "wag", "4ag" };
            Signatures = new uint[] { 0x40474157, 0x34464147 }; // 'GAF4'
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (0x0300 != file.View.ReadUInt16 (4))
                return null;
            int count = file.View.ReadInt32 (0x46);
            if (!IsSaneCount (count))
                return null;

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = FormatCatalog.Instance.CreateEntry (name);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
