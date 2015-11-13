//! \file       ArcGPC.cs
//! \date       Thu Nov 12 12:33:40 2015
//! \brief      Old Eushully archives.
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

using GameRes.Utility;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Eushully
{
    public abstract class HOpener : ArchiveFormat
    {
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        protected ArcFile TryOpenWithIndex (ArcView file, string entry_type, string entry_ext = "")
        {
            var ext = Path.GetExtension (file.Name).ToUpperInvariant();
            if (ext.Length != 4 || 'H' == ext[3])
                return null;
            // GPC -> GPH, SNR -> SNH, etc
            var idx_name = Path.ChangeExtension (file.Name, string.Concat (ext.Substring (0, 3), "H"));
            if (!VFS.FileExists (idx_name))
                return null;

            using (var idx = VFS.OpenView (idx_name))
            {
                long idx_offset = 0;
                var name_buffer = new byte[0x40];
                var dir = new List<Entry>();
                while (idx_offset < idx.MaxOffset)
                {
                    int name_length = idx.View.ReadByte (idx_offset++);
                    if (name_length > name_buffer.Length)
                        name_buffer = new byte[name_length];
                    if (name_length != idx.View.Read (idx_offset, name_buffer, 0, (uint)name_length))
                        return null;
                    for (int i = 0; i < name_length; ++i)
                        name_buffer[i] ^= 0xFF;
                    var name = Encodings.cp932.GetString (name_buffer, 0, name_length);
                    var entry = new Entry { Name = name + entry_ext, Type = entry_type };
                    idx_offset += name_length;
                    entry.Offset = idx.View.ReadUInt32 (idx_offset);
                    if (entry.Offset > file.MaxOffset)
                        return null;
                    idx_offset += 4;
                    dir.Add (entry);
                }
                if (0 == dir.Count)
                    return null;
                dir.Sort ((a, b) => (int)(a.Offset - b.Offset));
                for (int i = 0; i < dir.Count; ++i)
                {
                    long next_offset = i+1 == dir.Count ? file.MaxOffset : dir[i+1].Offset;
                    dir[i].Size = (uint)(next_offset - dir[i].Offset);
                }
                return new ArcFile (file, this, dir);
            }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class GpcOpener : HOpener
    {
        public override string         Tag { get { return "GPC"; } }
        public override string Description { get { return "Eushully graphic archive"; } }

        public override ArcFile TryOpen (ArcView file)
        {
            return TryOpenWithIndex (file, "image", ".gpcf");
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class SndOpener : HOpener
    {
        public override string         Tag { get { return "SND"; } }
        public override string Description { get { return "Eushully audio archive"; } }

        public override ArcFile TryOpen (ArcView file)
        {
            return TryOpenWithIndex (file, "audio", ".wav");
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Size < 0x16)
                return base.OpenEntry (arc, entry);
            // emulate WAV file
            var header = new byte[0x2C];
            LittleEndian.Pack (0x46464952, header, 0x0); // 'RIFF'
            LittleEndian.Pack (0x45564157, header, 0x8); // 'WAVE'
            LittleEndian.Pack (0x20746d66, header, 0xC); // 'fmt '
            header[0x10] = 0x10;
            arc.File.View.Read (entry.Offset+1, header, 0x14, 0x10);
            LittleEndian.Pack (0x61746164, header, 0x24); // 'data'
            uint data_size = arc.File.View.ReadUInt32 (entry.Offset+0x11);
            LittleEndian.Pack (data_size, header, 0x28);
            LittleEndian.Pack (data_size+0x24u, header, 4);
            var pcm = arc.File.CreateStream (entry.Offset+0x15, data_size);
            return new PrefixStream (header, pcm);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class SnrOpener : HOpener
    {
        public override string         Tag { get { return "SNR"; } }
        public override string Description { get { return "Eushully script archive"; } }

        public override ArcFile TryOpen (ArcView file)
        {
            return TryOpenWithIndex (file, "script");
        }
    }
}
