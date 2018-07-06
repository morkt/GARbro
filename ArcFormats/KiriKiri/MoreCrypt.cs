//! \file       MoreCrypt.cs
//! \date       2018 Jan 24
//! \brief      PureMore/More filename scrambling scheme.
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
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace GameRes.Formats.KiriKiri
{
    [Serializable]
    public class PureMoreCrypt : NoCrypt
    {
        public string    FileListName { get; set; }
        public string         CharMap { get; set; }
        public string LayerNameSuffix { get; set; }

        [NonSerialized]
        Lazy<Dictionary<string, string>> KnownNames;

        public PureMoreCrypt ()
        {
            KnownNames = new Lazy<Dictionary<string, string>> (ReadFileList);
            CharMap = DefaultCharMap;
        }

        [OnDeserialized()]
        void PostDeserialization (StreamingContext context)
        {
            KnownNames = new Lazy<Dictionary<string, string>> (ReadFileList);
        }

        public override string ReadName (BinaryReader header)
        {
            var name = base.ReadName (header);
            if (null == name || name.Length != 36 || !name.HasExtension (".tlg"))
                return name;
            string mapped_name;
            if (KnownNames.Value.TryGetValue (name, out mapped_name))
                name = mapped_name;
            return name;
        }

        Dictionary<string, string> ReadFileList ()
        {
            var dict = new Dictionary<string, string>();
            if (string.IsNullOrEmpty (FileListName))
                return dict;
            try
            {
                using (var sha = SHA256.Create())
                {
                    var str = new StringBuilder (36);
                    var bytes = new byte[0x100];
                    var comma = new char[] {','};
                    string layer_suffix = LayerNameSuffix ?? "";
                    FormatCatalog.Instance.ReadFileList (FileListName, line => {
                        var parts = line.Split (comma, 2);
                        string name = parts[0];
                        int ext = name.LastIndexOf ('.');
                        if (ext != -1)
                            name = name.Substring (0, ext);
                        if (2 == parts.Length)
                            name += layer_suffix;
                        int len = Encoding.Unicode.GetBytes (name, 0, name.Length, bytes, 0);
                        var hash = sha.ComputeHash (bytes, 0, len);
                        str.Clear();
                        foreach (byte b in hash)
                        {
                            str.Append (CharMap[b]);
                        }
                        str.Append (".tlg");
                        dict[str.ToString()] = parts[0];
                    });
                }
            }
            catch (Exception X)
            {
                System.Diagnostics.Trace.WriteLine (X.Message, "[PureMoreCrypt]");
            }
            return dict;
        }

        [NonSerialized]
        static readonly string DefaultCharMap =
            "0123456789abcdefghijklmnopqrstuvwxyz"+
            "０１２３４５６７８９ａｂｃｄｅｆｇｈｉｊｋｌｍｎｏｐｑｒｓｔｕｖｗｘｙｚ"+
            "ぁあぃいぅうぇえぉおかがきぎくぐけげこごさざしじすずせぜそぞただ"+
            "ちぢっつづてでとどなにぬねのはばぱひびぴふぶぷへべぺほぼぽまみむ"+
            "めもゃやゅゆょよらりるれろゎわゐゑをんァアィイゥウェエォオカガキ"+
            "ギクグケゲコゴサザシジスズセゼソゾタダチヂッツヅテデトドナニヌネ"+
            "ノハバパヒビピフブプヘベペホボポマミムメモャヤュユョヨラリルレロ"+
            "ヮワヰヱヲンヴヵヶ零一二三四五六七八九十百千万億";
    }
}
