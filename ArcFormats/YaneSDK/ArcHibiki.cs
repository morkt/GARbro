//! \file       ArcHibiki.cs
//! \date       Fri May 12 19:06:19 2017
//! \brief      Hibiki Works resource archive.
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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using GameRes.Utility;

namespace GameRes.Formats.YaneSDK
{
    [Export(typeof(ArchiveFormat))]
    public class HDatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/hibiki"; } }
        public override string Description { get { return "YaneSDK engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public static readonly string SchemeFileName = "hibiki_works.dat";

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".dat"))
                return null;
            int count = ReadCount (file);
            if (!IsSaneCount (count))
                return null;
            var scheme = QueryScheme (file.Name);
            if (null == scheme)
                return null;
            return TryOpenWithScheme (file, count, scheme);
        }

        int ReadCount (ArcView file)
        {
            return (short)(file.View.ReadUInt16 (0) ^ 0x8080);
        }

        ArcFile TryOpenWithScheme (ArcView file, int count, HibikiDatScheme scheme)
        {
            var dat_name = Path.GetFileName (file.Name).ToLowerInvariant();
            IList<HibikiTocRecord> toc_table = null;
            if (scheme.ArcMap != null && scheme.ArcMap.TryGetValue (dat_name, out toc_table))
            {
                if (toc_table.Count != count)
                    toc_table = null;
            }
            using (var input = OpenLstIndex (file, dat_name, scheme))
            using (var dec = new XoredStream (input, 0x80))
            using (var index = new BinaryReader (dec))
            {
                const int name_length = 0x100;
                int data_offset = 2 + (name_length + 10) * count;
                index.BaseStream.Position = 2;
                Func<int, Entry> read_entry;
                if (null == toc_table)
                {
                    var name_buf = new byte[name_length];
                    read_entry = i => {
                        if (name_length != index.Read (name_buf, 0, name_length))
                            return null;
                        var name = Binary.GetCString (name_buf, 0);
                        var entry = FormatCatalog.Instance.Create<Entry> (name);
                        index.ReadUInt16();
                        entry.Size = index.ReadUInt32();
                        entry.Offset = index.ReadUInt32();
                        return entry;
                    };
                }
                else
                {
                    read_entry = i => {
                        index.BaseStream.Seek (name_length + 6, SeekOrigin.Current);
                        index.ReadUInt32(); // throws in case of EOF
                        var toc_entry = toc_table[i];
                        var entry = FormatCatalog.Instance.Create<Entry> (toc_entry.Name);
                        entry.Offset = toc_entry.Offset;
                        entry.Size = toc_entry.Size;
                        return entry;
                    };
                }
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var entry = read_entry (i);
                    if (null == entry || string.IsNullOrWhiteSpace (entry.Name)
                        || entry.Offset < data_offset || entry.Size > file.MaxOffset)
                        return null;
                    dir.Add (entry);
                }
                return new HibikiArchive (file, this, dir, scheme.ContentKey);
            }
        }

        Stream OpenLstIndex (ArcView file, string dat_name, HibikiDatScheme scheme)
        {
            var lst_name = Path.ChangeExtension (file.Name, ".lst");
            if (VFS.FileExists (lst_name))
                return VFS.OpenStream (lst_name);
            else if ("init.dat" == dat_name)
                return file.CreateStream();
            // try to open 'init.dat' archive in the same directory
            var dir_name = VFS.GetDirectoryName (file.Name);
            var init_dat = VFS.CombinePath (dir_name, "init.dat");
            if (!VFS.FileExists (init_dat))
            {
                init_dat = VFS.CombinePath (VFS.CombinePath (dir_name, "arc"), "init.dat");
                if (!VFS.FileExists (init_dat))
                    return file.CreateStream();
            }
            try
            {
                using (var init = VFS.OpenView (init_dat))
                using (var init_arc = TryOpenWithScheme (init, ReadCount (init), scheme))
                {
                    lst_name = Path.GetFileName (lst_name);
                    var lst_entry = init_arc.Dir.First (e => e.Name == lst_name);
                    return init_arc.OpenEntry (lst_entry);
                }
            }
            catch
            {
                return file.CreateStream();
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var harc = arc as HibikiArchive;
            if (null == harc)
                return base.OpenEntry (arc, entry);
            var key = harc.Key;
            uint encrypted = Math.Min (entry.Size, (uint)key.Length);
            var header = arc.File.View.ReadBytes (entry.Offset, encrypted);
            for (int i = 0; i < header.Length; ++i)
                header[i] ^= key[i];
            if (encrypted == entry.Size)
                return new BinMemoryStream (header);
            var rest = arc.File.CreateStream (entry.Offset + encrypted, entry.Size - encrypted);
            return new PrefixStream (header, rest);
        }

        HibikiDatScheme QueryScheme (string arc_name)
        {
            if (null == KnownSchemes)
                return null;
            // XXX add GUI widget to select scheme
            return KnownSchemes.Values.FirstOrDefault();
        }

        static Lazy<HibikiScheme> s_Scheme = new Lazy<HibikiScheme> (DeserializeScheme);

        internal IDictionary<string, HibikiDatScheme> KnownSchemes {
            get { return s_Scheme.Value.KnownSchemes; }
        }

        static HibikiScheme DeserializeScheme ()
        {
            try
            {
                var dir = FormatCatalog.Instance.DataDirectory;
                var scheme_file = Path.Combine (dir, SchemeFileName);
                using (var input = File.OpenRead (scheme_file))
                {
                    var bin = new BinaryFormatter();
                    return (HibikiScheme)bin.Deserialize (input);
                }
            }
            catch (Exception X)
            {
                Trace.WriteLine (X.Message, "hibiki_works scheme deserialization failed");
                return new HibikiScheme();
            }
        }
    }

    internal class HibikiArchive : ArcFile
    {
        public readonly byte[] Key;

        public HibikiArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Serializable]
    public class HibikiScheme : ResourceScheme
    {
        public IDictionary<string, HibikiDatScheme> KnownSchemes;
    }

    [Serializable]
    public class HibikiDatScheme
    {
        public byte[]   ContentKey;
        public IDictionary<string, IList<HibikiTocRecord>>   ArcMap;
    }

    [Serializable]
    public class HibikiTocRecord
    {
        public string   Name;
        public uint     Offset;
        public uint     Size;

        public HibikiTocRecord (string name, uint offset, uint size)
        {
            Name = name;
            Offset = offset;
            Size = size;
        }
    }
}
