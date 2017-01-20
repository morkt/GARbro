//! \file       AudioVMD.cs
//! \date       Fri Jan 20 08:35:26 2017
//! \brief      C4 engine obfuscated MP3 audio.
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

using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.C4
{
    [Export(typeof(AudioFormat))]
    public class VmdAudio : AudioFormat
    {
        public override string         Tag { get { return "VMD"; } }
        public override string Description { get { return "C4 engine MP3 audio"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        const byte Key = 0xE5;

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (3);
            if (0xFF != (header[0] ^ Key) || 0xE2 != ((header[1] ^ Key) & 0xE6) ||
                0xF0 == ((header[2] ^ Key) & 0xF0))
                return null;
            file.Position = 0;
            var input = new XoredStream (file.AsStream, Key);
            return new Mp3Input (input);
        }
    }
}
