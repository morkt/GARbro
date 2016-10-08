//! \file       ImageRSA.cs
//! \date       Tue Jul 05 20:38:47 2016
//! \brief      RSA-encrypted PNG image.
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
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GameRes.Cryptography;

namespace GameRes.Formats.Dogenzaka
{
    internal class Rc4PngMetaData : ImageMetaData
    {
        public byte[]   Key;
    }

    [Export(typeof(ImageFormat))]
    public class Rc4PngFormat : PngFormat
    {
        public override string         Tag { get { return "PNG/RC4"; } }
        public override string Description { get { return "RC4 encrypted PNG image"; } }
        public override uint     Signature { get { return 0xC4F7F61A; } }

        public Rc4PngFormat ()
        {
            Extensions = new string[] { "a" };
        }

        public static readonly byte[] KnownKey = Encoding.ASCII.GetBytes ("Hlk9D28p");

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            using (var sha = SHA1.Create())
            {
                var key = sha.ComputeHash (KnownKey).Take (16).ToArray();
                using (var proxy = new InputProxyStream (stream, true))
                using (var input = new CryptoStream (proxy, new Rc4Transform (key), CryptoStreamMode.Read))
                {
                    var info = base.ReadMetaData (input);
                    if (null == info)
                        return null;
                    return new Rc4PngMetaData
                    {
                        Width = info.Width,
                        Height = info.Height,
                        OffsetX = info.OffsetX,
                        OffsetY = info.OffsetY,
                        BPP = info.BPP,
                        Key = key,
                    };
                }
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var rc4 = (Rc4PngMetaData)info;
            using (var sha = SHA1.Create())
            using (var proxy = new InputProxyStream (stream, true))
            using (var input = new CryptoStream (proxy, new Rc4Transform (rc4.Key), CryptoStreamMode.Read))
                return base.Read (input, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("Rc4PngFormat.Write not implemented");
        }
    }
}
