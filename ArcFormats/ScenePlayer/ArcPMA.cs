//! \file       ArcPMA.cs
//! \date       2017 Dec 26
//! \brief      ScenePlayer animation resource.
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

namespace GameRes.Formats.ScenePlayer
{
    [Export(typeof(ArchiveFormat))]
    public class PmaOpener : PmxOpener
    {
        public override string         Tag { get { return "PMA"; } }
        public override string Description { get { return "ScenePlayer animation resource"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".pma")
                || file.View.ReadByte (0) != (0x78^0x21))
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var input = CreatePmxStream (file);
            bool index_complete = false;
            try
            {
                using (var index = new BinaryStream (input, file.Name, true))
                {
                    int count = index.ReadInt32();
                    if (!IsSaneCount (count))
                        return null;
                    var dir = new List<Entry> (count);
                    for (int i = 0; i < count; ++i)
                    {
                        index.ReadByte();
                        var offset = index.Position;
                        if (index.ReadUInt16() != 0x4D42) // 'BM'
                            return null;
                        uint size = index.ReadUInt32();
                        var entry = new Entry {
                            Name = string.Format ("{0}#{1}.bmp", base_name, i),
                            Type = "image",
                            Offset = offset,
                            Size =  size,
                        };
                        dir.Add (entry);
                        index.Position = offset + size;
                    }
                    index_complete = true;
                    return new PmxArchive (file, this, dir, input);
                }
            }
            finally
            {
                if (!index_complete)
                    input.Dispose();
            }
        }
    }
}
