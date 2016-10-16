//! \file       VideoAMV.cs
//! \date       Sat Aug 13 08:31:51 2016
//! \brief      Blue Gale animation format.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.BlueGale
{
    [Export(typeof(ArchiveFormat))]
    public class AmvOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AMPV"; } }
        public override string Description { get { return "BlueGale animation format"; } }
        public override uint     Signature { get { return 0x56706D61; } } // 'ampV'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public AmvOpener ()
        {
            Extensions = new string[] { "amv" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadInt16 (4) != 1)
                return null;
            uint unpacked_size = file.View.ReadUInt32 (0x16);
            uint width  = file.View.ReadUInt32 (0x1A);
            uint height = file.View.ReadUInt32 (0x1E);
            int count = file.View.ReadInt32 (0x2A);
            if (!IsSaneCount (count))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint offset = 0x32;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint size = file.View.ReadUInt32 (offset);
                var entry = new PackedEntry
                {
                    Name = string.Format ("{0}#{1:D4}.bmp", base_name, i),
                    Type = "image",
                    Offset = offset + 4,
                    Size = size,
                    IsPacked = true,
                    UnpackedSize = unpacked_size + 0x36,
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                offset += 4 + size;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = (PackedEntry)entry;
            var output = new byte[pent.UnpackedSize];
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
                ZbmFormat.Unpack (input, output, 0xE);

            output[0] = (byte)'B';
            output[1] = (byte)'M';
            LittleEndian.Pack (pent.UnpackedSize, output, 2);
            int header_size = LittleEndian.ToInt32 (output, 0xE);
            LittleEndian.Pack (header_size+0xE, output, 0xA);
            return new BinMemoryStream (output, entry.Name);
        }
    }
}
