//! \file       ArcAM.cs
//! \date       Wed Aug 17 18:31:08 2016
//! \brief      Leaf video resources archive.
//
// Copyright (C) 2016 by morkt
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

namespace GameRes.Formats.Leaf
{
    [Export(typeof(ArchiveFormat))]
    public class AmOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AM/Leaf"; } }
        public override string Description { get { return "Leaf video resources archive"; } }
        public override uint     Signature { get { return 0x30306D61; } } // 'am00'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public AmOpener ()
        {
            Extensions = new string[] { "am" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_size = file.View.ReadUInt32 (4);
            byte key = file.View.ReadByte (8);
            var index = file.View.ReadBytes (9, index_size);
            if (index.Length != index_size)
                return null;
            for (int i = 0; i < index.Length; ++i)
                index[i] ^= key;

            uint base_offset = 9 + index_size;
            int index_offset = 0;
            var dir = new List<Entry>();
            while (index_offset < index.Length)
            {
                int name_end = Array.IndexOf<byte> (index, 0, index_offset);
                if (-1 == name_end || name_end == index_offset)
                    return null;
                var name = Encodings.cp932.GetString (index, index_offset, name_end-index_offset);
                index_offset = name_end+1;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = base_offset + LittleEndian.ToUInt32 (index, index_offset);
                entry.Size = LittleEndian.ToUInt32 (index, index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (null == DecryptTable)
                return input;
            return new AmStream (input, DecryptTable);
        }

        static byte[] DecryptTable = null;

        public override ResourceScheme Scheme
        {
            get { return new AmScheme { DecryptTable = DecryptTable }; }
            set { DecryptTable = ((AmScheme)value).DecryptTable; }
        }
    }

    internal class AmStream : InputProxyStream
    {
        byte[]  m_table;

        public AmStream (Stream input, byte[] table) : base (input)
        {
            m_table = table;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int pos = (int)Position;
            int read = BaseStream.Read (buffer, offset, count);
            for (int i = 0; i < read; ++i)
            {
                buffer[offset+i] ^= m_table[(pos+i) & 0xFFFF];
            }
            return read;
        }

        public override int ReadByte ()
        {
            int b = BaseStream.ReadByte();
            if (-1 != b)
                b ^= m_table[(Position-1) & 0xFFFF];
            return b;
        }
    }

    [Serializable]
    public class AmScheme : ResourceScheme
    {
        public byte[]   DecryptTable;
    }
}
