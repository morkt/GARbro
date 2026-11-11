//! \file       ArcSuika.cs
//! \date       2026-09-06
//! \brief      Suika 2/3 resource archive.
//
// Copyright (C) 2026 by morkt
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
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Suika
{
    internal class SuikaArchive : ArcFile
    {
        public ulong ObfsKey;

        public SuikaArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, ulong key)
            : base (arc, impl, dir)
        {
            ObfsKey = key;
        }
    } 

    internal class ArcEntry : Entry
    {
        public int Order;
    }

    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/SUIKA"; } }
        public override string Description { get { return "Suika resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        static readonly ulong[] KnownKeys = new[] { 0xABADCAFEDEADBEEFu, 0xCAFEF00DABADBEEFu };

        public override ArcFile TryOpen (ArcView file)
        {
            ulong count = file.View.ReadUInt64 (0);
            if (!IsSaneCount ((int)count) || (count >> 32) != 0)
                return null;

            if (file.View.ReadByte (0x107) != 0)
            {
                foreach (ulong key in KnownKeys)
                {
                    try
                    {
                        return OpenEncrypted (file, (int)count, key);
                    }
                    catch { /* ignore errors */ }
                }
                return null;
            }
            else
            {
                uint index_offset = 8;
                var dir = new List<Entry> ((int)count);
                for (int i = 0; i < (int)count; i++)
                {
                    var name = file.View.ReadString (index_offset, 0x100);
                    if (string.IsNullOrEmpty (name))
                        return null;
                    var entry = Create<ArcEntry> (name);
                    entry.Size = (uint)file.View.ReadInt64 (index_offset + 0x100);
                    entry.Offset = file.View.ReadInt64 (index_offset + 0x108);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    index_offset += 0x110;
                }
                return new ArcFile (file, this, dir);
            }
        }

        ArcFile OpenEncrypted (ArcView file, int count, ulong key)
        {
            uint index_offset = 8;
            var dir = new List<Entry> (count);

            for (int i = 0; i < count; i++)
            {
                var name_buffer = file.View.ReadBytes (index_offset, 0x100);
                var rnd = new RandomGenerator (i, key);
                for (int j = 0; j < name_buffer.Length; j++)
                    name_buffer[j] ^= rnd.Rand();
                var name = Binary.GetCString (name_buffer, 0, Encoding.UTF8); // XXX may not throw exception on invalid strings
                if (string.IsNullOrEmpty (name))
                    throw new InvalidFormatException();
                var entry = Create<ArcEntry> (name);
                entry.Size = (uint)file.View.ReadInt64 (index_offset + 0x100);
                entry.Offset = file.View.ReadInt64 (index_offset + 0x108);
                entry.Order = i;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x110;
            }

            return new SuikaArchive (file, this, dir, key);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var sarc = arc as SuikaArchive;
            var aent = entry as ArcEntry;

            if (sarc != null)
            {
                var data = sarc.File.View.ReadBytes (aent.Offset, aent.Size);
                var rnd = new RandomGenerator (aent.Order, sarc.ObfsKey);
                for (int i = 0; i < aent.Size; i++)
                    data[i] ^= rnd.Rand();
                return new BinMemoryStream (data, aent.Name);
            }
            return base.OpenEntry (arc, entry);
        }
    }

    internal class RandomGenerator
    {
        ulong m_seed, m_key;

        public RandomGenerator (int index, ulong seed)
        {
            index &= 63;

            m_seed = m_key = seed;
            for (int i = 0; i < index; i++)
            {
                m_seed = Binary.RotL (m_seed ^ 0xAFCB8F2FF4FFF33Fu, 1);
            }
        }

        public byte Rand ()
        {
            ulong ret = m_seed;

            m_seed = (((m_key & 0xFF00) * m_seed + (m_key & 0xFF)) % m_key) ^ 0xFCBFAFF8F2F4F3F0u;

            return (byte)ret;
        }
    }
}
