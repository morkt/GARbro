//! \file       ArcTAC.cs
//! \date       Wed Jan 25 04:41:35 2017
//! \brief      TanukiSoft resource archive.
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
using GameRes.Compression;
using GameRes.Cryptography;

namespace GameRes.Formats.Tanuki
{
    [Export(typeof(ArchiveFormat))]
    public class TacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "TAC"; } }
        public override string Description { get { return "TanukiSoft resource archive"; } }
        public override uint     Signature { get { return 0x63724154; } } // 'TArc'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public TacOpener ()
        {
            Extensions = new string[] { "tac", "stx" };
        }

        static readonly byte[] IndexKey = Encoding.ASCII.GetBytes ("TLibArchiveData");

        static readonly string ListFileName = "tanuki.lst";

        public override ArcFile TryOpen (ArcView file)
        {
            int version;
            if (file.View.AsciiEqual (4, "1.00"))
                version = 100;
            else if (file.View.AsciiEqual (4, "1.10"))
                version = 110;
            else
                return null;
            int count = file.View.ReadInt32 (0x14);
            if (!IsSaneCount (count))
                return null;

            int bucket_count = file.View.ReadInt32 (0x18);
            uint index_size = file.View.ReadUInt32 (0x1C);
            uint arc_seed = file.View.ReadUInt32 (0x20);
            long index_offset = version >= 110 ? 0x2C : 0x24;
            long base_offset = index_offset + index_size;
            var blowfish = new Blowfish (IndexKey);
            var packed_bytes = file.View.ReadBytes (index_offset, index_size);
            blowfish.Decipher (packed_bytes, packed_bytes.Length & ~7);

            using (var input = new MemoryStream (packed_bytes))
            using (var unpacked = new ZLibStream (input, CompressionMode.Decompress))
            using (var index = new BinaryReader (unpacked))
            {
                var file_map = BuildFileNameMap (arc_seed);
                var dir_table = new List<TacBucket> (bucket_count);
                for (int i = 0; i < bucket_count; ++i)
                {
                    var entry = new TacBucket();
                    entry.Hash = index.ReadUInt16();
                    entry.Count = index.ReadUInt16();
                    entry.Index = index.ReadInt32();
                    dir_table.Add (entry);
                }
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var entry = new TacEntry();
                    entry.Hash = index.ReadUInt64();
                    entry.IsPacked = index.ReadInt32() != 0;
                    entry.UnpackedSize = index.ReadUInt32();
                    entry.Offset = base_offset + index.ReadUInt32();
                    entry.Size = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                var buffer = new byte[8];
                foreach (var bucket in dir_table)
                {
                    for (int i = 0; i < bucket.Count; ++i)
                    {
                        var entry = dir[bucket.Index+i] as TacEntry;
                        entry.Hash = entry.Hash << 16 | bucket.Hash;
                        bool known_name = file_map.ContainsKey (entry.Hash);
                        if (known_name)
                        {
                            entry.Name = file_map[entry.Hash];
                            entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name);
                        }
                        else
                        {
                            entry.Name = string.Format ("{0:X16}", entry.Hash);
                        }
                        if (entry.IsPacked)
                            continue;
                        entry.Key = Encoding.ASCII.GetBytes (string.Format ("{0}_tlib_secure_", entry.Hash));
                        if (!known_name)
                        {
                            var bf = new Blowfish (entry.Key);
                            file.View.Read (entry.Offset, buffer, 0, 8);
                            bf.Decipher (buffer, 8);
                            var res = AutoEntry.DetectFileType (buffer.ToUInt32 (0));
                            if (res != null)
                                entry.ChangeType (res);
                        }
                        if ("image" == entry.Type && !entry.Name.HasExtension (".af"))
                            entry.EncryptedSize = Math.Min (10240, entry.Size);
                        else
                            entry.EncryptedSize = entry.Size;
                    }
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var tent = entry as TacEntry;
            if (null == tent)
                return base.OpenEntry (arc, entry);
            if (tent.IsPacked)
            {
                var input = arc.File.CreateStream (entry.Offset, entry.Size);
                return new ZLibStream (input, CompressionMode.Decompress);
            }
            var bf = new Blowfish (tent.Key);
            if (tent.EncryptedSize < tent.Size)
            {
                var header = arc.File.View.ReadBytes (tent.Offset, tent.EncryptedSize);
                bf.Decipher (header, header.Length);
                var rest = arc.File.CreateStream (tent.Offset+tent.EncryptedSize, tent.Size-tent.EncryptedSize);
                return new PrefixStream (header, rest);
            }
            else if (0 == (tent.Size & 7))
            {
                var input = arc.File.CreateStream (tent.Offset, tent.Size);
                return new InputCryptoStream (input, bf.CreateDecryptor());
            }
            else
            {
                var data = arc.File.View.ReadBytes (tent.Offset, tent.Size);
                bf.Decipher (data, data.Length & ~7);
                return new BinMemoryStream (data);
            }
        }

        internal static ulong HashFromString (string s, uint seed)
        {
            s = s.Replace ('\\', '/').ToUpperInvariant();
            var bytes = Encodings.cp932.GetBytes (s);
            ulong hash = 0;
            for (int i = 0; i < bytes.Length; ++i)
            {
                hash = bytes[i] + 0x19919 * hash + seed;
            }
            return hash;
        }

        internal static ulong HashFromAsciiString (string s, uint seed)
        {
            ulong hash = 0;
            for (int i = 0; i < s.Length; ++i)
            {
                hash = (uint)char.ToUpperInvariant (s[i]) + 0x19919 * hash + seed;
            }
            return hash;
        }

        Dictionary<ulong, string> BuildFileNameMap (uint seed)
        {
            var map = new Dictionary<ulong, string> (KnownNames.Length);
            foreach (var name in KnownNames)
            {
                map[HashFromAsciiString (name, seed)] = name;
            }
            return map;
        }

        internal string[] KnownNames { get { return s_known_file_names.Value; } }

        static Lazy<string[]> s_known_file_names = new Lazy<string[]> (ReadTanukiLst);

        static string[] ReadTanukiLst ()
        {
            try
            {
                var names = new List<string>();
                FormatCatalog.Instance.ReadFileList (ListFileName, name => names.Add (name));
                return names.ToArray();
            }
            catch (Exception X)
            {
                System.Diagnostics.Trace.WriteLine (X.Message, "[TAC]");
                return new string[0];
            }
        }
    }

    internal class TacEntry : PackedEntry
    {
        public ulong    Hash;
        public byte[]   Key;
        public uint     EncryptedSize;
    }

    internal class TacBucket
    {
        public ushort   Hash;
        public int      Count;
        public int      Index;
    }
}
