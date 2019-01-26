//! \file       ArcDAT.cs
//! \date       Tue Nov 03 07:05:36 2015
//! \brief      ACTGS engine resource archive.
//
// Copyright (C) 2015-2019 by morkt
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

        internal static byte[][] KnownKeys { get { return DefaultScheme.KnownKeys; } }

        static ActressScheme DefaultScheme = new ActressScheme { KnownKeys = Array.Empty<byte[]>() };

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            if (0 != (file.View.ReadInt32 (4) | file.View.ReadInt32 (8) | file.View.ReadInt32 (12)))
                return null;
            const int entry_size = 0x20;
            uint index_length = (uint)(count * entry_size);
            IBinaryStream input = file.CreateStream (0x10, index_length);
            try
            {
                uint first_offset = 0x10u + index_length;
                uint actual_offset = input.Signature;
                byte[] key = null;
                if (actual_offset != first_offset)
                {
                    key = FindKey (first_offset, actual_offset);
                    if (null == key)
                        return null;
                    var decrypted = new ByteStringEncryptedStream (input.AsStream, key);
                    input = new BinaryStream (decrypted, file.Name);
                }
                var reader = new IndexReader (file.MaxOffset);
                var dir = reader.Read (input, count);
                if (null == dir)
                    return null;
                if (null == key)
                    return new ArcFile (file, this, dir);
                else
                    return new ActressArchive (file, this, dir, key);
            }
            finally
            {
                input.Dispose();
            }
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
            if (entry.Name.HasExtension (".wav") && arc.File.View.AsciiEqual (entry.Offset, "RIFF"))
            {
                return arc.File.CreateStream (entry.Offset, entry.Size);
            }
            var header = ReadEntryHeader (actarc, entry);
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

        internal static void Decrypt (byte[] data, int index, int length, byte[] key)
        {
            for (int i = 0; i < length; ++i)
            {
                data[index+i] ^= key[i % key.Length];
            }
        }

        internal byte[] ReadEntryHeader (ActressArchive arc, Entry entry)
        {
            uint length = Math.Min (entry.Size, 0x20u);
            var header = arc.File.View.ReadBytes (entry.Offset, length);
            Decrypt (header, 0, header.Length, arc.Key);
            return header;
        }

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (ActressScheme)value; }
        }
    }

    internal sealed class IndexReader
    {
        long            m_arc_length;
        List<Entry>     m_dir = new List<Entry>();

        public IndexReader (long arc_length)
        {
            m_arc_length = arc_length;
        }

        public List<Entry> Read (IBinaryStream input, int count)
        {
            m_dir.Clear();
            if (m_dir.Capacity < count)
                m_dir.Capacity = count;
            try
            {
                for (int i = 0; i < count; ++i)
                {
                    uint offset = input.ReadUInt32();
                    uint size   = input.ReadUInt32();
                    var name = input.ReadCString (0x18);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = offset;
                    entry.Size   = size;
                    if (!entry.CheckPlacement (m_arc_length))
                        return null;
                    m_dir.Add (entry);
                }
                return m_dir;
            }
            catch
            {
                return null;
            }
        }
    }
}
