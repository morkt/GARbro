//! \file       ArcANI.cs
//! \date       Thu Mar 09 10:06:53 2017
//! \brief      Anime Game System animation resource.
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
using System.Linq;

namespace GameRes.Formats.Ags
{
    internal class AniEntry : Entry
    {
        public int  FrameIndex;
        public int  FrameType;
        public int  KeyFrame;
    }

    [Export(typeof(ArchiveFormat))]
    public class AniOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ANI"; } }
        public override string Description { get { return "Anime Game System animation resource"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".ani"))
                return null;
            uint first_offset = file.View.ReadUInt32 (0);
            if (first_offset < 4 || file.MaxOffset > int.MaxValue || first_offset >= file.MaxOffset || 0 != (first_offset & 3))
                return null;
            int frame_count = (int)(first_offset / 4);
            if (frame_count > 10000)
                return null;
            long index_offset = 4;

            var frame_table = new uint[frame_count];
            frame_table[0] = first_offset;
            for (int i = 1; i < frame_count; ++i)
            {
                var offset = file.View.ReadUInt32 (index_offset);
                index_offset += 4;
                if (offset < first_offset || offset >= file.MaxOffset)
                    return null;
                frame_table[i] = offset;
            }

            var frame_map = new Dictionary<uint, byte>();
            foreach (var offset in frame_table)
            {
                if (!frame_map.ContainsKey (offset))
                {
                    byte frame_type = file.View.ReadByte (offset);
                    if (frame_type >= 0x20)
                        return null;
                    frame_map[offset] = frame_type;
                }
            }

            int last_key_frame = 0;
            var dir = new List<Entry>();
            for (int i = 0; i < frame_count; ++i)
            {
                var offset = frame_table[i];
                int frame_type = frame_map[offset];
                if (1 == frame_type)
                    continue;
                frame_type &= 0xF;
                if (0 == frame_type || 0xA == frame_type)
                    last_key_frame = dir.Count;
                var entry = new AniEntry
                {
                    Name = i.ToString ("D4"),
                    Type = "image",
                    Offset = offset,
                    FrameType = frame_type,
                    KeyFrame = last_key_frame,
                    FrameIndex = dir.Count,
                };
                dir.Add (entry);
            }
            if (0 == dir.Count)
                return null;

            var ordered = dir.OrderBy (e => e.Offset).ToList();
            for (int i = 0; i < ordered.Count; ++i)
            {
                var entry = ordered[i] as AniEntry;
                long next_offset = file.MaxOffset;
                for (int j = i+1; j <= ordered.Count; ++j)
                {
                    next_offset = j == ordered.Count ? file.MaxOffset : ordered[j].Offset;
                    if (next_offset != entry.Offset)
                        break;
                }
                entry.Size = (uint)(next_offset - entry.Offset);
            }
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var ani = (AniEntry)entry;
            byte[] key_frame = null;
            if (ani.KeyFrame != ani.FrameIndex)
            {
                var dir = (List<Entry>)arc.Dir;
                for (int i = ani.KeyFrame; i < ani.FrameIndex; ++i)
                {
                    var frame = dir[i];
                    using (var s = arc.File.CreateStream (frame.Offset, frame.Size))
                    {
                        var frame_info = Cg.ReadMetaData (s) as CgMetaData;
                        if (null == frame_info)
                            break;
                        using (var reader = new CgFormat.Reader (s, frame_info, key_frame))
                        {
                            reader.Unpack();
                            key_frame = reader.Data;
                        }
                    }
                }
            }
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            try
            {
                var info = Cg.ReadMetaData (input) as CgMetaData;
                if (null == info)
                    throw new InvalidFormatException();
                return new CgFormat.Reader (input, info, key_frame);
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        static Lazy<ImageFormat> s_Cg = new Lazy<ImageFormat> (() => ImageFormat.FindByTag ("CG"));

        ImageFormat Cg { get { return s_Cg.Value; } }
    }
}
