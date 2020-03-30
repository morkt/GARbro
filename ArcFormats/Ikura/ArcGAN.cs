//! \file       ArcGAN.cs
//! \date       2018 Sep 03
//! \brief      Ikura animation resource format.
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
using System.Linq;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Ikura
{
    internal class GanEntry : Entry
    {
        public int  Index;
        public int  Id;
        public int  Ref;
    }

    [Export(typeof(ArchiveFormat))]
    public class GanOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GAN"; } }
        public override string Description { get { return "IKURA GDL engine animation resource"; } }
        public override uint     Signature { get { return 0x4D4E4147; } } // 'GANM0100'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "0100"))
                return null;
            int count = file.View.ReadInt32 (12);
            if (!IsSaneCount (count))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint index_offset = 0x2010;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new GanEntry {
                    Name   = string.Format ("{0}#{1:D2}", base_name, i),
                    Type   = "image",
                    Index  = i,
                    Id     = file.View.ReadInt32 (index_offset),
                    Ref    = file.View.ReadInt32 (index_offset+4),
                    Offset = file.View.ReadUInt32 (index_offset+8),
                    Size   = file.View.ReadUInt32 (index_offset+12),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x10;
            }
            return new GanFrameArchive (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var garc = (GanFrameArchive)arc;
            var gent = (GanEntry)entry;
            var pixels = garc.GetFrame (gent);
            return new Entis.BitmapDecoder (pixels, garc.Info, PixelFormats.Bgr24, null);
        }
    }

    internal class GanFrameArchive : ArcFile
    {
        public readonly ImageMetaData   Info;
        byte[][]    Frames;

        const uint DefaultWidth  = 800;
        const uint DefaultHeight = 600;

        public GanFrameArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir)
            : base (arc, impl, dir)
        {
            Info = new ImageMetaData { Width = DefaultWidth, Height = DefaultHeight, BPP = 24 };
            Frames = new byte[dir.Count][];
        }

        public byte[] GetFrame (GanEntry entry)
        {
            int index = entry.Index;
            if (Frames[index] != null)
                return Frames[index];

            byte[] ref_frame = null;
            if (entry.Ref != 0)
            {
                var ref_entry = Dir.Cast<GanEntry>().FirstOrDefault (e => e.Id == entry.Ref);
                if (ref_entry != null && ref_entry != entry)
                    ref_frame = GetFrame (ref_entry);
            }
            using (var stream = File.CreateStream (entry.Offset, entry.Size))
            {
                byte[] pixels;
                if (ref_frame != null)
                {
                    pixels = ref_frame.Clone() as byte[];
                    UnpackRefFrame (stream, pixels);
                }
                else
                {
                    pixels = new byte[(int)Info.Width * (int)Info.Height * 3];
                    UnpackKeyFrame (stream, pixels);
                }
                Frames[index] = pixels;
                return pixels;
            }
        }

        void UnpackKeyFrame (IBinaryStream input, byte[] output)
        {
            int last = -1;
            int dst = 0;
            while (dst < output.Length)
            {
                if (input.Read (output, dst, 3) < 3)
                    break;
                int color = output[dst] | output[dst+1] << 8 | output[dst+2] << 16;
                dst += 3;
                if (color == last)
                {
                    int count = ReadInteger (input);
                    if (-1 == count)
                        break;
                    if (count > 2)
                    {
                        count = Math.Min ((count - 2) * 3, output.Length - dst);
                        Binary.CopyOverlapped (output, dst - 3, dst, count);
                        dst += count;
                    }
                }
                last = color;
            }
        }

        void UnpackRefFrame (IBinaryStream input, byte[] output)
        {
            int header_size = input.ReadInt32();
            if (header_size < 4)
                throw new InvalidFormatException();
            var header = input.ReadBytes (header_size-4);
            using (var chunks = new BinMemoryStream (header))
            {
                int count = 0;
                int last = -1;
                int dst = 0;
                while (dst < output.Length)
                {
                    int length = ReadInteger (chunks);
                    if (-1 == length)
                        break;
                    while (length --> 0)
                    {
                        if (0 == count)
                        {
                            input.Read (output, dst, 3);
                            int color = output[dst] | output[dst+1] << 8 | output[dst+2] << 16;
                            dst += 3;
                            if (color == last)
                            {
                                count = ReadInteger (input);
                                if (-1 == count)
                                    return;
                                if (count > 2)
                                    count -= 2;
                                else
                                    count = 0;
                            }
                            last = color;
                        }
                        else
                        {
                            count--;
                            output[dst++] = (byte)last;
                            output[dst++] = (byte)(last >> 8);
                            output[dst++] = (byte)(last >> 16);
                        }
                    }
                    length = ReadInteger (chunks);
                    if (-1 == length)
                        break;
                    length = Math.Min (length * 3, output.Length - dst);
                    dst += length;
                }
            }
        }

        static int ReadInteger (IBinaryStream input)
        {
            int val = input.ReadByte();
            if (-1 == val)
                return val;
            if (val > 0x7F)
            {
                val = (val & 0x7F) << 8 | input.ReadUInt8();
                if (0x7FFF == val)
                    val = input.ReadInt32();
            }
            return val;
        }
    }
}
