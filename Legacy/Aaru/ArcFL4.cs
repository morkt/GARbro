//! \file       ArcFL4.cs
//! \date       2018 Jan 13
//! \brief      Aaru resource archive.
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
using GameRes.Compression;

// [021025][Aaru] Kokoro... II
// [030530][Aaru] Naked Edge

namespace GameRes.Formats.Aaru
{
    [Export(typeof(ArchiveFormat))]
    public class Fl4Opener : ArchiveFormat
    {
        public override string         Tag { get { return "FL4/AARU"; } }
        public override string Description { get { return "Aaru resource archive"; } }
        public override uint     Signature { get { return 0x2E344C46; } } // 'FL4.0'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadByte (4) != '0')
                return null;
            uint data_offset  = file.View.ReadUInt16 (8);
            uint index_size   = file.View.ReadUInt32 (0xA);
            long index_offset = file.View.ReadUInt32 (0xE);
            if (index_offset + index_size > file.MaxOffset)
                return null;
            ushort key   = file.View.ReadUInt16 (0x16);
            ushort flags = file.View.ReadUInt16 (0x18);
            var index = file.View.ReadBytes (index_offset, index_size);
            int pos = index.ToInt32 (0);
            if (pos <= 0)
                return null;
            var dir = new List<Entry>();
            while (pos < index.Length)
            {
                uint offset = index.ToUInt32 (pos);
                if (uint.MaxValue == offset)
                    break;
                uint size = index.ToUInt32 (pos+4);
                int name_length = index[pos+8];
                pos += 9;
                var name = Encodings.cp932.GetString (index, pos, name_length);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset = offset + data_offset;
                entry.Size   = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                pos += name_length;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
            {
                if (arc.File.View.AsciiEqual (entry.Offset, "PD2A"))
                {
                    pent.IsPacked = true;
                    pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset+12);
                }
                else if (arc.File.View.AsciiEqual (entry.Offset, "PD"))
                {
                    pent.IsPacked = true;
                    pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset+6);
                }
                else if (arc.File.View.AsciiEqual (entry.Offset, "RD1.0"))
                {
                    pent.IsPacked = true;
                }
                if (!pent.IsPacked)
                    return base.OpenEntry (arc, entry);
            }
            if (arc.File.View.AsciiEqual (entry.Offset, "PD2A"))
            {
                var input = arc.File.CreateStream (entry.Offset+16, entry.Size-16);
                return new LzssStream (input);
            }
            else if (arc.File.View.AsciiEqual (entry.Offset, "PD"))
            {
                var input = arc.File.CreateStream (entry.Offset+10, entry.Size-10);
                return new LzssStream (input);
            }
            else
            {
                uint offset = arc.File.View.ReadUInt16 (entry.Offset+6);
                var input = arc.File.CreateStream (entry.Offset+offset, entry.Size-offset);
                int rle_chunks = arc.File.View.ReadInt32 (entry.Offset+0xA);
                return new RlePackedStream (input, rle_chunks);
            }
        }
    }

    internal class RlePackedStream : PackedStream<RleDecompressor>
    {
        public RlePackedStream (Stream input, int rle_chunks) : base (input)
        {
            Reader.Chunks = rle_chunks;
        }
    }

    internal class RleDecompressor : Decompressor
    {
        IBinaryStream   m_input;

        public int Chunks { get; set; }

        public override void Initialize (Stream input)
        {
            m_input = BinaryStream.FromStream (input, "");
        }

        protected override IEnumerator<int> Unpack ()
        {
            for (int i = 0; i < Chunks; ++i)
            {
                int ctl = m_input.ReadByte();
                if (-1 == ctl)
                    yield break;
                int count;
                if (ctl <= 1)
                {
                    if (0 == ctl)
                        count = m_input.ReadUInt8();
                    else
                        count = 0x100;
                    while (count > 0)
                    {
                        int avail = Math.Min (count, m_length);
                        int read = m_input.Read (m_buffer, m_pos, avail);
                        if (0 == read)
                            yield break;
                        count -= read;
                        m_pos += read;
                        m_length -= read;
                        if (0 == m_length)
                            yield return m_pos;
                    }
                }
                else
                {
                    if (3 == ctl)
                        count = m_input.ReadUInt16();
                    else
                        count = ctl;
                    byte v = m_input.ReadUInt8();
                    while (count --> 0)
                    {
                        m_buffer[m_pos++] = v;
                        if (0 == --m_length)
                            yield return m_pos;
                    }
                }
            }
        }
    }
}
