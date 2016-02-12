//! \file       ArcXARC.cs
//! \date       Fri Feb 12 20:00:44 2016
//! \brief      Xuse XARC resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using GameRes.Utility;

namespace GameRes.Formats.Xuse
{
    [Export(typeof(ArchiveFormat))]
    public class XarcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "XARC/XUSE"; } }
        public override string Description { get { return "Xuse resource archive"; } }
        public override uint     Signature { get { return 0x43524158; } } // 'XARC'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public XarcOpener ()
        {
            Extensions = new string[] { "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 8;
            uint first_offset = file.View.ReadUInt32 (index_offset);
            if ((uint)count*4 + 10 != first_offset)
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry { Offset = file.View.ReadUInt32 (index_offset) };
                dir.Add (entry);
                index_offset += 4;
            }
            foreach (var entry in dir)
            {
                if (!file.View.AsciiEqual (entry.Offset, "DATA"))
                    return null;
                uint name_length = file.View.ReadUInt16 (entry.Offset+0x18);
                entry.Size = file.View.ReadUInt32 (entry.Offset+0x1C);
                var name = file.View.ReadBytes (entry.Offset+0x20, name_length);
                entry.Name = DecryptName (name);
                entry.Offset += 0x22 + name_length;
                uint signature = file.View.ReadUInt32 (entry.Offset);
                var res = AutoEntry.DetectFileType (signature);
                if (res != null)
                    entry.Type = res.Type;
            }
            return new ArcFile (file, this, dir);
        }

        string DecryptName (byte[] name)
        {
            for (int i = 0; i < name.Length; ++i)
            {
                name[i] = Binary.RotByteL (name[i], 4);
            }
            return Encodings.cp932.GetString (name);
        }
    }
}
