//! \file       AudioTmr.cs
//! \date       Wed Dec 23 16:23:48 2015
//! \brief      Tmr-Hiro WAV audio.
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
using System.Text;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.TmrHiro
{
    [Export(typeof(AudioFormat))]
    public class TmrHiroAudio : AudioFormat
    {
        public override string         Tag { get { return "WAV/TMR-HIRO"; } }
        public override string Description { get { return "Tmr-Hiro wave audio"; } }
        public override uint     Signature { get { return 0; } }

        public TmrHiroAudio ()
        {
            Extensions = new string[] { "" };
        }

        public override SoundInput TryOpen (Stream file)
        {
            if (file.ReadByte() != 0x44)
                return null;
            file.Position = 4;
            if (file.ReadByte() != 0)
                return null;
            int length = ReadInt32 (file);
            if (-1 == length || length != file.Length - 9)
                return null;
            var format = new WaveFormat
            {
                FormatTag                = 1,
                Channels                 = 2,
                SamplesPerSecond         = 44100,
                BitsPerSample            = 16,
                BlockAlign               = 4,
                AverageBytesPerSecond    = 44100*4,
            };
            var pcm = new StreamRegion (file, 9, length);
            return new RawPcmInput (pcm, format);
        }

        private static int ReadInt32 (Stream file)
        {
            int dword = file.ReadByte();
            dword |= file.ReadByte() << 8;
            dword |= file.ReadByte() << 16;
            dword |= file.ReadByte() << 24;
            return dword;
        }
    }
}
