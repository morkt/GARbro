//! \file       ArcAR2.cs
//! \date       2023 Oct 17
//! \brief      Neon resource archive.
//
// Copyright (C) 2023 by morkt
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

// [990527][Neon] Onegai! Maid☆Roid

namespace GameRes.Formats.Neon
{
    [Export(typeof(ArchiveFormat))]
    public class Ar2Opener : ArchiveFormat
    {
        public override string         Tag => "AR2/NEON";
        public override string Description => "Neon resource archive";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        const byte DefaultKey = 0x55;

        public override ArcFile TryOpen (ArcView file)
        {
            uint uKey = DefaultKey | DefaultKey << 16;
            uKey |= uKey << 8;
            if (file.MaxOffset <= 0x10
                || file.View.ReadUInt32 (8) != uKey
                || file.View.ReadUInt32 (0) != file.View.ReadUInt32 (4)
                || (file.View.ReadUInt32 (0xC) ^ uKey) > 0x100)
                return null;
            using (var stream = file.CreateStream())
            using (var input = new XoredStream (stream, DefaultKey))
            {
                var buffer = new byte[0x100];
                var dir = new List<Entry>();
                while (0x10 == input.Read (buffer, 0, 0x10))
                {
                    uint size = buffer.ToUInt32 (0);
//                    uint orig_size = buffer.ToUInt32 (4); // original size?
//                    uint extra = buffer.ToUInt32 (8); // header size/compression?
                    int name_length = buffer.ToInt32 (0xC);
                    if (0 == size && 0 == name_length)
                        continue;
                    if (name_length <= 0 || name_length > buffer.Length)
                        return null;
                    input.Read (buffer, 0, name_length);
                    var name = Encodings.cp932.GetString (buffer, 0, name_length);
                    var entry = Create<Entry> (name);
                    entry.Offset = input.Position;
                    entry.Size = size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    input.Seek (size, SeekOrigin.Current);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new XoredStream (input, DefaultKey);
        }
    }
}
