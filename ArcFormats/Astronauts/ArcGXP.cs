//! \file       ArcGXP.cs
//! \date       Thu Jun 30 08:17:12 2016
//! \brief      Astronauts resource archive.
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
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Astronauts
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GXP"; } }
        public override string Description { get { return "Astronauts resource archive"; } }
        public override uint     Signature { get { return 0x505847; } } // 'GXP'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        private bool UseNameAsKey = false;

        static readonly byte[] KnownKey = {
            0x40, 0x21, 0x28, 0x38, 0xA6, 0x6E, 0x43, 0xA5, 0x40, 0x21, 0x28, 0x38, 0xA6, 0x43, 0xA5, 0x64,
            0x3E, 0x65, 0x24, 0x20, 0x46, 0x6E, 0x74,
        };

        public override ArcFile TryOpen(ArcView file)
        {
            UseNameAsKey = false;
            ArcFile arc = TryOpenGxpFile(file);
            if(null == arc)
            {
                UseNameAsKey = true;    //try use arc name as key
                arc = TryOpenGxpFile(file);
            }
            return arc;
        }

        private ArcFile TryOpenGxpFile (ArcView file)
        {
            int count = file.View.ReadInt32 (0x18);
            if (!IsSaneCount (count))
                return null;
            string arcname = Path.GetFileName(file.Name);
            byte[] arcname_bytes = Encoding.ASCII.GetBytes(arcname);
            long base_offset = file.View.ReadInt64 (0x28);
            uint entry_key = KnownKey[0] | (1u ^ KnownKey[1]) << 8 | (2u ^ KnownKey[2]) << 16 | (3u ^ KnownKey[3]) << 24;
            if (UseNameAsKey)
            {
                uint arcname_key = (uint)(arcname_bytes[0] | (arcname_bytes[1 % arcname_bytes.Length]) << 8 | (arcname_bytes[2 % arcname_bytes.Length]) << 16 | (arcname_bytes[3 % arcname_bytes.Length]) << 24);
                entry_key ^= arcname_key;
            }
            uint index_offset = 0x30;
            var entry_buffer = new byte[0x100];
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry_length = file.View.ReadUInt32 (index_offset) ^ entry_key;
                if (entry_length < 0x20 || entry_length > 0x1000)
                    return null;
                if (entry_length > entry_buffer.Length)
                    entry_buffer = new byte[entry_length];
                if (entry_length != file.View.Read (index_offset, entry_buffer, 0, entry_length))
                    return null;
                if (UseNameAsKey)
                    Decrypt(entry_buffer, entry_length, arcname_bytes);
                else
                    Decrypt(entry_buffer, entry_length);
                int name_length = LittleEndian.ToInt32 (entry_buffer, 0xC) * 2; // length in characters
                if (name_length >= entry_length)
                    return null;
                var name = Encoding.Unicode.GetString (entry_buffer, 0x20, name_length);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = base_offset + LittleEndian.ToInt64 (entry_buffer, 0x18);
                entry.Size   = LittleEndian.ToUInt32 (entry_buffer, 4); // length is 64-bit actually
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += entry_length;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            if (UseNameAsKey)
            {
                string arcname = Path.GetFileName(arc.File.Name);
                byte[] arcname_bytes = Encoding.ASCII.GetBytes(arcname);
                Decrypt(data, entry.Size, arcname_bytes);
            }
            else
                Decrypt(data, entry.Size);
            return new BinMemoryStream (data, entry.Name);
        }

        static void Decrypt (byte[] data, uint length, byte[] key=null)
        {
            for (uint i = 0; i < length; ++i)
            {
                byte xorkey = (byte)(i ^ KnownKey[i % KnownKey.Length]);
                if(null != key)
                    xorkey ^= key[i % key.Length];
                data[i] ^= xorkey;
            }
        }
    }
}
