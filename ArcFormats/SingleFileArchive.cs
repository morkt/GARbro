//! \file       SingleFileArchive.cs
//! \date       2023 Oct 05
//! \brief      represent single file as an archive for convenience.
//
// Copyright (C) 2023 by morkt
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

namespace GameRes.Formats
{
    public class WrapSingleFileArchive : ArcFile
    {
        internal static readonly ArchiveFormat Format = new SingleFileArchiveFormat();

        public WrapSingleFileArchive (ArcView file, Entry entry)
            : base (file, Format, new List<Entry> { entry })
        {
        }

        public WrapSingleFileArchive (ArcView file, string entry_name)
            : base (file, Format, new List<Entry> { CreateEntry (file, entry_name) })
        {
        }

        private static Entry CreateEntry (ArcView file, string name)
        {
            var entry = FormatCatalog.Instance.Create<Entry> (name);
            entry.Offset = 0;
            entry.Size = (uint)file.MaxOffset;
            return entry;
        }

        /// this format is not registered in catalog and only accessible via WrapSingleFileArchive.Format singleton.
        private class SingleFileArchiveFormat : ArchiveFormat
        {
            public override string         Tag => "DAT/BOGUS";
            public override string Description => "Not an archive";
            public override uint     Signature => 0;
            public override bool  IsHierarchic => false;
            public override bool      CanWrite => false;

            public override ArcFile TryOpen (ArcView file)
            {
                return new WrapSingleFileArchive (file, System.IO.Path.GetFileName (file.Name));
            }
        }
    }
}
