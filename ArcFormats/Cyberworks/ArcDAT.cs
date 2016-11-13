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
            Extensions = new string[] { "dat", "04", "05", "06" };
        }

        static Regex s_arcname_re = new Regex (@"^.+0(?<id>(?<num>\d)(?<idx>[a-z])?)(?:|\..*)$", RegexOptions.IgnoreCase);
        static Regex s_datname_re = new Regex (@"^(?<name>d[a-z]+?)(?<idx>[ah])?\.dat$", RegexOptions.IgnoreCase);

        public override ArcFile TryOpen (ArcView file)
        {
            var arc_name = Path.GetFileName (file.Name);
            int arc_idx = 0;
            StringBuilder toc_name_builder;
            var match = s_arcname_re.Match (arc_name);
            if (match.Success)
            {
                char num = match.Groups["num"].Value[0];
                if (num < '4' || num > '6')
                    return null;
                if (match.Groups["idx"].Success)
                    arc_idx = char.ToUpper (match.Groups["idx"].Value[0]) - '@';

                toc_name_builder = new StringBuilder (arc_name);
                var num_pos = match.Groups["id"].Index;
                toc_name_builder.Remove (num_pos, match.Groups["id"].Length);
                toc_name_builder.Insert (num_pos, num-'3');
            }
            else if ((match = s_datname_re.Match (arc_name)).Success)
            {
                toc_name_builder = new StringBuilder (match.Groups["name"].Value);
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
            }
            else
                return null;

            var toc_name = toc_name_builder.ToString();
            toc_name = VFS.CombinePath (VFS.GetDirectoryName (file.Name), toc_name);
            if (!VFS.FileExists (toc_name))
                return null;
            var toc = ReadToc (toc_name);
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
            if (0 == dir.Count)
                return null;
            if (!has_images)
                return new ArcFile (file, this, dir);
            var scheme = QueryScheme (file.Name);
            return new BellArchive (file, this, dir, scheme);
        }

        byte[] ReadToc (string toc_name)
        {
            using (var toc_view = VFS.OpenView (toc_name))
            {
                if (toc_view.MaxOffset <= 0x10)
                    return null;
                uint unpacked_size = DecodeDecimal (toc_view, 0);
                if (unpacked_size <= 4 || unpacked_size > 0x1000000)
                    return null;
                uint packed_size = DecodeDecimal (toc_view, 8);
                if (packed_size > toc_view.MaxOffset - 0x10)
                    return null;
                using (var toc_s = toc_view.CreateStream (0x10, packed_size))
                using (var lzss = new LzssStream (toc_s))
                {
                    var toc = new byte[unpacked_size];
                    if (toc.Length != lzss.Read (toc, 0, toc.Length))
                        return null;
                    return toc;
                }
            }
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

        uint DecodeDecimal (ArcView file, long offset)
        {
            uint v = 0;
            uint rank = 1;
            for (int i = 7; i >= 0; --i, rank *= 10)
            {
                uint b = file.View.ReadByte (offset+i);
                if (b != 0xFF)
                    v += (b ^ 0x7F) * rank;
            }
            return v;
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

        static readonly Regex s_arcname_re = new Regex (@"^Arc0(?<num>\d)\..*$", RegexOptions.IgnoreCase);

        static readonly IDictionary<int, int> s_arcmap = new Dictionary<int, int>
        {
            { 2, 0 }, { 3, 1 }, { 5, 4 } 
        };

        public override ArcFile TryOpen (ArcView file)
        {
            var arc_name = Path.GetFileName (file.Name);
            var match = s_arcname_re.Match (arc_name);
            if (!match.Success)
                return null;
            int num = match.Groups["num"].Value[0] - '0';
            int index_num;
            if (!s_arcmap.TryGetValue (num, out index_num))
                return null;

            var toc_name_builder = new StringBuilder (arc_name);
            var num_pos = match.Groups["num"].Index;
            toc_name_builder.Remove (num_pos, match.Groups["num"].Length);
            toc_name_builder.Insert (num_pos, index_num);

            var toc_name = toc_name_builder.ToString();
            toc_name = VFS.CombinePath (VFS.GetDirectoryName (file.Name), toc_name);
            var toc = ReadToc (toc_name);
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
            if (0 == dir.Count)
                return null;
            if (!has_images)
                return new ArcFile (file, this, dir);
            var scheme = QueryScheme (file.Name);
            return new BellArchive (file, this, dir, scheme);
        }

        protected override IImageDecoder DecryptImage (IBinaryStream input, AImageScheme scheme)
        {
            int id = input.ReadByte();
            if (id == scheme.Value2)
                return new AImageReader (input, scheme);
            input.Position = 0;
            return new ImageFormatDecoder (input);
        }

        byte[] ReadToc (string toc_name)
        {
            using (var toc_view = VFS.OpenView (toc_name))
            {
                if (toc_view.MaxOffset <= 8)
                    return null;
                int unpacked_size = DecodeDecimal (toc_view, 0);
                if (unpacked_size <= 4 || unpacked_size > 0x1000000)
                    return null;
                int packed_size = DecodeDecimal (toc_view, 4);
                if (packed_size > toc_view.MaxOffset - 8)
                    return null;
                using (var toc_s = toc_view.CreateStream (8, (uint)packed_size))
                using (var lzss = new LzssStream (toc_s))
                {
                    var toc = new byte[unpacked_size];
                    if (toc.Length != lzss.Read (toc, 0, toc.Length))
                        return null;
                    return toc;
                }
            }
        }

        int DecodeDecimal (ArcView file, long offset)
        {
            int v = 0;
            for (int rank = 1000; rank != 0; rank /= 10)
            {
                int b = file.View.ReadByte (offset++);
                if (b != 0xFF)
                    v += (b ^ 0x7F) * rank;
            }
            return v;
        }
    }
}
