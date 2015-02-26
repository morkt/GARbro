//! \file       ArcLST.cs
//! \date       Fri Feb 06 05:47:09 2015
//! \brief      Moon. archive format.
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
using System.Text;

namespace GameRes.Formats.Tactics
{
    [Export(typeof(ArchiveFormat))]
    public class LstOpener : ArchiveFormat
    {
        public override string         Tag { get { return "LST"; } }
        public override string Description { get { return "Tactics resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public LstOpener ()
        {
            Extensions = new string[] { "" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            string lstname = file.Name + ".lst";
            if (!File.Exists (lstname))
                return null;
            using (var lst = new ArcView (lstname))
            {
                int count = (int)(lst.View.ReadUInt32 (0) ^ 0xcccccccc);
                if (count <= 0 || (4 + count*0x2c) > lst.MaxOffset)
                    return null;
                var cp932 = Encodings.cp932.WithFatalFallback();
                var dir = new List<Entry> (count);
                uint index_offset = 4;
                for (int i = 0; i < count; ++i)
                {
                    string name = ReadName (lst, index_offset+8, 0x24, cp932);
                    var entry = FormatCatalog.Instance.CreateEntry (name);
                    entry.Offset = lst.View.ReadUInt32 (index_offset) ^ 0xcccccccc;
                    entry.Size   = lst.View.ReadUInt32 (index_offset+4) ^ 0xcccccccc;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    index_offset += 0x2c;
                }
                return new ArcFile (file, this, dir);
            }
        }

        private static string ReadName (ArcView view, long offset, uint size, Encoding enc)
        {
            byte[] buffer = new byte[size];
            uint n;
            for (n = 0; n < size; ++n)
            {
                byte b = view.View.ReadByte (offset+n);
                if (0 == b)
                    break;
                buffer[n] = (byte)(b^0xcc);
            }
            return enc.GetString (buffer, 0, (int)n);
        }
    }
}
