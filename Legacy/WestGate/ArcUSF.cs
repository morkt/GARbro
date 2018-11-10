//! \file       ArcALH.cs
//! \date       2017 Dec 15
//! \brief      West Gate resource archive.
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

namespace GameRes.Formats.WestGate
{
    [Export(typeof(ArchiveFormat))]
    public class UsfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "USF"; } }
        public override string Description { get { return "West Gate resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public UsfOpener ()
        {
            Extensions = new string[] { "alh", "usf", "udc", "uwb", "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint first_offset = file.View.ReadUInt32 (0xC);
            if (first_offset >= file.MaxOffset || 0 != (first_offset & 0xF))
                return null;
            int count = (int)(first_offset / 0x10);
            if (!IsSaneCount (count))
                return null;

            var dir = UcaTool.ReadIndex (file, 0, count, "");
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
