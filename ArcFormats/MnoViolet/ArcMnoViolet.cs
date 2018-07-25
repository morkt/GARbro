//! \file       ArcMnoViolet.cs
//! \date       Fri Apr 03 23:32:37 2015
//! \brief      M no Violet engine archives implementation.
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

namespace GameRes.Formats.MnoViolet
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MNV"; } }
        public override string Description { get { return "M no Violet resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        static readonly uint[] NameSizes = { 100, 68, 44 };
        static readonly Lazy<ImageFormat> s_GraFormat = new Lazy<ImageFormat> (() => ImageFormat.FindByTag ("GRA"));

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            List<Entry> dir = null;
            foreach (var name_size in NameSizes)
            {
                uint index_size = (uint)((name_size+8) * count);
                uint first_offset = file.View.ReadUInt32 (4+name_size+4);
                if (first_offset == (4 + index_size) && first_offset < file.MaxOffset)
                {
                    if (null == dir)
                        dir = new List<Entry> (count);
                    else
                        dir.Clear();
                    long index_offset = 4;
                    for (int i = 0; i < count; ++i)
                    {
                        string name = file.View.ReadString (index_offset, name_size);
                        if (string.IsNullOrWhiteSpace (name))
                            goto CheckNextLength;
                        index_offset += name_size;
                        uint offset = file.View.ReadUInt32 (index_offset+4);
                        var entry = new AutoEntry (name, () => {
                            uint signature = file.View.ReadUInt32 (offset);
                            if (1 == signature)
                                return s_GraFormat.Value;
                            return AutoEntry.DetectFileType (signature);
                        });
                        entry.Offset = offset;
                        entry.Size   = file.View.ReadUInt32 (index_offset);
                        if (offset <= index_size || !entry.CheckPlacement (file.MaxOffset))
                            goto CheckNextLength;
                        dir.Add (entry);
                        index_offset += 8;
                    }
                    return new ArcFile (file, this, dir);
                }
CheckNextLength:
                ;
            }
            return null;
        }
    }
}
