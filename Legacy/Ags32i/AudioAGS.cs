//! \file       AudioAGS.cs
//! \date       2018 Mar 28
//! \brief      AGS32i engine encrypted wave audio.
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
using System.IO;

namespace GameRes.Formats.Ags32i
{
    [Export(typeof(AudioFormat))]
    public class AgsAudio : AudioFormat
    {
        public override string         Tag { get { return "WAV/AGS32I"; } }
        public override string Description { get { return "AGS32i engine encrypted wave audio"; } }
        public override uint     Signature { get { return 0x66424047; } }
        public override bool      CanWrite { get { return false; } }

        public AgsAudio ()
        {
            Signatures = new uint[] { 0x66424047, 0 } ;
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            uint key = file.Signature ^ 0x46464952u;
            if (0 == key)
                return null;
            Stream input = new InputCryptoStream (file.AsStream, new Ags32Transform (key));
            input = new SeekableStream (input);
            var header = new byte[12];
            input.Read (header, 0, 12);
            if (!header.AsciiEqual (8, "WAVE"))
                return null;
            input.Position = 0;
            return new WaveInput (input);
        }
    }
}
