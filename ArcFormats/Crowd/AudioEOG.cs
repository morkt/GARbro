//! \file       AudioEOG.cs
//! \date       Thu Jun 11 12:46:35 2015
//! \brief      Crowd engine audio file.
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

using System;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Crowd
{
    [Export(typeof(AudioFormat))]
    public class EogAudio : AudioFormat
    {
        public override string         Tag { get { return "EOG"; } }
        public override string Description { get { return "Crowd engine audio format (Ogg/Vorbis)"; } }
        public override uint     Signature { get { return 0x004D5243; } } // 'CRM'

        public EogAudio ()
        {
            Extensions = new string[] { "eog", "amb" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var ogg = new StreamRegion (file.AsStream, 8);
            return new OggInput (ogg);
            // in case of exception ogg stream is left undisposed
        }

        public override void Write (SoundInput source, Stream output)
        {
            throw new System.NotImplementedException ("EogFormat.Write not implemenented");
        }
    }
}
