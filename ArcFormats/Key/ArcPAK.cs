//! \file       ArcPAK.cs
//! \date       2018 Nov 23
//! \brief      Key resource archive.
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
using System.Text;

namespace GameRes.Formats.Key
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/KEY"; } }
        public override string Description { get { return "Key resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static Encoding DefaultEncoding = Encoding.UTF8;

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint data_offset = file.View.ReadUInt32 (0);
            if (data_offset <= 0x24 || data_offset >= file.MaxOffset)
                return null;
            uint block_size = file.View.ReadUInt32 (0xC);
            if (0 == block_size)
                return null;
            uint first_offset = data_offset / block_size;
            if (first_offset * block_size != data_offset)
                return null;
            byte flags = file.View.ReadByte (0x21);
            uint index_offset = 0x24;
            while (index_offset < data_offset)
            {
                if (file.View.ReadUInt32 (index_offset) == first_offset)
                    break;
                index_offset += 4;
            }
            if (index_offset == data_offset)
                return null;

            string[] names;
            if ((flags & 2) != 0) // index has names
            {
                var encoding = DefaultEncoding;
                names = new string[count];
                using (var input = file.CreateStream())
                {
                    uint names_offset = file.View.ReadUInt32 (index_offset - 4);
                    input.Position = names_offset;
                    for (int i = 0; i < count; ++i)
                    {
                        names[i] = input.ReadCString (encoding);
                    }
                }
            }
            else
            {
                names = Enumerable.Range (0, count).Select (x => x.ToString ("D5")).ToArray();
            }
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry { Name = names[i] };
                entry.Offset = (long)file.View.ReadUInt32 (index_offset) * block_size;
                entry.Size   = file.View.ReadUInt32 (index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            foreach (var entry in dir)
            {
                uint signature = file.View.ReadUInt32 (entry.Offset);
                entry.ChangeType (AutoEntry.DetectFileType (signature));
            }
            return new ArcFile (file, this, dir);
        }
    }

    /*
    internal sealed class PakDeserializer : IDisposable
    {
        IBinaryStream   m_input;
        int             m_data_offset;
        int             m_count1;
        int             m_count2;
        int             m_count3;
        int             field_58;
        int             field_5C;
        int             field_60;
        int             field_64;
        int             field_68;

        public PakDeserializer (IBinaryStream input)
        {
            m_input = input;
        }

        public void Read ()
        {
            m_input.Position = 0;
            ReadHeader();
        }

        void ReadHeader ()
        {
            m_data_offset = ReadInt32();
            m_count1 = ReadInt32();
            m_count2 = ReadInt32();
            m_count3 = ReadInt32();
            ReadInt32();
            ReadInt32();
            ReadInt32();
            ReadInt32();
            int n = ReadUInt8();
            int flag = ReadBit();
            ReadBit();
            for (int i = 0; i < n; ++i)
            {
                int val1 = ReadUInt8();
                int val2 = ReadUInt8();
                int val3 = ReadUInt32();
            }
            ReadAlign();
            field_58 = ReadInt32();
            field_60 = (int)m_input.Position;
            if (flag != 0)
            {
                field_68 = field_60 + 8 * m_count1;
                m_input.Position = field_68;
            }
            if (field_58 != 0)
            {
                field_5C = m_data_offset - field_58;
            }
        }

        int     m_bit_pos;
        byte    m_bits;

        int ReadInt32 ()
        {
            if (m_bit_pos != 0)
                m_bit_pos = 0;
            return m_input.ReadInt32();
        }

        byte ReadUInt8 ()
        {
            if (m_bit_pos != 0)
                m_bit_pos = 0;
            return m_input.ReadUInt8();
        }

        int ReadBit ()
        {
            if (0 == m_bit_pos)
            {
                m_bits = m_input.ReadUInt8();
                m_bit_pos = 8;
            }
            return (m_bits >> --m_bit_pos) & 1;
        }

        byte[] m_align_buf = new byte[4];

        int ReadAlign ()
        {
            int pos_align = (int)m_input.Position & 3;
            if (pos_align != 0)
                m_input.Read (m_align_buf, 0, 4 - pos_align);
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
    */
}
