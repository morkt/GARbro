//! \file       ArcVoice.cs
//! \date       2017 Nov 25
//! \brief      Seraphim engine audio archive.
//
// Copyright (C) 2015-2017 by morkt
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
using GameRes.Compression;

namespace GameRes.Formats.Seraphim
{
    [Export(typeof(ArchiveFormat))]
    public class VoiceDatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SERAPH/VOICE"; } }
        public override string Description { get { return "Seraphim engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }
        public          bool   IsAmbiguous { get { return true; } }

        public VoiceDatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        static readonly Regex   VoiceRe = new Regex (@"^Voice\d\.dat$", RegexOptions.IgnoreCase);

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset > uint.MaxValue)
                return null;
            string name = Path.GetFileName (file.Name);
            if (!VoiceRe.Match (name).Success)
                return null;

            int count = file.View.ReadInt16 (0);
            if (!IsSaneCount (count))
                return null;

            uint data_offset = 2 + 4 * (uint)count;
            uint next_offset = file.View.ReadUInt32 (2);
            List<Entry> dir = null;
            if (next_offset < data_offset || next_offset >= file.MaxOffset)
                dir = ReadV2 (file, count);
            else
                dir = ReadV1 (file, count);
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }

        List<Entry> ReadV1 (ArcView file, int count)
        {
            int index_offset = 2;
            uint next_offset = file.View.ReadUInt32 (index_offset);
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
            return dir;
        }

        List<Entry> ReadV2 (ArcView file, int count)
        {
            int index_offset = 6;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = string.Format ("{0:D5}.ogg", i),
                    Type = "audio",
                    Offset = file.View.ReadUInt32 (index_offset),
                    Size   = file.View.ReadUInt32 (index_offset+4),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 12;
            }
            return dir;
        }
    }
}
