//! \file       ArcPAK.cs
//! \date       Sun Feb 05 13:07:40 2017
//! \brief      cromwell graphic resource archive.
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
using GameRes.Compression;

namespace GameRes.Formats.Cromwell
{
    [Export(typeof(ArchiveFormat))]
    public class GraphicPakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/cromwell"; } }
        public override string Description { get { return "cromwell graphic resource archive"; } }
        public override uint     Signature { get { return 0x70617247; } } // 'Graphic PackData'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public GraphicPakOpener ()
        {
            Signatures = new uint[] { 0x70617247, 0x63696F56 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            bool is_graphic = file.View.AsciiEqual (0, "Graphic PackData");
            if (!is_graphic && !file.View.AsciiEqual (0, "Voice PackData. "))
                return null;
            int count = file.View.ReadInt32 (0x10);
            if (!IsSaneCount (count))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            if (is_graphic && base_name.Equals ("VOICE", System.StringComparison.OrdinalIgnoreCase))
                is_graphic = false;
            string type = is_graphic ? "image" : "audio";

            uint index_offset = 0x14;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0xC);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                index_offset += 0xC;
                var entry = new PackedEntry { Name = name, Type = type };
                entry.Offset = file.View.ReadUInt32 (index_offset);
                if (entry.Offset >= file.MaxOffset)
                    return null;
                entry.UnpackedSize = file.View.ReadUInt32 (index_offset+4);
                entry.IsPacked = true;
                dir.Add (entry);
                index_offset += 8;
            }
            for (int i = 0; i < count; ++i)
            {
                long next_offset = i+1 < count ? dir[i+1].Offset : file.MaxOffset;
                dir[i].Size = (uint)(next_offset - dir[i].Offset);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new ZLibStream (input, CompressionMode.Decompress);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class OpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "OPK"; } }
        public override string Description { get { return "cromwell audio resource archive"; } }
        public override uint     Signature { get { return 0x63696F56; } } // 'VoiceOggPackFile'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "eOggPackFile"))
                return null;
            int count = file.View.ReadInt32 (0x10);
            if (!IsSaneCount (count))
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            using (var input = file.CreateStream())
            {
                input.Position = 0x14;
                var dir = new List<Entry> (count);
                uint next_offset = input.ReadUInt32();
                for (int i = 0; i < count; ++i)
                {
                    var entry = new Entry { Type = "audio" };
                    entry.Offset = next_offset;
                    next_offset = input.ReadUInt32();
                    entry.Size = (uint)(next_offset - entry.Offset);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                input.Position = next_offset;
                foreach (var entry in dir)
                {
                    entry.Name = input.ReadCString (8) + ".ogg";
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
