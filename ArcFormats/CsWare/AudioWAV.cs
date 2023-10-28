//! \file       AudioWAV.cs
//! \date       2023 Oct 26
//! \brief      Obscure C's ware WAVE file encoding.
//
// Copyright (C) 2023 by morkt
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

using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;

// [960405][C's Ware] GLO-RI-A ~Kindan no Ketsuzoku~

namespace GameRes.Formats.CsWare
{
    [Export(typeof(AudioFormat))]
    [ExportMetadata("Priority", 1)] // should be tried before generic WAVE format
    public class WavAudio : AudioFormat
    {
        public override string         Tag => "WAV/CSWARE";
        public override string Description => "C's ware encoded audio";
        public override uint     Signature => 0x46464952; // 'RIFF'
        public override bool      CanWrite => false;

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x2E);
            if (header[0x14] != 1 || header[0x15] != 0xFF
                || !header.AsciiEqual (8, "WAVEfmt ")
                || !header.AsciiEqual (0x26, "data"))
                return null;
            var format = new WaveFormat {
                FormatTag = 1,
                Channels = header.ToUInt16 (0x16),
                SamplesPerSecond = header.ToUInt32 (0x18),
                AverageBytesPerSecond = header.ToUInt32 (0x1C) * 2,
                BlockAlign = (ushort)(header.ToUInt16 (0x20) * 2),
                BitsPerSample = 16,
            };
            uint input_size = header.ToUInt32 (0x2A);
            var samples = new byte[input_size * 2];
            Decode (file, samples);
            var decoded = new BinMemoryStream (samples, file.Name);
            file.Dispose();
            return new RawPcmInput (decoded, format);
        }

        void Decode (IBinaryStream input, byte[] output)
        {
            int dst = 0;
            while (input.PeekByte() != -1)
            {
                sbyte sample = input.ReadInt8();
                LittleEndian.Pack (SampleMap[sample + 128], output, dst);
                dst += 2;
            }
        }

        static readonly short[] SampleMap = InitSampleMap();

        static short[] InitSampleMap ()
        {
            var map = new short[256];
            for (int i = 1; i <= 127; ++i)
            {
                map[128 + i] = (short)(Math.Pow (10.0, ((double)i + 44.8637) / 38.0597) - 14.5342);
                map[128 - i] = (short)-map[i + 128];
            }
            map[0] = -0x8000;
            return map;
        }
    }
}
