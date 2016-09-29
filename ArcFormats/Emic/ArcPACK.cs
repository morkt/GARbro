//! \file       ArcPACK.cs
//! \date       Sun Aug 30 01:11:18 2015
//! \brief      Emic engine archive implementation.
//
// Copyright (C) 2015-2016 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Emic
{
    internal class EmicArchive : ArcFile
    {
        public readonly byte[] Key;

        public EmicArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/EMIC"; } }
        public override string Description { get { return "Emic engine resource archive"; } }
        public override uint     Signature { get { return 0x4B434150; } } // 'PACK'
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public PacOpener ()
        {
            Extensions = new string[] { "pac" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            byte[] key;
            int count = file.View.ReadInt32 (0x28);
            uint is_encrypted = file.View.ReadUInt32 (4);
            if (IsSaneCount (count) && is_encrypted <= 1)
            {
                key = file.View.ReadBytes (8, 0x20);
                for (int i = 0; i < key.Length; ++i)
                    key[i] ^= 0xAA;
            }
            else
            {
                count = file.View.ReadInt32 (4);
                is_encrypted = file.View.ReadUInt32 (8);
                if (!IsSaneCount (count) || is_encrypted > 1)
                    return null;
                key = file.View.ReadBytes (0xC, 0x20);
                for (int i = 0; i < key.Length; ++i)
                    key[i] ^= 0xAB;
            }
            Stream input = file.CreateStream();
            if (1 == is_encrypted)
                input = new ByteStringEncryptedStream (input, 0, key);
            using (var reader = new BinaryReader (input))
            {
                input.Position = 0x2C;
                var index_buf = new byte[0x108];
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    int name_len = reader.ReadInt32();
                    if (name_len <= 0 || name_len > index_buf.Length) // file name is too long
                        return null;
                    if (name_len != reader.Read (index_buf, 0, name_len))
                        return null;
                    string name = Binary.GetCString (index_buf, 0, name_len);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Size   = reader.ReadUInt32();
                    entry.Offset = reader.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                if (1 == is_encrypted)
                    return new EmicArchive (file, this, dir, key);
                else
                    return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var emic = arc as EmicArchive;
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (null != emic)
                input = new ByteStringEncryptedStream (input, entry.Offset, emic.Key);
            return input;
        }
    }

    internal class ByteStringEncryptedStream : InputProxyStream
    {
        byte[]  m_key;
        int     m_base_pos;

        public ByteStringEncryptedStream (Stream main, long start_pos, byte[] key, bool leave_open = false)
            : base (main, leave_open)
        {
            m_key = key;
            m_base_pos = (int)(start_pos % m_key.Length);
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int start_pos = (int)((m_base_pos + BaseStream.Position) % m_key.Length);
            int read = BaseStream.Read (buffer, offset, count);
            if (read > 0)
            {
                for (int i = 0; i < read; ++i)
                {
                    buffer[offset+i] ^= m_key[(start_pos + i) % m_key.Length];
                }
            }
            return read;
        }

        public override int ReadByte ()
        {
            long pos = BaseStream.Position;
            int b = BaseStream.ReadByte();
            if (-1 != b)
            {
                b ^= m_key[(m_base_pos + pos) % m_key.Length];
            }
            return b;
        }
    }
}
