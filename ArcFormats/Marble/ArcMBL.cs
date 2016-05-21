//! \file       ArcMBL.cs
//! \date       Fri Mar 27 23:11:19 2015
//! \brief      Marble Engine archive implementation.
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
using GameRes.Formats.Properties;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.Marble
{
    public class MblOptions : ResourceOptions
    {
        public string PassPhrase;
    }

    public class MblArchive : ArcFile
    {
        public readonly byte[] Key;

        public MblArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, string password)
            : base (arc, impl, dir)
        {
            Key = Encodings.cp932.GetBytes (password);
        }
    }

    [Serializable]
    public class MblScheme : ResourceScheme
    {
        public Dictionary<string, string> KnownKeys;
    }

    [Export(typeof(ArchiveFormat))]
    public class MblOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MBL"; } }
        public override string Description { get { return "Marble engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            ArcFile arc = null;
            uint filename_len = file.View.ReadUInt32 (4);
            if (filename_len > 0 && filename_len <= 0xff)
                arc = ReadIndex (file, count, filename_len, 8);
            if (null == arc)
                arc = ReadIndex (file, count, 0x10, 4);
            if (null == arc)
                arc = ReadIndex (file, count, 0x38, 4);
            return arc;
        }

        static readonly Lazy<ImageFormat> PrsFormat = new Lazy<ImageFormat> (() => ImageFormat.FindByTag ("PRS"));

        private ArcFile ReadIndex (ArcView file, int count, uint filename_len, uint index_offset)
        {
            uint index_size = (8u + filename_len) * (uint)count;
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            try
            {
                bool contains_scripts = false;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    string name = file.View.ReadString (index_offset, filename_len);
                    if (0 == name.Length)
                        break;
                    if (filename_len-name.Length > 1)
                    {
                        string ext = file.View.ReadString (index_offset+name.Length+1, filename_len-(uint)name.Length-1);
                        if (0 != ext.Length)
                            name = Path.ChangeExtension (name, ext);
                    }
                    name = name.ToLowerInvariant();
                    index_offset += (uint)filename_len;
                    uint offset = file.View.ReadUInt32 (index_offset);
                    string type = null;
                    if (name.EndsWith (".s"))
                    {
                        type = "script";
                        contains_scripts = true;
                    }
                    else if (4 == Path.GetExtension (name).Length)
                    {
                        type = FormatCatalog.Instance.GetTypeFromName (name);
                    }
                    Entry entry;
                    if (string.IsNullOrEmpty (type))
                    {
                        entry = new AutoEntry (name, () => {
                            uint signature = file.View.ReadUInt32 (offset);
                            if (0x4259 == (0xffff & signature))
                                return PrsFormat.Value;
                            else if (0 != signature)
                                return FormatCatalog.Instance.LookupSignature (signature).FirstOrDefault();
                            else
                                return null;
                        });
                    }
                    else
                    {
                        entry = new Entry { Name = name, Type = type };
                    }
                    entry.Offset = offset;
                    entry.Size = file.View.ReadUInt32 (index_offset+4);
                    if (offset < index_size || !entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    index_offset += 8;
                }
                if (0 == dir.Count || (1 == dir.Count && count > 1))
                    return null;
                if (contains_scripts)
                {
                    var options = Query<MblOptions> (arcStrings.MBLNotice);
                    if (options.PassPhrase.Length > 0)
                        return new MblArchive (file, this, dir, options.PassPhrase);
                }
                return new ArcFile (file, this, dir);
            }
            catch
            {
                return null;
            }
        }

        public static Dictionary<string, string> KnownKeys = new Dictionary<string, string>();

        public override ResourceScheme Scheme
        {
            get { return new MblScheme { KnownKeys = KnownKeys }; }
            set { KnownKeys = ((MblScheme)value).KnownKeys; }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Type != "script" || !entry.Name.EndsWith (".s"))
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var marc = arc as MblArchive;
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            if (null == marc || null == marc.Key)
            {
                for (int i = 0; i < data.Length; ++i)
                {
                    data[i] = (byte)-data[i];
                }
            }
            else if (marc.Key.Length > 0)
            {
                for (int i = 0; i < data.Length; ++i)
                {
                    data[i] ^= marc.Key[i % marc.Key.Length];
                }
            }
            return new MemoryStream (data);
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new MblOptions { PassPhrase = Settings.Default.MBLPassPhrase };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetMBL();
        }
    }
}
