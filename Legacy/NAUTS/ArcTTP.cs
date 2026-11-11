//! \file       ArcTTP.cs
//! \date       2026-10-01
//! \brief      NAUTS image archive format.
//
// Copyright (C) 2026 by morkt
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

namespace GameRes.Formats.Nauts
{
    internal class TtpBlock
    {
        public long   Offset;
        public uint   Length;
        public ushort X, Y, W, H;
    }

    internal class TtpEntry : Entry
    {
        public uint     Flags;
        public int      Width;
        public int      Height;
        public TtpBlock Clut;
        public TtpBlock Image;
    }

    [Export(typeof(ArchiveFormat))]
    public class TtpOpener : ArchiveFormat
    {
        public override string         Tag { get { return "TTP/NAUTS"; } }
        public override string Description { get { return "NAUTS TTP image archive"; } }
        public override uint     Signature { get { return 0x00100008; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public TtpOpener ()
        {
            Extensions = new string[] { "ttp" };
            Signatures = new uint[] { 0x00100008, 0x00100004 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension ("ttp"))
                return null;
            // magic is 0x0010000X, the low byte varies between archives
            if (0x00100000 != (file.View.ReadUInt32 (0) & 0xFFFFF000))
                return null;
            int count = file.View.ReadInt32 (0x14);
            if (!IsSaneCount (count))
                return null;
            long index_size = HeaderSize + (long)count * EntrySize;
            if (index_size > file.MaxOffset)
                return null;
            var dir = new List<Entry> (count);
            long index_offset = HeaderSize;
            long data_offset = index_size;
            for (int i = 0; i < count; ++i)
            {
                int width  = file.View.ReadUInt16 (index_offset + 20);
                int height = file.View.ReadUInt16 (index_offset + 22);
                if (0 == width || 0 == height)
                    return null;
                var entry = FormatCatalog.Instance.Create<TtpEntry> (file.View.ReadString (index_offset, 16));
                entry.Width = width;
                entry.Height = height;
                long pos = data_offset;
                var first = new TtpBlock ();
                if (!ReadBlock (file, ref pos, first))
                    return null;
                var image = first;
                var clut = (TtpBlock)null;
                uint mode = InferMode (width, height, first);
                if (2 != mode)
                {
                    var second = new TtpBlock ();
                    if (!ReadBlock (file, ref pos, second))
                        return null;
                    uint second_mode = InferMode (width, height, second);
                    if ((8 == mode || 9 == mode) && IsClutBlock (second))
                        clut = second;
                    else if (IsClutBlock (first) && (8 == second_mode || 9 == second_mode))
                    {
                        mode = second_mode;
                        clut = first;
                        image = second;
                    }
                    else
                        return null;
                }
                entry.Flags = mode;
                entry.Clut = clut;
                entry.Image = image;
                entry.Offset = data_offset;
                entry.Size = (uint)(pos - data_offset);
                data_offset = pos;
                dir.Add (entry);
                index_offset += EntrySize;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var tent = (TtpEntry)entry;
            var output = new MemoryStream ();
            using (var writer = new BinaryWriter (output, System.Text.Encoding.ASCII, true))
            {
                writer.Write (0x10u);
                writer.Write (tent.Flags);
                WriteBlock (writer, arc.File, tent.Clut);
                WriteBlock (writer, arc.File, tent.Image);
            }
            output.Position = 0;
            return output;
        }

        static void WriteBlock (BinaryWriter writer, ArcView file, TtpBlock block)
        {
            if (null == block)
                return;
            writer.Write (block.Length + 12);
            writer.Write (block.X);
            writer.Write (block.Y);
            writer.Write (block.W);
            writer.Write (block.H);
            writer.Write (file.View.ReadBytes (block.Offset, block.Length));
        }

        // mode is derived from the image block dimensions: 2 = 16bpp, 9 = 8bpp, 8 = 4bpp.
        static uint InferMode (int width, int height, TtpBlock block)
        {
            if (block.H != height || block.Length != (uint)block.W * block.H * 2)
                return 0;
            if (block.W == width)
                return 2;
            if (block.W * 2 == width)
                return 9;
            if (block.W * 4 == width)
                return 8;
            return 0;
        }

        static bool IsClutBlock (TtpBlock block)
        {
            return block.H <= 16 && block.W <= 256 && block.Length == (uint)block.W * block.H * 2;
        }

        static bool ReadBlock (ArcView file, ref long pos, TtpBlock block)
        {
            if (pos + 12 > file.MaxOffset)
                return false;
            block.X = file.View.ReadUInt16 (pos);
            block.Y = file.View.ReadUInt16 (pos + 2);
            block.W = file.View.ReadUInt16 (pos + 4);
            block.H = file.View.ReadUInt16 (pos + 6);
            block.Length = file.View.ReadUInt32 (pos + 8);
            if (0 == block.W || 0 == block.H || pos + 12 + block.Length > file.MaxOffset)
                return false;
            block.Offset = pos + 12;
            pos += 12 + block.Length;
            return true;
        }

        const long HeaderSize = 0x24;
        const int  EntrySize  = 0x2C;
    }
}