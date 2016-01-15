//! \file       ArcEncrypted.cs
//! \date       Thu Jan 14 03:27:52 2016
//! \brief      Encrypted AZ system resource archives.
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

using GameRes.Compression;
using GameRes.Utility;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;

namespace GameRes.Formats.AZSys
{
    [Serializable]
    public class EncryptionScheme
    {
        public readonly uint    IndexKey;
        public readonly uint?   ContentKey;

        public static readonly uint[] DefaultSeed = { 0x2F4D7DFE, 0x47345292, 0x1BA5FE82, 0x7BC04525 };

        public EncryptionScheme (uint ikey, uint ckey)
        {
            IndexKey = ikey;
            ContentKey = ckey;
        }

        public EncryptionScheme (uint[] iseed)
        {
            IndexKey = GenerateKey (iseed);
            ContentKey = null;
        }

        public EncryptionScheme (uint[] iseed, byte[] cseed)
        {
            IndexKey = GenerateKey (iseed);
            ContentKey = GenerateContentKey (cseed);
        }

        public static uint GenerateKey (uint[] seed)
        {
            if (null == seed)
                throw new ArgumentNullException ("seed");
            if (seed.Length < 4)
                throw new ArgumentException();
            byte[] seed_bytes = new byte[0x10];
            Buffer.BlockCopy (seed, 0, seed_bytes, 0, 0x10);
            uint key = Crc32.UpdateCrc (~seed[0], seed_bytes, 0, seed_bytes.Length)
                     ^ Crc32.UpdateCrc (~(seed[1] & 0xFFFF), seed_bytes, 0, seed_bytes.Length)
                     ^ Crc32.UpdateCrc (~(seed[1] >> 16), seed_bytes, 0, seed_bytes.Length)
                     ^ Crc32.UpdateCrc (~seed[2], seed_bytes, 0, seed_bytes.Length)
                     ^ Crc32.UpdateCrc (~seed[3], seed_bytes, 0, seed_bytes.Length);
            return seed[0] ^ ~key;
        }

        public static uint GenerateContentKey (byte[] env_bytes)
        {
            if (null == env_bytes)
                throw new ArgumentNullException ("env_bytes");
            if (env_bytes.Length < 0x10)
                throw new ArgumentException();
            uint crc = Crc32.Compute (env_bytes, 0, 0x10);
            var sfmt = new FastMersenneTwister (crc);
            var seed = new uint[4];
            seed[0] = sfmt.GetRand32();
            seed[1] = sfmt.GetRand32() & 0xFFFF;
            seed[1] |= sfmt.GetRand32() << 16;
            seed[2] = sfmt.GetRand32();
            seed[3] = sfmt.GetRand32();
            return GenerateKey (seed);
        }
    }

    [Serializable]
    public class AzScheme : ResourceScheme
    {
        public Dictionary<string, EncryptionScheme> KnownSchemes;
    }

    internal class AzArchive : ArcFile
    {
        public readonly uint SysenvKey;
        public readonly uint RegularKey;

        public AzArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, uint syskey, uint regkey)
            : base (arc, impl, dir)
        {
            SysenvKey = syskey;
            RegularKey = regkey;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class ArcEncryptedOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/AZ/encrypted"; } }
        public override string Description { get { return "AZ system encrypted resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public static Dictionary<string, EncryptionScheme> KnownSchemes = new Dictionary<string, EncryptionScheme>
        {
            { "Default", new EncryptionScheme (EncryptionScheme.DefaultSeed) },
        };

        public override ResourceScheme Scheme
        {
            get { return new AzScheme { KnownSchemes = KnownSchemes }; }
            set { KnownSchemes = ((AzScheme)value).KnownSchemes; }
        }

        public ArcEncryptedOpener ()
        {
            Extensions = new string[] { "arc" };
            Signatures = new uint[] { 0x53EA06EB, 0x74F98F2F };
        }

        EncryptionScheme CurrentScheme;

        public override ArcFile TryOpen (ArcView file)
        {
            byte[] header_encrypted = file.View.ReadBytes (0, 0x30);
            if (header_encrypted.Length < 0x30)
                return null;
            byte[] header = new byte[header_encrypted.Length];
            if (CurrentScheme != null)
            {
                try
                {
                    Buffer.BlockCopy (header_encrypted, 0, header, 0, header.Length);
                    Decrypt (header, 0, CurrentScheme.IndexKey);
                    if (Binary.AsciiEqual (header, 0, "ARC\0"))
                    {
                        var arc = ReadIndex (file, header, CurrentScheme);
                        if (null != arc)
                            return arc;
                    }
                }
                catch { /* ignore parse errors */ }
            }
            foreach (var scheme in KnownSchemes.Values)
            {
                Buffer.BlockCopy (header_encrypted, 0, header, 0, header.Length);
                Decrypt (header, 0, scheme.IndexKey);
                if (Binary.AsciiEqual (header, 0, "ARC\0"))
                {
                    var arc = ReadIndex (file, header, scheme);
                    if (null != arc)
                        CurrentScheme = new EncryptionScheme (arc.SysenvKey, arc.RegularKey);
                    return arc;
                }
            }
            return null;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var azarc = arc as AzArchive;
            if (null == azarc)
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            if (entry.Name.Equals ("sysenv.tbl", StringComparison.InvariantCultureIgnoreCase))
            {
                Decrypt (data, entry.Offset, azarc.SysenvKey);
                return UnpackData (data);
            }
            Decrypt (data, entry.Offset, azarc.RegularKey);
            if (data.Length > 0x14 && Binary.AsciiEqual (data, 0, "ASB\0"))
            {
                return OpenAsb (data);
            }
            return new MemoryStream (data);
        }

        Stream OpenAsb (byte[] data)
        {
            int packed_size = LittleEndian.ToInt32 (data, 4);
            if (packed_size <= 4 || packed_size > data.Length-0x10)
                return new MemoryStream (data);

            uint unpacked_size = LittleEndian.ToUInt32 (data, 8);
            uint key = unpacked_size ^ 0x9E370001;
            unsafe
            {
                fixed (byte* raw = &data[0x10])
                {
                    uint* data32 = (uint*)raw;
                    for (int i = packed_size/4; i > 0; --i)
                        *data32++ -= key;
                }
            }
            var asb = UnpackData (data, 0x10);
            var header = new byte[0x10];
            Buffer.BlockCopy (data, 0, header, 0, 0x10);
            return new PrefixStream (header, asb);
        }

        byte[] ReadSysenvSeed (ArcView file, Entry entry, uint key)
        {
            var data = file.View.ReadBytes (entry.Offset, entry.Size);
            if (data.Length <= 4)
                throw new InvalidFormatException ("Invalid sysenv.tbl size");
            Decrypt (data, entry.Offset, key);
            uint adler32 = LittleEndian.ToUInt32 (data, 0);
            if (adler32 != Adler32.Compute (data, 4, data.Length-4))
                throw new InvalidEncryptionScheme();
            using (var input = new MemoryStream (data, 4, data.Length-4))
            using (var sysenv_stream = new ZLibStream (input, CompressionMode.Decompress))
            {
                var seed = new byte[0x10];
                if (0x10 != sysenv_stream.Read (seed, 0, 0x10))
                    throw new InvalidFormatException ("Invalid sysenv.tbl size");
                return seed;
            }
        }

        Stream UnpackData (byte[] data, int index = 0)
        {
            int length = data.Length - index;
            if (length <= 4)
                return new MemoryStream (data, index, length);
            uint adler32 = LittleEndian.ToUInt32 (data, index);
            if (adler32 != Adler32.Compute (data, index+4, length-4))
                return new MemoryStream (data, index, length);
            var input = new MemoryStream (data, index+4, length-4);
            return new ZLibStream (input, CompressionMode.Decompress);
        }

        AzArchive ReadIndex (ArcView file, byte[] header, EncryptionScheme scheme)
        {
            int ext_count = LittleEndian.ToInt32 (header, 4);
            int count = LittleEndian.ToInt32 (header, 8);
            uint index_length = LittleEndian.ToUInt32 (header, 12);
            if (ext_count < 1 || ext_count > 8 || !IsSaneCount (count) || index_length >= file.MaxOffset)
                return null;
            var packed_index = file.View.ReadBytes (header.Length, index_length);
            if (packed_index.Length != index_length)
                return null;
            Decrypt (packed_index, header.Length, scheme.IndexKey);
            uint checksum = LittleEndian.ToUInt32 (packed_index, 0);
            if (checksum != Adler32.Compute (packed_index, 4, packed_index.Length-4))
            {
                if (checksum != Crc32.Compute (packed_index, 4, packed_index.Length-4))
                    throw new InvalidFormatException ("Index checksum mismatch");
            }
            uint base_offset = (uint)header.Length + index_length;
            using (var input = new MemoryStream (packed_index, 4, packed_index.Length-4))
            using (var zstream = new ZLibStream (input, CompressionMode.Decompress))
            using (var index = new BinaryReader (zstream))
            {
                var dir = new List<Entry> (count);
                var name_buffer = new byte[0x20];
                for (int i = 0; i < count; ++i)
                {
                    uint offset = index.ReadUInt32();
                    uint size   = index.ReadUInt32();
                    uint crc    = index.ReadUInt32();
                    index.ReadInt32();
                    if (name_buffer.Length != index.Read (name_buffer, 0, name_buffer.Length))
                        return null;
                    var name = Binary.GetCString (name_buffer, 0, 0x20);
                    if (0 == name.Length)
                        return null;
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = base_offset + offset;
                    entry.Size   = size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                uint content_key = GetContentKey (file, dir, scheme);
                return new AzArchive (file, this, dir, scheme.IndexKey, content_key);
            }
        }

        static void Decrypt (byte[] data, long offset, uint key)
        {
            ulong hash = key * 0x9E370001ul;
            if (0 != (offset & 0x3F))
            {
                hash = Binary.RotL (hash, (int)offset);
            }
            for (uint i = 0; i < data.Length; ++i)
            {
                data[i] ^= (byte)hash;
                hash = Binary.RotL (hash, 1);
            }
        }

        uint GetContentKey (ArcView file, List<Entry> dir, EncryptionScheme scheme)
        {
            if (null != scheme.ContentKey)
                return scheme.ContentKey.Value;

            if ("system.arc".Equals (Path.GetFileName (file.Name), StringComparison.InvariantCultureIgnoreCase))
            {
                var sysenv = dir.FirstOrDefault (e => e.Name.Equals ("sysenv.tbl", StringComparison.InvariantCultureIgnoreCase));
                if (null != sysenv)
                {
                    var seed = ReadSysenvSeed (file, sysenv, scheme.IndexKey);
                    return EncryptionScheme.GenerateContentKey (seed);
                }
            }
            else
            {
                var system_arc = VFS.CombinePath (Path.GetDirectoryName (file.Name), "system.arc");
                using (var arc = VFS.OpenView (system_arc))
                {
                    var header = arc.View.ReadBytes (0, 0x30);
                    Decrypt (header, 0, scheme.IndexKey);
                    using (var arc_file = ReadIndex (arc, header, scheme))
                    {
                        var sysenv = arc_file.Dir.FirstOrDefault (e => e.Name.Equals ("sysenv.tbl", StringComparison.InvariantCultureIgnoreCase));
                        if (null != sysenv)
                        {
                            var seed = ReadSysenvSeed (arc, sysenv, scheme.IndexKey);
                            return EncryptionScheme.GenerateContentKey (seed);
                        }
                    }
                }
            }
            return scheme.IndexKey;
        }
    }
}
