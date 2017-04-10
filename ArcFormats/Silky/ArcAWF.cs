//! \file       ArcAWF.cs
//! \date       Thu Nov 05 01:47:53 2015
//! \brief      Silky's audio archive.
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

namespace GameRes.Formats.Silky
{
    [Export(typeof(ArchiveFormat))]
    public class AwfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AWF"; } }
        public override string Description { get { return "Silky's audio archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            // enforce extension to avoid false positives
            if (!file.Name.HasExtension (".awf"))
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 4;
            uint index_size = (uint)count*0x34;
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;

            // rather loose criterion, haven't found anything better yet.
            bool is_mp3 = VFS.IsPathEqualsToFileName (file.Name, "voice.awf");
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x20);
                if (is_mp3)
                    name = Path.ChangeExtension (name, "mp3");
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x20);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x24);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x34;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Name.HasExtension (".mp3"))
                return base.OpenEntry (arc, entry);
            // prepend WAV header
            var header = new byte[0x2C];
            using (var mem = new MemoryStream (header))
            using (var wav = new BinaryWriter (mem))
            {
                wav.Write ("RIFF".ToCharArray());
                wav.Write ((uint)(entry.Size + 0x24u));
                wav.Write ("WAVE".ToCharArray());
                wav.Write ("fmt ".ToCharArray());
                wav.Write (0x10);
                wav.Write ((ushort)1);
                wav.Write ((ushort)2);
                wav.Write (22050);
                wav.Write (22050*4);
                wav.Write ((ushort)4);
                wav.Write ((ushort)16);
                wav.Write ("data".ToCharArray());
                wav.Write (entry.Size);
            }
            var pcm = arc.File.CreateStream (entry.Offset, entry.Size);
            return new PrefixStream (header, pcm);
        }
    }
}
