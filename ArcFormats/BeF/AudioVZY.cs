//! \file       AudioVZY.cs
//! \date       2017 Oct 22
//! \brief      Obfuscated WAVE file.
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
using System.IO;

namespace GameRes.Formats.BeF
{
    [Export(typeof(AudioFormat))]
    public class VzyAudio : AudioFormat
    {
        public override string         Tag { get { return "VZY"; } }
        public override string Description { get { return "Obfuscated WAVE audio"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x12);
            if (!header.AsciiEqual ("\0\0\0\0"))
                return null;
            int riff_length = header.ToInt32 (4);
            if (file.Length != riff_length+8)
                return null;
            if (!header.AsciiEqual (8, "\0\0\0\0\0\0\0\0"))
                return null;
            int header_length = header.ToUInt16 (0x10);
            if (header_length < 0x10 || header_length > riff_length)
                return null;
            header = file.ReadHeader (0x18+header_length);
            if (!header.AsciiEqual (0x14+header_length, "data"))
                return null;
            var header_bytes = new byte[0x10] {
                (byte)'R', (byte)'I', (byte)'F', (byte)'F', header[4], header[5], header[6], header[7],
                (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' '
            };
            Stream riff = new StreamRegion (file.AsStream, 0x10);
            riff = new PrefixStream (header_bytes, riff);
            var wav = new BinaryStream (riff, file.Name);
            try
            {
                return Wav.TryOpen (wav);
            }
            catch
            {
                wav.Dispose();
                throw;
            }
        }
    }
}
