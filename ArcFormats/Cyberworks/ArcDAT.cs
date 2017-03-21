//! \file       ArcDAT.cs
//! \date       Thu Jun 16 13:48:04 2016
//! \brief      Tinker Bell resource archive.
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
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GameRes.Compression;
using GameRes.Formats.Properties;
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
        public ArcNameParser () : base (@"^.+0(?<id>(?<num>\d)(?<idx>[a-z])?)(?:|\..*)$") { }

        protected override string ParseMatch (Match match, out int arc_idx)
        {
            arc_idx = 0;
            char num = match.Groups["num"].Value[0];
            if (num < '4' || num > '6')
                return null;
            if (match.Groups["idx"].Success)
                arc_idx = char.ToUpper (match.Groups["idx"].Value[0]) - '@';

            var toc_name_builder = new StringBuilder (match.Value);
            var num_pos = match.Groups["id"].Index;
            toc_name_builder.Remove (num_pos, match.Groups["id"].Length);
            toc_name_builder.Insert (num_pos, num-'3');
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

    internal class InKyouParser : ArchiveNameParser
    {
        public InKyouParser () : base (@"^inyoukyou_kuon\.app$") { }

        protected override string ParseMatch (Match match, out int arc_idx)
        {
            arc_idx = 0;
            return "inyoukyou_kuon.dat";
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

        static readonly ArchiveNameParser[] s_name_parsers = { new ArcNameParser(), new DatNameParser(), new InKyouParser() };

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

            int entry_size = LittleEndian.ToInt32 (toc, 0) + 4;
            if (entry_size < 0x16)
                return null;
            int count = toc.Length / entry_size;
            if (!IsSaneCount (count))
                return null;
            var type = new char[2];
            var dir = new List<Entry> (count);
            bool has_images = false;
            using (var index = new BinMemoryStream (toc, file.Name))
            {
                while (index.Position < index.Length)
                {
                    entry_size = index.ReadInt32();
                    if (entry_size <= 0)
                        return null;
                    var next_pos = index.Position + entry_size;
                    uint id = index.ReadUInt32();
                    var entry = new PackedEntry { Name = id.ToString ("D6") };
                    entry.UnpackedSize = index.ReadUInt32();
                    entry.Size = index.ReadUInt32();
                    entry.IsPacked = entry.UnpackedSize != entry.Size;
                    entry.Offset = index.ReadUInt32();
                    type[0] = (char)index.ReadByte();
                    type[1] = (char)index.ReadByte();
                    int entry_idx = 0;
                    if (entry_size >= 0x17)
                    {
                        index.ReadInt32();
                        entry_idx = index.ReadByte();
                    }
                    if (entry_idx == arc_idx)
                    {
                        if (type[0] > 0x20 && type[0] < 0x7F)
                        {
                            string ext;
                            if (type[1] > 0x20 && type[1] < 0x7F)
                                ext = new string (type);
                            else
                                ext = new string (type[0], 1);
                            if ("b0" == ext || "n0" == ext || "o0" == ext)
                            {
                                entry.Type = "image";
                                has_images = true;
                            }
                            else if ("j0" == ext || "k0" == ext || "u0" == ext)
                                entry.Type = "audio";
                            entry.Name = Path.ChangeExtension (entry.Name, ext);
                        }
                        dir.Add (entry);
                    }
                    index.Position = next_pos;
                }
            }
            return ArchiveFromDir (file, dir, has_images);
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
                return DecryptImage (input, barc.Scheme);
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
            else if (scheme != null && 'a' == type && input.Length > 21)
            {
                int id = input.ReadByte();
                if (id == scheme.Value2)
                {
                    return new AImageReader (input, scheme);
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
            return new BellOptions { Scheme = GetScheme (Settings.Default.BELLTitle) };
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

        public static Dictionary<string, AImageScheme> KnownSchemes = new Dictionary<string, AImageScheme>();

        public override ResourceScheme Scheme
        {
            get { return new SchemeMap { KnownSchemes = KnownSchemes }; }
            set { KnownSchemes = ((SchemeMap)value).KnownSchemes; }
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

            bool has_images = false;
            var dir = new List<Entry>();
            int entry_size = toc.ToInt32 (0);
            if (entry_size <= 0 || entry_size > 0x11)
                return null;
            using (var index = new BinMemoryStream (toc))
            {
                while (index.Position < index.Length)
                {
                    entry_size = index.ReadInt32();
                    if (entry_size <= 0)
                        return null;
                    var next_pos = index.Position + entry_size;
                    uint id = index.ReadUInt32();
                    var entry = new PackedEntry { Name = id.ToString ("D6") };
                    entry.UnpackedSize = index.ReadUInt32();
                    entry.Size = index.ReadUInt32();
                    entry.IsPacked = entry.UnpackedSize != entry.Size;
                    entry.Offset = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    char type = (char)index.ReadByte();
                    if (type > 0x20 && type < 0x7F)
                    {
                        string ext = new string (type, 1);
                        if ('b' == type)
                        {
                            entry.Type = "image";
                            has_images = true;
                        }
                        else if ('k' == type || 'j' == type)
                            entry.Type = "audio";
                        entry.Name = Path.ChangeExtension (entry.Name, ext);
                    }
                    dir.Add (entry);
                    index.Position = next_pos;
                }
            }
            return ArchiveFromDir (file, dir, has_images);
        }
    }

    internal sealed class TocUnpacker : IDisposable
    {
        ArcView     m_file;

        public long Length { get { return m_file.MaxOffset; } }

        public TocUnpacker (string toc_name)
        {
            m_file = VFS.OpenView (toc_name);
        }

        public byte[] Unpack (int num_length)
        {
            int data_offset = num_length*2;
            if (m_file.MaxOffset <= data_offset)
                return null;
            uint unpacked_size = DecodeDecimal (0, num_length);
            if (unpacked_size <= 4 || unpacked_size > 0x1000000)
                return null;
            uint packed_size = DecodeDecimal (num_length, num_length);
            if (packed_size > m_file.MaxOffset - data_offset)
                return null;
            return Unpack (data_offset, packed_size, unpacked_size);
        }

        byte[] Unpack (int offset, uint packed_size, uint unpacked_size)
        {
            using (var toc_s = m_file.CreateStream (offset, packed_size))
            using (var lzss = new LzssStream (toc_s))
            {
                var toc = new byte[unpacked_size];
                if (toc.Length != lzss.Read (toc, 0, toc.Length))
                    return null;
                return toc;
            }
        }

        uint DecodeDecimal (long offset, int num_length)
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
            if (!_disposed)
            {
                m_file.Dispose();
                _disposed = true;
            }
        }
    }
}
