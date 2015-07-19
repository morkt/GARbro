//! \file       ArcSeraph.cs
//! \date       Fri Jul 17 13:47:34 2015
//! \brief      Seraphim engine resource archives.
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
using System.Text.RegularExpressions;
using ZLibNet;

namespace GameRes.Formats.Seraphim
{
    internal class ArchPacEntry : Entry
    {
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT"; } }
        public override string Description { get { return "Seraphim engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }
        public          bool   IsAmbiguous { get { return true; } }

        static readonly Regex   VoiceRe = new Regex (@"^Voice\d\.dat$", RegexOptions.IgnoreCase);

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset > uint.MaxValue)
                return null;
            string name = Path.GetFileName (file.Name);
            if (name.Equals ("ArchPac.dat", StringComparison.InvariantCultureIgnoreCase))
                return OpenArchPac (file);
            else if (VoiceRe.Match (name).Success)
                return OpenVoice (file);
//            else if (name.Equals ("ArchCash.dat", StringComparison.InvariantCultureIgnoreCase))
//                return OpenArchCash (file);
            return null;
        }

        const long ArchPacOffset = 0x0C23659F;

        private ArcFile OpenArchPac (ArcView file)
        {
            long index_offset = ArchPacOffset;
            int base_count = file.View.ReadInt32 (index_offset);
            int file_count = file.View.ReadInt32 (index_offset + 4);
            index_offset += 8;
            if (base_count <= 0 || base_count > 0x100 || !IsSaneCount (file_count))
                return null;
            var base_offsets = new List<Tuple<uint, int>> (base_count);
            int total_count = 0;
            for (int i = 0; i < base_count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                int count = file.View.ReadInt32 (index_offset+4);
                if (count <= 0 || count > file_count || offset > file.MaxOffset)
                    return null;
                total_count += count;
                if (total_count > file_count)
                    return null;
                base_offsets.Add (Tuple.Create (offset, count));
                index_offset += 8;
            }
            if (total_count != file_count)
                return null;
            var dir = new List<Entry> (file_count);
            for (int j = base_count-1; j >= 0; --j)
            {
                uint next_offset = file.View.ReadUInt32 (index_offset);
                index_offset += 4;
                for (int i = 0; i < base_offsets[j].Item2; ++i)
                {
                    var entry = new ArchPacEntry { Name = string.Format ("{0}-{1:D5}.cts", j, i), Type = "image" };
                    entry.Offset = next_offset;
                    next_offset = file.View.ReadUInt32 (index_offset);
                    index_offset += 4;
                    if (next_offset < entry.Offset)
                        return null;
                    entry.Size = (uint)(next_offset - entry.Offset);
                    entry.Offset += base_offsets[j].Item1;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
            }
            return new ArcFile (file, this, dir);
        }

        private ArcFile OpenVoice (ArcView file)
        {
            int count = file.View.ReadInt16 (0);
            if (!IsSaneCount (count))
                return null;
            uint data_offset = 2 + 4 * (uint)count;
            if (data_offset > file.View.Reserve (0, data_offset))
                return null;

            int index_offset = 2;
            uint next_offset = file.View.ReadUInt32 (index_offset);
            if (next_offset < data_offset)
                return null;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                index_offset += 4;
                var entry = new Entry { Name = string.Format ("{0:D5}.wav", i), Type = "audio" };
                entry.Offset = next_offset;
                if (i + 1 == count)
                    next_offset = (uint)file.MaxOffset;
                else
                    next_offset = file.View.ReadUInt32 (index_offset);
                if (next_offset <= entry.Offset)
                    return null;
                entry.Size = next_offset - (uint)entry.Offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (0 == entry.Size)
                return Stream.Null;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (!(entry is ArchPacEntry))
                return input;
            int signature = arc.File.View.ReadUInt16 (entry.Offset);
            if (0x9C78 != signature)
                return input;
            return new ZLibStream (input, CompressionMode.Decompress);
        }
    }
}
