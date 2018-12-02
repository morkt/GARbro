//! \file       AudioAOG.cs
//! \date       Sun Jan 17 08:38:31 2016
//! \brief      Aoi engine audio format (Ogg/Vorbis).
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

using GameRes.Utility;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Aoi
{
    [Export(typeof(AudioFormat))]
    public class AogAudio : AudioFormat
    {
        public override string         Tag { get { return "AOG"; } }
        public override string Description { get { return "Aoi engine audio format"; } }
        public override uint     Signature { get { return 0x4F696F41; } } // 'AoiO'

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x3C);
            if (!header.AsciiEqual (0, "AoiOgg"))
                return null;
            Stream ogg;
            if (header.AsciiEqual (0x2C, "OggS"))
                ogg = new StreamRegion (file.AsStream, 0x2C);
            else if (header.AsciiEqual (0xC, "Decode") && header.AsciiEqual (0x38, "OggS"))
                ogg = new StreamRegion (file.AsStream, 0x38);
            else
                return null;
            return new OggInput (ogg);
        }
    }
}
