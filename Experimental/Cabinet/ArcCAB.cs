//! \file       ArcCAB.cs
//! \date       Sat Dec 24 15:44:20 2016
//! \brief      Microsoft cabinet archive.
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
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using Microsoft.Deployment.Compression.Cab;

namespace GameRes.Formats.Microsoft
{
    internal class CabEntry : Entry
    {
        public readonly CabFileInfo  Info;

        public CabEntry (CabFileInfo file_info)
        {
            Info = file_info;
            Name = Info.Name;
            Type = FormatCatalog.Instance.GetTypeFromName (Info.Name);
            Size = (uint)Math.Min (Info.Length, uint.MaxValue);
            // offset is unknown and reported as '0' for all files.
            Offset = 0;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class CabOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CAB"; } }
        public override string Description { get { return "Microsoft cabinet archive"; } }
        public override uint     Signature { get { return 0x4643534D; } } // 'MSCF'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (VFS.IsVirtual)
                throw new NotSupportedException ("Cabinet files inside archives not supported");
            var info = new CabInfo (file.Name);
            var dir = info.GetFiles().Select (f => new CabEntry (f) as Entry);
            return new ArcFile (file, this, dir.ToList());
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            return ((CabEntry)entry).Info.OpenRead();
        }
    }
}
