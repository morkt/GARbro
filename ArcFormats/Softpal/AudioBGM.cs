//! \file       AudioBGM.cs
//! \date       Sat Feb 13 12:57:50 2016
//! \brief      Audio container used in Softpal PAC archives.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Softpal
{
    [Export(typeof(AudioFormat))]
    public class BgmAudio : AudioFormat
    {
        public override string         Tag { get { return "BGM/SOFTPAL"; } }
        public override string Description { get { return "Softpal BGM format (Ogg/Vorbis)"; } }
        public override uint     Signature { get { return 0x204D4742; } } // 'BGM '

        public BgmAudio ()
        {
            Extensions = new string[] { "ogg" };
        }
        
        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10); // header contains music loop timing
            if (!header.AsciiEqual (0xC, "OggS"))
                return null;
            var input = new StreamRegion (file.AsStream, 12);
            return new OggInput (input);
            // input is [intentionally] left undisposed in case of exception.
        }
    }
}
