//! \file       AudioKOG.cs
//! \date       2018 Aug 27
//! \brief      SVIU System audio (OGG)
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

using System;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Sviu
{
    [Export(typeof(AudioFormat))]
    public class KogAudio : AudioFormat
    {
        public override string         Tag { get { return "KOG"; } }
        public override string Description { get { return "SVIU System audio format (Ogg/Vorbis)"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            if (file.Signature != 0)
                return null;
            var header = file.ReadHeader (8);
            int header_size = header.ToInt32 (4);
            file.Position = header_size;
            uint signature = file.ReadUInt32();
            if (signature != OggAudio.Instance.Signature)
                return null;
            var ogg = new StreamRegion (file.AsStream, header_size);
            return new OggInput (ogg);
        }
    }
}
