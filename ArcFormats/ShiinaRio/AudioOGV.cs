//! \file       AudioOGV.cs
//! \date       Sat Apr 18 14:18:47 2015
//! \brief      ShiinaRio Ogg/Vorbis audio format.
//
// Copyright (C) 2015 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.ShiinaRio
{
    [Export(typeof(AudioFormat))]
    public class OgvAudio : OggAudio
    {
        public override string         Tag { get { return "OGV"; } }
        public override string Description { get { return "ShiinaRio audio format (Ogg/Vorbis)"; } }
        public override uint     Signature { get { return 0x0056474f; } } // 'OGV'
        
        public override SoundInput TryOpen (IBinaryStream file)
        {
            file.Position = 0xc;
            var header = new byte[8];
            if (8 != file.Read (header, 0, 8))
                return null;
            if (!Binary.AsciiEqual (header, 0, "fmt "))
                return null;
            uint offset = LittleEndian.ToUInt32 (header, 4);
            file.Seek (offset, SeekOrigin.Current);
            if (8 != file.Read (header, 0, 8))
                return null;
            if (!Binary.AsciiEqual (header, 0, "data"))
                return null;

            var input = new StreamRegion (file.AsStream, file.Position);
            return new OggInput (input);
            // input is left undisposed in case of exception.
        }
    }
}
