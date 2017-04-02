//! \file       ArcARC.cs
//! \date       Thu Mar 23 20:55:00 2017
//! \brief      MicroVision resource archive.
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

namespace GameRes.Formats.MicroVision
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/MICROVISION"; } }
        public override string Description { get { return "MicroVision resource archive"; } }
        public override uint     Signature { get { return 0x31435241; } } // 'ARC1'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            var arc = new ArcReader (file);
            int count = (int)arc.ReadHeader (0xD);
            if (!IsSaneCount (count))
                return null;
            uint names_length;
            uint names_offset = arc.ReadHeader (8);
            if (names_offset != 0)
            {
                names_length = arc.ReadHeader (9);
            }
            else
            {
                names_offset = arc.ReadHeader (7);
                names_length = arc.ReadHeader (6);
            }
            uint index_size = names_offset + names_length;
            index_size = (index_size + 0x7Fu) & ~0x7Fu;
            if (index_size < 0x40 || index_size >= file.MaxOffset)
                return null;
            file.View.Reserve (0, index_size);
            uint index_offset = arc.ReadHeader (4);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name_offset = arc.ReadIndex (index_offset+2);
                var name_length = arc.ReadIndex (index_offset+3);
                var name = file.View.ReadString (name_offset, name_length);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset = arc.ReadIndex (index_offset+4);
                entry.Size   = arc.ReadIndex (index_offset+5);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.UnpackedSize = arc.ReadIndex (index_offset+6);
                entry.IsPacked = entry.UnpackedSize != entry.Size;
                dir.Add (entry);
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var data = new byte[pent.UnpackedSize];
            LzUnpack (input, data);
            return new BinMemoryStream (data);
        }

        void LzUnpack (Stream input, byte[] output)
        {
            var frame = new byte[0x1000];
            int frame_pos = 1;
            const int frame_mask = 0xFFF;
            int dst = 0;
            int bit = 0;
            int ctl = 0;
            while (dst < output.Length)
            {
                if (0 == bit)
                {
                    ctl = input.ReadByte();
                    if (-1 == ctl)
                        break;
                    bit = 0x80;
                }
                if (0 != (ctl & bit))
                {
                    byte b = (byte)input.ReadByte();
                    frame[frame_pos++ & frame_mask] = b;
                    output[dst++] = b;
                }
                else
                {
                    int lo = input.ReadByte();
                    int hi = input.ReadByte();
                    if (-1 == lo || -1 == hi)
                        break;
                    int offset = hi >> 4 | lo << 4;
                    for (int count = 2 + (hi & 0xF); count > 0 && dst < output.Length; --count)
                    {
                        byte v = frame[offset++ & frame_mask];
                        frame[frame_pos++ & frame_mask] = v;
                        output[dst++] = v;
                    }
                }
                bit >>= 1;
            }
        }
    }

    internal sealed class ArcReader
    {
        ArcView     m_file;

        const uint HeaderObfuscationStep = 0xC;
        const uint IndexObfuscationStep = 7;

        public ArcReader (ArcView file)
        {
            m_file = file;
        }

        public uint ReadHeader (uint offset)
        {
            return ReadUInt32 (offset, HeaderObfuscationStep);
        }

        public uint ReadIndex (uint offset)
        {
            return ReadUInt32 (offset, IndexObfuscationStep);
        }

        uint ReadUInt32 (uint offset, uint step)
        {
            uint v = m_file.View.ReadByte (offset);
            offset += step;
            v |= (uint)m_file.View.ReadByte (offset) << 8;
            offset += step;
            v |= (uint)m_file.View.ReadByte (offset) << 16;
            offset += step;
            v |= (uint)m_file.View.ReadByte (offset) << 24;
            return v;
        }
    }
}
