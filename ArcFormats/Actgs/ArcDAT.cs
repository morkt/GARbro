//! \file       ArcDAT.cs
//! \date       Tue Nov 03 07:05:36 2015
//! \brief      ACTGS engine resource archive.
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
using System.Linq;
using GameRes.Utility;
using GameRes.Compression;

namespace GameRes.Formats.Actgs
{
    [Serializable]
    public class ActressScheme : ResourceScheme
    {
        public byte[][] KnownKeys;
    }

    internal class ActressArchive : ArcFile
    {
        public readonly byte[]  Key;

        public ActressArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/ACTGS"; } }
        public override string Description { get { return "ACTGS engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public static byte[][] KnownKeys = { };

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            if (0 != (file.View.ReadInt32 (4) | file.View.ReadInt32 (8) | file.View.ReadInt32 (12)))
                return null;
            const int entry_size = 0x20;
            var index = new byte[count * entry_size];
            if (index.Length != file.View.Read (0x10, index, 0, (uint)index.Length))
                return null;

            uint first_offset = 0x10u + (uint)index.Length;
            uint actual_offset = LittleEndian.ToUInt32 (index, 0);
            byte[] key = null;
            if (actual_offset != first_offset)
            {
                key = FindKey (first_offset, actual_offset);
                if (null == key)
                    return null;
                Decrypt (index, 0, index.Length, key);
            }

            int index_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = Binary.GetCString (index, index_offset+8, 0x18);
                if (0 == name.Length)
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = LittleEndian.ToUInt32 (index, index_offset);
                entry.Size   = LittleEndian.ToUInt32 (index, index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x20;
            }
            if (null == key)
                return new ArcFile (file, this, dir);
            else
                return new ActressArchive (file, this, dir, key);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var actarc = arc as ActressArchive;
            if (null == actarc || null == actarc.Key)
                return base.OpenEntry (arc, entry);
            if (entry.Name.HasExtension (".scr"))
            {
                if ('X' != arc.File.View.ReadByte (entry.Offset))
                    return base.OpenEntry (arc, entry);
                var data = new byte[entry.Size];
                arc.File.View.Read (entry.Offset, data, 0, entry.Size);
                Decrypt (data, 1, data.Length-1, actarc.Key);
                data[0] = (byte)'N';
                return new BinMemoryStream (data, entry.Name);
            }
            if (arc.File.View.AsciiEqual (entry.Offset, "PAK "))
            {
                uint packed_size = arc.File.View.ReadUInt32 (entry.Offset+4);
                var input = arc.File.CreateStream (entry.Offset+12, packed_size);
                return new LzssStream (input);
            }
            uint length = Math.Min (entry.Size, 0x20u);
            var header = new byte[length];
            arc.File.View.Read (entry.Offset, header, 0, length);
            Decrypt (header, 0, (int)length, actarc.Key);
            if (entry.Size <= 0x20)
                return new BinMemoryStream (header, entry.Name);
            var rest = arc.File.CreateStream (entry.Offset+0x20, entry.Size-0x20);
            return new PrefixStream (header, rest);
        }

        byte[] FindKey (uint first_offset, uint actual_offset)
        {
            var pattern = new byte[4];
            LittleEndian.Pack (first_offset ^ actual_offset, pattern, 0);
            return Array.Find (KnownKeys, k => k.Take (4).SequenceEqual (pattern));
        }

        static void Decrypt (byte[] data, int index, int length, byte[] key)
        {
            for (int i = 0; i < length; ++i)
            {
                data[index+i] ^= key[i % key.Length];
            }
        }

        public override ResourceScheme Scheme
        {
            get { return new ActressScheme { KnownKeys = KnownKeys }; }
            set { KnownKeys = ((ActressScheme)value).KnownKeys; }
        }
    }
}
