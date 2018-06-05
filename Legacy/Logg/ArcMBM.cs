//! \file       ArcMBM.cs
//! \date       2018 May 30
//! \brief      Logg resource archive.
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
using System.Globalization;
using System.Linq;

// [981030][Logg] Physical Lesson

namespace GameRes.Formats.Logg
{
    [Export(typeof(ArchiveFormat))]
    public class MbmOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MBM"; } }
        public override string Description { get { return "Logg Adv engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension ("MBM"))
                return null;
            var index = GetArchiveIndex (file);
            if (null == index)
                return null;
            var dir = index.Take (index.Count - 1)
                .Select (e => new Entry {
                    Name = e.Value,
                    Type = FormatCatalog.Instance.GetTypeFromName (e.Value),
                    Offset = e.Key
                }).ToList();
            for (int i = 1; i < dir.Count; ++i)
                dir[i-1].Size = (uint)(dir[i].Offset - dir[i-1].Offset);
            dir[dir.Count-1].Size = (uint)(file.MaxOffset - dir[dir.Count-1].Offset);
            return new ArcFile (file, this, dir);
        }

        IDictionary<uint, string> GetArchiveIndex (ArcView file)
        {
            uint last_offset = FileListMap.Value.Keys.Last();
            if (last_offset != file.MaxOffset)
                return null;
            return FileListMap.Value;
        }

        Lazy<IDictionary<uint, string>> FileListMap = new Lazy<IDictionary<uint, string>> (ReadFileList);

        static IDictionary<uint, string> ReadFileList ()
        {
            var file_map = new SortedDictionary<uint,string>();
            var comma = new char[] {','};
            FormatCatalog.Instance.ReadFileList ("logg_pl.lst", line => {
                var parts = line.Split (comma, 2);
                uint offset = uint.Parse (parts[0], NumberStyles.HexNumber);
                if (2 == parts.Length)
                {
                    file_map[offset] = parts[1];
                }
                else
                {
                    file_map[offset] = null;
                }
            });
            return file_map;
        }
    }
}
