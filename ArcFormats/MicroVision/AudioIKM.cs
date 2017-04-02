//! \file       AudioIKM.cs
//! \date       Fri Mar 24 00:01:54 2017
//! \brief      MicroVision audio format (Ogg/vorbis).
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

namespace GameRes.Formats.MicroVision
{
    [Export(typeof(AudioFormat))]
    public class IkmAudio : AudioFormat
    {
        public override string         Tag { get { return "IKM"; } }
        public override string Description { get { return "MicroVision audio format (Ogg/vorbis)"; } }
        public override uint     Signature { get { return 0x004D4B49; } } // 'IKM'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x40);
            uint length = header.ToUInt32 (0x24);
            var ogg = new StreamRegion (file.AsStream, file.Length-length, length);
            return new OggInput (ogg);
        }
    }
}
