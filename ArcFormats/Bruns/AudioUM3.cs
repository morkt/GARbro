//! \file       AudioUM3.cs
//! \date       Sat Jun 18 11:11:29 2016
//! \brief      UltraMarine3 encrypted OGG vorbis audio.
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

using System;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats
{
    [Export(typeof(AudioFormat))]
    public class Um3Audio : AudioFormat
    {
        public override string         Tag { get { return "UM3"; } }
        public override string Description { get { return "UltraMarine3 audio format (Ogg/Vorbis)"; } }
        public override uint     Signature { get { return 0xAC9898B0; } } // ~'OggS'

        public override SoundInput TryOpen (Stream file)
        {
            return new OggInput (new Um3Stream (file));
        }
    }

    internal class Um3Stream : InputProxyStream
    {
        public Um3Stream (Stream main, bool leave_open = false)
            : base (main, leave_open)
        {
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            var pos = Position;
            int read = BaseStream.Read (buffer, offset, count);
            if (pos < 0x800 && read > 0)
            {
                int enc_count = Math.Min (0x800 - (int)pos, read);
                for (int i = 0; i < enc_count; ++i)
                {
                    buffer[offset+i] ^= 0xFF;
                }
            }
            return read;
        }

        public override int ReadByte ()
        {
            var pos = Position;
            int b = BaseStream.ReadByte();
            if (-1 != b && pos < 0x800)
                b ^= 0xFF;
            return b;
        }
    }
}
