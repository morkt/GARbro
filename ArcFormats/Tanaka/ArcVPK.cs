//! \file       ArcVPK.cs
//! \date       Sun Jan 29 04:57:12 2017
//! \brief      Audio archive format by Tanaka Tatsuhiro.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Will
{
    [Export(typeof(ArchiveFormat))]
    public class VpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "VPK1"; } }
        public override string Description { get { return "Tanaka Tatsuhiro's engine audio archive"; } }
        public override uint     Signature { get { return 0x314B5056; } } // 'VPK1'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public VpkOpener ()
        {
            Extensions = new string[] { "vpk" };
            Signatures = new uint[] { 0x314B5056, 0x304B5056 };
            ContainedFormats = new[] { "WAV" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint data_size = file.View.ReadUInt32 (4);
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0x20;
            uint index_size = file.View.ReadUInt32 (0xC);
            if (data_size + index_size != file.MaxOffset)
                return null;
            long data_offset = index_offset + index_size;
            if (data_offset >= file.MaxOffset || data_offset <= index_offset)
                return null;
            int version = file.View.ReadByte (3) - '0';
            int name_length = version > 0 ? 4 : 2;
            using (var index = file.CreateStream (index_offset, index_size))
            {
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = index.ReadCString (name_length);
                    if (0 == name.Length)
                        return null;
                    uint n1 = index.ReadUInt16();
                    if (version > 0)
                    {
                        uint n2 = index.ReadUInt16();
                        uint n3 = index.ReadUInt32();
                        name = string.Format ("{0}_{1:D2}_{2}_{3:D3}.wav", name, n1, n2, n3);
                    }
                    else
                    {
                        uint n2 = index.ReadUInt32();
                        name = string.Format ("{0}_{1:D2}_{2:D3}.wav", name, n1, n2);
                    }
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = index.ReadUInt32();
                    entry.Size   = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
