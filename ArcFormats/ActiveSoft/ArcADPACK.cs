//! \file       ArcADPACK.cs
//! \date       Sun Feb 15 09:25:28 2015
//! \brief      A98SYS engine archive formats implementation.
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

namespace GameRes.Formats.AdPack
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "A98"; } }
        public override string Description { get { return "A98SYS Engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PakOpener ()
        {
            Extensions = new string[] { "pak" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt16 (0);
            if (count <= 1)
                return null;
            if (0x4000 == count)
                return TryOpenVoiceArchive (file);
            long index_offset = 2;
            uint index_size = (uint)(0x10 * count);
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            --count;
            var dir = new List<Entry> (count);
            for (uint i = 0; i < count; ++i)
            {
                string name = ReadName (file, index_offset);
                if (string.IsNullOrEmpty (name))
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                uint offset = file.View.ReadUInt32 (index_offset+12);
                uint next_offset = file.View.ReadUInt32 (index_offset+0x10+12);
                entry.Size = next_offset - offset;
                entry.Offset = offset;
                if (offset < index_size || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x10;
            }
            return new ArcFile (file, this, dir);
        }

        ArcFile TryOpenVoiceArchive (ArcView file)
        {
            int count = file.View.ReadInt16 (2);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset+4);
                index_offset += 8;
                if (offset == file.MaxOffset && i+1 == count)
                    break;
                if (offset > file.MaxOffset)
                    return null;
                var entry = new Entry { Offset = offset };
                dir.Add (entry);
            }
            foreach (var entry in dir)
            {
                var name = ReadName (file, index_offset);
                if (string.IsNullOrEmpty (name))
                    return null;
                entry.Name = name;
                entry.Type = FormatCatalog.Instance.GetTypeFromName (name);
                entry.Size = file.View.ReadUInt32 (index_offset+0xC);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                index_offset += 0x10;
            }
            return new ArcFile (file, this, dir);
        }

        string ReadName (ArcView file, long offset)
        {
            string name = file.View.ReadString (offset, 8).TrimEnd (null);
            if (0 == name.Length)
                return null;
            string ext  = file.View.ReadString (offset+8, 4).TrimEnd (null);
            if (0 != ext.Length)
                name += '.'+ext;
            return name;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class Pak32Opener : ArchiveFormat
    {
        public override string         Tag { get { return "ADPACK32"; } }
        public override string Description { get { return "Active Soft resource archive"; } }
        public override uint     Signature { get { return 0x41504441; } } // "ADPA"
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Pak32Opener ()
        {
            Extensions = new string[] { "pak" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "CK32"))
                return null;
            int count = file.View.ReadInt32 (12) - 1;
            if (count <= 0 || count > 0xfffff)
                return null;
            uint index_size = (uint)(0x20 * count);
            if (index_size > file.View.Reserve (0x10, index_size))
                return null;
            var dir = new List<Entry> (count);
            long index_offset = 0x10;
            for (uint i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset, 0x18);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                uint offset = file.View.ReadUInt32 (index_offset+0x1c);
                uint next_offset = file.View.ReadUInt32 (index_offset+0x20+0x1c);
                entry.Size = next_offset - offset;
                entry.Offset = offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
