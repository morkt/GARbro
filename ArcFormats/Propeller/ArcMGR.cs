//! \file       ArcMGR.cs
//! \date       Sat Nov 21 02:05:59 2015
//! \brief      Propeller multi-frame image.
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
using GameRes.Utility;

namespace GameRes.Formats.Propeller
{
    [Export(typeof(ArchiveFormat))]
    public class MgrOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MGR"; } }
        public override string Description { get { return "Propeller multi-frame image"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.EndsWith (".mgr", StringComparison.InvariantCultureIgnoreCase))
                return null;
            int count = file.View.ReadInt16 (0);
            if (count <= 0 || count >= 0x100)
                return null;
            uint current = 2;
            uint first_offset = current;
            if (count > 1)
            {
                first_offset = file.View.ReadUInt32 (current);
                if (first_offset != 2 + count * 4)
                    return null;
            }
            if (!file.View.AsciiEqual (first_offset+9, "BM"))
                return null;
            string base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            if (count > 1)
            {
                for (int i = 0; i < count; ++i)
                {
                    var entry = new PackedEntry {
                        Name = string.Format ("{0}#{1:D4}.bmp", base_name, i),
                        Type = "image",
                        Offset = file.View.ReadUInt32 (current),
                    };
                    if (entry.Offset < first_offset || entry.Offset >= file.MaxOffset)
                        return null;
                    dir.Add (entry);
                    current += 4;
                }
            }
            else
            {
                dir.Add (new PackedEntry { Name = base_name+".bmp", Type = "image", Offset = current });
            }
            foreach (PackedEntry entry in dir)
            {
                entry.UnpackedSize  = file.View.ReadUInt32 (entry.Offset);
                entry.Size          = file.View.ReadUInt32 (entry.Offset+4);
                entry.IsPacked      = true;
                if (entry.UnpackedSize < 0x36 || entry.Size > file.MaxOffset-entry.Offset)
                    return null;
                entry.Offset += 8;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
            {
                var bmp = new byte[(entry as PackedEntry).UnpackedSize];
                Decompress (input, bmp);
                return new MemoryStream (bmp);
            }
        }

        static public int Decompress (Stream input, byte[] output)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                int count = input.ReadByte();
                if (-1 == count)
                    break;
                if (count < 0x20)
                {
                    count = Math.Min (count+1, output.Length-dst);
                    int read = input.Read (output, dst, count);
                    dst += read;
                    if (read < count)
                        break;
                }
                else
                {
                    int offset = ((count & 0x1F) << 8) + 1;
                    count >>= 5;
                    if (7 == count)
                        count += input.ReadByte();
                    offset += input.ReadByte();
                    if (offset >= dst)
                        throw new InvalidFormatException();
                    count = Math.Min (count+2, output.Length-dst);
                    Binary.CopyOverlapped (output, dst - offset, dst, count);
                    dst += count;
                }
            }
            return dst;
        }
    }
}
