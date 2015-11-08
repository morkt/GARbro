//! \file       ArcARC.cs
//! \date       Sat Sep 19 22:24:12 2015
//! \brief      RPM engine resource archive.
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
using System.Text;
using GameRes.Compression;
using GameRes.Formats.Strings;
using GameRes.Formats.Properties;
using GameRes.Utility;

namespace GameRes.Formats.Rpm
{
    public class RpmOptions : ResourceOptions
    {
        public EncryptionScheme Scheme;
    }

    [Serializable]
    public class EncryptionScheme
    {
        public string   Keyword;
        public int      NameLength;

        public EncryptionScheme (string key, int name_length = 32)
        {
            Keyword = key;
            NameLength = name_length;
        }
    }

    [Serializable]
    public class ArcScheme : ResourceScheme
    {
        public Dictionary<string, EncryptionScheme> KnownSchemes;
    }

    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/RPM"; } }
        public override string Description { get { return "RPM engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "arc" };
        }

        public static Dictionary<string, EncryptionScheme> KnownSchemes = new Dictionary<string, EncryptionScheme>();

        public override ResourceScheme Scheme
        {
            get { return new ArcScheme { KnownSchemes = KnownSchemes }; }
            set { KnownSchemes = ((ArcScheme)value).KnownSchemes; }
        }

        /// <summary>Minimum entry length across all possible archive schemes.</summary>
        const int MinEntryLength = 0x24;

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count) || 8 + count * MinEntryLength >= file.MaxOffset)
                return null;
            uint is_compressed = file.View.ReadUInt32 (4);
            if (is_compressed > 1) // should be either 0 or 1
                return null;
            var scheme = GuessScheme (file, count);
            // additional filename extension check avoids dialog popup on false positives
            if (null == scheme && KnownSchemes.Count > 0 && file.Name.EndsWith (".arc", StringComparison.InvariantCultureIgnoreCase))
                scheme = QueryScheme();
            if (null == scheme)
                return null;

            // special case for "instdata.arc" archives
            if (scheme.Keyword != "inst"
                && Path.GetFileName (file.Name).Equals ("instdata.arc", StringComparison.InvariantCultureIgnoreCase))
                scheme = new EncryptionScheme ("inst", scheme.NameLength);

            int index_size = count * (scheme.NameLength + 12);
            var index = new byte[index_size];
            if (index_size != file.View.Read (8, index, 0, (uint)index_size))
                return null;
            DecryptIndex (index, scheme.Keyword);

            uint data_offset = LittleEndian.ToUInt32 (index, scheme.NameLength + 8);
            if (data_offset != 8 + index_size)
                return null;

            int index_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = Binary.GetCString (index, index_offset, scheme.NameLength);
                index_offset += scheme.NameLength;
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.UnpackedSize  = LittleEndian.ToUInt32 (index, index_offset);
                entry.Size          = LittleEndian.ToUInt32 (index, index_offset+4);
                entry.Offset        = LittleEndian.ToUInt32 (index, index_offset+8);
                entry.IsPacked      = is_compressed != 0;
                if (0 != entry.Size)
                {
                    if (entry.Offset < data_offset || !entry.CheckPlacement (file.MaxOffset))
                        return null;
                }
                dir.Add (entry);
                index_offset += 12;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (0 == entry.Size)
                return Stream.Null;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var packed = entry as PackedEntry;
            if (null == packed || !packed.IsPacked)
                return input;
            return new LzssStream (input);
        }

        EncryptionScheme GuessScheme (ArcView file, int count)
        {
            int[] possible_sizes = { 0x20, 0x18 };
            byte[] first_entry = new byte[possible_sizes[0] + 12];
            if (first_entry.Length != file.View.Read (8, first_entry, 0, (uint)first_entry.Length))
                return null;
            byte[] key_bits = new byte[4];
            byte[] actual_offset = new byte[4];
            foreach (var name_length in possible_sizes)
            {
                int first_offset = 8 + count * (name_length + 12);
                if (first_offset >= file.MaxOffset)
                    continue;
                LittleEndian.Pack (first_offset, actual_offset, 0);
                int i;
                for (i = 0; i < 4; ++i)
                {
                    key_bits[i] = (byte)(first_entry[name_length+8+i] - actual_offset[i]);
                }

                int first_match = ReverseFind (first_entry, name_length-4, key_bits);
                if (first_match < 4)
                    continue;
                int second_match = ReverseFind (first_entry, first_match-4, key_bits);
                if (second_match <= 0)
                    continue;
                int key_length = first_match - second_match;
                byte[] key = new byte[key_length];
                for (i = 0; i < key_length; ++i)
                {
                    byte sym = (byte)-first_entry[second_match+i];
                    if (sym < 0x21 || sym > 0x7E)
                        break;
                    key[(second_match+i) % key_length] = sym;
                }
                if (i == key_length)
                    return new EncryptionScheme (Encoding.ASCII.GetString (key), name_length);
            }
            return null;
        }

        static int ReverseFind (byte[] array, int pos, byte[] pattern)
        {
            int pattern_end_pos = pattern.Length-1;
            int pattern_pos = pattern_end_pos;
            for (int i = pos + pattern_pos; i >= 0; --i)
            {
                if (array[i] == pattern[pattern_pos])
                {
                    if (0 == pattern_pos)
                        return i;
                    --pattern_pos;
                }
                else if (pattern_end_pos != pattern_pos)
                {
                    i += pattern_end_pos - pattern_pos;
                    pattern_pos = pattern_end_pos;
                }
            }
            return -1;
        }

        private void DecryptIndex (byte[] data, string key)
        {
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] += (byte)key[i % key.Length];
            }
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new RpmOptions {
                Scheme = GetScheme (Settings.Default.RPMScheme),
            };
        }

        public override object GetAccessWidget ()
        {
            return new WidgetARC();
        }

        EncryptionScheme QueryScheme ()
        {
            var options = Query<RpmOptions> (arcStrings.RPMEncryptedNotice);
            return options.Scheme;
        }

        static EncryptionScheme GetScheme (string title)
        {
            EncryptionScheme scheme = null;
            KnownSchemes.TryGetValue (title, out scheme);
            return scheme;
        }
    }
}
