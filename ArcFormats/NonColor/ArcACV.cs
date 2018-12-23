//! \file       ArcACV.cs
//! \date       Wed Dec 28 09:49:14 2016
//! \brief      Mirai resource archive.
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

using System.ComponentModel.Composition;

namespace GameRes.Formats.NonColor
{
    [Export(typeof(ArchiveFormat))]
    public class AcvOpener : DatOpener
    {
        public override string         Tag { get { return "ACV"; } }
        public override string Description { get { return "Mirai resource archive"; } }
        public override uint     Signature { get { return 0x31564341; } } // 'ACV1'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public AcvOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint key = 0x8B6A4E5F;
            int count = file.View.ReadInt32 (4) ^ (int)key;
            if (!IsSaneCount (count))
                return null;

            var scheme = QueryScheme (file.Name);
            if (null == scheme)
                return null;

            using (var index = new NcIndexReader (file, count, key) { IndexPosition = 8 })
                return index.Read (this, scheme);
        }
    }
}
