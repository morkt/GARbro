//! \file       ArcASAR.cs
//! \date       2025-12-23
//! \brief      Electron/Atom Shell resource archive format.
//
// Copyright (C) 2025 by morkt
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
using System.Text;
using Newtonsoft.Json;

namespace GameRes.Formats.Chromium
{
    class AsarNode
    {
        [JsonProperty("files")]
        public Dictionary<string, AsarNode> Files { get; set; }

        [JsonProperty("size")]
        public uint Size { get; set; }

        [JsonProperty("offset")]
        public string Offset { get; set; }

        [JsonProperty("unpacked")]
        public bool Unpacked { get; set; }

        [JsonProperty("link")]
        public string Link { get; set; }
    }

    [Export(typeof(ArchiveFormat))]
    public class AsarOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ASAR"; } }
        public override string Description { get { return "Electron/Atom Shell archive format"; } }
        public override uint     Signature { get { return 0x00000004; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public AsarOpener ()
        {
            Extensions = new string[] { "asar" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset < 0x10)
                return null;
            if (file.View.ReadUInt32 (4) != file.View.ReadUInt32 (8) + 4)
                return null;
            uint index_size = file.View.ReadUInt32 (0x0C);
            if (index_size > file.MaxOffset - 0x10)
                return null;
            string json = file.View.ReadString (0x10, index_size, Encoding.UTF8);
            AsarNode dict;
            try
            {
                dict = JsonConvert.DeserializeObject<AsarNode> (json);
            }
            catch (JsonException)
            {
                return null;
            }
            if (null == dict)
                return null;
            var dir = new List<Entry> ();
            long pad = (0x10L + index_size + 3) & ~3L;
            ParseIndex (dir, dict, pad);
            dir.RemoveAll (e => e.Size != 0 && !e.CheckPlacement (file.MaxOffset));
            return new ArcFile (file, this, dir);
        }

        void ParseIndex (List<Entry> dir, AsarNode dict, long pad, string cur = "")
        {
            if (dict.Files != null)
            {
                foreach (var kv in dict.Files)
                {
                    string k = kv.Key;
                    AsarNode v = kv.Value;
                    ParseIndex (dir, v, pad, cur != "" ? $"{cur}/{k}" : k);
                }
            }
            else
            {
                if (dict.Unpacked || !string.IsNullOrEmpty (dict.Link) || string.IsNullOrEmpty (dict.Offset))
                    return;
                long offset;
                if (!long.TryParse (dict.Offset, out offset))
                    return;
                var entry = new Entry {
                    Name = cur,
                    Size = dict.Size,
                    Offset = offset + pad,
                    Type = FormatCatalog.Instance.GetTypeFromName (cur)
                };
                dir.Add (entry);
            }
        }
    }
}
