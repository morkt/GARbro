//! \file       AudioWPN.cs
//! \date       Mon Aug 10 09:22:18 2015
//! \brief      Wild Bug audio format.
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
using GameRes.Utility;

namespace GameRes.Formats.WildBug
{
    [Export(typeof(AudioFormat))]
    public class WpnAudio : AudioFormat
    {
        public override string         Tag { get { return "WPN"; } }
        public override string Description { get { return "Wild Bug's audio format"; } }
        public override uint     Signature { get { return 0x1A444257; } } // 'WBD'
        
        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x24);
            if (!header.AsciiEqual (4, "WAV"))
                return null;
            if (header.ToInt32 (8) < 2)
                return null;
            int fmt_offset = header.ToInt32 (0x10);
            int fmt_size   = header.ToInt32 (0x14);
            int data_offset = header.ToInt32 (0x1C);
            int data_size   = header.ToInt32 (0x20);

            var wav_header = new byte[8+12+fmt_size+8];
            Buffer.BlockCopy (WavHeader, 0, wav_header, 0, WavHeader.Length);
            LittleEndian.Pack (wav_header.Length-8+data_size, wav_header, 4);
            LittleEndian.Pack (fmt_size, wav_header, 0x10);
            file.Position = fmt_offset;
            file.Read (wav_header, 0x14, fmt_size);
            int d = wav_header.Length-8;
            wav_header[d++]= (byte)'d';
            wav_header[d++]= (byte)'a';
            wav_header[d++]= (byte)'t';
            wav_header[d++]= (byte)'a';
            LittleEndian.Pack (data_size, wav_header, d);
            var wav_data = new StreamRegion (file.AsStream, data_offset, data_size);
            return new WaveInput (new PrefixStream (wav_header, wav_data));
        }

        static readonly byte[] WavHeader = new byte[] {
            (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0,
            (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', 0x20,
        };

        public override void Write (SoundInput source, Stream output)
        {
            throw new System.NotImplementedException ("WpnFormat.Write not implemenented");
        }
    }
}
