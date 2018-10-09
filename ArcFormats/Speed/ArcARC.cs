//! \file       ArcARC.cs
//! \date       2018 Oct 09
//! \brief      Speed resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Speed
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/REC"; } }
        public override string Description { get { return "REC engine resource archive"; } }
        public override uint     Signature { get { return 0xFF; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            uint rec_length = file.View.ReadUInt32 (0);
            if (rec_length < 0x10 || rec_length > 0x200)
                return null;
            int rec_count = file.View.ReadInt32 (4);
            if (rec_count < count || rec_length * rec_count >= file.MaxOffset)
                return null;
            long index_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, rec_length);
                var entry = Create<Entry> (name);
                dir.Add (entry);
                index_offset += rec_length;
            }
            index_offset = 0x10 + rec_length * (rec_count + 2);
            rec_length = file.View.ReadUInt32 (index_offset);
            rec_count = file.View.ReadInt32 (index_offset+4);
            if (rec_length != 4 || count != file.View.ReadInt32 (index_offset+8))
                return null;
            index_offset += 0x10;
            long data_offset = index_offset + rec_length * (rec_count + 2) + 4;
            foreach (var entry in dir)
            {
                entry.Offset = data_offset + file.View.ReadUInt32 (index_offset);
                if (entry.Type == "image")
                    entry.Offset += 16;
                else
                    entry.Offset += 4;
                if (entry.Offset > file.MaxOffset)
                    return null;
                index_offset += rec_length;
            }
            for (int i = 1; i < count; ++i)
                dir[i-1].Size = (uint)(dir[i].Offset - 4 - dir[i-1].Offset);
            dir[count-1].Size = (uint)(file.MaxOffset - dir[count-1].Offset);
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            uint size = arc.File.View.ReadUInt32 (entry.Offset-4);
            return arc.File.CreateStream (entry.Offset, size, entry.Name);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            int width = arc.File.View.ReadInt32 (entry.Offset-16);
            int height = arc.File.View.ReadInt32 (entry.Offset-12);
            uint unpacked_size = arc.File.View.ReadUInt32 (entry.Offset-8);
            using (var input = OpenEntry (arc, entry))
            {
                var pixels = new byte[unpacked_size];
                LzUnpack (input, pixels);
                var bitmap = BitmapSource.Create (width, height, ImageData.DefaultDpiX, ImageData.DefaultDpiY,
                                                  PixelFormats.Bgra32, null, pixels, width * 4);
                return new BitmapSourceDecoder (bitmap);
            }
        }

        void LzUnpack (Stream input, byte[] output)
        {
            using (var bits = new MsbBitStream (input, true))
            {
                int dst = 0;
                while (dst < output.Length)
                {
                    int ctl = bits.GetNextBit();
                    if (-1 == ctl)
                        break;
                    if (0 == ctl)
                    {
                        output[dst++] = (byte)bits.GetBits (8);
                    }
                    else
                    {
                        int offset = bits.GetBits (8);
                        int count = bits.GetBits (8);
                        if (offset <= 0)
                            break;
                        Binary.CopyOverlapped (output, dst - offset, dst, count);
                        dst += count;
                    }
                }
            }
        }
    }
}
