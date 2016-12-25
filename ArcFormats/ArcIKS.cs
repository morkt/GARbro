//! \file       ArcIKS.cs
//! \date       Wed Jul 08 07:07:09 2015
//! \brief      [IKS] archives implementation.
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
using System.Linq;
using System.Security.Cryptography;
using GameRes.Utility;

namespace GameRes.Formats.X
{
    [Export(typeof(ArchiveFormat))]
    public class IksOpener : ArchiveFormat
    {
        public override string         Tag { get { return "IKS"; } }
        public override string Description { get { return "X[iks] resource archive"; } }
        public override uint     Signature { get { return 0x5253504E; } } // 'NPSR'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public static Dictionary<string, byte> KnownKeys = new Dictionary<string, byte>() {
            { "Shikkan ~Hazukashimerareta Karada, Oreta Kokoro~", 0x66 },
        };

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (count <= 0 || count > 0xfffff)
                return null;
            uint index_offset = 0x10;
            uint index_size = (uint)(0x28 * count);
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            long data_offset = index_offset + index_size;
            var dir = new List<Entry> (count);
            var name_buffer = new byte[0x18];
            for (int i = 0; i < count; ++i)
            {
                byte name_length = Math.Min ((byte)0x17, file.View.ReadByte (index_offset));
                if (name_length > name_buffer.Length)
                    return null;
                file.View.Read (index_offset+1, name_buffer, 0, name_length);
                string name = Encodings.cp932.GetString (name_buffer, 0, name_length);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x20);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x1C);
                if (entry.Offset < data_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x28;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            byte key = KnownKeys.First().Value;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new XoredStream (input, key);
        }
    }
}
