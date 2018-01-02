//! \file       AudioDWV.cs
//! \date       2017 Nov 25
//! \brief      SYSD audio file.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.SysD
{
    [Export(typeof(AudioFormat))]
    public class DwvAudio : AudioFormat
    {
        public override string         Tag { get { return "DWV"; } }
        public override string Description { get { return "SYSD engine PCM audio"; } }
        public override uint     Signature { get { return 0x5744; } } // 'DW'
        public override bool      CanWrite { get { return false; } }

        public DwvAudio ()
        {
            Signatures = new uint[] { 0x5744, 0 };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x1C);
            if (!header.AsciiEqual ("DW") || header.ToUInt32 (4) != file.Length)
                return null;
            var format = new WaveFormat {
                FormatTag       = header.ToUInt16 (8),
                Channels        = header.ToUInt16 (0xA),
                SamplesPerSecond = header.ToUInt32 (0xC),
                AverageBytesPerSecond = header.ToUInt32 (0x10),
                BlockAlign      = header.ToUInt16 (0x14),
            };
            format.BitsPerSample = (ushort)(format.AverageBytesPerSecond * 8 / format.SamplesPerSecond / format.Channels);
            uint pcm_size = header.ToUInt32 (0x18);
            var pcm = new StreamRegion (file.AsStream, file.Position, pcm_size);
            return new RawPcmInput (pcm, format);
        }
    }
}
