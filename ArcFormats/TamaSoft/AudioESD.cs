//! \file       AudioESD.cs
//! \date       Sat Sep 24 10:43:20 2016
//! \brief      TamaSoft audio format.
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
using GameRes.Utility;

namespace GameRes.Formats.Tama
{
    [Export(typeof(AudioFormat))]
    public class EsdAudio : AudioFormat
    {
        public override string         Tag { get { return "ESD"; } }
        public override string Description { get { return "TamaSoft ADV system audio"; } }
        public override uint     Signature { get { return 0x20445345; } } // 'ESD '

        public override SoundInput TryOpen (Stream file)
        {
            var header = new byte[0x20];
            if (header.Length != file.Read (header, 0, header.Length))
                return null;
            var format = new WaveFormat
            {
                FormatTag = 1,
                Channels = LittleEndian.ToUInt16 (header, 0x10),
                SamplesPerSecond = LittleEndian.ToUInt32 (header, 8),
                BitsPerSample = LittleEndian.ToUInt16 (header, 0xC),
            };
            format.BlockAlign = (ushort)(format.Channels * format.BitsPerSample / 8);
            format.AverageBytesPerSecond = format.SamplesPerSecond * format.BlockAlign;
            var pcm = new StreamRegion (file, 0x20);
            return new RawPcmInput (pcm, format);
        }
    }
}
