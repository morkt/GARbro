//! \file       ArcMSP.cs
//! \date       2026-10-01
//! \brief      NAUTS screenshot archive format.
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

namespace GameRes.Formats.Nauts
{
    [Export(typeof(ArchiveFormat))]
    public class MspOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MSP/NAUTS"; } }
        public override string Description { get { return "NAUTS MSP screenshot archive"; } }
        public override uint     Signature { get { return 0x4D535020; } } // ' PSM'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public MspOpener ()
        {
            Extensions = new string[] { "msp" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension ("msp"))
                return null;
            if (Signature != file.View.ReadUInt32 (0))
                return null;
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            long index_size = HeaderSize + (long)count * EntrySize;
            if (index_size > file.MaxOffset)
                return null;
            var dir = new List<Entry> (count);
            long index_offset = HeaderSize;
            long data_offset = index_size;
            for (int i = 0; i < count; ++i)
            {
                var entry = FormatCatalog.Instance.Create<Entry> (file.View.ReadString (index_offset, 16));
                entry.Offset = data_offset;
                entry.Size = file.View.ReadUInt32 (index_offset + 20);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                data_offset += entry.Size;
                index_offset += EntrySize;
            }
            return new ArcFile (file, this, dir);
        }

        const long HeaderSize = 0x14;
        const int  EntrySize  = 0x28;
    }
}