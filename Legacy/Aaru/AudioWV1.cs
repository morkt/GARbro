//! \file       AudioWV1.cs
//! \date       2018 Dec 13
//! \brief      Aaru compressed audio format.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;

namespace GameRes.Formats.Aaru
{
    [Export(typeof(AudioFormat))]
    public class Wv1Audio : AudioFormat
    {
        public override string         Tag { get { return "WV1"; } }
        public override string Description { get { return "Aaru compressed audio"; } }
        public override uint     Signature { get { return 0x2E315657; } } // 'WV1.0'
        public override bool      CanWrite { get { return false; } }

        public Wv1Audio ()
        {
            Extensions = new string[] { "wv1", "wav" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x30);
            if (!header.AsciiEqual (0, "WV1.0\0"))
                return null;
            var format = new WaveFormat {
                FormatTag = 1,
                Channels = header.ToUInt16 (0xA),
                SamplesPerSecond = header.ToUInt32 (0xE),
                BitsPerSample = 16,
            };
            format.BlockAlign = (ushort)(format.Channels * format.BitsPerSample / 8);
            format.SetBPS();
            int sample_count = header.ToInt32 (0x26);
            var pcm = new MemoryStream (2 * sample_count);
            using (var output = new BinaryWriter (pcm, Encoding.ASCII, true))
            {
                var l_decoder = new Wv1Decoder();
                var r_decoder = l_decoder;
                if (format.Channels > 1)
                    r_decoder = new Wv1Decoder();
                int input_sample = 0;
                for (int i = 0; i < sample_count; ++i)
                {
                    bool odd_sample = (i & 1) != 0;
                    short sample;
                    if (odd_sample)
                    {
                        sample = r_decoder.DecodeSample (input_sample >> 4);
                    }
                    else
                    {
                        input_sample = file.ReadByte();
                        if (-1 == input_sample)
                            break;
                        sample = l_decoder.DecodeSample (input_sample & 0xF);
                    }
                    output.Write (sample);
                }
            }
            file.Dispose();
            pcm.Position = 0;
            return new RawPcmInput (pcm, format);
        }
    }

    internal class Wv1Decoder
    {
        short   last_sample = 0;
        int     last_index = 0;
        int[]   shift_table = new int[8];

        public short DecodeSample (int input)
        {
            int s0 = SampleTable[last_index];
            shift_table[0] = s0 >> 3;
            shift_table[1] = shift_table[0] + (s0 >> 2);
            shift_table[2] = shift_table[0] + (s0 >> 1);
            shift_table[3] = shift_table[1] + (s0 >> 1);
            shift_table[4] = shift_table[0] + s0;
            shift_table[5] = shift_table[1] + s0;
            shift_table[6] = shift_table[2] + s0;
            shift_table[7] = shift_table[3] + s0;
            int shift_index = input & 7;
            if ((input & 8) != 0)
                last_sample -= (short)shift_table[shift_index];
            else
                last_sample += (short)shift_table[shift_index];
            switch (shift_index)
            {
            case 0:
            case 1:
            case 2:
            case 3:
                if (last_index > 0)
                    --last_index;
                break;
            case 4:
                last_index = Math.Min (last_index + 2, 0x7F);
                break;
            case 5:
                last_index = Math.Min (last_index + 4, 0x7F);
                break;
            case 6:
                last_index = Math.Min (last_index + 6, 0x7F);
                break;
            case 7:
                last_index = Math.Min (last_index + 8, 0x7F);
                break;
            default:
                break;
            }
            return (short)(2 * last_sample);
        }

        static readonly short[] SampleTable = {
            0x0007, 0x0008, 0x0009, 0x000A, 0x000B, 0x000C, 0x000E, 0x000F,
            0x0010, 0x0012, 0x0014, 0x0015, 0x0017, 0x0019, 0x001B, 0x001D,
            0x0020, 0x0022, 0x0025, 0x0028, 0x002B, 0x002E, 0x0031, 0x0035,
            0x0038, 0x003C, 0x0041, 0x0045, 0x004A, 0x004F, 0x0055, 0x005A,
            0x0061, 0x0067, 0x006E, 0x0075, 0x007D, 0x0085, 0x008E, 0x0098,
            0x00A2, 0x00AC, 0x00B7, 0x00C3, 0x00D0, 0x00DE, 0x00EC, 0x00FB,
            0x010B, 0x011C, 0x012F, 0x0142, 0x0157, 0x016D, 0x0184, 0x019C,
            0x01B7, 0x01D3, 0x01F0, 0x0210, 0x0231, 0x0254, 0x027A, 0x02A2,
            0x02CD, 0x02FA, 0x032A, 0x035D, 0x0393, 0x03CD, 0x040A, 0x044B,
            0x0490, 0x04D9, 0x0527, 0x057A, 0x05D2, 0x062F, 0x0693, 0x06FC,
            0x076C, 0x07E3, 0x0862, 0x08E8, 0x0977, 0x0A0E, 0x0AAF, 0x0B5A,
            0x0C10, 0x0CD1, 0x0D9E, 0x0E78, 0x0F60, 0x1056, 0x115B, 0x1270,
            0x1397, 0x14D1, 0x161D, 0x177F, 0x18F7, 0x1A86, 0x1C2E, 0x1DF0,
            0x1FCF, 0x21CB, 0x23E7, 0x2625, 0x2886, 0x2B0E, 0x2DBE, 0x3098,
            0x33A1, 0x36D9, 0x3A46, 0x3DE9, 0x41C5, 0x45E0, 0x4A3C, 0x4EDE,
            0x53CA, 0x5904, 0x5E92, 0x6478, 0x6ABC, 0x7165, 0x7878, 0x7FFF,
        };
    }
}
