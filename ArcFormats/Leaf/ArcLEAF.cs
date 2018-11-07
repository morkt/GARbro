//! \file       ArcLEAF.cs
//! \date       2018 Nov 06
//! \brief      Leaf resource archive.
//
// Copyright (C) 2018 by morkt
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
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.Leaf
{
    internal class LeafArchive : ArcFile
    {
        public readonly byte[] Key;

        public LeafArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Serializable]
    public class LeafPackScheme : ResourceScheme
    {
        public IDictionary<string, byte[]>  KnownSchemes;
    }

    [Export(typeof(ArchiveFormat))]
    public class LeafPackOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/LEAF"; } }
        public override string Description { get { return "Leaf resource archive"; } }
        public override uint     Signature { get { return 0x4641454C; } } // 'LEAFPACK'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        LeafPackOpener ()
        {
            ContainedFormats = new[] { "LFG", "P16", "DAT/GENERIC" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "PACK"))
                return null;
            int count = file.View.ReadInt16 (8);
            if (!IsSaneCount (count))
                return null;
            uint index_size = (uint)count * 0x18;
            if (index_size >= file.MaxOffset)
                return null;
            var key = QueryKey (file.Name);
            if (null == key)
                return null;
            var index = file.View.ReadBytes (file.MaxOffset - index_size, index_size);
            DecryptData (index, key);
            int index_pos = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = Binary.GetCString (index, index_pos, 8).TrimEnd();
                var ext  = Binary.GetCString (index, index_pos+8, 3).TrimEnd();
                if (!string.IsNullOrWhiteSpace (ext))
                    name = Path.ChangeExtension (name, ext);
                var entry = Create<Entry> (name);
                entry.Offset = index.ToUInt32 (index_pos+0xC);
                entry.Size   = index.ToUInt32 (index_pos+0x10);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_pos += 0x18;
            }
            return new LeafArchive (file, this, dir, key);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var larc = (LeafArchive)arc;
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            DecryptData (data, larc.Key);
            return new BinMemoryStream (data, entry.Name);
        }

        void DecryptData (byte[] data, byte[] key)
        {
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] -= key[i % key.Length];
            }
        }

        byte[] QueryKey (string arc_name)
        {
            var title = FormatCatalog.Instance.LookupGame (arc_name, @"*.exe");
            var key = GetTitleKey (title);
            if (null == key)
            {
                var options = Query<LeafOptions> (arcStrings.ArcEncryptedNotice);
                key = options.Key;
            }
            return key;
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new LeafOptions {
                Key = GetTitleKey (Properties.Settings.Default.LEAFTitle),
            };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetLEAF (KnownKeys.Keys);
        }

        byte[] GetTitleKey (string title)
        {
            byte[] key = null;
            if (!string.IsNullOrEmpty (title))
                KnownKeys.TryGetValue (title, out key);
            return key;
        }

        public static readonly byte[] DefaultKey = {
            0x71, 0x48, 0x6A, 0x55, 0x9F, 0x13, 0x58, 0xF7, 0xD1, 0x7C, 0x3E
        };

        static LeafPackScheme DefaultScheme = new LeafPackScheme { KnownSchemes = new Dictionary<string, byte[]>() };

        public IDictionary<string, byte[]> KnownKeys { get { return DefaultScheme.KnownSchemes; } }

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (LeafPackScheme)value; }
        }
    }

    public class LeafOptions : ResourceOptions
    {
        public byte[]   Key;
    }
}
