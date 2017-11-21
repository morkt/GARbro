//! \file       ArcZENOS.cs
//! \date       2017 Nov 21
//! \brief      Zenos resource archive format.
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

using System.ComponentModel.Composition;

namespace GameRes.Formats.Rpm
{
    [Export(typeof(ArchiveFormat))]
    public class ZenosOpener : ArcOpener
    {
        public override string         Tag { get { return "ARC/ZENOS"; } }
        public override string Description { get { return "Zenos resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count) || 4 + count * 0x1C >= file.MaxOffset)
                return null;

            var index_reader = new ArcIndexReader (file, count, true);
//            var scheme = new EncryptionScheme ("haku", 0x10);
            var scheme = index_reader.GuessScheme (4, new int[] { 0x10 });
            if (null == scheme)
                return null;
            var dir = index_reader.ReadIndex (4, scheme);
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
