//! \file       ArcERI.cs
//! \date       Wed Jan 27 18:24:06 2016
//! \brief      Entis multi-frame image format.
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
using System.Linq;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Entis
{
    [Export(typeof(ArchiveFormat))]
    public class EriOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ERI/MULTI"; } }
        public override string Description { get { return "Entis multi-frame image format"; } }
        public override uint     Signature { get { return 0x69746e45u; } } // 'Enti'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public EriOpener ()
        {
            Extensions = new string[] { "eri" };
        }

        static readonly Lazy<ImageFormat> s_EriFormat = new Lazy<ImageFormat> (() => ImageFormat.FindByTag ("ERI"));

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0x10, "Entis Rasterized Image"))
                return null;
            EriMetaData info;
            using (var eris = file.CreateStream())
                info = s_EriFormat.Value.ReadMetaData (eris) as EriMetaData;

            if (null == info || null == info.Header || !IsSaneCount (info.Header.FrameCount))
                return null;
            info.FileName = file.Name;
            string base_name = Path.GetFileNameWithoutExtension (file.Name);

            int count = info.Header.FrameCount;
            long current_offset = info.StreamPos;
            var dir = new List<Entry> (count);
            var id = new AsciiString (8);
            Color[] palette = null;
            int i = 0;
            while (i < count && current_offset < file.MaxOffset)
            {
                if (file.View.Read (current_offset, id.Value, 0, 8) < 8)
                    break;
                if ("Stream  " == id)
                {
                    current_offset += 0x10;
                    continue;
                }
                long section_size = file.View.ReadInt64 (current_offset+8);
                if (section_size < 0 || section_size > int.MaxValue)
                    throw new FileSizeException();
                current_offset += 0x10;
                if (0 == section_size)
                    continue;
                if ("Palette " == id)
                {
                    using (var stream = file.CreateStream (current_offset, (uint)section_size))
                        palette = EriFormat.ReadPalette (stream, (int)section_size);
                }
                else if ("ImageFrm" == id || "DiffeFrm" == id)
                {
                    var entry = new EriEntry {
                        Name    = string.Format ("{0}#{1:D4}.tga", base_name, i++),
                        Type    = "image",
                        Offset  = current_offset,
                        Size    = (uint)section_size,
                        FrameIndex = dir.Count,
                        IsDiff  = "DiffeFrm" == id,
                    };
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                current_offset += section_size;
            }
            if (0 == dir.Count)
                return null;
            return new EriMultiImage (file, this, dir, info, palette);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var earc = (EriMultiImage)arc;
            var eent = (EriEntry)entry;
            var pixels = earc.GetFrame (eent.FrameIndex);
            return TgaStream.Create (earc.Info, pixels);
        }
    }

    internal class EriMultiImage : ArcFile
    {
        public readonly EriMetaData     Info;
        public readonly Color[]         Palette;
        byte[][]        Frames;

        public EriMultiImage (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, EriMetaData info, Color[] palette)
            : base (arc, impl, dir)
        {
            Info = info;
            Palette = palette;
            Frames = new byte[dir.Count][];
        }

        public byte[] GetFrame (int index)
        {
            if (index >= Frames.Length)
                throw new ArgumentException ("index");
            if (null != Frames[index])
                return Frames[index];

            var entry = Dir.ElementAt (index) as EriEntry;
            byte[] prev_frame = null;
            if (index > 0 && entry.IsDiff)
            {
                prev_frame = GetFrame (index-1);
            }
            using (var stream = File.CreateStream (entry.Offset, entry.Size))
            {
                var reader = new EriReader (stream, Info, Palette, prev_frame);
                reader.DecodeImage();
                Frames[index] = reader.Data;
            }
            return Frames[index];
        }
    }

    internal class EriEntry : Entry
    {
        public int  FrameIndex;
        public bool IsDiff;
    }
}
