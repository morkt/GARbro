//! \file       ArcDAT.cs
//! \date       Sat May 14 02:20:37 2016
//! \brief      'non color' resource archive.
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
using GameRes.Compression;
using GameRes.Utility;
using GameRes.Formats.Properties;
using GameRes.Formats.Strings;

namespace GameRes.Formats.NonColor
{
    internal class ArcDatEntry : PackedEntry
    {
        public byte[]   RawName;
        public ulong    Hash;
        public int      Flags;
    }

    internal class ArcDatArchive : ArcFile
    {
        public readonly ulong MasterKey;

        public ArcDatArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, ulong key)
            : base (arc, impl, dir)
        {
            MasterKey = key;
        }
    }

    [Serializable]
    public class Scheme
    {
        public string   Title;
        public ulong    Hash;

        public Scheme(string title)
        {
            Title = title;
            var key = Encodings.cp932.GetBytes(title);
            Hash = Crc64.Compute(key, 0, key.Length);
        }
    }

    [Serializable]
    public class ArcDatScheme : ResourceScheme
    {
        public Dictionary<string, Scheme> KnownSchemes;

        public static ulong GetKey (string title)
        {
            var key = Encodings.cp932.GetBytes (title);
            return Crc64.Compute (key, 0, key.Length);
        }
    }

    public class ArcDatOptions : ResourceOptions
    {
        public string Scheme;
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/noncolor"; } }
        public override string Description { get { return "'non color' resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public static readonly string PersistentFileMapName = "NCFileMap.dat";

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.EndsWith (".dat", StringComparison.InvariantCultureIgnoreCase))
                return null;
            int count = file.View.ReadInt32 (0) ^ 0x26ACA46E;
            if (!IsSaneCount (count))
                return null;

            var scheme = QueryScheme();
            if (null == scheme)
                return null;
            var file_map = ReadFilenameMap (scheme);

            uint index_offset = 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                ulong hash   = file.View.ReadUInt64 (index_offset);
                int   flags  = file.View.ReadByte (index_offset+8) ^ (byte)hash;
                uint  offset = file.View.ReadUInt32 (index_offset+9) ^ (uint)hash;
                uint  size   = file.View.ReadUInt32 (index_offset+0xD) ^ (uint)hash;
                uint  unpacked_size = file.View.ReadUInt32 (index_offset+0x11) ^ (uint)hash;
                index_offset += 0x15;

                string name;
                byte[] raw_name = null;
                if (file_map.TryGetValue (hash, out raw_name))
                {
                    name = Encodings.cp932.GetString (raw_name);
                }
                else if (2 == flags)
                {
                    name = hash.ToString ("X8");
                }
                else
                {
                    System.Diagnostics.Trace.WriteLine ("Unknown hash", hash.ToString ("X8"));
                    continue;
                }
                if (flags != 2)
                {
                    offset          ^= raw_name[raw_name.Length >> 1];
                    size            ^= raw_name[raw_name.Length >> 2];
                    unpacked_size   ^= raw_name[raw_name.Length >> 3];
                }
                var entry = new ArcDatEntry {
                    Name = name,
                    Type = FormatCatalog.Instance.GetTypeFromName (name),
                    RawName = raw_name,
                    Hash = hash,
                    Flags = flags,
                    Offset = offset,
                    Size = size,
                    UnpackedSize = unpacked_size,
                    IsPacked = 2 == flags,
                };

                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new ArcDatArchive (file, this, dir, scheme.Hash);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var darc = arc as ArcDatArchive;
            var dent = entry as ArcDatEntry;
            if (null == darc || null == dent || 0 == dent.Flags || 0 == dent.Size)
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            if (2 == dent.Flags)
            {
                if (darc.MasterKey != 0)
                {
                    unsafe
                    {
                        fixed (byte* data8 = data)
                        {
                            uint key = (uint)(dent.Hash ^ darc.MasterKey);
                            uint* data32 = (uint*)data8;
                            for (int i = data.Length/4; i > 0; --i)
                                *data32++ ^= key;
                        }
                    }
                }
                return new ZLibStream (new MemoryStream (data), CompressionMode.Decompress);
            }
            // 1 == dent.Flags
            int block_length = data.Length / dent.RawName.Length;
            int n = 0;
            for (int i = 0; i < dent.RawName.Length-1; ++i)
            for (int j = 0; j < block_length; ++j)
                data[n++] ^= dent.RawName[i];
            return new MemoryStream (data);
        }

        static IDictionary<ulong, Tuple<uint, int>> FileMapIndex = null;

        internal IDictionary<ulong, Tuple<uint, int>> ReadIndex (BinaryReader idx)
        {
            int scheme_count = idx.ReadInt32();
            idx.BaseStream.Seek (12, SeekOrigin.Current);
            var map = new Dictionary<ulong, Tuple<uint, int>> (scheme_count);
            for (int i = 0; i < scheme_count; ++i)
            {
                ulong   key = idx.ReadUInt64();
                uint offset = idx.ReadUInt32();
                int   count = idx.ReadInt32();
                map[key] = Tuple.Create (offset, count);
            }
            return map;
        }

        Tuple<ulong, Dictionary<ulong, byte[]>> LastAccessedScheme;

        internal IDictionary<ulong, byte[]> ReadFilenameMap (Scheme scheme)
        {
            if (null != LastAccessedScheme && LastAccessedScheme.Item1 == scheme.Hash)
                return LastAccessedScheme.Item2;
            var dir = FormatCatalog.Instance.DataDirectory;
            var lst_file = Path.Combine (dir, PersistentFileMapName);
            var idx_file = Path.ChangeExtension (lst_file, ".idx");
            using (var idx_stream = File.OpenRead (idx_file))
            using (var idx = new BinaryReader (idx_stream))
            {
                if (null == FileMapIndex)
                    FileMapIndex = ReadIndex (idx);

                Tuple<uint, int> nc_info;
                if (!FileMapIndex.TryGetValue (scheme.Hash, out nc_info))
                    throw new UnknownEncryptionScheme();

                using (var lst_stream = File.OpenRead (lst_file))
                using (var lst = new BinaryReader (lst_stream))
                {
                    var name_map = new Dictionary<ulong, byte[]> (nc_info.Item2);
                    idx_stream.Position = nc_info.Item1;
                    for (int i = 0; i < nc_info.Item2; ++i)
                    {
                        ulong   key = idx.ReadUInt64();
                        uint offset = idx.ReadUInt32();
                        int  length = idx.ReadInt32();
                        lst_stream.Position = offset;
                        name_map[key] = lst.ReadBytes (length);
                    }
                    LastAccessedScheme = Tuple.Create (scheme.Hash, name_map);
                    return name_map;
                }
            }
        }

        public static Dictionary<string, Scheme> KnownSchemes = new Dictionary<string, Scheme>();

        public override ResourceScheme Scheme
        {
            get { return new ArcDatScheme { KnownSchemes = KnownSchemes }; }
            set { KnownSchemes = ((ArcDatScheme)value).KnownSchemes; }
        }

        Scheme QueryScheme ()
        {
            var options = Query<ArcDatOptions> (arcStrings.ArcEncryptedNotice);
            Scheme scheme;
            if (string.IsNullOrEmpty (options.Scheme) || !KnownSchemes.TryGetValue (options.Scheme, out scheme))
                return null;
            return scheme;
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new ArcDatOptions { Scheme = Settings.Default.NCARCScheme };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetNCARC();
        }
    }
}
