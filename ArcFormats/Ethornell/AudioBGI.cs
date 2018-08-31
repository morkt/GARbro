//! \file       AudioBGI.cs
//! \date       Sat Aug 29 21:44:06 2015
//! \brief      BURIKO engine audio file (Ogg/Vorbis)
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

namespace GameRes.Formats.BGI
{
    [Export(typeof(AudioFormat))]
    public class BgiAudio : AudioFormat
    {
        public override string         Tag { get { return "BW"; } }
        public override string Description { get { return "BGI/Ethornell engine audio (Ogg/Vorbis)"; } }
        public override uint     Signature { get { return 0; } }

        public BgiAudio ()
        {
            Extensions = new string[] { "bw", "", "_bw" };
        }
        
        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            if (!header.AsciiEqual (4, "bw  "))
                return null;
            uint offset = header.ToUInt32 (0);
            if (offset >= file.Length)
                return null;

            var input = new StreamRegion (file.AsStream, offset);
            return new OggInput (input);
            // input is left undisposed in case of exception.
        }
    }
}
