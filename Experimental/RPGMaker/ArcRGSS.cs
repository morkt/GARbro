//! \file       ArcRGSS3.cs
//! \date       2017 Nov 18
//! \brief      RPG Maker resource archive implementation.
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
using System.Text;

namespace GameRes.Formats.RPGMaker
{
    [Export(typeof(ArchiveFormat))]
    public class RgssOpener : ArchiveFormat
    {
        public override string         Tag { get { return "RGSSAD"; } }
        public override string Description { get { return "RPG Maker engine resource archive"; } }
        public override uint     Signature { get { return 0x53534752; } } // 'RGSS'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public RgssOpener ()
        {
            Extensions = new string[] { "rgss3a", "rgss2a", "rgssad" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "AD\0"))
                return null;
            int version = file.View.ReadByte (7);
            using (var index = file.CreateStream())
            {
                List<Entry> dir = null;
                if (3 == version)
                    dir = ReadIndexV3 (index);
                else if (1 == version)
                    dir = ReadIndexV1 (index);
                if (null == dir || 0 == dir.Count)
                    return null;
                return new ArcFile (file, this, dir);
            }
        }

        List<Entry> ReadIndexV1 (IBinaryStream file)
        {
            var max_offset = file.Length;
            file.Position = 8;
            var key_gen = new KeyGenerator (0xDEADCAFE);
            var dir = new List<Entry>();
            while (file.PeekByte() != -1)
            {
                uint name_length = file.ReadUInt32() ^ key_gen.GetNext();
                var name_bytes = file.ReadBytes ((int)name_length);
                var name = DecryptName (name_bytes, key_gen);
                var entry = FormatCatalog.Instance.Create<RgssEntry> (name);
                entry.Size   = file.ReadUInt32() ^ key_gen.GetNext();
                entry.Offset = file.Position;
                entry.Key    = key_gen.Current;
                if (!entry.CheckPlacement (max_offset))
                    return null;
                dir.Add (entry);
                file.Seek (entry.Size, SeekOrigin.Current);
            }
            return dir;
        }

        List<Entry> ReadIndexV3 (IBinaryStream file)
        {
            var max_offset = file.Length;
            file.Position = 8;
            uint key = file.ReadUInt32() * 9 + 3;
            var dir = new List<Entry>();
            while (file.PeekByte() != -1)
            {
                uint offset = file.ReadUInt32() ^ key;
                if (0 == offset)
                    break;
                uint size        = file.ReadUInt32() ^ key;
                uint entry_key   = file.ReadUInt32() ^ key;
                uint name_length = file.ReadUInt32() ^ key;
                var name_bytes = file.ReadBytes ((int)name_length);
                var name = DecryptName (name_bytes, key);
                var entry = FormatCatalog.Instance.Create<RgssEntry> (name);
                entry.Offset = offset;
                entry.Size   = size;
                entry.Key    = entry_key;
                if (!entry.CheckPlacement (max_offset))
                    return null;
                dir.Add (entry);
            }
            return dir;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var rent = (RgssEntry)entry;
            var data = arc.File.View.ReadBytes (rent.Offset, rent.Size);
            var key_gen = new KeyGenerator (rent.Key);
            uint key = key_gen.GetNext();
            for (int i = 0; i < data.Length; )
            {
                data[i] ^= (byte)(key >> (i << 3));
                ++i;
                if (0 == (i & 3))
                {
                    key = key_gen.GetNext();
                }
            }
            return new BinMemoryStream (data);
        }

        string DecryptName (byte[] name, KeyGenerator key_gen)
        {
            for (int i = 0; i < name.Length; ++i)
            {
                name[i] ^= (byte)key_gen.GetNext();
            }
            return Encoding.UTF8.GetString (name);
        }

        string DecryptName (byte[] name, uint key)
        {
            for (int i = 0; i < name.Length; ++i)
            {
                name[i] ^= (byte)(key >> (i << 3));
            }
            return Encoding.UTF8.GetString (name);
        }
    }

    internal class RgssEntry : Entry
    {
        public uint Key;
    }

    internal class KeyGenerator
    {
        uint    m_seed;

        public KeyGenerator (uint seed)
        {
            m_seed = seed;
        }

        public uint Current { get { return m_seed; } }

        public uint GetNext ()
        {
            uint key = m_seed;
            m_seed = m_seed * 7 + 3;
            return key;
        }
    }
}
