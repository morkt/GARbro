//! \file       ArcMCD.cs
//! \date       2017 Dec 10
//! \brief      TSD resource archive.
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

namespace GameRes.Formats.Tsd
{
    [Export(typeof(ArchiveFormat))]
    public class McdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MCD/TSD"; } }
        public override string Description { get { return "TSD engine resource archive"; } }
        public override uint     Signature { get { return 0x20484C4F; } } // 'OLH '
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "for Win"))
                return null;
            uint index_offset = file.View.ReadUInt32 (file.MaxOffset-8);
            int count = file.View.ReadInt32 (file.MaxOffset-4);
            if (!IsSaneCount (count) || index_offset >= file.MaxOffset)
                return null;
            uint index_size = (uint)count * 8u;
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D4}", base_name, i),
                    Offset = file.View.ReadUInt32 (index_offset),
                    Size   = file.View.ReadUInt32 (index_offset+4),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            foreach (var entry in dir)
            {
                uint signature = file.View.ReadUInt32 (entry.Offset);
                if (0 == (signature & 0xFFFF) || 0x4D42 == (signature & 0xFFFF))
                    entry.Type = "image";
                else if (AudioFormat.Wav.Signature == signature)
                    entry.Type = "audio";
            }
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var header = arc.File.View.ReadBytes (entry.Offset, 2);
            if (header[0] != 0 || header[1] != 0)
                return base.OpenImage (arc, entry);
            header[0] = (byte)'B';
            header[1] = (byte)'M';
            Stream input = arc.File.CreateStream (entry.Offset+2, entry.Size-2);
            try
            {
                input = new PrefixStream (header, input);
                var bmp = new BinaryStream (input, entry.Name);
                var info = ImageFormat.Bmp.ReadMetaData (bmp);
                if (null == info)
                    throw new InvalidFormatException();
                bmp.Position = 0;
                return new ImageFormatDecoder (bmp, ImageFormat.Bmp, info);
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }
    }
}
