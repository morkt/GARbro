//! \file       ArcLPK.cs
//! \date       Mon Feb 16 17:21:47 2015
//! \brief      Lucifen Easy Game System archive implementation.
//
// Copyright (C) 2015 by morkt
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
using GameRes.Compression;
using GameRes.Formats.Properties;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.Lucifen
{
    internal class LuciEntry : PackedEntry
    {
        public byte Flag;
    }

    internal class LuciArchive : ArcFile
    {
        public          LpkInfo Info;
        public EncryptionScheme Scheme;

        public LuciArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, EncryptionScheme scheme, LpkInfo info)
            : base (arc, impl, dir)
        {
            Info = info;
            Scheme = scheme;
        }
    }

    public class EncryptionScheme
    {
        public LpkOpener.Key BaseKey;
        public          byte ContentXor;
        public          uint RotatePattern;
        public          bool ImportGameInit;
    }

    internal class LuciOptions : ResourceOptions
    {
        public string Scheme;
        public LpkOpener.Key Key;
    }

    internal class LpkInfo
    {
        public bool AlignedOffset;
        public bool Flag1;
        public bool IsEncrypted;
        public bool PackedEntries;
        public bool WholeCrypt;
        public uint Key;
        public byte[] Prefix;
    }

    [Export(typeof(ArchiveFormat))]
    public class LpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "LPK"; } }
        public override string Description { get { return "Lucifen system resource archive"; } }
        public override uint     Signature { get { return 0x314b504c; } } // 'LPK1'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public class Key
        {
            public uint Key1, Key2;
            public Key (uint k1, uint k2)
            {
                Key1 = k1;
                Key2 = k2;
            }
        }

        static readonly EncryptionScheme DefaultScheme = new EncryptionScheme {
            BaseKey = new Key (0xA5B9AC6B, 0x9A639DE5), ContentXor = 0x5d, RotatePattern = 0x31746285,
            ImportGameInit = true
        };
        public static readonly Dictionary<string, EncryptionScheme> KnownSchemes =
            new Dictionary<string, EncryptionScheme> {
                { "Default", DefaultScheme },
                { "Terios games", new EncryptionScheme {
                    BaseKey = new Key (0x39A5B67D, 0xD63AB5E9), ContentXor = 0xa6, RotatePattern = 0x36147352,
                    ImportGameInit = false } },
            };

        EncryptionScheme CurrentScheme = KnownSchemes["Default"];
        Dictionary<string, Key> CurrentFileMap = new Dictionary<string, Key>();

        static Dictionary<string, Dictionary<string, Key>> KnownKeys =
            new Dictionary<string, Dictionary<string, Key>> {
                { "Ne~pon? x Raipon!", new Dictionary<string, Key> {
                    { "SYS.LPK",    new Key (0x67DB35ED, 0xBE4D6A37) },
                    { "CHR.LPK",    new Key (0x9E3BAD56, 0x95FE9DC3) },
                    { "PIC.LPK",    new Key (0xD9B5E6C3, 0xBA53E5D5) },
                    { "BGM.LPK",    new Key (0xEB6DE3B5, 0xCD571DE9) },
                    { "SE.LPK",     new Key (0x9AB3AD5E, 0xAD3D4D96) },
                    { "VOICE.LPK",  new Key (0x75CBD59D, 0x5ED59ACA) },
                    { "DATA.LPK",   new Key (0xDE6AD53E, 0x9E3B5E3D) },
                    { "DESKTOP.LPK",new Key (0xACD5B36D, 0xD56ADB5D) } } },
                { "Doki Doki Rooming", new Dictionary<string, Key> {
                    { "SYS.LPK",    new Key (0x8030AB96, 0xB9F6638E) },
                    { "CHR.LPK",    new Key (0xA7B9178B, 0xF79C547C) },
                    { "PIC.LPK",    new Key (0x34B73229, 0xB690E07D) },
                    { "BGM.LPK",    new Key (0x41E7505F, 0x1E10C3C5) },
                    { "SE.LPK",     new Key (0x34DF875E, 0x435603E8) },
                    { "VOICE.LPK",  new Key (0x30701D73, 0xFB524CE5) },
                    { "ANIME.LPK",  new Key (0x867ABB47, 0xA25F4248) },
                    { "DATA.LPK",   new Key (0x5BCB1333, 0x99325DFA) } } },
                { "Shinsetsu Ryouki no Ori Dai 2 Shou", new Dictionary<string, Key> {
                    { "SYS.LPK",    new Key (0x3986AFC9, 0xFC8C32E7) },
                    { "CHR.LPK",    new Key (0x1B478946, 0x489E83F9) },
                    { "PIC.LPK",    new Key (0x3F9D8075, 0xA75CBDA3) },
                    { "BGM.LPK",    new Key (0xEAB5949C, 0x48ABE813) },
                    { "SE.LPK",     new Key (0x5A95B36B, 0x249F4BEB) },
                    { "VOICE.LPK",  new Key (0x6B954634, 0xA1CF23CE) },
                    { "DATA.LPK",   new Key (0x8395C7CB, 0xE4CF9E3B) } } },
                { "Ore-tachi ni Tsubasa wa Nai", new Dictionary<string, Key> {
                    { "SYS.LPK",    new Key (0x524657EA, 0x782B5A5C) },
                    { "CHR.LPK",    new Key (0x8C982BCA, 0x46BC4CDD) },
                    { "PIC.LPK",    new Key (0xB78C913C, 0x57ECB345) },
                    { "BGM.LPK",    new Key (0x45E32E7D, 0x4F538CE3) },
                    { "SE.LPK",     new Key (0x9A5AF6DE, 0xCB1866C7) },
                    { "VOICE.LPK",  new Key (0x3EB61D34, 0xB451AB16) },
                    { "DATA.LPK",   new Key (0x7CE5AC6A, 0x49A3A35B) } } },
                { "Shuffle! Essence+", new Dictionary<string, Key> {
                    { "SYS.LPK",    new Key (0xA3635FC4, 0xA14A4686) },
                    { "CHR.LPK",    new Key (0x4E34463C, 0xB4D8BD6D) },
                    { "PIC.LPK",    new Key (0xF592B54B, 0xACCD3A1C) },
                    { "BGM.LPK",    new Key (0xC50D2FD6, 0xC89DE7A9) },
                    { "SE.LPK",     new Key (0xE9CD4E1B, 0x98AD9D63) },
                    { "VOICE.LPK",  new Key (0x865A8D4E, 0x742DA395) },
                    { "DATA.LPK",   new Key (0xCEA56246, 0xDE46F9A6) } } },
            };

        public override ArcFile TryOpen (ArcView file)
        {
            string name = Path.GetFileName (file.Name).ToUpperInvariant();
            if (string.IsNullOrEmpty (name))
                return null;
            Key file_key = null;
            var basename = Encodings.cp932.GetBytes (Path.GetFileNameWithoutExtension (name));
            if (name != "SCRIPT.LPK")
                CurrentFileMap.TryGetValue (name, out file_key);
            try
            {
                var arc = Open (basename, file, CurrentScheme, file_key);
                if (null != arc)
                    return arc;
            }
            catch { /* unknown encryption, ignore parse errors */ }
            var new_scheme = QueryEncryptionScheme();
            if (new_scheme == CurrentScheme && !CurrentScheme.ImportGameInit)
                return null;
            CurrentScheme = new_scheme;
            if (name != "SCRIPT.LPK" && CurrentScheme.ImportGameInit)
            {
                if (0 == CurrentFileMap.Count)
                    ImportKeys (file.Name);
                if (!CurrentFileMap.TryGetValue (name, out file_key))
                    return null;
            }
            return Open (basename, file, CurrentScheme, file_key);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (0 == entry.Size)
                return Stream.Null;
            var larc = arc as LuciArchive;
            var lent = entry as LuciEntry;
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (null == larc || null == lent)
                return input;
            byte[] data;
            using (input)
            {
                if (lent.IsPacked)
                {
                    using (var reader = new LzssReader (input, (int)lent.Size, (int)lent.UnpackedSize))
                    {
                        reader.Unpack();
                        data = reader.Data;
                    }
                }
                else
                {
                    data = new byte[lent.Size];
                    input.Read (data, 0, data.Length);
                }
            }
            if (larc.Info.WholeCrypt)
            {
                DecryptContent (data, larc.Scheme.ContentXor);
            }
            if (larc.Info.IsEncrypted)
            {
                int count = Math.Min (data.Length, 0x100);
                if (count != 0)
                    DecryptEntry (data, count, larc.Info.Key, larc.Scheme.RotatePattern);
            }
            input = new MemoryStream (data);
            if (null != larc.Info.Prefix)
                return new PrefixStream (larc.Info.Prefix, input);
            else
                return input;
        }

        static void DecryptContent (byte[] data, byte key)
        {
            for (int i = 0; i < data.Length; ++i)
            {
                int v = data[i] ^ key;
                data[i] = (byte)(v >> 4 | v << 4);
            }
        }

        static unsafe void DecryptEntry (byte[] data, int length, uint key, uint pattern)
        {
            fixed (byte* buf_raw = data)
            {
                uint* encoded = (uint*)buf_raw;
                length /= 4;
                for (int i = 0; i < length; ++i)
                {
                    encoded[i] ^= key;
                    pattern = (pattern >> 4) | (pattern << 28);
                    int cl = (int)(pattern & 0x1f);
                    key = (key << cl) | (key >> (32-cl));
                }
            }
        }

        static unsafe void DecryptIndex (byte[] data, int length, uint key, uint pattern)
        {
            fixed (byte* buf_raw = data)
            {
                uint* encoded = (uint*)buf_raw;
                length /= 4;
                for (int i = 0; i < length; ++i)
                {
                    encoded[i] ^= key;
                    pattern = (pattern << 4) | (pattern >> 28);
                    int cl = (int)(pattern & 0x1f);
                    key = (key >> cl) | (key << (32-cl));
                }
            }
        }

        ArcFile Open (byte[] basename, ArcView file, EncryptionScheme scheme, Key key)
        {
            uint key1 = scheme.BaseKey.Key1;
            uint key2 = scheme.BaseKey.Key2;
            for (int b = 0, e = basename.Length-1; e >= 0; ++b, --e)
            {
                key1 ^= basename[e];
                key2 ^= basename[b];
                key1 = (key1 << 25) | (key1 >>  7);
                key2 = (key2 <<  7) | (key2 >> 25);
            }
            if (null != key)
            {
                key1 ^= key.Key1;
                key2 ^= key.Key2;
            }
            uint code = file.View.ReadUInt32 (4) ^ key2;
            int index_size = (int)(code & 0xffffff);
            byte flags = (byte)(code >> 24);
            if (0 != (flags & 1))
                index_size = (index_size << 11) - 8;
            if (index_size < 5 || index_size >= file.MaxOffset)
                return null;
            var index = new byte[index_size];
            if (index_size != file.View.Read (8, index, 0, (uint)index_size))
                return null;
            DecryptIndex (index, index_size, key2, scheme.RotatePattern);
            var lpk_info = new LpkInfo
            {
                AlignedOffset = 0 != (flags & 1),
                Flag1         = 0 != (flags & 2),
                IsEncrypted   = 0 != (flags & 4),
                PackedEntries = 0 != (flags & 8),
                WholeCrypt    = 0 != (flags & 0x10),
                Key           = key1
            };
            var reader = new IndexReader (lpk_info);
            var dir = reader.Read (index);
            if (null == dir)
                return null;
            return new LuciArchive (file, this, dir, scheme, reader.Info);
        }

        static readonly byte[] ScriptName = Encoding.ASCII.GetBytes ("SCRIPT");

        void ImportKeys (string source_name)
        {
            var script_lpk = Path.Combine (Path.GetDirectoryName (source_name), "SCRIPT.LPK");
            using (var script_file = new ArcView (script_lpk))
            using (var script_arc  = Open (ScriptName, script_file, CurrentScheme, null))
            {
                if (null == script_arc)
                    throw new UnknownEncryptionScheme();
                var entry = script_arc.Dir.Where (e => e.Name.Equals ("gameinit.sob", StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
                if (null == entry)
                    throw new FileNotFoundException ("Missing 'gameinit.sob' entry in SCRIPT.LPK");
                using (var gameinit = script_arc.OpenEntry (entry))
                {
                    var init_data = new byte[gameinit.Length];
                    gameinit.Read (init_data, 0, init_data.Length);
                    if (!ParseGameInit (init_data))
                        throw new UnknownEncryptionScheme();
                }
            }
        }

        bool ParseGameInit (byte[] sob)
        {
            CurrentFileMap.Clear();
            if (!Binary.AsciiEqual (sob, "SOB0"))
                return false;
            int offset = LittleEndian.ToInt32 (sob, 4) + 8;
            if (offset <= 0 || offset >= sob.Length)
                return false;
            unsafe
            {
                fixed (byte* buf_raw = sob)
                {
                    uint* p = (uint*)(buf_raw + offset);
                    uint index_len = *p;
                    if (offset+8+(int)index_len >= sob.Length)
                        return false;
                    p += 2;
                    uint* last = p + index_len / 4 - 28;

                    while (p < last)
                    {
                        if (p[0] == 0x28  && p[1] == 0  && p[2] != 0   && p[3] == 8  &&
                            p[4] == 1     && p[5] == 8  && p[6] == 1   && p[7] == 8  &&
                            p[8] != 0     && p[9] == 8  && p[10] != 0  && p[11] == 5 &&
                            p[17] == 0x28 && p[18] == 0 && p[19] != 0  && p[20] == 8 &&
                            p[21] != 0    && p[22] == 8 && p[23] == 0xffffffff && p[24] == 8 &&
                            p[25] == 1    && p[26] == 8 && p[27] == 1  && p[28] == 5)
                        {
                            byte* lpk = (byte*)p + p[21] - p[2] + 0x34;
                            int name_index = (int)(lpk - buf_raw);
                            if (name_index < 0 || name_index >= sob.Length)
                            {
                                ++p;
                                continue;
                            }
                            string name = Binary.GetCString (sob, name_index, sob.Length-name_index);
                            name = name.ToUpperInvariant();
                            CurrentFileMap[name] = new Key (p[8], p[10]);
                            p += 0x22;
                        } else
                            ++p;
                    }
                    return true;
                }
            }
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new LuciOptions { Scheme = Settings.Default.LPKScheme };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetLPK();
        }

        EncryptionScheme QueryEncryptionScheme ()
        {
            CurrentFileMap.Clear();
            var options = Query<LuciOptions> (arcStrings.ArcEncryptedNotice);
            if (null == options)
                return KnownSchemes["Default"];
            string title = options.Scheme;
            if (null == options.Key)
            {
                Dictionary<string, Key> file_map = null;
                if (KnownKeys.TryGetValue (title, out file_map))
                    CurrentFileMap = new Dictionary<string, Key> (file_map);
            }
            return KnownSchemes[title];
        }
    }

    internal class IndexReader
    {
        byte[]          m_index;
        LpkInfo         m_info;
        List<Entry>     m_dir;
        byte[]          m_name;
        int             m_index_width;
        int             m_entries_offset;

        public LpkInfo  Info { get { return m_info; } }
        public int EntrySize { get; private set; }

        public IndexReader (LpkInfo info)
        {
            if (!info.Flag1)
                throw new NotSupportedException ("Not supported LPK index format");
            m_info = info;
            EntrySize = m_info.PackedEntries ? 13 : 9;
        }

        public List<Entry> Read (byte[] index)
        {
            m_index = index;
            int count = LittleEndian.ToInt32 (m_index, 0);
            if (count <= 0 || count > 0xfffff)
                return null;
            int index_offset = 4;
            int prefix_length = m_index[index_offset++];
            if (0 != prefix_length)
            {
                m_info.Prefix = new byte[prefix_length];
                Buffer.BlockCopy (m_index, index_offset, m_info.Prefix, 0, prefix_length);
                index_offset += prefix_length;
            }
            m_index_width = 0 != m_index[index_offset++] ? 4 : 2;
            int letter_table_length = LittleEndian.ToInt32 (m_index, index_offset);
            index_offset += 4;
            m_entries_offset = index_offset + letter_table_length;
            if (m_entries_offset >= m_index.Length)
                return null;
            if ((m_index.Length - m_entries_offset) / EntrySize < count)
                EntrySize = (m_index.Length - m_entries_offset) / count;
            if (EntrySize < 8 || m_info.PackedEntries && EntrySize < 12)
                return null;

            m_dir = new List<Entry> (count);
            m_name = new byte[260];
            TraverseIndex (index_offset, 0);
            return m_dir.Count == count ? m_dir : null;
        }

        void TraverseIndex (int index_offset, int name_length)
        {
            if (index_offset < 0 || index_offset >= m_index.Length)
                throw new InvalidFormatException ("Error parsing LPK index");
            if (name_length >= m_name.Length)
                throw new InvalidFormatException ("Entry filename is too long");
            int entries = m_index[index_offset++];
            for (int i = 0; i < entries; ++i)
            {
                byte next_letter = m_index[index_offset++];
                int next_offset  = 4 == m_index_width ? LittleEndian.ToInt32 (m_index, index_offset)
                                                      : (int)LittleEndian.ToUInt16 (m_index, index_offset);
                m_name[name_length] = next_letter;
                index_offset += m_index_width;;
                if (0 != next_letter)
                    TraverseIndex (index_offset+next_offset, name_length+1);
                else
                    AddEntry (name_length, next_offset);
            }
        }

        void AddEntry (int name_length, int entry_num)
        {
            if (name_length < 1)
                throw new InvalidFormatException ("Invalid LPK entry name");
            string name = Encodings.cp932.GetString (m_name, 0, name_length);
            var entry = new LuciEntry {
                Name = name,
                Type = FormatCatalog.Instance.GetTypeFromName (name),
                IsPacked = m_info.PackedEntries,
            };
            int entry_pos = m_entries_offset + EntrySize * entry_num;
            if (entry_pos+EntrySize > m_index.Length)
                throw new InvalidFormatException ("Invalid LPK entry index");
            if (0 != (EntrySize & 1))
                entry.Flag = m_index[entry_pos++];
            long offset = LittleEndian.ToUInt32 (m_index, entry_pos);
            entry.Offset = m_info.AlignedOffset ? offset << 11 : offset;
            entry.Size = LittleEndian.ToUInt32 (m_index, entry_pos+4);
            if (entry.IsPacked)
                entry.UnpackedSize = LittleEndian.ToUInt32 (m_index, entry_pos+8);
            else
                entry.UnpackedSize = entry.Size;
            m_dir.Add (entry);
        }
    }
}
