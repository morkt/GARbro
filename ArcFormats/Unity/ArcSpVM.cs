//! \file       ArcSpVM.cs
//! \date       2018 May 02
//! \brief      Unity SpVM resources deserializer.
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
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace GameRes.Formats.Unity
{
    [Export(typeof(ArchiveFormat))]
    public class BytesOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BYTES/UNITY"; } }
        public override string Description { get { return "Unity engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.EndsWith ("DAT.bytes", StringComparison.OrdinalIgnoreCase))
                return null;
            var inf_name = file.Name.Substring (0, file.Name.Length - "DAT.bytes".Length);
            inf_name += "INF.bytes";
            if (!VFS.FileExists (inf_name))
                return null;
            using (var inf = VFS.OpenStream (inf_name))
            {
                var bin = new BinaryFormatter { Binder = new SpTypeBinder() };
                var list = bin.Deserialize (inf) as List<LinkerInfo>;
                if (null == list || 0 == list.Count)
                    return null;
                var base_name = Path.GetFileNameWithoutExtension (file.Name);
                string type = "";
                if (base_name.StartsWith ("WAVE", StringComparison.OrdinalIgnoreCase))
                    type = "audio";
                else if (base_name.StartsWith ("CG", StringComparison.OrdinalIgnoreCase))
                    type = "image";
                var dir = list.Select (e => new Entry {
                    Name = e.name, Type = type, Offset = e.offset, Size = (uint)e.size
                }).ToList();
                return new ArcFile (file, this, dir);
            }
        }
    }

    [Serializable]
    public class LinkerInfo
    {
        public string   name { get; set; }
        public int    offset { get; set; }
        public int      size { get; set; }
    }

    internal class SpTypeBinder : SerializationBinder
    {
        public override Type BindToType (string assemblyName, string typeName)
        {
            if ("Assembly-CSharp" == assemblyName && "SpVM.Library.LinkerInfo" == typeName)
            {
                return typeof(LinkerInfo);
            }
            if (assemblyName.StartsWith ("mscorlib,") && typeName.StartsWith ("System.Collections.Generic.List`1[[SpVM.Library.LinkerInfo"))
            {
                return typeof(List<LinkerInfo>);
            }
            return null;
        }
    }
}
