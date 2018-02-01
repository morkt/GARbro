//! \file       ArcCG.cs
//! \date       2018 Jan 09
//! \brief      ACTGS engine encrypted archive.
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Actgs
{
    [Export(typeof(ArchiveFormat))]
    public class CgOpener : DatOpener
    {
        public override string         Tag { get { return "CG/ACTGS"; } }
        public override string Description { get { return "ACTGS engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            var pattern = file.View.ReadBytes (4, 8);
            var key = FindKey (4, pattern);
            if (null == key)
                return null;
            uint signature_key = key.ToUInt32 (0);
            int count = (int)(file.View.ReadUInt32 (0) ^ signature_key);
            if (!IsSaneCount (count))
                return null;
            uint index_size = 32 * (uint)count;
            using (var enc = file.CreateStream (0x10, index_size))
            using (var input = new ByteStringEncryptedStream (enc, key))
            using (var index = new BinaryStream (input, file.Name))
            {
                var reader = new IndexReader (file.MaxOffset);
                var dir = reader.Read (index, count);
                if (null == dir)
                    return null;
                return new ActressArchive (file, this, dir, key);
            }
        }

        internal byte[] FindKey (int offset, byte[] pattern)
        {
            return Array.Find (KnownKeys, k => KeySequence (k).Skip (offset).Take (pattern.Length).SequenceEqual (pattern));
        }

        internal static IEnumerable<byte> KeySequence (byte[] key)
        {
            for (;;)
            {
                for (int i = 0; i < key.Length; ++i)
                    yield return key[i];
            }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class ArcCgOpener : CgOpener
    {
        public override string         Tag { get { return "CG/ACTGS/2"; } }
        public override string Description { get { return "ACTGS engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset <= 0x1020)
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            var pattern = file.View.ReadBytes (0x1018, 8);
            var key = FindKey (0x18, pattern);
            if (null == key)
                return null;
            var dir = new List<Entry> (count);
            var buffer = new byte[0x20];
            long offset = 0x1000;
            while (file.View.Read (offset, buffer, 0, 0x20) == 0x20)
            {
                offset += 0x20;
                Decrypt (buffer, 0, 0x20, key);
                uint size = buffer.ToUInt32 (0);
                var name = Binary.GetCString (buffer, 4);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = offset;
                entry.Size = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                offset += (size + 0xFu) & ~0xFu;
                dir.Add (entry);
            }
            if (0 == dir.Count)
                return null;
            return new ActressArchive (file, this, dir, key);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var actarc = arc as ActressArchive;
            if (null == actarc || null == actarc.Key)
                return base.OpenEntry (arc, entry);
            var header = ReadEntryHeader (actarc, entry);
            Stream input;
            if (entry.Size <= 0x20)
                input = new BinMemoryStream (header, entry.Name);
            else
                input = new PrefixStream (header, arc.File.CreateStream (entry.Offset+0x20, entry.Size-0x20));
            if (header.AsciiEqual ("BM"))
                return input;
            input.Position = 4;
            return new LzssStream (input);
        }
    }
}
