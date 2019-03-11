//! \file       ArcDAT.cs
//! \date       2019 Mar 11
//! \brief      Cat System resource archive.
//
// Copyright (C) 2019 by morkt
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
using System.Text;

namespace GameRes.Formats.CatSystem
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/CSPACK"; } }
        public override string Description { get { return "Cat System resource archive"; } }
        public override uint     Signature { get { return 0x61507343; } } // 'CsPack2'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "CsPack2"))
                return null;
            uint data_offset = file.View.ReadUInt32 (8);
            const int entry_size = 24;
            int count = (int)(data_offset - 12) / entry_size;
            if (!IsSaneCount (count))
                return null;

            var dir = new List<Entry> (count);
            uint next_offset = data_offset;
            var name_decoder = new CsNameDecryptor (0x1E);
            var entry_buffer = new byte[entry_size];
            int index_pos = 12;
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (index_pos, entry_buffer, 0, entry_size);
                var name = name_decoder.Decrypt (entry_buffer);
                var entry = Create<Entry> (name);
                entry.Offset = next_offset;
                next_offset = entry_buffer.ToUInt32 (0)
                            ^ entry_buffer.ToUInt32 (4)
                            ^ entry_buffer.ToUInt32 (entry_size - 4);
                entry.Size = (uint)(next_offset - entry.Offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_pos += entry_size;
            }
            return new ArcFile (file, this, dir);
        }
    }

    internal class CsNameDecryptor
    {
        int             m_name_length;
        byte[]          m_buffer;
        StringBuilder   m_name;

        public CsNameDecryptor (int name_length)
        {
            m_name_length = name_length;
            m_buffer = new byte[m_name_length];
            m_name = new StringBuilder (m_name_length);
        }

        public string Decrypt (byte[] buffer)
        {
            int length = m_name_length / 6 * 4;
            int dst = 0;
            for (int pos = 0; pos < length; pos += 4)
            {
                uint num = buffer.ToUInt32 (pos);
                for (int i = 5; i >= 0; --i)
                {
                    uint val = num % 40;
                    num /= 40;
                    m_buffer[dst+i] = (byte)val;
                }
                dst += 6;
            }
            m_name.Clear();
            AppendChars (0, m_name_length - 4);
            if (m_buffer[0x10] != 0)
            {
                m_name.Append ('.');
                AppendChars (0x10, 3);
            }
            return m_name.ToString();
        }

        const string Alphabet = "_0123456789abcdefghijklmnopqrstuvwxyz_";

        void AppendChars (int pos, int length)
        {
            for (int i = 0; i < length; ++i)
            {
                if (0 == m_buffer[pos+i])
                    break;
                m_name.Append (Alphabet[m_buffer[pos+i]]);
            }
        }
    }
}
