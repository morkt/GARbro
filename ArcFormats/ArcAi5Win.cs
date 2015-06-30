//! \file       ArcAi5Win.cs
//! \date       Mon Jun 29 04:41:29 2015
//! \brief      Ai5Win engine resource archive.
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
using GameRes.Utility;

namespace GameRes.Formats.Elf
{
    public class ArcIndexScheme
    {
        public int  NameLength;
        public byte NameKey;
        public uint SizeKey;
        public uint OffsetKey;
    }

    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/AI5WIN"; } }
        public override string Description { get { return "AI5WIN engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public static readonly Dictionary<string, ArcIndexScheme> KnownSchemes = new Dictionary<string, ArcIndexScheme> {
            { "Jokei Kazoku ~Inbou~", new ArcIndexScheme
                { NameLength = 0x1E, NameKey = 0x73, SizeKey = 0xAF5789BC, OffsetKey = 0x59FACB45 } },
        };

        public ArcOpener ()
        {
            Extensions = new string[] { "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (count <= 0 || count > 0xfffff)
                return null;
            long index_offset = 4;
            var scheme = KnownSchemes.First().Value;
            uint index_size = (uint)(count * (scheme.NameLength + 8));
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            var name_buf = new byte[scheme.NameLength];
            var dir = new List<Entry>();
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (index_offset, name_buf, 0, (uint)scheme.NameLength);
                for (int n = 0; n < name_buf.Length; ++n)
                {
                    name_buf[n] ^= scheme.NameKey;
                    if (0 == name_buf[n])
                        break;
                }
                string name = Binary.GetCString (name_buf, 0, name_buf.Length);
                if (0 == name.Length)
                    return null;
                index_offset += scheme.NameLength;
                var entry = FormatCatalog.Instance.CreateEntry (name);
                entry.Size   = file.View.ReadUInt32 (index_offset)   ^ scheme.SizeKey;
                entry.Offset = file.View.ReadUInt32 (index_offset+4) ^ scheme.OffsetKey;
                if (entry.Offset < index_size+4 || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
