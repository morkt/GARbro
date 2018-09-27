//! \file       ArcUWF.cs
//! \date       2017 Dec 15
//! \brief      West Gate resource archive.
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

namespace GameRes.Formats.WestGate
{
    [Export(typeof(ArchiveFormat))]
    public class UwfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "UWF"; } }
        public override string Description { get { return "West Gate audio archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public UwfOpener ()
        {
            Extensions = new[] { "uwf", "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset <= 0x1500)
                return null;
            uint first_offset = file.View.ReadUInt32 (0x14FC);
            if (first_offset >= file.MaxOffset || first_offset < 0x1500)
                return null;
            int count = (int)((first_offset - 0x14F0) / 0x10);
            if (!IsSaneCount (count))
                return null;
            var dir = UcaTool.ReadIndex (file, 0x14F0, count, "audio");
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            uint fmt_size = arc.File.View.ReadUInt16 (entry.Offset);
            if (fmt_size >= entry.Size)
                return base.OpenEntry (arc, entry);
            uint pcm_size = arc.File.View.ReadUInt32 (entry.Offset+2+fmt_size);
            if (pcm_size >= entry.Size)
                return base.OpenEntry (arc, entry);
            using (var mem = new MemoryStream())
            using (var riff = new BinaryWriter (mem))
            {
                uint total_size = (uint)(0x1C + fmt_size + pcm_size);
                riff.Write (AudioFormat.Wav.Signature);
                riff.Write (total_size);
                riff.Write (0x45564157); // 'WAVE'
                riff.Write (0x20746d66); // 'fmt '
                riff.Write (fmt_size);
                var fmt = arc.File.View.ReadBytes (entry.Offset+2, fmt_size);
                riff.Write (fmt);
                riff.Write (0x61746164); // 'data'
                riff.Flush();
                var wav_header = mem.ToArray();
                var pcm = arc.File.CreateStream (entry.Offset+fmt_size+2, pcm_size+4);
                return new PrefixStream (wav_header, pcm);
            }
        }
    }
}
