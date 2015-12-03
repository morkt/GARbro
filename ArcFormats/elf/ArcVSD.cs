//! \file       ArcVSD.cs
//! \date       Mon Nov 30 23:44:53 2015
//! \brief      Silky's MPG video.
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

namespace GameRes.Formats.Silky
{
    [Export(typeof(ArchiveFormat))]
    public class VsdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "VSD/AI5WIN"; } }
        public override string Description { get { return "AI5WIN engine video file"; } }
        public override uint     Signature { get { return 0x31445356; } } // 'VSD1'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public VsdOpener ()
        {
            Extensions = new string[] { "vsd" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint skip = file.View.ReadUInt32 (4);
            if (skip >= file.MaxOffset)
                return null;

            var dir = new List<Entry> (1);
            dir.Add (new Entry {
                Name = Path.GetFileNameWithoutExtension (file.Name)+".mpg",
                Type = "video",
                Offset = 8+skip,
                Size = (uint)(file.MaxOffset-(8+skip)),
            });
            return new ArcFile (file, this, dir);
        }
    }
}
