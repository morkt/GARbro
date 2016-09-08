//! \file       ArcTactics.cs
//! \date       Thu Jul 23 16:27:55 2015
//! \brief      Tactics archive file implementation.
//
// Copyright (C) 2015-2016 by morkt
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
using System.Security.Cryptography;
using GameRes.Compression;
using GameRes.Formats.Properties;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.Tactics
{
    internal class TacticsArcFile : ArcFile
    {
        public readonly byte[]  Password;
        public readonly bool    CustomLzss;

        public TacticsArcFile (ArcView file, ArchiveFormat format, ICollection<Entry> dir, byte[] pass)
            : base (file, format, dir)
        {
            Password = pass;
            CustomLzss = false;
        }

        public TacticsArcFile (ArcView file, ArchiveFormat format, ICollection<Entry> dir, ArcScheme scheme)
            : base (file, format, dir)
        {
            Password = Encodings.cp932.GetBytes (scheme.Password);
            CustomLzss = scheme.CustomLzss;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/Tactics"; } }
        public override string Description { get { return "Tactics archive file"; } }
        public override uint     Signature { get { return 0x54434154; } } // 'TACT'
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "arc", "adf" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "ICS_ARC_FILE"))
                return null;

            var reader = new IndexReader (file);
            var dir = reader.ReadIndex();
            if (null == dir)
                return null;
            return new TacticsArcFile (file, this, dir, reader.Password);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var tarc = arc as TacticsArcFile;
            var tent = (PackedEntry)entry;
            if (null == tarc || null == tarc.Password && !tent.IsPacked)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            if (null == tarc.Password)
                return new LzssStream (arc.File.CreateStream (entry.Offset, entry.Size));

            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            int p = 0;
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] ^= tarc.Password[p++];
                if (p == tarc.Password.Length)
                    p = 0;
            }
            if (tarc.CustomLzss && tent.IsPacked)
            {
                data = UnpackCustomLzss (data);
                return new MemoryStream (data);
            }
            Stream input = new MemoryStream (data);
            if (tent.IsPacked)
                input = new LzssStream (input);
            return input;
        }

        internal class IndexReader
        {
            ArcView         m_file;
            uint            m_packed_size;
            uint            m_unpacked_size;
            int             m_count;
            byte[]          m_index;
            Lazy<List<Entry>> m_dir;

            public byte[] Password { get; private set; }

            public IndexReader (ArcView file)
            {
                m_file = file;
                m_packed_size = m_file.View.ReadUInt32 (0x10);
                m_unpacked_size = m_file.View.ReadUInt32 (0x14);
                m_count = m_file.View.ReadInt32 (0x18);
                m_dir = new Lazy<List<Entry>> (() => new List<Entry> (m_count), false);
            }

            public List<Entry> ReadIndex ()
            {
                if (!IsSaneCount (m_count) || m_packed_size+0x20L > m_file.MaxOffset)
                    return null;
                m_index = new byte[m_unpacked_size];
                var readers = new Func<Stream, bool>[] {
                    ReadV0,
                    s => ReadV1 (s, 0x18),
                    s => ReadV1 (s, 0x10),
                };
                using (var input = m_file.CreateStream (0x20, m_packed_size))
                {
                    foreach (var read in readers)
                    {
                        try
                        {
                            if (read (input))
                                return m_dir.Value;
                        }
                        catch { /* ignore parse errors */ }
                        input.Position = 0;
                        if (m_dir.IsValueCreated)
                            m_dir.Value.Clear();
                    }
                    return null;
                }
            }

            bool ReadV1 (Stream input, int entry_size)
            {
                // NOTE CryptoStream will close an input stream
                using (var proxy = new InputProxyStream (input, true))
                using (var xored = new CryptoStream (proxy, new NotTransform(), CryptoStreamMode.Read))
                using (var lzss = new LzssStream (xored))
                    if (m_index.Length != lzss.Read (m_index, 0, m_index.Length))
                        return false;

                int index_offset = Array.IndexOf<byte> (m_index, 0);
                if (-1 == index_offset || 0 == index_offset)
                    return false;
                Password = m_index.Take (index_offset++).ToArray();
                long base_offset = 0x20 + m_packed_size;

                for (int i = 0; i < m_count; ++i)
                {
                    var entry = new PackedEntry();
                    entry.Offset = LittleEndian.ToUInt32 (m_index, index_offset) + base_offset;
                    entry.Size   = LittleEndian.ToUInt32 (m_index, index_offset + 4);
                    if (!entry.CheckPlacement (m_file.MaxOffset))
                        return false;
                    entry.UnpackedSize = LittleEndian.ToUInt32 (m_index, index_offset + 8);
                    entry.IsPacked = entry.UnpackedSize != 0;
                    if (!entry.IsPacked)
                        entry.UnpackedSize = entry.Size;
                    int name_len = LittleEndian.ToInt32 (m_index, index_offset + 0xC);
                    if (name_len <= 0 || name_len > 0x100)
                        return false;
                    entry.Name = Encodings.cp932.GetString (m_index, index_offset+entry_size, name_len);
                    entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name);
                    m_dir.Value.Add (entry);
                    index_offset += entry_size + name_len;
                }
                return true;
            }

            bool ReadV0 (Stream input)
            {
                long current_offset = 0x20 + m_packed_size;
                uint offset_table_size = (uint)m_count * 0x10;
                if (offset_table_size > m_file.View.Reserve (current_offset, offset_table_size))
                    return false;

                using (var lzss = new LzssStream (input, LzssMode.Decompress, true))
                    if (m_index.Length != lzss.Read (m_index, 0, m_index.Length))
                        return false;

                for (int i = 0; i < m_index.Length; ++i)
                {
                    m_index[i] = (byte)(~m_index[i] - 5);
                }
                int index_offset = Array.IndexOf<byte> (m_index, 0);
                if (-1 == index_offset || 0 == index_offset)
                    return false;
                index_offset++;
//                Password = m_index.Take (index_offset++).ToArray();

                for (int i = 0; i < m_count && index_offset < m_index.Length; ++i)
                {
                    int name_end = Array.IndexOf<byte> (m_index, 0, index_offset);
                    if (-1 == name_end)
                        name_end = m_index.Length;
                    if (index_offset == name_end)
                        return false;
                    var entry = new PackedEntry();
                    entry.Offset = m_file.View.ReadUInt32 (current_offset);
                    entry.Size   = m_file.View.ReadUInt32 (current_offset+4);
                    entry.UnpackedSize = m_file.View.ReadUInt32 (current_offset+8);
                    entry.IsPacked = entry.UnpackedSize != 0;
                    if (!entry.CheckPlacement (m_file.MaxOffset))
                        return false;
                    entry.Name = Encodings.cp932.GetString (m_index, index_offset, name_end-index_offset);
                    entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name);
                    m_dir.Value.Add (entry);
                    index_offset = name_end+1;
                    current_offset += 0x10;
                }
                return true;
            }
        }

        static readonly ushort[] LzRefTable = {
            0x0001, 0x0804, 0x1001, 0x2001, 0x0002, 0x0805, 0x1002, 0x2002, 
            0x0003, 0x0806, 0x1003, 0x2003, 0x0004, 0x0807, 0x1004, 0x2004, 
            0x0005, 0x0808, 0x1005, 0x2005, 0x0006, 0x0809, 0x1006, 0x2006, 
            0x0007, 0x080A, 0x1007, 0x2007, 0x0008, 0x080B, 0x1008, 0x2008, 
            0x0009, 0x0904, 0x1009, 0x2009, 0x000A, 0x0905, 0x100A, 0x200A, 
            0x000B, 0x0906, 0x100B, 0x200B, 0x000C, 0x0907, 0x100C, 0x200C, 
            0x000D, 0x0908, 0x100D, 0x200D, 0x000E, 0x0909, 0x100E, 0x200E, 
            0x000F, 0x090A, 0x100F, 0x200F, 0x0010, 0x090B, 0x1010, 0x2010, 
            0x0011, 0x0A04, 0x1011, 0x2011, 0x0012, 0x0A05, 0x1012, 0x2012, 
            0x0013, 0x0A06, 0x1013, 0x2013, 0x0014, 0x0A07, 0x1014, 0x2014, 
            0x0015, 0x0A08, 0x1015, 0x2015, 0x0016, 0x0A09, 0x1016, 0x2016, 
            0x0017, 0x0A0A, 0x1017, 0x2017, 0x0018, 0x0A0B, 0x1018, 0x2018, 
            0x0019, 0x0B04, 0x1019, 0x2019, 0x001A, 0x0B05, 0x101A, 0x201A, 
            0x001B, 0x0B06, 0x101B, 0x201B, 0x001C, 0x0B07, 0x101C, 0x201C, 
            0x001D, 0x0B08, 0x101D, 0x201D, 0x001E, 0x0B09, 0x101E, 0x201E, 
            0x001F, 0x0B0A, 0x101F, 0x201F, 0x0020, 0x0B0B, 0x1020, 0x2020, 
            0x0021, 0x0C04, 0x1021, 0x2021, 0x0022, 0x0C05, 0x1022, 0x2022, 
            0x0023, 0x0C06, 0x1023, 0x2023, 0x0024, 0x0C07, 0x1024, 0x2024, 
            0x0025, 0x0C08, 0x1025, 0x2025, 0x0026, 0x0C09, 0x1026, 0x2026, 
            0x0027, 0x0C0A, 0x1027, 0x2027, 0x0028, 0x0C0B, 0x1028, 0x2028, 
            0x0029, 0x0D04, 0x1029, 0x2029, 0x002A, 0x0D05, 0x102A, 0x202A, 
            0x002B, 0x0D06, 0x102B, 0x202B, 0x002C, 0x0D07, 0x102C, 0x202C, 
            0x002D, 0x0D08, 0x102D, 0x202D, 0x002E, 0x0D09, 0x102E, 0x202E, 
            0x002F, 0x0D0A, 0x102F, 0x202F, 0x0030, 0x0D0B, 0x1030, 0x2030, 
            0x0031, 0x0E04, 0x1031, 0x2031, 0x0032, 0x0E05, 0x1032, 0x2032, 
            0x0033, 0x0E06, 0x1033, 0x2033, 0x0034, 0x0E07, 0x1034, 0x2034, 
            0x0035, 0x0E08, 0x1035, 0x2035, 0x0036, 0x0E09, 0x1036, 0x2036, 
            0x0037, 0x0E0A, 0x1037, 0x2037, 0x0038, 0x0E0B, 0x1038, 0x2038, 
            0x0039, 0x0F04, 0x1039, 0x2039, 0x003A, 0x0F05, 0x103A, 0x203A, 
            0x003B, 0x0F06, 0x103B, 0x203B, 0x003C, 0x0F07, 0x103C, 0x203C, 
            0x0801, 0x0F08, 0x103D, 0x203D, 0x1001, 0x0F09, 0x103E, 0x203E, 
            0x1801, 0x0F0A, 0x103F, 0x203F, 0x2001, 0x0F0B, 0x1040, 0x2040, 
        };

        internal byte[] UnpackCustomLzss (byte[] input)
        {
            int unpacked_size = 0;
            int src = 0;
            int i = 0;
            byte b;
            do
            {
                b = input[src++];
                unpacked_size |= (b & 0x7F) << i;
                i += 7;
            }
            while (b >= 0x80);
            if (unpacked_size <= 0)
                throw new InvalidEncryptionScheme();

            var output = new byte[unpacked_size];
            int dst = 0;
            while (dst < unpacked_size)
            {
                b = input[src++];
                if (0 != (b & 3))
                {
                    int offset_length = (LzRefTable[b] >> 8) & ~7;
                    int offset = 0;
                    for (i = 0; i < offset_length; i += 8)
                        offset |= input[src++] << i;
                    offset += LzRefTable[b] & 0x700;

                    int count = LzRefTable[b] & 0xFF;
                    Binary.CopyOverlapped (output, dst - offset, dst, count);
                    dst += count;
                }
                else
                {
                    int count = (b >> 2) + 1;
                    if (count >= 0x3D)
                    {
                        int count_length = (count - 0x3C) * 8;
                        count = 0;
                        for (i = 0; i < count_length; i += 8)
                            count |= input[src++] << i;
                        count++;        
                    }
                    Buffer.BlockCopy (input, src, output, dst, count);
                    src += count;
                    dst += count;
                }
            }
            return output;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class Arc2Opener : ArcOpener
    {
        public override string         Tag { get { return "ARC/Tactics/2"; } }
        public override bool     CanCreate { get { return false; } }

        public Arc2Opener ()
        {
            Extensions = new string[] { "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "ICS_ARC_FILE"))
                return null;

            var dir = new List<Entry>();
            long offset = 0x10;
            while (offset < file.MaxOffset)
            {
                uint size = file.View.ReadUInt32 (offset);
                uint unpacked_size = file.View.ReadUInt32 (offset+4);
                uint name_length = file.View.ReadUInt32 (offset+8);
                if (0 == name_length)
                    break;
                if (name_length > 0x100)
                    return null;
                offset += 0x14;
                var name = file.View.ReadString (offset, name_length);
                offset += name_length;
                if (offset + size > file.MaxOffset)
                    return null;
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset    = offset;
                entry.Size      = size;
                entry.IsPacked  = unpacked_size != 0;
                entry.UnpackedSize = entry.IsPacked ? unpacked_size : size;
                dir.Add (entry);
                offset += size;
            }
            if (0 == dir.Count)
                return null;
            var scheme = QueryScheme();
            if (null == scheme)
                return null;
            return new TacticsArcFile (file, this, dir, scheme);
        }

        ArcScheme QueryScheme ()
        {
            var options = Query<TacticsOptions> (arcStrings.ArcEncryptedNotice);
            return options.Scheme;
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            string title = Settings.Default.TacticsArcTitle;
            ArcScheme scheme = null;
            if (!KnownSchemes.TryGetValue (title, out scheme) && !string.IsNullOrEmpty (Settings.Default.TacticsArcPassword))
                scheme = new ArcScheme (Settings.Default.TacticsArcPassword);
            return new TacticsOptions { Scheme = scheme };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetTactics();
        }

        public static Dictionary<string, ArcScheme> KnownSchemes = new Dictionary<string, ArcScheme>();

        public override ResourceScheme Scheme
        {
            get { return new SchemeMap { KnownSchemes = KnownSchemes }; }
            set { KnownSchemes = ((SchemeMap)value).KnownSchemes; }
        }
    }

    public class TacticsOptions : ResourceOptions
    {
        public ArcScheme    Scheme;
    }

    [Serializable]
    public class ArcScheme
    {
        public string   Password;
        public bool     CustomLzss;

        public ArcScheme (string password, bool custom_lzss = false)
        {
            Password = password;
            CustomLzss = custom_lzss;
        }
    }

    [Serializable]
    public class SchemeMap : ResourceScheme
    {
        public Dictionary<string, ArcScheme> KnownSchemes;
    }
}
