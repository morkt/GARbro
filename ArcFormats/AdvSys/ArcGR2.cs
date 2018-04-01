//! \file       ArcGR2.cs
//! \date       2017 Dec 26
//! \brief      Studio Polaris resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.UMeSoft
{
    [Export(typeof(ArchiveFormat))]
    public class Gr2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "GR2/PACK"; } }
        public override string Description { get { return "Studio Polaris resource archive"; } }
        public override uint     Signature { get { return 0x4B434150; } } // 'PACK'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Gr2Opener ()
        {
            Extensions = new string[] { "gr2", "vic", "pac" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint data_offset = file.View.ReadUInt32 (8);
            if (data_offset < 0x10 || data_offset >= file.MaxOffset)
                return null;
            bool is_gr2 = file.Name.HasExtension ("gr2");

            uint index_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x10);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x10);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x14); 
                if (entry.Offset < data_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (is_gr2)
                    entry.Type = "image";
                dir.Add (entry);
                index_offset += 0x18;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!arc.File.View.AsciiEqual (entry.Offset, "LL5\0"))
                return base.OpenEntry (arc, entry);
            using (var input = arc.File.CreateStream (entry.Offset+4, entry.Size-4))
            {
                var output = new MemoryStream();
                LL5Decompress (input, output);
                output.Position = 0;
                return output;
            }
        }

        void LL5Decompress (IBinaryStream input, Stream output)
        {
            var buffer = new byte[0x100];
            while (input.PeekByte() != -1)
            {
                int count = input.ReadInt8();
                if (count < 0)
                {
                    count = -count;
                    input.Read (buffer, 0, count);
                    output.Write (buffer, 0, count);
                }
                else
                {
                    byte v = input.ReadUInt8();
                    for (int i = 0; i < count; ++i)
                        buffer[i] = v;
                    output.Write (buffer, 0, count);
                }
            }
        }
    }
}
