//! \file       GenericVideo.cs
//! \date       2026-09-25
//! \brief      Generic video formats.
//
// Copyright (C) 2026 by morkt
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

namespace GameRes.Formats
{
    [Export(typeof(ArchiveFormat))]
    public class WmvOpener : ArchiveFormat
    {
        public override string         Tag => "GENERIC WMV";
        public override string Description => "Generic Windows Media Video Format"; // will not display
        public override uint     Signature => 0x75B22630;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadUInt32 (4) != 0x11CF668E)
                return null;
            return new WrapSingleFileArchive (file, Path.GetFileNameWithoutExtension (file.Name)+".wmv");
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class AviOpener : ArchiveFormat
    {
        public override string         Tag => "GENERIC AVI";
        public override string Description => "Generic Audio Video Interleave Format";
        public override uint     Signature => 0x46464952; // 'RIFF'
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadUInt32 (8) != 0x20495641) // 'AVI '
                return null;
            return new WrapSingleFileArchive (file, Path.GetFileNameWithoutExtension (file.Name)+".avi");
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class MpgOpener : ArchiveFormat
    {
        public override string         Tag => "GENERIC MPG";
        public override string Description => "Generic MPEG Video Format";
        public override uint     Signature => 0xBA010000;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            return new WrapSingleFileArchive (file, Path.GetFileNameWithoutExtension (file.Name)+".mpg");
        }
    }
}
