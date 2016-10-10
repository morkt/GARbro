//! \file       ArcGSP.cs
//! \date       Wed Mar 04 11:30:32 2015
//! \brief      GSP archive implementation.
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

namespace GameRes.Formats.BlackRainbow
{
    [Export(typeof(ArchiveFormat))]
    public class GspOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GSP"; } }
        public override string Description { get { return "GSP resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            uint index_size = 0x40u * (uint)count;
            if (index_size > file.View.Reserve (4, index_size))
                return null;
            var dir = new List<Entry> (count);
            long index_offset = 4;
            long data_offset = index_offset + index_size;
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset+8, 0x38);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset);
                entry.Size   = file.View.ReadUInt32 (index_offset+4);
                if (entry.Offset < data_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x40;
            }
            return new ArcFile (file, this, dir);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/BR"; } }
        public override string Description { get { return "BlackRainbow resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat", "pak" };
            Signatures = new uint[] { 2u, 4u, 5u, 6u };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            uint base_offset = file.View.ReadUInt32 (0x0c);
            uint index_offset = 0x10;
            uint index_size = 4u * (uint)count;
            if (base_offset >= file.MaxOffset || base_offset < (index_offset+index_size))
                return null;
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            var index = new List<uint> (count);
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                if (offset != 0xffffffff)
                    index.Add (base_offset + offset);
                index_offset += 4;
            }
            var dir = new List<Entry> (index.Count);
            for (int i = 0; i < index.Count; ++i)
            {
                long offset = index[i];
                string name = file.View.ReadString (offset, 0x20);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = offset + 0x24;
                entry.Size   = file.View.ReadUInt32 (offset+0x20);
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
