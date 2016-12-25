//! \file       ArcARK.cs
//! \date       Sun Aug 28 06:06:07 2016
//! \brief      Irrlicht engine resource archive.
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
using System.Security.Cryptography;

namespace GameRes.Formats.Irrlicht
{
    [Export(typeof(ArchiveFormat))]
    public class ArkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARK"; } }
        public override string Description { get { return "Irrlicht engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;

            int index_offset = 4;
            int first_offset = file.View.ReadInt32 (index_offset+0x104);
            if (first_offset != (index_offset + count * 0x10C))
                return null;
            var name_buffer = new byte[0x104];
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (index_offset, name_buffer, 0, 0x104);
                int l;
                for (l = 0; l < name_buffer.Length && 0xFF != name_buffer[l]; ++l)
                    name_buffer[l] ^= 0xFF;
                if (0 == l)
                    return null;
                var name = Encodings.cp932.GetString (name_buffer, 0, l);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x104);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x108);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x10C;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new InputCryptoStream (input, new NotTransform());
        }
    }
}
