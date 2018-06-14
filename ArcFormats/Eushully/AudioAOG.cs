//! \file       AudioAOG.cs
//! \date       2018 Jun 13
//! \brief      System3 engine audio format.
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

using System.ComponentModel.Composition;

namespace GameRes.Formats.Eushully
{
    [Export(typeof(AudioFormat))]
    public class AogAudio : AudioFormat
    {
        public override string         Tag { get { return "AOG/SYS3"; } }
        public override string Description { get { return "System3 engine audio format"; } }
        public override uint     Signature { get { return 0x47474F41; } } // 'AOGG'

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            if (!header.AsciiEqual (0x14, "OggS"))
                return null;
            var ogg = new StreamRegion (file.AsStream, 0x14);
            return new OggInput (ogg);
        }
    }
}
