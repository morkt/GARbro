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

namespace GameRes.Formats.ShiinaRio
{
    [Export(typeof(AudioFormat))]
    public class OgvAudio : OggAudio
    {
        public override string         Tag { get { return "OGV"; } }
        public override string Description { get { return "ShiinaRio audio format (Ogg/Vorbis)"; } }
        public override uint     Signature { get { return 0x0056474f; } } // 'OGV'
        
        public override SoundInput TryOpen (Stream file)
        {
            var input = file as MemoryStream;
            if (null == input || !input.CanWrite)
            {
                input = new MemoryStream();
                file.CopyTo (input);
            }
            input.Position = 1;
            input.WriteByte ((byte)'g');
            input.WriteByte ((byte)'g');
            input.WriteByte ((byte)'S');
            input.Position = 0;
            var ogg = new OggInput (input);
            if (file != input)
                file.Dispose();
            return ogg;
        }
    }
}
