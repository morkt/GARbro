//! \file       ArcDAT.cs
//! \date       Sat Jan 21 18:32:40 2017
//! \brief      0verflow resource archive.
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
using System.Text;

namespace GameRes.Formats.GameSystem
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/0verflow"; } }
        public override string Description { get { return "0verflow resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_size = file.View.ReadUInt32 (0);
            if (index_size <= 2 || index_size >= (uint.MaxValue >> 9))
                return null;
            index_size <<= 9;
            if (index_size >= file.MaxOffset)
                return null;
            uint index_offset = 0x400;
            long offset = (long)file.View.ReadUInt32 (index_offset+12) << 9;
            if (offset != index_size)
                return null;
            var dir = new List<Entry>();
            var entry_buf = file.View.ReadBytes (index_offset, 12);
            while (!Array.TrueForAll (entry_buf, x => x == 0xFF))
            {
                index_offset += 0x10;
                if (index_offset >= index_size)
                    return null;
                var name = RestoreName (entry_buf);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                file.View.Read (index_offset, entry_buf, 0, 12);
                long next_offset = (long)file.View.ReadUInt32 (index_offset+12) << 9;
                if (next_offset < offset || next_offset > file.MaxOffset)
                    return null;
                if (name.EndsWith (".CRGB") || name.EndsWith (".CHAR"))
                    entry.Type = "image";
                entry.Offset = offset;
                entry.Size = (uint)(next_offset - offset);
                dir.Add (entry);
                offset = next_offset;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        static string RestoreName (byte[] index)
        {
            var name_buf = new byte[0x10];
            int dst = 0;
            for (int i = 0; i < 12; i += 3)
            {
                int word = index[i] << 16 | index[i+1] << 8 | index[i+2];
                name_buf[dst++] = (byte)(0x20 + ((word >> 18) & 0x3F));
                name_buf[dst++] = (byte)(0x20 + ((word >> 12) & 0x3F));
                name_buf[dst++] = (byte)(0x20 + ((word >> 6) & 0x3F));
                name_buf[dst++] = (byte)(0x20 +  (word & 0x3F));
            }
            int name_end = Array.IndexOf<byte> (name_buf, 0x20, 0, 12);
            if (0 == name_end)
                throw new InvalidFormatException();
            if (-1 == name_end)
                name_end = 12;
            var name = Encoding.ASCII.GetString (name_buf, 0, name_end);
            int ext_end = Array.IndexOf<byte> (name_buf, 0x20, 12, 4);
            if (12 == ext_end)
                return name;
            if (-1 == ext_end)
                ext_end = 16;
            var ext = Encoding.ASCII.GetString (name_buf, 12, ext_end-12);
            return name + '.' + ext;
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            if (entry.Name.EndsWith (".BGD") || entry.Name.EndsWith (".CRGB"))
            {
                var input = arc.File.CreateStream (entry.Offset, entry.Size);
                var info = new ImageMetaData { Width = 800, Height = 600, BPP = 24 };
                return new CgdReader (input, info);
            }
            else if (entry.Name.EndsWith (".CHAR"))
            {
                var input = arc.File.CreateStream (entry.Offset, entry.Size);
                var info = new ChrMetaData {
                    Width = 800, Height = 600, BPP = 32,
                    DataOffset = 0, RgbSize = (int)input.Length,
                };
                return new ChrReader (input, info);
            }
            return base.OpenImage (arc, entry);
        }
    }
}
