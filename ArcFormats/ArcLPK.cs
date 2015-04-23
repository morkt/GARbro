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
        public LpkInfo Info;

        public LuciArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, LpkInfo info)
            : base (arc, impl, dir)
        {
            Info = info;
        }
    }

    internal class LuciOptions : ResourceOptions
    {
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

        static Key       BaseKey = new Key (0xA5B9AC6B, 0x9A639DE5);
        static byte[] ScriptName = Encoding.ASCII.GetBytes ("SCRIPT");
        static Key     ScriptKey = new Key (0, 0);

        Dictionary<string, Key> CurrentScheme = new Dictionary<string, Key>();

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
            };

        public override ArcFile TryOpen (ArcView file)
        {
            string name = Path.GetFileName (file.Name).ToUpperInvariant();
            if (string.IsNullOrEmpty (name))
                return null;
            var key = ScriptKey;
            if (name != "SCRIPT.LPK" && !CurrentScheme.TryGetValue (name, out key))
            {
                try
                {
                    ImportKeys (file.Name);
                    CurrentScheme.TryGetValue (name, out key);
                }
                catch
                {
                    key = null;
                }
                if (null == key)
                    key = QueryEncryptionKey (name);
            }
            var basename = Encodings.cp932.GetBytes (Path.GetFileNameWithoutExtension (name));
            return Open (basename, file, key);
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
                for (int i = 0; i < data.Length; ++i)
                {
                    int v = data[i] ^ 0x5d;
                    data[i] = (byte)(v >> 4 | v << 4);
                }
            }
            if (larc.Info.IsEncrypted)
            {
                int count = Math.Min (data.Length / 4, 0x40);
                if (count != 0)
                {
                    unsafe
                    {
                        fixed (byte* buf_raw = data)
                        {
                            uint key = larc.Info.Key;
                            uint* encoded = (uint*)buf_raw;
                            uint ecx = 0x31746285;
                            for (int i = 0; i < count; ++i)
                            {
                                encoded[i] ^= key;
                                ecx = (ecx >> 4) | (ecx << 28);
                                int cl = (int)(ecx & 0x1f);
                                key = (key << cl) | (key >> (32-cl));
                            }
                        }
                    }
                }
            }
            input = new MemoryStream (data);
            if (null != larc.Info.Prefix)
                return new PrefixStream (larc.Info.Prefix, input);
            else
                return input;
        }

        ArcFile Open (byte[] basename, ArcView file, Key key)
        {
            uint key1 = BaseKey.Key1;
            uint key2 = BaseKey.Key2;
            for (int b = 0, e = basename.Length-1; e >= 0; ++b, --e)
            {
                key1 ^= basename[e];
                key2 ^= basename[b];
                key1 = (key1 << 25) | (key1 >>  7);
                key2 = (key2 <<  7) | (key2 >> 25);
            }
            key1 ^= key.Key1;
            key2 ^= key.Key2;
            uint code = file.View.ReadUInt32 (4) ^ key2;
            int index_size = (int)(code & 0xffffff);
            byte flags = (byte)(code >> 24);
            if (0 != (flags & 1))
                index_size = (index_size << 11) - 8;
            if (index_size < 5)
                return null;
            var index = new byte[index_size];
            if (index_size != file.View.Read (8, index, 0, (uint)index_size))
                return null;
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
            unsafe
            {
                fixed (byte* buf_raw = index)
                {
                    uint* encoded = (uint*)buf_raw;
                    uint ecx = 0x31746285;
                    for (int i = 0; i < index_size/4; ++i)
                    {
                        encoded[i] ^= key2;
                        ecx = (ecx << 4) | (ecx >> 28);
                        int cl = (int)(ecx & 0x1f);
                        key2 = (key2 << (32-cl)) | (key2 >> cl);
                    }
                }
            }
            var dir = reader.Read (index);
            if (null == dir)
                return null;
            return new LuciArchive (file, this, dir, reader.Info);
        }

        void ImportKeys (string source_name)
        {
            var script_lpk = Path.Combine (Path.GetDirectoryName (source_name), "SCRIPT.LPK");
            using (var script_file = new ArcView (script_lpk))
            using (var script_arc  = Open (ScriptName, script_file, ScriptKey))
            {
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
            CurrentScheme.Clear();
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
                            if (name_index >= sob.Length)
                            {
                                ++p;
                                continue;
                            }
                            string name = Binary.GetCString (sob, name_index, sob.Length-name_index);
                            name = name.ToUpperInvariant();
                            CurrentScheme[name] = new Key (p[8], p[10]);
                            p += 0x22;
                        } else
                            ++p;
                    }
                    return true;
                }
            }
        }

        Key QueryEncryptionKey (string lpk_name)
        {
            var options = Query<LuciOptions> (arcStrings.ArcEncryptedNotice);
            if (null == options)
                throw new UnknownEncryptionScheme();
            return options.Key;
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

        public LpkInfo Info { get { return m_info; } }

        public IndexReader (LpkInfo info)
        {
            if (!info.Flag1)
                throw new NotSupportedException ("Not supported LPK index format");
            m_info = info;
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
            int entry_size = m_info.PackedEntries ? 13 : 9;
            int entry_pos = m_entries_offset + entry_size*entry_num;
            if (entry_pos+entry_size > m_index.Length)
                throw new InvalidFormatException ("Invalid LPK entry index");
            entry.Flag = m_index[entry_pos];
            long offset = LittleEndian.ToUInt32 (m_index, entry_pos+1);
            entry.Offset = m_info.AlignedOffset ? offset << 11 : offset;
            entry.Size = LittleEndian.ToUInt32 (m_index, entry_pos+5);
            if (entry.IsPacked)
                entry.UnpackedSize = LittleEndian.ToUInt32 (m_index, entry_pos+9);
            else
                entry.UnpackedSize = entry.Size;
            m_dir.Add (entry);
        }
    }
}
