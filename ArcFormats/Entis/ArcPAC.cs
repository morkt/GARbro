//! \file       ArcPAC.cs
//! \date       Sun Jan 08 11:43:23 2017
//! \brief      Terios resource archive.
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

namespace GameRes.Formats.Entis
{
    /// <summary>
    /// Archive format used in Teri☆Mix
    /// </summary>
    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/TERIOS"; } }
        public override string Description { get { return "Terios resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static readonly byte[] DefaultPassword = Encodings.cp932.GetBytes ("パブロ・ディエゴ・ホセ・フランチスコ・ド・ポール・ジャン・ネボムチェーノ・クリスバン・ クリスピアノ・ド・ラ・ンチシュ・トリニダット・ルイス・イ・ピカソのシプリアーノ･サンティシマ･トリニダードは三位一体の事だったりする");

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".pac"))
                return null;
            uint index_offset = 0;
            var name_buf = new byte[0x18];
            var dir = new List<Entry>();
            while (index_offset < file.MaxOffset && dir.Count < 0x2000)
            {
                if (0x18 != file.View.Read (index_offset, name_buf, 0, 0x18))
                    return null;
                if (0x20 == name_buf[0])
                    break;
                int name_length = 0;
                for (name_length = 0; name_length < name_buf.Length; ++name_length)
                {
                    if (name_buf[name_length] < 0x20)
                        return null;
                    else if (0x20 == name_buf[name_length])
                        break;
                }
                if (name_buf.Length == name_length)
                    return null;
                var name = Encodings.cp932.GetString (name_buf, 0, name_length);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                index_offset += 0x18;
                entry.Offset = 0x40000 + file.View.ReadUInt32 (index_offset);
                entry.Size = file.View.ReadUInt32 (index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            if (0 == dir.Count || 0x2000 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (0 != arc.File.View.ReadByte (entry.Offset))
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset+1, entry.Size-1);
            int k = 0;
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] ^= (byte)~DefaultPassword[k++];
                if (k >= DefaultPassword.Length)
                    k = 0;
            }
            return new BinMemoryStream (data, entry.Name);
        }
    }
}
