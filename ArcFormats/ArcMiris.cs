//! \file       ArcMiris.cs
//! \date       Thu Sep 03 13:12:53 2015
//! \brief      Studio Miris archives implementation.
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
using System.Text.RegularExpressions;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Miris
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/MIRIS"; } }
        public override string Description { get { return "Studio Miris resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        static readonly Regex IndexEntryRe = new Regex (@"\G([^,]+),(\d+),(\d+)#");

        public override ArcFile TryOpen (ArcView file)
        {
            var base_dir = Path.GetDirectoryName (file.Name);
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var list_file = VFS.CombinePath (base_dir, base_name+"l.dat");
            if (!VFS.FileExists (list_file))
                return null;
            string index;
            using (var ls = VFS.OpenStream (list_file))
            using (var zls = new ZLibStream (ls, CompressionMode.Decompress))
            using (var reader = new StreamReader (zls, Encodings.cp932))
            {
                index = reader.ReadToEnd();
            }
            if (string.IsNullOrEmpty (index))
                return null;

            var dir = new List<Entry>();
            var match = IndexEntryRe.Match (index);
            while (match.Success)
            {
                var entry = new Entry {
                    Name    = match.Groups[1].Value,
                    Offset  = UInt32.Parse (match.Groups[3].Value),
                    Size    = UInt32.Parse (match.Groups[2].Value),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                match = match.NextMatch();
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
