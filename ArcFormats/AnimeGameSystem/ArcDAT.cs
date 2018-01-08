//! \file       ArcDAT.cs
//! \date       Thu Nov 05 04:40:35 2015
//! \brief      AnimeGameSystem resource archive.
//
// Copyright (C) 2015-2017 by morkt
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

namespace GameRes.Formats.Ags
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/AGS"; } }
        public override string Description { get { return "AnimeGameSystem resource archive"; } }
        public override uint     Signature { get { return 0x6B636170; } } // 'pack'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt16 (4);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 6;
            uint index_size = (uint)count*0x18;
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x10);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x10);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x14);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x18;
            }
            var arc_name = Path.GetFileName (file.Name);
            if (EncryptedArchives.Contains (arc_name))
            {
                var options = Query<AgsOptions> (arcStrings.AGSMightBeEncrypted);
                EncryptionKey key;
                if (options.Scheme.FileMap.TryGetValue (arc_name, out key))
                    return new DatArchive (file, this, dir, key);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var earc = arc as DatArchive;
            if (null == earc)
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            byte key = earc.Key.Initial;
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] ^= key;
                key += earc.Key.Increment;
            }
            return new BinMemoryStream (data, entry.Name);
        }

        public static readonly EncryptionScheme DefaultScheme = new EncryptionScheme {
            FileMap = new Dictionary<string, EncryptionKey>()
        };

        public override ResourceOptions GetDefaultOptions ()
        {
            return new AgsOptions { Scheme = GetScheme (Properties.Settings.Default.AGSTitle) };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetAGS (KnownSchemes.Keys);
        }

        public EncryptionScheme GetScheme (string title)
        {
            EncryptionScheme scheme;
            if (string.IsNullOrEmpty (title) || !KnownSchemes.TryGetValue (title, out scheme))
                scheme = DefaultScheme;
            return scheme;
        }

        AgsScheme m_scheme = new AgsScheme
        {
            KnownSchemes = new Dictionary<string, EncryptionScheme>(),
            EncryptedArchives = new HashSet<string>()
        };

        Dictionary<string, EncryptionScheme> KnownSchemes { get { return m_scheme.KnownSchemes; } }
        HashSet<string>                 EncryptedArchives { get { return m_scheme.EncryptedArchives; } }

        public override ResourceScheme Scheme
        {
            get { return m_scheme; }
            set { m_scheme = (AgsScheme)value; }
        }
    }

    internal class DatArchive : ArcFile
    {
        public EncryptionKey Key;

        public DatArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, EncryptionKey key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    internal class AgsOptions : ResourceOptions
    {
        public EncryptionScheme Scheme;
    }

    [Serializable]
    public struct EncryptionKey
    {
        public byte Initial;
        public byte Increment;
    }

    [Serializable]
    public class EncryptionScheme
    {
        public Dictionary<string, EncryptionKey> FileMap;
    }

    [Serializable]
    public class AgsScheme : ResourceScheme
    {
        public Dictionary<string, EncryptionScheme> KnownSchemes;
        public HashSet<string>                      EncryptedArchives;
    }
}
