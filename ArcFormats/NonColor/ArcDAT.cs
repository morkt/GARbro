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
using System.Diagnostics;
using System.IO;
using GameRes.Compression;
using GameRes.Utility;
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
        public string   FileListName;
        public bool     LowCaseNames;

        public Scheme(string title)
        {
            Title = title;
            var key = Encodings.cp932.GetBytes(title);
            Hash = ComputeHash (key);
        }

        public virtual ulong ComputeHash (byte[] name)
        {
            return Crc64.Compute (name, 0, name.Length);
        }

        public byte[] GetBytes (string name)
        {
            if (LowCaseNames)
                return name.ToLowerShiftJis();
            else
                return Encodings.cp932.GetBytes (name);
        }
    }

    [Serializable]
    public class ArcDatScheme : ResourceScheme
    {
        public Dictionary<string, Scheme> KnownSchemes;
    }

    public class ArcDatOptions : ResourceOptions
    {
        public string Scheme;
    }

    internal struct NameRecord
    {
        public string   Name;
        public byte[]   NameBytes;
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

        internal const int SignatureKey = 0x26ACA46E;

        public static readonly string PersistentFileMapName = "NCFileMap.dat";

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".dat"))
                return null;
            int count = file.View.ReadInt32 (0) ^ SignatureKey;
            if (!IsSaneCount (count))
                return null;

            var scheme = QueryScheme (file.Name);
            if (null == scheme)
                return null;

            using (var index = new NcIndexReader (file, count))
            {
                var file_map = ReadFilenameMap (scheme);
                var dir = index.Read (file_map);
                if (null == dir)
                    return null;
                return new ArcDatArchive (file, this, dir, scheme.Hash);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var darc = arc as ArcDatArchive;
            var dent = entry as ArcDatEntry;
            if (null == darc || null == dent || 0 == dent.Size)
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            if (dent.IsPacked)
            {
                if (darc.MasterKey != 0)
                    DecryptData (data, (uint)(dent.Hash ^ darc.MasterKey));
                return new ZLibStream (new MemoryStream (data), CompressionMode.Decompress);
            }
            // 1 == dent.Flags
            if (dent.RawName != null && 0 != dent.Flags)
                DecryptWithName (data, dent.RawName);
            return new BinMemoryStream (data, entry.Name);
        }

        internal unsafe void DecryptData (byte[] data, uint key)
        {
            fixed (byte* data8 = data)
            {
                uint* data32 = (uint*)data8;
                for (int i = data.Length/4; i > 0; --i)
                    *data32++ ^= key;
            }
        }

        internal void DecryptWithName (byte[] data, byte[] name)
        {
            int block_length = data.Length / name.Length;
            int n = 0;
            for (int i = 0; i < name.Length-1; ++i)
            for (int j = 0; j < block_length; ++j)
                data[n++] ^= name[i];
        }

        static IDictionary<ulong, Tuple<uint, int>> FileMapIndex = null;

        internal IDictionary<ulong, Tuple<uint, int>> ReadFileMapIndex (BinaryReader idx)
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

        Tuple<ulong, Dictionary<ulong, NameRecord>> LastAccessedScheme;

        internal IDictionary<ulong, NameRecord> ReadFilenameMap (Scheme scheme)
        {
            if (null != LastAccessedScheme && LastAccessedScheme.Item1 == scheme.Hash)
                return LastAccessedScheme.Item2;
            if (!string.IsNullOrEmpty (scheme.FileListName))
            {
                var dict = new Dictionary<ulong, NameRecord>();
                FormatCatalog.Instance.ReadFileList (scheme.FileListName, line => {
                    var bytes = scheme.GetBytes (line);
                    ulong hash = scheme.ComputeHash (bytes);
                    dict[hash] = new NameRecord { Name = line, NameBytes = bytes };
                });
                LastAccessedScheme = Tuple.Create (scheme.Hash, dict);
                return dict;
            }
            var dir = FormatCatalog.Instance.DataDirectory;
            var lst_file = Path.Combine (dir, PersistentFileMapName);
            var idx_file = Path.ChangeExtension (lst_file, ".idx");
            using (var idx_stream = File.OpenRead (idx_file))
            using (var idx = new BinaryReader (idx_stream))
            {
                if (null == FileMapIndex)
                    FileMapIndex = ReadFileMapIndex (idx);

                Tuple<uint, int> nc_info;
                if (!FileMapIndex.TryGetValue (scheme.Hash, out nc_info))
                    throw new UnknownEncryptionScheme();

                using (var lst_stream = File.OpenRead (lst_file))
                using (var lst = new BinaryReader (lst_stream))
                {
                    var name_map = new Dictionary<ulong, NameRecord> (nc_info.Item2);
                    idx_stream.Position = nc_info.Item1;
                    for (int i = 0; i < nc_info.Item2; ++i)
                    {
                        ulong   key = idx.ReadUInt64();
                        uint offset = idx.ReadUInt32();
                        int  length = idx.ReadInt32();
                        lst_stream.Position = offset;
                        var name_bytes = lst.ReadBytes (length);
                        var name = Encodings.cp932.GetString (name_bytes);
                        name_map[key] = new NameRecord { Name = name, NameBytes = name_bytes };
                    }
                    LastAccessedScheme = Tuple.Create (scheme.Hash, name_map);
                    return name_map;
                }
            }
        }

        static ArcDatScheme DefaultScheme = new ArcDatScheme { KnownSchemes = new Dictionary<string, Scheme>() };

        public static Dictionary<string, Scheme> KnownSchemes { get { return DefaultScheme.KnownSchemes; } }

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (ArcDatScheme)value; }
        }

        internal Scheme QueryScheme (string arc_name)
        {
            var title = FormatCatalog.Instance.LookupGame (arc_name);
            if (!string.IsNullOrEmpty (title) && KnownSchemes.ContainsKey (title))
                return KnownSchemes[title];
            var options = Query<ArcDatOptions> (arcStrings.ArcEncryptedNotice);
            Scheme scheme;
            if (string.IsNullOrEmpty (options.Scheme) || !KnownSchemes.TryGetValue (options.Scheme, out scheme))
                return null;
            return scheme;
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new ArcDatOptions { Scheme = Properties.Settings.Default.NCARCScheme };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetNCARC();
        }
    }

    internal abstract class NcIndexReaderBase : IDisposable
    {
        protected IBinaryStream m_input;
        private   List<Entry>   m_dir;
        private   int           m_count;
        private   long          m_max_offset;

        public long IndexPosition { get; set; }
        public long     MaxOffset { get { return m_max_offset; } }
        public bool ExtendByteSign { get; protected set; }

        protected NcIndexReaderBase (ArcView file, int count)
        {
            m_input = file.CreateStream();
            m_dir = new List<Entry> (count);
            m_count = count;
            m_max_offset = file.MaxOffset;
            IndexPosition = 4;
        }

        public List<Entry> Read (IDictionary<ulong, NameRecord> file_map)
        {
            int skipped = 0, last_reported = -1;
            string last_name = null;
            m_input.Position = IndexPosition;
            for (int i = 0; i < m_count; ++i)
            {
                var entry = ReadEntry();
                NameRecord known_rec;
                if (file_map.TryGetValue (entry.Hash, out known_rec))
                {
                    entry.Name = known_rec.Name;
                    entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name);
                    entry.RawName = known_rec.NameBytes;
                }
                if (0 == (entry.Flags & 2))
                {
                    if (null == known_rec.Name)
                    {
                        if (last_name != null && last_reported != i-1)
                            Trace.WriteLine (string.Format ("[{0}] {1}", i-1, last_name), "[noncolor]");
                        Trace.WriteLine (string.Format ("[{0}] Unknown hash {1:X8}", i, entry.Hash), "[noncolor]");
                        last_name = null;
                        ++skipped;
                        continue;
                    }
                    else
                    {
                        var raw_name = known_rec.NameBytes;
                        if (null == last_name && i > 0)
                        {
                            Trace.WriteLine (string.Format ("[{0}] {1}", i, known_rec.Name), "[noncolor]");
                            last_reported = i;
                        }
                        entry.Offset        ^= Extend8Bit (raw_name[raw_name.Length >> 1]);
                        entry.Size          ^= Extend8Bit (raw_name[raw_name.Length >> 2]);
                        entry.UnpackedSize  ^= Extend8Bit (raw_name[raw_name.Length >> 3]);
                    }
                }
                last_name = known_rec.Name;
                if (!entry.CheckPlacement (MaxOffset))
                {
                    Trace.WriteLine (string.Format ("{0}: invalid placement [key:{1:X8}] [{2:X8}:{3:X8}]", entry.Name, entry.Hash, entry.Offset, entry.Size));
                    continue;
                }
                if (string.IsNullOrEmpty (entry.Name))
                    entry.Name = string.Format ("{0:D5}#{1:X8}", i, entry.Hash);
                m_dir.Add (entry);
            }
            if (skipped != 0)
                Trace.WriteLine (string.Format ("Missing {0} names", skipped), "[noncolor]");
            if (0 == m_dir.Count)
                return null;
            return m_dir;
        }

        uint Extend8Bit (byte v)
        {
            // 0xFF -> -1 -> 0xFFFFFFFF
            return ExtendByteSign ? (uint)(int)(sbyte)v : v;
        }

        protected abstract ArcDatEntry ReadEntry ();

        #region IDisposable Members
        bool m_disposed = false;
        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }

    internal class NcIndexReader : NcIndexReaderBase
    {
        readonly uint   m_master_key;

        public NcIndexReader (ArcView file, int count, uint master_key = 0) : base (file, count)
        {
            m_master_key = master_key;
        }

        protected override ArcDatEntry ReadEntry ()
        {
            var hash   = m_input.ReadUInt64();
            int flags  = m_input.ReadByte() ^ (byte)hash;
            return new ArcDatEntry {
                Hash   = hash,
                Flags  = flags,
                Offset = m_input.ReadUInt32() ^ (uint)hash ^ m_master_key,
                Size   = m_input.ReadUInt32() ^ (uint)hash,
                UnpackedSize = m_input.ReadUInt32() ^ (uint)hash,
                IsPacked = 0 != (flags & 2),
            };
        }
    }
}
