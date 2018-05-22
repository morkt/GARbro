//! \file       ArcMGX.cs
//! \date       2018 May 20
//! \brief      U-Me Soft multi-frame image.
//
// Copyright (C) 2018 by morkt
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

namespace GameRes.Formats.UMeSoft
{
    [Export(typeof(ArchiveFormat))]
    public class MgxOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MGX"; } }
        public override string Description { get { return "U-Me Soft multi-frame image"; } }
        public override uint     Signature { get { return 0x1A58474D; } } // 'MGX'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public MgxOpener ()
        {
            Extensions = new string[] { "grx" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            int index_offset = 8;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D4}.GRX", base_name, i),
                    Type = "image",
                    Offset = file.View.ReadUInt32 (index_offset),
                };
                index_offset += 4;
                if (entry.Offset > file.MaxOffset)
                    return null;
                dir.Add (entry);
            }
            for (int i = 1; i < count; ++i)
                dir[i-1].Size = (uint)(dir[i].Offset - dir[i-1].Offset);
            dir[count-1].Size = (uint)(file.MaxOffset - dir[count-1].Offset);
            return new ArcFile (file, this, dir);
        }
    }

    internal class MgxMetaData : GrxMetaData
    {
        public uint GrxOffset;
    }

    [Export(typeof(ImageFormat))]
    public class MgxFormat : GrxFormat
    {
        public override string         Tag { get { return "MGX"; } }
        public override string Description { get { return "U-Me Soft multi-frame image"; } }
        public override uint     Signature { get { return 0x1A58474D; } } // 'MGX'

        public MgxFormat ()
        {
            Extensions = new string[] { "grx" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (12);
            uint first_offset = header.ToUInt32 (8);
            file.Position = first_offset;
            if (file.ReadUInt32() != 0x1A585247) // 'GRX'
                return null;
            var info = ReadInfo (file);
            return new MgxMetaData {
                Width    = info.Width,
                Height   = info.Height,
                BPP      = info.BPP,
                IsPacked = info.IsPacked,
                HasAlpha = info.HasAlpha,
                AlphaOffset = info.AlphaOffset,
                GrxOffset = first_offset,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (MgxMetaData)info;
            using (var input = new StreamRegion (file.AsStream, meta.GrxOffset, true))
            {
                var reader = new Reader (input, meta);
                reader.Unpack();
                return ImageData.Create (info, reader.Format, null, reader.Data, reader.Stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MgxFormat.Write not implemented");
        }
    }
}
