//! \file       ArcPAK.cs
//! \date       2017 Dec 15
//! \brief      Cocktail Soft resource archive.
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

namespace GameRes.Formats.Cocktail
{
    internal class CompressedEntry : PackedEntry
    {
        public int Compression;
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/COCKTAIL"; } }
        public override string Description { get { return "Cocktail Soft resource archive"; } }
        public override uint     Signature { get { return 0x4B434150; } } // 'PACK'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

//        static readonly uint[] KnownKeyCode = { 0x385AB4BA, 0x52CCCF4E };
//        static readonly uint[] KnownKeyCode = { 0x33C074B5, 0xB6744357 };
        static readonly uint[] KnownKeyCode = { 0xBBB64423, 0x4D765A33 };

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint record_size = file.View.ReadUInt32 (8);
            if (record_size <= 0x10 || record_size > 0x100)
                return null;
            uint index_offset = 0xC;
            uint data_offset = index_offset + (uint)count * record_size;
            if (data_offset >= file.MaxOffset
                || data_offset > file.View.Reserve (0, data_offset))
                return null;

            uint name_size = record_size - 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset+0x10, name_size);
                if (string.IsNullOrEmpty (name))
                    return null;
                var entry = FormatCatalog.Instance.Create<CompressedEntry> (name);
                entry.UnpackedSize = file.View.ReadUInt32 (index_offset);
                entry.Size = file.View.ReadUInt32 (index_offset+4);
                entry.Compression = file.View.ReadInt32 (index_offset+8);
                entry.Offset = file.View.ReadUInt32 (index_offset+0xC) + data_offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.IsPacked = entry.Compression != 0;
                index_offset += record_size;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as CompressedEntry;
            if (null == pent || pent.Compression != 2)
                return base.OpenEntry (arc, entry);

            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
            using (var unpacker = new LzCompression (input, (int)pent.UnpackedSize))
            {
                var data = unpacker.Unpack();
                return new BinMemoryStream (data, entry.Name);
            }
        }
    }

    internal sealed class LzCompression : IDisposable
    {
        MsbBitStream    m_input;
        byte[]          m_output;
        byte[]          m_frame;

        public LzCompression (IBinaryStream input, int unpacked_size)
        {
            m_input = new MsbBitStream (input.AsStream);
            m_output = new byte[unpacked_size];
            m_frame = new byte[0x1000];
        }

        public byte[] Unpack ()
        {
            int dst = 0;
            int frame_pos = 1;
            while (dst < m_output.Length)
            {
                int bit = m_input.GetNextBit();
                if (bit != 0)
                {
                    if (-1 == bit)
                        break;
                    int v = m_input.GetBits (8);
                    if (-1 == v)
                        break;
                    m_frame[frame_pos++ & 0xFFF] = m_output[dst++] = (byte)v;
                }
                else
                {
                    int offset = m_input.GetBits (12);
                    if (-1 == offset)
                        break;
                    int count = m_input.GetBits (4);
                    if (-1 == count)
                        break;
                    count += 2;
                    while (count --> 0)
                    {
                        byte v = m_frame[offset++ & 0xFFF];
                        m_output[dst++] = v;
                        m_frame[frame_pos++ & 0xFFF] = v;
                    }
                }
            }
            return m_output;
        }

        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
    }
}
