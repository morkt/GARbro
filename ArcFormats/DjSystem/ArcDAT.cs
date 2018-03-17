//! \file       ArcDAT.cs
//! \date       2018 Mar 12
//! \brief      DJSYSTEM resource archive.
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
using System.Text.RegularExpressions;

namespace GameRes.Formats.DjSystem
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/DJSYSTEM"; } }
        public override string Description { get { return "DJSYSTEM engine resource archive"; } }
        public override uint     Signature { get { return 0x454C4946; } } // 'FILE'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static readonly Regex IndexEntryRe = new Regex (@"^(\S+)\t(\d+)\s*\t(\d+)");

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "FILECMB-DATA-LIST-IN\n"))
                return null;
            using (var input = file.CreateStream())
            using (var index = new StreamReader (input, Encodings.cp932))
            {
                var dir = new List<Entry>();
                index.ReadLine();
                for (;;)
                {
                    var line = index.ReadLine();
                    if (null == line || "LIST-END" == line)
                        break;
                    var match = IndexEntryRe.Match (line);
                    if (!match.Success)
                        return null;
                    var name   = match.Groups[1].Value;
                    uint start = UInt32.Parse (match.Groups[2].Value);
                    uint end   = UInt32.Parse (match.Groups[3].Value);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = start;
                    entry.Size   = end - start;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    if (name.HasExtension (".vic"))
                        entry.Type = "audio";
                    dir.Add (entry);
                }
                if (0 == dir.Count)
                    return null;
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (arc.File.View.AsciiEqual (entry.Offset, "DJCODE NLINE-"))
            {
                if (arc.File.View.AsciiEqual (entry.Offset+13, "ENCODE\n"))
                {
                    var input = arc.File.CreateStream (entry.Offset+20, entry.Size-20);
                    return new XoredStream (input, 0xFF);
                }
                else if (arc.File.View.AsciiEqual (entry.Offset+13, "NO-ENCODE\n"))
                {
                    var data = arc.File.View.ReadBytes (entry.Offset+23, entry.Size-23);
                    for (int i = 0; i < data.Length; ++i)
                    {
                        if (data[i] == '\r' && i+1 < data.Length && data[i+1] == '\n')
                            ++i;
                        else
                            data[i] ^= 0xFF;
                    }
                    return new BinMemoryStream (data, entry.Name);
                }
            }
            return base.OpenEntry (arc, entry);
        }
    }
}
