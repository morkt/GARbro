//! \file       AudioADP.cs
//! \date       2017 Dec 29
//! \brief      CrossNet ADPCM-compressed audio.
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
using GameRes.Utility;

namespace GameRes.Formats.CrossNet
{
    [Export(typeof(AudioFormat))]
    public class AdpAudio : AudioFormat
    {
        public override string         Tag { get { return "ADP/CROSSNET"; } }
        public override string Description { get { return "CrossNet ADPCM-compressed audio"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            if (file.Signature != Wav.Signature || !file.Name.HasExtension (".adp"))
                return null;

            var header = file.ReadHeader (0x24);
            if (!header.AsciiEqual (8, "WAVEfmt "))
                return null;
            uint fmt_size = header.ToUInt32 (0x10);
            int codec = header.ToUInt16 (0x14);
            if (0xFFFF != codec)
                return null;

            uint sample_rate = header.ToUInt32 (0x18);
            ushort channels = header.ToUInt16 (0x16);
            if (channels != 1 && channels != 2)
                return null;

            int shift = 2;
            uint section_offset = 0x14 + fmt_size;
            uint section_size;
            for (;;)
            {
                file.Position = section_offset;
                uint section_id = file.ReadUInt32();
                section_size = file.ReadUInt32();
                if (section_id == 0x61746164) // 'data'
                    break;
                if (section_id == 0x74666873) // 'shft'
                    shift = file.ReadInt32();
                section_offset += 8 + ((section_size + 1u) & ~1u);
            }
            int samples = (int)(2 * section_size / channels);
            if (shift < 0)
                shift = 2;
            var output = new byte[samples * 2 * channels];
            var first = new AdpDecoder (shift);
            int dst = 0;
            if (1 == channels)
            {
                while (samples > 0)
                {
                    byte v = file.ReadUInt8();
                    LittleEndian.Pack (first.DecodeSample (v), output, dst);
                    LittleEndian.Pack (first.DecodeSample (v >> 4), output, dst+2);
                    dst += 4;
                    samples -= 2;
                }
            }
            else
            {
                var second = new AdpDecoder (shift);
                while (samples > 0)
                {
                    byte v = file.ReadUInt8();
                    LittleEndian.Pack (first.DecodeSample (v), output, dst);
                    LittleEndian.Pack (first.DecodeSample (v >> 4), output, dst+4);
                    v = file.ReadUInt8();
                    LittleEndian.Pack (second.DecodeSample (v), output, dst+2);
                    LittleEndian.Pack (second.DecodeSample (v >> 4), output, dst+6);
                    dst += 8;
                    samples -= 2;
                }
            }
            var format = new WaveFormat();
            format.FormatTag        = 1;
            format.Channels         = channels;
            format.SamplesPerSecond = sample_rate;
            format.AverageBytesPerSecond = 2u * channels * sample_rate;
            format.BlockAlign       = (ushort)(2 * channels);
            format.BitsPerSample    = 0x10;
            var pcm = new MemoryStream (output);
            var sound = new RawPcmInput (pcm, format);
            file.Dispose();
            return sound;
        }
    }

    internal class AdpDecoder
    {
        int prev_sample = 0;
        int quant_idx = 0;
        int shift;

        public AdpDecoder (int shift = 2)
        {
            this.shift = shift;
        }

        public short DecodeSample (int code)
        {
            code &= 0xF;
            int sample = (ScaleTable[code] * QuantizeTable[quant_idx] << shift) + prev_sample;
            if (sample < -32768 )
                sample = -32768;
            else if (sample > 0x7FFF)
                sample = 0x7FFF;
            quant_idx += IncrementTable[code];
            if (quant_idx < 0)
                quant_idx = 0;
            else if (quant_idx > 48)
                quant_idx = 48;
            prev_sample = sample;
            return (short)sample;
        }

        static readonly ushort[] QuantizeTable = {
            0x010, 0x011, 0x013, 0x015, 0x017, 0x019, 0x01C, 0x01F, 0x022, 0x025, 0x029, 0x02D, 0x032, 0x037,
            0x03C, 0x042, 0x049, 0x050, 0x058, 0x061, 0x06B, 0x076, 0x082, 0x08F, 0x09D, 0x0AD, 0x0BE, 0x0D1,
            0x0E6, 0x0FD, 0x117, 0x133, 0x151, 0x173, 0x198, 0x1C1, 0x1EE, 0x220, 0x256, 0x292, 0x2D4, 0x31C,
            0x36C, 0x3C3, 0x424, 0x48E, 0x502, 0x583, 0x610
        };
        static readonly sbyte[] ScaleTable = { 1, 3, 5, 7, 9, 11, 13, 15, -1, -3, -5, -7, -9, -11, -13, -15 };
        static readonly sbyte[] IncrementTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };
    }
}
