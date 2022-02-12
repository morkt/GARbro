//! \file       ArcAN21.cs
//! \date       Sun Apr 30 21:04:25 2017
//! \brief      KaGuYa script engine animation resource.
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
using System.Linq;
using System.Windows.Media;

namespace GameRes.Formats.Kaguya
{
    class PL10Entry : PackedEntry
    {
        public int FrameIndex;
        public int RleStep;
    }

    [Export(typeof(ArchiveFormat))]
    public class PL10Opener : ArchiveFormat
    {
        public override string Tag { get { return "PL10/KAGUYA"; } }
        public override string Description { get { return "KaGuYa script engine animation resource"; } }
        public override uint Signature { get { return 0x30314C50; } } // 'PL10'
        public override bool IsHierarchic { get { return false; } }
        public override bool CanWrite { get { return false; } }

        public PL10Opener()
        {
            Extensions = new string[] { "plt" };
        }

        public override ArcFile TryOpen(ArcView file)
        {
            uint current_offset = 4;
            int frame_count = file.View.ReadUInt16(current_offset);
            if (!IsSaneCount(frame_count))
                return null;
            current_offset += 0x12;
            string base_name = Path.GetFileNameWithoutExtension(file.Name);
            var dir = new List<Entry>(frame_count);
            var info = new ImageMetaData
            {
                OffsetX = file.View.ReadInt32(current_offset),
                OffsetY = file.View.ReadInt32(current_offset + 4),
                Width = file.View.ReadUInt32(current_offset + 8),
                Height = file.View.ReadUInt32(current_offset + 12),
            };
            int channels = file.View.ReadInt32(current_offset + 0x10);
            info.BPP = channels * 8;
            current_offset += 0x14;
            var entry = new PL10Entry
            {
                FrameIndex = 0,
                Name = string.Format("{0}#{1:D2}", base_name, 0),
                Type = "image",
                Offset = current_offset,
                Size = (uint)channels * info.Width * info.Height,
                IsPacked = false,
            };
            dir.Add(entry);
            current_offset += entry.Size;
            for (int i = 1; i < frame_count; ++i)
            {
                int step = file.View.ReadByte(current_offset++);
                if (0 == step)
                    return null;
                uint packed_size = file.View.ReadUInt32(current_offset);
                uint unpacked_size = (uint)(channels * (info.OffsetX + (int)info.Width)
                                                     * (info.OffsetY + (int)info.Height));
                current_offset += 4;
                entry = new PL10Entry
                {
                    FrameIndex = i,
                    Name = string.Format("{0}#{1:D2}", base_name, i),
                    Type = "image",
                    Offset = current_offset,
                    Size = packed_size,
                    UnpackedSize = unpacked_size,
                    IsPacked = true,
                    RleStep = step,
                };
                dir.Add(entry);
                current_offset += packed_size;
            }
            return new PL10Archive(file, this, dir, info);
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            var anent = entry as PL10Entry;
            var input = arc.File.CreateStream(entry.Offset, entry.Size);
            if (null == anent || !anent.IsPacked)
                return input;
            using (input)
            {
                var data = DecompressRLE(input, anent.UnpackedSize, anent.RleStep);
                return new BinMemoryStream(data);
            }
        }

        public override IImageDecoder OpenImage(ArcFile arc, Entry entry)
        {
            var anarc = (PL10Archive)arc;
            var anent = (PL10Entry)entry;
            var pixels = anarc.GetFrame(anent.FrameIndex);
            return new BitmapDecoder(pixels, anarc.ImageInfo);
        }

        internal static byte[] DecompressRLE(IBinaryStream input, uint unpacked_size, int rle_step)
        {
            var output = new byte[unpacked_size];
            for (int i = 0; i < rle_step; ++i)
            {
                byte v1 = input.ReadUInt8();
                output[i] = v1;
                int dst = i + rle_step;
                while (dst < output.Length)
                {
                    byte v2 = input.ReadUInt8();
                    output[dst] = v2;
                    dst += rle_step;
                    if (v2 == v1)
                    {
                        int count = input.ReadUInt8();
                        if (0 != (count & 0x80))
                            count = input.ReadUInt8() + ((count & 0x7F) << 8) + 128;
                        while (count-- > 0 && dst < output.Length)
                        {
                            output[dst] = v2;
                            dst += rle_step;
                        }
                        if (dst < output.Length)
                        {
                            v2 = input.ReadUInt8();
                            output[dst] = v2;
                            dst += rle_step;
                        }
                    }
                    v1 = v2;
                }
            }
            return output;
        }
    }

    class PL10Archive : ArcFile
    {
        byte[][] Frames;

        public readonly ImageMetaData ImageInfo;

        public PL10Archive(ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, ImageMetaData base_info)
            : base(arc, impl, dir)
        {
            Frames = new byte[dir.Count][];
            ImageInfo = base_info;
        }

        public byte[] GetFrame(int index)
        {
            if (index >= Frames.Length)
                throw new ArgumentException("index");
            if (null != Frames[index])
                return Frames[index];

            var entry = Dir.ElementAt(index);
            byte[] pixels;
            using (var stream = OpenEntry(entry))
            {
                pixels = new byte[stream.Length];
                stream.Read(pixels, 0, pixels.Length);
            }
            if (index > 0)
            {
                var prev_frame = GetFrame(index - 1);
                for (int i = 0; i < pixels.Length; ++i)
                    pixels[i] += prev_frame[i];
            }
            Frames[index] = pixels;
            return pixels;
        }
    }

}
