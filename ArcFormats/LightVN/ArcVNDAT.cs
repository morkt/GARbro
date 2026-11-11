//! \file       ArcVNDAT.cs
//! \date       2026-08-20
//! \brief      Light.vn engine resource archive.
//
// Copyright (C) 2026 by morkt
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Formats.PkWare;

using SharpZip = ICSharpCode.SharpZipLib.Zip;

namespace GameRes.Formats.LightVN
{
    [Export(typeof(ArchiveFormat))]
    public class VndatOpener : ZipOpener
    {
        public override string         Tag { get { return "ZIP/VNDAT"; } }
        public override string Description { get { return "PKWARE archive format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public VndatOpener ()
        {
            Settings = null;
            Extensions = new string[] { "vndat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".vndat"))
                return null;
            if (-1 == SearchForSignature (file, PkDirSignature))
                return null;
            var input = file.CreateStream();
            try
            {
                return OpenZipArchive (file, input);
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        new ArcFile OpenZipArchive (ArcView file, Stream input)
        {
            var sc = SharpZip.StringCodec.FromCodePage (Encoding.UTF8.CodePage);
            var zip = new SharpZip.ZipFile (input, false, sc);
            try
            {
                var files = zip.Cast<SharpZip.ZipEntry>().Where (z => !z.IsDirectory);
                bool has_encrypted = files.Any (z => z.IsCrypted);
                if (has_encrypted)
                    throw new InvalidFormatException();
                var dir = files.Select (z => new ZipEntry (z) as Entry).ToList();
                return new PkZipArchive (file, this, dir, zip);
            }
            catch
            {
                zip.Close();
                throw;
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var zarc = (PkZipArchive)arc;
            var zent = (ZipEntry)entry;
            var data = new byte[zent.UnpackedSize];
            using (var input = zarc.Native.GetInputStream (zent.NativeEntry))
                input.Read (data, 0, data.Length);
            McdatArchive.Decrypt (data, McdatOpener.DefaultKey, 100);
            return new BinMemoryStream (data, zent.Name);
        }

        public override ResourceScheme Scheme { get; set; }
    }
}
