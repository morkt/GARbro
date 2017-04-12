//! \file       ArcPKG2.cs
//! \date       Wed Apr 12 03:03:20 2017
//! \brief      Yatagarasu resource archive with different encryption method.
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
using System.Linq;

namespace GameRes.Formats.Yatagarasu
{
    [Export(typeof(ArchiveFormat))]
    public class Pkg2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "PKG/2"; } }
        public override string Description { get { return "Yatagarasu resource archive v2"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".pkg"))
                return null;
            uint first_key = file.View.ReadUInt32 (0) ^ (uint)file.MaxOffset;
            foreach (var key in KnownKeys.Values.Where (k => k[0] == first_key))
            {
                int count = (int)(file.View.ReadUInt32 (4) ^ key[0]);
                if (!IsSaneCount (count))
                    continue;
                try
                {
                    var arc = ReadIndex (file, key, count);
                    if (arc != null)
                        return arc;
                }
                catch { /* ignore parse errors caused by incorrect key */ }
            }
            return null;
        }

        ArcFile ReadIndex (ArcView file, uint[] key, int count)
        {
            using (var input = file.CreateStream (8, (uint)count * 0x80u))
            using (var dec = new ByteStringEncryptedStream (input, GetKeyBytes (key)))
            using (var index = new BinaryStream (dec, file.Name))
            {
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = index.ReadCString (0x74);
                    if (0 == name.Length)
                        return null;
                    var entry = FormatCatalog.Instance.Create<Pkg2Entry> (name);
                    entry.Size = index.ReadUInt32();
                    entry.Offset = index.ReadUInt32();
                    entry.EncryptedSize = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new Pkg2Archive (file, this, dir, key);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pkg_arc = (Pkg2Archive)arc;
            var pkg_ent = (Pkg2Entry)entry;
            var data = new byte[(pkg_ent.Size + 3) & ~3];
            arc.File.View.Read (pkg_ent.Offset, data, 0, pkg_ent.Size);

            uint encrypted_size = pkg_ent.EncryptedSize;
            if (0 == encrypted_size || encrypted_size > pkg_ent.Size)
                encrypted_size = pkg_ent.Size;
            unsafe
            {
                fixed (byte* data8 = data)
                {
                    uint* data32 = (uint*)data8;
                    uint count = (encrypted_size + 3) / 4;
                    uint mask = (entry.Size / 4) & 7;
                    for (uint i = 0; i < count; ++i)
                        data32[i] ^= pkg_arc.Key[i & mask];
                }
            }
            return new BinMemoryStream (data, 0, (int)entry.Size);
        }

        static byte[] GetKeyBytes (uint[] key)
        {
            var bytes = new byte[sizeof(uint) * key.Length];
            Buffer.BlockCopy (key, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        PkgScheme m_scheme = new PkgScheme { KnownKeys = new Dictionary<string, uint[]>() };

        public IDictionary<string, uint[]> KnownKeys
        {
            get { return m_scheme.KnownKeys; }
        }

        public override ResourceScheme Scheme
        {
            get { return m_scheme; }
            set { m_scheme = (PkgScheme)value; }
        }
    }

    internal class Pkg2Archive : ArcFile
    {
        public readonly uint[] Key;

        public Pkg2Archive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, uint[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    internal class Pkg2Entry : Entry
    {
        public uint EncryptedSize;
    }

    [Serializable]
    public class PkgScheme : ResourceScheme
    {
        public IDictionary<string, uint[]>  KnownKeys;
    }
}
