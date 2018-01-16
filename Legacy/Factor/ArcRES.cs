//! \file       ArcRES.cs
//! \date       2017 Nov 29
//! \brief      Factor resource archive.
//
// Copyright (C) 2017 by morkt
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

namespace GameRes.Formats.Factor
{
#if DEBUG
    [Export(typeof(ArchiveFormat))]
#endif
    public class PackOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PACK/FACTOR"; } }
        public override string Description { get { return "Factor resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PackOpener ()
        {
            Extensions = new string[] { "" };
        }

        static readonly Regex PackNameRe = new Regex (@"^pack(\d)$");

        public override ArcFile TryOpen (ArcView file)
        {
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var match = PackNameRe.Match (base_name);
            if (!match.Success)
                return null;
            var pack_ext = Path.GetExtension (file.Name);
            List<string> names = new List<string>();
            /*
            if (pack_ext.Equals (".bmp", StringComparison.InvariantCultureIgnoreCase))
            {
                var res_name = Path.ChangeExtension (file.Name, "res");
                var res_num  = match.Groups[1].Value;
                names.AddRange (ReadNames (res_name, res_num));
            }
            */
            var dir = new List<Entry>();
            long offset = 0;
            int i = 0;
            while (offset < file.MaxOffset)
            {
                uint size = file.View.ReadUInt32 (offset);
                offset += 4;
                string name;
                if (i < names.Count)
                    name = names[i];
                else
                    name = string.Format ("{0}#{1:D4}{2}", base_name, i, pack_ext);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = offset;
                entry.Size   = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                ++i;
                offset += size;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (entry.Name.HasExtension (".res"))
                input = new XoredStream (input, 0x80);
            return input;
        }

        static readonly Regex FirstLineRe = new Regex (@"\\(\d)\\$");

        IEnumerable<string> ReadNames (string res_name, string num)
        {
            if (!VFS.FileExists (res_name))
                yield break;
            using (var pack = VFS.OpenView (res_name))
            {
                uint offset = 4 + pack.View.ReadUInt32 (0);
                offset += 4 + pack.View.ReadUInt32 (offset);
                uint size = pack.View.ReadUInt32 (offset);
                offset += 4;
                if (offset >= pack.MaxOffset)
                    yield break;
                using (var res = pack.CreateStream (offset, size))
                using (var decrypted = new XoredStream (res, 0x80))
                using (var input = new StreamReader (decrypted, Encodings.cp932))
                {
                    var line = input.ReadLine();
                    if (string.IsNullOrEmpty (line))
                        yield break;
                    var match = FirstLineRe.Match (line);
                    if (!match.Success || match.Groups[1].Value != num)
                        yield break;
                    while ((line = input.ReadLine()) != null)
                    {
                        yield return line;
                    }
                }
            }
        }
    }
}
