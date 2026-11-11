//! \file       ArcDAT.cs
//! \date       Thu Nov 10 14:53:03 2016
//! \brief      YaneSDK resource archive.
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

namespace GameRes.Formats.YaneSDK
{
    internal class YaneEntry : Entry
    {
        public uint EncryptedSize;
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/YaneSDK"; } }
        public override string Description { get { return "YaneSDK engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = (short)(file.View.ReadUInt16 (0) ^ 0x8080);
            if (!IsSaneCount (count))
                return null;

            using (var input = file.CreateStream())
            using (var dec = new XoredStream (input, 0x80))
            using (var index = new BinaryReader (dec))
            {
                index.BaseStream.Position = 2;
                int data_offset = 2 + 0x2C * count;
                var name_buf = new byte[0x22];
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    if (0x22 != index.Read (name_buf, 0, 0x22))
                        return null;
                    var name = Binary.GetCString (name_buf, 0);
                    if (string.IsNullOrWhiteSpace (name))
                        return null;
                    var entry = FormatCatalog.Instance.Create<YaneEntry> (name);
                    entry.EncryptedSize = index.ReadUInt16();
                    entry.Size = index.ReadUInt32();
                    entry.Offset = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset) || entry.Offset < data_offset)
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var yent = entry as YaneEntry;
            if (null == yent || 0 == yent.EncryptedSize)
                return base.OpenEntry (arc, entry);
            var header = arc.File.View.ReadBytes (yent.Offset, yent.EncryptedSize);
            for (int i = 0; i < header.Length; ++i)
                header[i] ^= 0x80;
            if (yent.EncryptedSize >= yent.Size)
                return new BinMemoryStream (header);
            var rest = arc.File.CreateStream (yent.Offset + yent.EncryptedSize, yent.Size - yent.EncryptedSize);
            return new PrefixStream (header, rest);
        }
    }
}
