//! \file       ArcAVC.cs
//! \date       Sun Mar 15 20:41:17 2015
//! \brief      AVC engine resource archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.AVC
{
    public class ArchiveFile : ArcFile
    {
        public readonly byte[] Key;
        public readonly int    HeaderOffset;

        public ArchiveFile (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, int offset, byte[] key)
            : base (arc, impl, dir)
        {
            HeaderOffset = offset;
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AVC"; } }
        public override string Description { get { return "AVC engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var reader = new AdvReader (file);
            var dir = reader.GetIndex();
            if (null == dir)
                return null;
            return new ArchiveFile (file, this, dir, reader.HeaderOffset, reader.Key);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var arcf = arc as ArchiveFile;
            if (null == arcf)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var data = new byte[entry.Size];
            arc.File.View.Read (entry.Offset, data, 0, entry.Size);
            int base_offset = (int)(entry.Offset-arcf.HeaderOffset);
            for (int i = 0; i < data.Length; ++i)
                data[i] ^= arcf.Key[((base_offset+i)&7)];
            return new MemoryStream (data, false);
        }

        internal class AdvReader
        {
            ArcView     m_file;
            byte[]      m_input = new byte[0x80];
            byte[]      m_header = new byte[0x24];
            byte[]      m_key = new byte[8];
            byte[]      m_index;
            int         m_index_offset;
            int         m_count;
            int         m_header_offset;

            public byte[]       Key { get { return m_key; } }
            public int HeaderOffset { get { return m_header_offset; } }

            public AdvReader (ArcView file)
            {
                m_file = file;
            }

            public List<Entry> GetIndex ()
            {
                if (m_input.Length != m_file.View.Read (0, m_input, 0, (uint)m_input.Length))
                    return null;
                foreach (var scheme in KnownSchemes)
                {
                    if (!ReadIndex (scheme.KeyOffset, scheme.HeaderOffset))
                        continue;
                    try
                    {
                        var dir = ParseIndex();
                        if (null != dir)
                            return dir;
                    }
                    catch { /* ignore parse errors */ }
                }
                return null;
            }

            bool ReadIndex (int key_offset, int header_offset)
            {
                // placing predefined string into XORed file is a very smart move
                for (int i = 0; i < 8; ++i)
                {
                    var symbol = m_input[header_offset+i] ^ "ARCHIVE\0"[i];
                    var check = m_input[key_offset+i] ^ symbol;
                    if (check < 0x20 || check > 0x7e)
                        return false;
                    Key[i] = (byte)symbol;
                }
                for (int i = 0x10; i < 0x24; ++i)
                    m_header[i] = (byte)(m_input[header_offset+i] ^ Key[i&7]);
                int entry_size = LittleEndian.ToInt32 (m_header, 0x14);
                if (0x114 != entry_size)
                    return false;
                m_index_offset = LittleEndian.ToInt32 (m_header, 0x10);
                m_count = LittleEndian.ToInt32 (m_header, 0x20);
                if (m_index_offset < 0x24 || (long)m_index_offset+header_offset >= m_file.MaxOffset
                    || m_count <= 0 || m_count > 0xffff)
                    return false;
                int index_size = entry_size * m_count;
                if (null == m_index || m_index.Length < index_size)
                    m_index = new byte[index_size];
                if (index_size != m_file.View.Read (m_index_offset+header_offset, m_index, 0, (uint)index_size))
                    return false;
                m_header_offset = header_offset;
                return true;
            }

            List<Entry> ParseIndex()
            {
                for (int i = 0; i < 0x114 * m_count; ++i)
                    m_index[i] ^= Key[(m_index_offset+i)&7];
                var dir = new List<Entry> (m_count);
                int index_offset = 0;
                for (int i = 0; i < m_count; ++i)
                {
                    if (0 != m_index[index_offset++])
                        return null;
                    int name_length = 0;
                    while (name_length < 0x100 && 0 != m_index[index_offset+name_length])
                        name_length++;
                    if (0 == name_length)
                    {
                        index_offset += 0x113;
                        continue;
                    }
                    var name = Encodings.cp932.GetString (m_index, index_offset, name_length);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    index_offset += 0x107;
                    entry.Offset = m_header_offset + LittleEndian.ToUInt32 (m_index, index_offset);
                    entry.Size   = LittleEndian.ToUInt32 (m_index, index_offset+4);
                    index_offset += 0x0c;
                    dir.Add (entry);
                }
                return dir;
            }

            internal class Scheme
            {
                public string   Password;
                public int      KeyOffset;
                public int      HeaderOffset;
            };

            private static readonly Scheme[] KnownSchemes = new Scheme[] {
                new Scheme { Password="SETSUEI-", KeyOffset=0x08, HeaderOffset=0x10 }, // Setsuei
                new Scheme { Password="CHOKOPAI", KeyOffset=0x35, HeaderOffset=0x51 }, // Chokotto*Vampire!
                new Scheme { Password="ClOVeRrE", KeyOffset=0x11, HeaderOffset=0x46 }, // Clover Point
                new Scheme { Password="-AYASEKE", KeyOffset=0x0c, HeaderOffset=0x15 }, // Ayase Ke no Onna
            };
        }
    }
}
