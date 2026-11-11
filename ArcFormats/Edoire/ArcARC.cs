//! \file       ArcARC.cs
//! \date       2026 Feb 02
//! \brief      Edoire's resource archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text;

namespace GameRes.Formats.Edoire
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC"; } }
        public override string Description { get { return "Edoire's resource archive"; } }
        public override uint     Signature { get { return 0x43524140; } } // "@ARCH000"
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "@ARCH000"))
                return null;
            var index_offset = file.View.ReadInt64 (file.MaxOffset-8);
            if (index_offset <= 0 || index_offset >= file.MaxOffset-12)
                return null;
            var count = file.View.ReadInt32 (index_offset);
            if (!IsSaneCount (count))
                return null;
            index_offset += 4;
            var dir = new List<Entry> (count);
            for (var i = 0; i < count; i++)
            {
                var len = file.View.ReadByte (index_offset);
                index_offset += 1;
                var name = file.View.ReadString (index_offset, len, Encoding.UTF8);
                index_offset += len;
                var offset = file.View.ReadInt64 (index_offset);
                index_offset += 8;
                var size = file.View.ReadInt64 (index_offset);
                index_offset += 9;
                len = file.View.ReadByte (index_offset);
                index_offset += 1;
                var path = file.View.ReadString (index_offset, len, Encoding.UTF8);
                index_offset += len;
                if (path.StartsWith ("/"))
                    path = path.Substring (1);
                if (!string.IsNullOrEmpty (path) && !path.EndsWith ("/"))
                    path += "/";
                var entry = Create<Entry> (path+name);
                entry.Offset = offset;
                entry.Size = Convert.ToUInt32 (size);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
