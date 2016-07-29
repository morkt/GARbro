//! \file       AudioOWP.cs
//! \date       Fri Jul 29 11:25:09 2016
//! \brief      RealLive obfuscated OGG files.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Security.Cryptography;

namespace GameRes.Formats.RealLive
{
    [Export(typeof(AudioFormat))]
    public class OwpAudio : AudioFormat
    {
        public override string         Tag { get { return "OWP"; } }
        public override string Description { get { return "RealLive engine obfuscated OGG audio"; } }
        public override uint     Signature { get { return 0x6A5E5E76; } } // 'OggS' ^ 0x39

        public override SoundInput TryOpen (Stream file)
        {
            Stream input = new CryptoStream (file, new XorTransform (0x39), CryptoStreamMode.Read);
            input = new SeekableStream (input);
            return OggAudio.Instance.TryOpen (input);
        }
    }
}
