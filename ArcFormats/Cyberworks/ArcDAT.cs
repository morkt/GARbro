//! \file       ArcDAT.cs
//! \date       Thu Jun 16 13:48:04 2016
//! \brief      Tinker Bell resource archive.
//
// Copyright (C) 2016-2017 by morkt
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
using System.Text;
using System.Text.RegularExpressions;
using GameRes.Compression;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.Cyberworks
{
    internal class BellArchive : ArcFile
    {
        public readonly AImageScheme    Scheme;

        public BellArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, AImageScheme scheme)
            : base (arc, impl, dir)
        {
            Scheme = scheme;
        }
    }

    internal abstract class ArchiveNameParser
    {
        readonly Regex m_regex;

        protected ArchiveNameParser (string pattern)
        {
            m_regex = new Regex (pattern, RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Returns toc filename and archive index corresponding to <paramref name="arc_name"/>.
        /// </summary>
        public Tuple<string, int> ParseName (string arc_name)
        {
            var match = m_regex.Match (arc_name);
            if (!match.Success)
                return null;
            int arc_idx;
            var toc_name = ParseMatch (match, out arc_idx);
            if (null == toc_name)
                return null;
            return Tuple.Create (toc_name, arc_idx);
        }

        protected abstract string ParseMatch (Match match, out int arc_idx);
    }

    internal class ArcNameParser : ArchiveNameParser
    {
        public ArcNameParser () : base (@"^.+0?(?<id>(?<num>\d)(?<idx>[a-z])?)(?:|\..*)$") { }

        protected override string ParseMatch (Match match, out int arc_idx)
        {
            arc_idx = 0;
            char num = match.Groups["num"].Value[0];
            int index_num;
            if (num >= '4' && num <= '6')
                index_num = num - '3';
            else if ('8' == num)
                index_num = 7;
            else
                return null;
            if (match.Groups["idx"].Success)
                arc_idx = char.ToUpper (match.Groups["idx"].Value[0]) - '@';

            var toc_name_builder = new StringBuilder (match.Value);
            var num_pos = match.Groups["id"].Index;
            toc_name_builder.Remove (num_pos, match.Groups["id"].Length);
            toc_name_builder.Insert (num_pos, index_num);
            return toc_name_builder.ToString();
        }
    }

    internal class DatNameParser : ArchiveNameParser
    {
        public DatNameParser () : base (@"^(?<name>d[a-z]+?)(?<idx>[ah])?\.dat$") { }

        protected override string ParseMatch (Match match, out int arc_idx)
        {
            var toc_name_builder = new StringBuilder (match.Groups["name"].Value);
            arc_idx = 0;
            if (match.Groups["idx"].Success)
            {
                if ('a' == match.Groups["idx"].Value[0])
                {
                    arc_idx = 1;
                    toc_name_builder.Append ('h');
                }
            }
            else
                toc_name_builder.Append ('h');
            toc_name_builder.Append (".dat");
            return toc_name_builder.ToString();
        }
    }

    internal class OldArcNameParser : ArchiveNameParser
    {
        public OldArcNameParser () : base (@"^Arc0(?<num>\d)\..*$") { }

        // matches archive body to its index
        static readonly IDictionary<char, int> s_arcmap = new Dictionary<char, int> {
            { '2', 0 }, { '3', 1 }, { '5', 4 } 
        };

        protected override string ParseMatch (Match match, out int arc_idx)
        {
            arc_idx = 0;
            char num = match.Groups["num"].Value[0];
            int index_num;
            if (!s_arcmap.TryGetValue (num, out index_num))
                return null;

            var toc_name_builder = new StringBuilder (match.Value);
            var num_pos = match.Groups["num"].Index;
            toc_name_builder.Remove (num_pos, match.Groups["num"].Length);
            toc_name_builder.Insert (num_pos, index_num);
            return toc_name_builder.ToString();
        }
    }

    internal class PatchNameParser : ArchiveNameParser
    {
        public PatchNameParser () : base (@"^patch0(?<num>[2468])\.dat$") { }

        protected override string ParseMatch (Match match, out int arc_idx)
        {
            arc_idx = 0;
            int index_num = match.Groups["num"].Value[0] - '0' - 1;
            var toc_name_builder = new StringBuilder (match.Value);
            var num_pos = match.Groups["num"].Index;
            toc_name_builder.Remove (num_pos, match.Groups["num"].Length);
            toc_name_builder.Insert (num_pos, index_num);
            return toc_name_builder.ToString();
        }
    }

    internal class InKyouParser : ArchiveNameParser
    {
        public InKyouParser () : base (@"^(inyoukyou_kuon|mugen.*)\.app$") { }

        protected override string ParseMatch (Match match, out int arc_idx)
        {
            arc_idx = 0;
            return match.Groups[1].Value + ".dat";
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/Cyberworks"; } }
        public override string Description { get { return "Cyberworks/TinkerBell resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat", "04", "05", "06", "app" };
        }

        public bool BlendOverlayImages = true;

        static readonly ArchiveNameParser[] s_name_parsers = {
            new ArcNameParser(),
            new DatNameParser(),
            new PatchNameParser(),
            new InKyouParser()
        };

        public override ArcFile TryOpen (ArcView file)
        {
            var arc_name = Path.GetFileName (file.Name);
            var dir_name = VFS.GetDirectoryName (file.Name);
            string game_name = arc_name != "Arc06.dat" ? TryParseMeta (VFS.CombinePath (dir_name, "Arc06.dat")) : null;
            Tuple<string, int> parsed = null;
            if (string.IsNullOrEmpty (game_name))
                parsed = s_name_parsers.Select (p => p.ParseName (arc_name)).FirstOrDefault (p => p != null);
            else // Shukujo no Tsuyagoto special case
                parsed = OldDatOpener.ArcNameParser.ParseName (arc_name);
            if (null == parsed)
                return null;
            string toc_name = parsed.Item1;
            int arc_idx = parsed.Item2;

            toc_name = VFS.CombinePath (dir_name, toc_name);
            var toc = ReadToc (toc_name, 8);
            if (null == toc)
                return null;
            using (var index = new ArcIndexReader (toc, file, arc_idx))
            {
                if (!index.Read())
                    return null;
                return ArchiveFromDir (file, index.Dir, index.HasImages);
            }
        }

        internal ArcFile ArchiveFromDir (ArcView file, List<Entry> dir, bool has_images)
        {
            if (0 == dir.Count)
                return null;
            if (!has_images)
                return new ArcFile (file, this, dir);
            var scheme = QueryScheme (file.Name);
            return new BellArchive (file, this, dir, scheme);
        }

        /// <summary>
        // Try to parse file containing game meta-information.
        /// </summary>
        internal string TryParseMeta (string meta_arc_name)
        {
            if (!VFS.FileExists (meta_arc_name))
                return null;
            using (var unpacker = new TocUnpacker (meta_arc_name))
            {
                if (unpacker.Length > 0x1000)
                    return null;
                var data = unpacker.Unpack (8);
                if (null == data)
                    return null;
                using (var content = new BinMemoryStream (data))
                {
                    int title_length = content.ReadInt32();
                    if (title_length <= 0 || title_length > content.Length)
                        return null;
                    var title = content.ReadBytes (title_length);
                    if (title.Length != title_length)
                        return null;
                    return Encodings.cp932.GetString (title);
                }
            }
        }

        internal byte[] ReadToc (string toc_name, int num_length)
        {
            if (!VFS.FileExists (toc_name))
                return null;
            using (var toc_unpacker = new TocUnpacker (toc_name))
                return toc_unpacker.Unpack (num_length);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pent = entry as PackedEntry;
            if (null != pent && pent.IsPacked)
            {
                input = new LzssStream (input);
            }
            return input;
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var barc = arc as BellArchive;
            if (null == barc || entry.Size < 5)
                return base.OpenImage (arc, entry);
            var input = arc.OpenBinaryEntry (entry);
            try
            {
                var reader = DecryptImage (input, barc.Scheme);
                if (BlendOverlayImages)
                {
                    var overlay = reader as AImageReader;
                    if (overlay != null)
                        overlay.ReadBaseline (barc, entry);
                }
                return reader;
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        protected virtual IImageDecoder DecryptImage (IBinaryStream input, AImageScheme scheme)
        {
            int type = input.ReadByte();
            if ('c' == type || 'b' == type)
            {
                uint img_size = Binary.BigEndian (input.ReadUInt32());
                if (input.Length - 5 == img_size)
                {
                    input = BinaryStream.FromStream (new StreamRegion (input.AsStream, 5, img_size), input.Name);
                }
            }
            else if (scheme != null && ('a' == type || 'd' == type) && input.Length > 21)
            {
                int id = input.ReadByte();
                if (id == scheme.Value2)
                {
                    return new AImageReader (input, scheme, type);
                }
            }
            input.Position = 0;
            return new ImageFormatDecoder (input);
        }

        internal AImageScheme QueryScheme (string arc_name)
        {
            var title = FormatCatalog.Instance.LookupGame (arc_name);
            if (!string.IsNullOrEmpty (title) && KnownSchemes.ContainsKey (title))
                return KnownSchemes[title];
            var options = Query<BellOptions> (arcStrings.ArcEncryptedNotice);
            return options.Scheme;
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new BellOptions { Scheme = GetScheme (Properties.Settings.Default.BELLTitle) };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetBELL();
        }

        public static AImageScheme GetScheme (string title)
        {
            AImageScheme scheme = null;
            if (string.IsNullOrEmpty (title) || !KnownSchemes.TryGetValue (title, out scheme))
                return null;
            return scheme;
        }

        static SchemeMap DefaultScheme = new SchemeMap {
            KnownSchemes = new Dictionary<string, AImageScheme>()
        };

        public static Dictionary<string, AImageScheme> KnownSchemes { get { return DefaultScheme.KnownSchemes; } }

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (SchemeMap)value; }
        }
    }

    [Serializable]
    public class AImageScheme
    {
        public byte     Value1;
        public byte     Value2;
        public byte     Value3;
        public byte[]   HeaderOrder;
        public bool     Flipped;

        public AImageScheme ()
        {
            Flipped = true;
        }
    }

    [Serializable]
    public class SchemeMap : ResourceScheme
    {
        public Dictionary<string, AImageScheme> KnownSchemes;
    }

    public class BellOptions : ResourceOptions
    {
        public AImageScheme Scheme;
    }

    [Export(typeof(ArchiveFormat))]
    public class OldDatOpener : DatOpener
    {
        public override string         Tag { get { return "ARC/Csystem"; } }
        public override string Description { get { return "TinkerBell resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public OldDatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        internal static readonly ArchiveNameParser ArcNameParser = new OldArcNameParser();

        public override ArcFile TryOpen (ArcView file)
        {
            var arc_name = Path.GetFileName (file.Name);
            var parsed = ArcNameParser.ParseName (arc_name);
            if (null == parsed)
                return null;
            var toc_name = VFS.CombinePath (VFS.GetDirectoryName (file.Name), parsed.Item1);
            var toc = ReadToc (toc_name, 4);
            if (null == toc)
                return null;

            bool has_images = false;
            var dir = new List<Entry>();
            using (var toc_stream = new MemoryStream (toc))
            using (var index = new StreamReader (toc_stream))
            {
                string line;
                while ((line = index.ReadLine()) != null)
                {
                    var fields = line.Split (',');
                    if (fields.Length != 5)
                        return null;
                    var name = Path.ChangeExtension (fields[0], fields[4]);
                    string type = "";
                    if ("b" == fields[4])
                    {
                        type = "image";
                        has_images = true;
                    }
                    else if ("k" == fields[4] || "j" == fields[4])
                        type = "audio";
                    var entry = new PackedEntry
                    {
                        Name = name,
                        Type = type,
                        Offset       = UInt32.Parse (fields[3]),
                        Size         = UInt32.Parse (fields[2]),
                        UnpackedSize = UInt32.Parse (fields[1]),
                    };
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    entry.IsPacked = entry.UnpackedSize != entry.Size;
                    dir.Add (entry);
                }
            }
            return ArchiveFromDir (file, dir, has_images);
        }

        protected override IImageDecoder DecryptImage (IBinaryStream input, AImageScheme scheme)
        {
            int id = input.ReadByte();
            if (id == scheme.Value2)
                return new AImageReader (input, scheme);
            input.Position = 0;
            return new ImageFormatDecoder (input);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class OldDatOpener2 : DatOpener
    {
        public override string         Tag { get { return "ARC/Csystem/2"; } }
        public override string Description { get { return "TinkerBell resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public OldDatOpener2 ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var arc_name = Path.GetFileName (file.Name);
            var parsed = OldDatOpener.ArcNameParser.ParseName (arc_name);
            if (null == parsed)
                return null;
            var toc_name = VFS.CombinePath (VFS.GetDirectoryName (file.Name), parsed.Item1);
            var toc = ReadToc (toc_name, 4);
            if (null == toc)
                return null;
            using (var index = new DatIndexReader (toc, file))
            {
                if (!index.Read())
                    return null;
                return ArchiveFromDir (file, index.Dir, index.HasImages);
            }
        }
    }

    internal sealed class TocUnpacker : IDisposable
    {
        ArcView   m_file;
        bool      m_should_dispose;

        public long       Length { get { return m_file.MaxOffset; } }
        public uint   PackedSize { get; private set; }
        public uint UnpackedSize { get; private set; }

        public TocUnpacker (string toc_name) : this (VFS.OpenView (toc_name), true)
        {
        }

        public TocUnpacker (ArcView file, bool should_dispose = false)
        {
            m_file = file;
            m_should_dispose = should_dispose;
        }

        public byte[] Unpack (int num_length)
        {
            return Unpack (0, num_length);
        }

        public byte[] Unpack (long offset, int num_length)
        {
            long data_offset = offset + num_length*2;
            if (m_file.MaxOffset <= data_offset)
                return null;
            UnpackedSize = DecodeDecimal (offset, num_length);
            if (UnpackedSize <= 4 || UnpackedSize > 0x1000000)
                return null;
            PackedSize = DecodeDecimal (offset+num_length, num_length);
            if (PackedSize > m_file.MaxOffset - data_offset || 0 == PackedSize)
                return null;
            return UnpackAt (data_offset);
        }

        byte[] UnpackAt (long offset)
        {
            using (var toc_s = m_file.CreateStream (offset, PackedSize))
            using (var lzss = new LzssStream (toc_s))
            {
                var toc = new byte[UnpackedSize];
                if (toc.Length != lzss.Read (toc, 0, toc.Length))
                    return null;
                return toc;
            }
        }

        internal uint DecodeDecimal (long offset, int num_length)
        {
            uint v = 0;
            uint rank = 1;
            for (int i = num_length-1; i >= 0; --i, rank *= 10)
            {
                uint b = m_file.View.ReadByte (offset+i);
                if (b != 0xFF)
                    v += (b ^ 0x7F) * rank;
            }
            return v;
        }

        bool _disposed = false;
        public void Dispose ()
        {
            if (m_should_dispose && !_disposed)
            {
                m_file.Dispose();
                _disposed = true;
            }
        }
    }

    internal abstract class IndexReader : IDisposable
    {
        protected IBinaryStream   m_index;
        readonly  long            m_max_offset;
        private   List<Entry>     m_dir;

        public List<Entry> Dir { get { return m_dir; } }
        public long  MaxOffset { get { return m_max_offset; } }
        public bool  HasImages { get; protected set; }

        public IndexReader (byte[] toc, ArcView file)
        {
            m_index = new BinMemoryStream (toc);
            m_max_offset = file.MaxOffset;
        }

        public bool Read ()
        {
            int entry_size = m_index.ReadInt32();
            if (entry_size < 0x11)
                return false;
            int count = (int)m_index.Length / (entry_size + 4);
            if (!ArchiveFormat.IsSaneCount (count))
                return false;
            long next_pos = 0;
            m_dir = new List<Entry> (count);
            while (next_pos < m_index.Length)
            {
                m_index.Position = next_pos;
                entry_size = m_index.ReadInt32();
                if (entry_size <= 0)
                    return false;
                next_pos += 4 + entry_size;
                var entry = ReadEntryInfo();
                if (ReadEntryType (entry, entry_size))
                {
                    if (entry.CheckPlacement (MaxOffset))
                        m_dir.Add (entry);
                }
            }
            return true;
        }

        internal PackedEntry ReadEntryInfo ()
        {
            uint id = m_index.ReadUInt32();
            var entry = new PackedEntry { Name = id.ToString ("D6") };
            entry.UnpackedSize = m_index.ReadUInt32();
            entry.Size = m_index.ReadUInt32();
            entry.IsPacked = entry.UnpackedSize != entry.Size;
            entry.Offset = m_index.ReadUInt32();
            return entry;
        }

        protected abstract bool ReadEntryType (Entry entry, int entry_size);

        public void Dispose ()
        {
            Dispose (true);
        }

        bool _disposed = false;
        protected virtual void Dispose (bool disposing)
        {
            if (disposing && !_disposed)
            {
                m_index.Dispose();
                _disposed = true;
            }
        }
    }

    internal class ArcIndexReader : IndexReader
    {
        int     m_arc_number;

        public ArcIndexReader (byte[] toc, ArcView file, int arc_number) : base (toc, file)
        {
            m_arc_number = arc_number;
        }

        char[]  m_type = new char[2];

        protected override bool ReadEntryType (Entry entry, int entry_size)
        {
            m_type[0] = (char)m_index.ReadByte();
            m_type[1] = (char)m_index.ReadByte();
            int entry_idx = 0;
            if (entry_size >= 0x17)
            {
                m_index.ReadInt32();
                entry_idx = m_index.ReadByte();
            }
            if (entry_idx != m_arc_number)
                return false;
            if (m_type[0] > 0x20 && m_type[0] < 0x7F)
            {
                string ext;
                if (m_type[1] > 0x20 && m_type[1] < 0x7F)
                    ext = new string (m_type);
                else
                    ext = new string (m_type[0], 1);
                if ("b0" == ext || "n0" == ext || "o0" == ext || "0b" == ext || "b" == ext)
                {
                    entry.Type = "image";
                    HasImages = true;
                }
                else if ("j0" == ext || "k0" == ext || "u0" == ext || "j" == ext || "k" == ext)
                    entry.Type = "audio";
                entry.Name = Path.ChangeExtension (entry.Name, ext);
            }
            return true;
        }
    }

    internal class DatIndexReader : IndexReader
    {
        public DatIndexReader (byte[] toc, ArcView file) : base (toc, file)
        {
        }

        protected override bool ReadEntryType (Entry entry, int entry_size)
        {
            if (entry_size > 0x11)
                throw new InvalidFormatException();
            char type = (char)m_index.ReadByte();
            if (type > 0x20 && type < 0x7F)
            {
                string ext = new string (type, 1);
                if ('b' == type)
                {
                    entry.Type = "image";
                    HasImages = true;
                }
                else if ('k' == type || 'j' == type)
                    entry.Type = "audio";
                entry.Name = Path.ChangeExtension (entry.Name, ext);
            }
            return true;
        }
    }
}
