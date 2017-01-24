//! \file       ArcPCD.cs
//! \date       Tue Jan 24 06:49:35 2017
//! \brief      NEJII engine audio archive.
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

namespace GameRes.Formats.Nejii
{
    internal class PcdEntry : Entry
    {
        public byte[] WaveFormat;
    }

    [Export(typeof(ArchiveFormat))]
    public class PcdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PCD/NEJII"; } }
        public override string Description { get { return "NEJII engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset < 0x30 || !file.Name.EndsWith (".pcd", StringComparison.InvariantCultureIgnoreCase))
                return null;
            // check if first entry looks like WAVEFORMAT
            ushort channels = file.View.ReadUInt16 (0x12);
            if (0 == channels || channels > 2)
                return null;
            uint rate = file.View.ReadUInt32 (0x14);
            uint bps = file.View.ReadUInt32 (0x18);
            uint block_align = file.View.ReadUInt16 (0x1C);
            if (block_align * rate != bps)
                return null;
            bps = file.View.ReadUInt16 (0x1E);
            if (bps != 8 && bps != 16)
                return null;

            long offset = 0;
            var dir = new List<Entry>();
            while (offset < file.MaxOffset)
            {
                var name = file.View.ReadString (offset, 0x10);
                var entry = FormatCatalog.Instance.Create<PcdEntry> (name);
                entry.WaveFormat = file.View.ReadBytes (offset+0x10, 0x10);
                entry.Offset = offset + 0x20;
                entry.Size = file.View.ReadUInt32 (entry.Offset) + 4;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                offset = entry.Offset+entry.Size;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = (PcdEntry)entry;
            using (var riff_mem = new MemoryStream (0x2C))
            using (var riff = new BinaryWriter (riff_mem))
            {
                uint riff_size = entry.Size + 0x20;
                riff.Write (AudioFormat.Wav.Signature);
                riff.Write (riff_size);
                riff.Write (0x45564157); // 'WAVE'
                riff.Write (0x20746d66); // 'fmt '
                riff.Write (0x10);
                riff.Write (pent.WaveFormat);
                riff.Write (0x61746164); // 'data'
                riff.Flush();
                var header = riff_mem.ToArray();
                var pcm = arc.File.CreateStream (entry.Offset, entry.Size);
                return new PrefixStream (header, pcm);
            }
        }
    }
}
