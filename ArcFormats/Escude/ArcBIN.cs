//! \file       ArcBIN.cs
//! \date       Sat Dec 19 06:16:35 2015
//! \brief      Escu:de resource archives.
//
// Copyright (C) 2015 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Escude
{
    [Export(typeof(ArchiveFormat))]
    public class BinOpener : FVP.BinOpener
    {
        public override string         Tag { get { return "BIN/ESC-ARC"; } }
        public override string Description { get { return "Escu:de resource archive"; } }
        public override uint     Signature { get { return 0x2D435345; } } // 'ESC-'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public BinOpener ()
        {
            Signatures = new uint[] { 0x2D435345 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "ARC"))
                return null;
            int version = file.View.ReadByte (7) - '0';
            var reader = new IndexReader (file);
            List<Entry> dir = null;
            if (1 == version)
                dir = reader.ReadIndexV1();
            else if (2 == version)
                dir = reader.ReadIndexV2();
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }
    }

    internal sealed class IndexReader
    {
        ArcView   m_file;
        uint      m_seed;
        uint      m_count;

        public IndexReader (ArcView file)
        {
            m_file = file;
            m_seed = m_file.View.ReadUInt32 (8);
            m_count = file.View.ReadUInt32 (0xC) ^ NextKey();
        }

        public List<Entry> ReadIndexV1 ()
        {
            if (!ArchiveFormat.IsSaneCount ((int)m_count))
                return null;
            uint index_size = m_count * 0x88;
            var index = m_file.View.ReadBytes (0x10, index_size);
            if (index.Length != index_size)
                return null;
            Decrypt (index);
            int index_offset = 0;
            var dir = new List<Entry> ((int)m_count);
            for (uint i = 0; i < m_count; ++i)
            {
                var name = Binary.GetCString (index, index_offset, 0x80);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = LittleEndian.ToUInt32 (index, index_offset+0x80);
                entry.Size   = LittleEndian.ToUInt32 (index, index_offset+0x84);
                if (!entry.CheckPlacement (m_file.MaxOffset))
                    return null;
                index_offset += 0x88;
                dir.Add (entry);
            }
            return dir;
        }

        public List<Entry> ReadIndexV2 ()
        {
            if (!ArchiveFormat.IsSaneCount ((int)m_count))
                return null;

            uint names_size = m_file.View.ReadUInt32 (0x10) ^ NextKey();
            uint index_size = m_count * 12;
            var index = m_file.View.ReadBytes (0x14, index_size);
            if (index.Length != index_size)
                return null;
            uint filenames_base = 0x14 + index_size;
            var names = m_file.View.ReadBytes (filenames_base, names_size);
            if (names.Length != names_size)
                return null;
            Decrypt (index);
            int index_offset = 0;
            var dir = new List<Entry> ((int)m_count);
            for (uint i = 0; i < m_count; ++i)
            {
                int filename_offset = LittleEndian.ToInt32 (index, index_offset);
                if (filename_offset < 0 || filename_offset >= names.Length)
                    return null;
                var name = Binary.GetCString (names, filename_offset, names.Length-filename_offset);
                if (0 == name.Length)
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = LittleEndian.ToUInt32 (index, index_offset+4);
                entry.Size   = LittleEndian.ToUInt32 (index, index_offset+8);
                if (!entry.CheckPlacement (m_file.MaxOffset))
                    return null;
                index_offset += 12;
                dir.Add (entry);
            }
            return dir;
        }

        unsafe void Decrypt (byte[] data)
        {
            fixed (byte* raw = data)
            {
                uint* data32 = (uint*)raw;
                for (int i = data.Length/4; i > 0; --i)
                {
                    *data32++ ^= NextKey();
                }
            }
        }

        uint NextKey ()
        {
            m_seed ^= 0x65AC9365;
            m_seed ^= (((m_seed >> 1) ^ m_seed) >> 3)
                    ^ (((m_seed << 1) ^ m_seed) << 3);
            return m_seed;
        }
    }
}
