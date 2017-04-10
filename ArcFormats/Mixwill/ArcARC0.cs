//! \file       ArcARC0.cs
//! \date       Sat Jan 14 23:23:37 2017
//! \brief      Mixwill soft resource archive.
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

namespace GameRes.Formats.Mixwill
{
    [Export(typeof(ArchiveFormat))]
    public class Arc0Opener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC0"; } }
        public override string Description { get { return "Mixwill soft resource archive"; } }
        public override uint     Signature { get { return 0x30435241; } } // 'ARC0'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Arc0Opener ()
        {
            Extensions = new string[] { "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0x10004);
            if (!IsSaneCount (count))
                return null;
            var name_buf = new byte[0x100];
            uint index_offset = 0x10008;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                if (0x100 != file.View.Read (index_offset, name_buf, 0, 0x100))
                    return null;
                int n;
                for (n = 0; n < 0x100; ++n)
                {
                    name_buf[n] ^= (byte)n;
                    if (0 == name_buf[n])
                        break;
                }
                if (0 == n)
                    return null;
                index_offset += 0x100;
                var name = Encodings.cp932.GetString (name_buf, 0, n);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Size   = file.View.ReadUInt32 (index_offset);
                entry.Offset = file.View.ReadUInt32 (index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            uint encrypted_size = entry.Size;
            if (!entry.Name.HasExtension (".txt")
                && encrypted_size > 0x100)
                encrypted_size = 0x100;
            var prefix = arc.File.View.ReadBytes (entry.Offset, encrypted_size);
            for (int i = 0; i < prefix.Length; ++i)
                prefix[i] ^= (byte)i;
            if (entry.Size == encrypted_size)
                return new BinMemoryStream (prefix, entry.Name);
            var rest = arc.File.CreateStream (entry.Offset+encrypted_size, entry.Size-encrypted_size);
            return new PrefixStream (prefix, rest);
        }
    }
}
