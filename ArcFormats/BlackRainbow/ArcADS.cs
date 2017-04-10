//! \file       ArcADS.cs
//! \date       Sun Feb 07 14:40:32 2016
//! \brief      ADVDX engine resource archive.
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
using GameRes.Utility;

namespace GameRes.Formats.BlackRainbow
{
    using EncryptedViewStream = NScripter.EncryptedViewStream;

    [Serializable]
    public class AdsScheme : ResourceScheme
    {
        public Dictionary<string, byte[]> KnownKeys;
    }

    [Export(typeof(ArchiveFormat))]
    public class AdsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ADS"; } }
        public override string Description { get { return "ADVDX engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public AdsOpener ()
        {
            Signatures = new uint[] { 0x84D9514E, 0 };
        }

        public static Dictionary<string, byte[]> KnownKeys = new Dictionary<string, byte[]>();

        public override ResourceScheme Scheme
        {
            get { return new AdsScheme { KnownKeys = KnownKeys }; }
            set { KnownKeys = ((AdsScheme)value).KnownKeys; }
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".ads"))
                return null;
            var arc_name = Path.GetFileNameWithoutExtension (file.Name);
            foreach (var key in KnownKeys.Values)
            {
                using (var arc = new EncryptedViewStream (file, key))
                {
                    uint signature = FormatCatalog.ReadSignature (arc);
                    if (2 == signature || 4 == signature || 5 == signature)
                    {
                        var dir = ReadIndex (arc, key, arc_name);
                        if (dir != null)
                            return new AdsArchive (file, this, dir, key);
                    }
                }
            }
            return null;
        }

        List<Entry> ReadIndex (Stream arc, byte[] key, string arc_name)
        {
            arc.Position = 8;
            using (var reader = new ArcView.Reader (arc))
            {
                int count = reader.ReadInt32();
                if (!IsSaneCount (count))
                    return null;
                uint base_offset = reader.ReadUInt32();
                uint index_size = 4u * (uint)count;
                var max_offset = arc.Length;
                if (base_offset >= max_offset || base_offset < (0x10+index_size))
                    return null;
                var index = new List<uint> (count);
                for (int i = 0; i < count; ++i)
                {
                    uint offset = reader.ReadUInt32();
                    if (offset != 0xffffffff)
                    {
                        if (offset >= max_offset-base_offset)
                            return null;
                        index.Add (base_offset + offset);
                    }
                }
                var name_buffer = new byte[0x20];
                var dir = new List<Entry> (index.Count);
                for (int i = 0; i < index.Count; ++i)
                {
                    long offset = index[i];
                    reader.BaseStream.Position = offset;
                    reader.Read (name_buffer, 0, 0x20);
                    string name = Binary.GetCString (name_buffer, 0, 0x20);
                    Entry entry;
                    if (0 == name.Length)
                        entry = new Entry { Name = string.Format ("{0}#{1:D5}", arc_name, i), Type = "image" };
                    else
                        entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = offset + 0x24;
                    entry.Size = reader.ReadUInt32();
                    dir.Add (entry);
                }
                return dir;
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var ads_arc = arc as AdsArchive;
            if (null == ads_arc)
                return base.OpenEntry (arc, entry);
            var input = new EncryptedViewStream (ads_arc.File, ads_arc.Key);
            return new StreamRegion (input, entry.Offset, entry.Size);
        }
    }

    internal class AdsArchive : ArcFile
    {
        public readonly byte[] Key;

        public AdsArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }
}
