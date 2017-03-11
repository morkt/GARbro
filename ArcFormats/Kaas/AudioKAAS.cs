//! \file       AudioKAAS.cs
//! \date       Fri Mar 10 18:46:22 2017
//! \brief      KAAS engine PCM audio format.
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

namespace GameRes.Formats.KAAS
{
    [Export(typeof(AudioFormat))]
    public class KaasAudio : AudioFormat
    {
        public override string         Tag { get { return "PCM/KAAS"; } }
        public override string Description { get { return "KAAS engine audio format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public KaasAudio ()
        {
            Extensions = new string[] { "" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            var length = header.ToInt32 (0);
            if (header.ToUInt16 (4) != 0x800 || file.Length != length + 0x10
                || header.ToUInt32 (0xC) != 0x84BE2329)
                return null;
            ushort channels = header.ToUInt16 (6);
            if (channels > 1)
                return null;
            ++channels;
            var format = new WaveFormat {
                FormatTag       = 1,
                Channels        = channels,
                SamplesPerSecond = header.ToUInt32 (8),
                BlockAlign      = (ushort)(2 * channels),
                BitsPerSample   = 16,
            };
            format.SetBPS();
            var pcm = new MemoryStream (length * 2);
            using (var pcm_writer = new BinaryWriter (pcm, System.Text.Encoding.Default, true))
            {
                for (int i = 0; i < length; ++i)
                {
                    ushort sample = SampleTable[file.ReadUInt8()];
                    pcm_writer.Write (sample);
                }
            }
            file.Dispose();
            pcm.Position = 0;
            return new RawPcmInput (pcm, format);
        }

        static readonly ushort[] SampleTable = {
            0x0000, 0x0001, 0x0007, 0x0011, 0x001F, 0x0031, 0x0047, 0x0061,
            0x007E, 0x00A0, 0x00C6, 0x00EF, 0x011D, 0x014E, 0x0184, 0x01BD,
            0x01FA, 0x023C, 0x0281, 0x02CA, 0x0318, 0x0369, 0x03BE, 0x0417,
            0x0474, 0x04D5, 0x053A, 0x05A3, 0x0610, 0x0681, 0x06F6, 0x076E,
            0x07EB, 0x086C, 0x08F0, 0x0979, 0x0A06, 0x0A96, 0x0B2B, 0x0BC3,
            0x0C60, 0x0D00, 0x0DA4, 0x0E4D, 0x0EF9, 0x0FA9, 0x105D, 0x1115,
            0x11D1, 0x1291, 0x1356, 0x141D, 0x14E9, 0x15B9, 0x168D, 0x1765,
            0x1841, 0x1921, 0x1A04, 0x1AEC, 0x1BD8, 0x1CC7, 0x1DBB, 0x1EB2,
            0x1FAE, 0x20AD, 0x21B0, 0x22B8, 0x23C3, 0x24D2, 0x25E6, 0x26FD,
            0x2818, 0x2937, 0x2A5A, 0x2B81, 0x2CAC, 0x2DDB, 0x2F0E, 0x3045,
            0x3180, 0x32BE, 0x3401, 0x3548, 0x3692, 0x37E1, 0x3934, 0x3A8A,
            0x3BE5, 0x3D43, 0x3EA6, 0x400C, 0x4176, 0x42E5, 0x4457, 0x45CD,
            0x4747, 0x48C5, 0x4A47, 0x4BCD, 0x4D58, 0x4EE5, 0x5077, 0x520D,
            0x53A7, 0x5545, 0x56E7, 0x588D, 0x5A36, 0x5BE4, 0x5D96, 0x5F4B,
            0x6105, 0x62C2, 0x6484, 0x6649, 0x6812, 0x69E0, 0x6BB1, 0x6D86,
            0x6F60, 0x713D, 0x731E, 0x7503, 0x76EC, 0x78D9, 0x7ACA, 0x7CBF,
            0xFFFF, 0xFFFE, 0xFFF8, 0xFFEE, 0xFFE0, 0xFFCE, 0xFFB8, 0xFF9E,
            0xFF81, 0xFF5F, 0xFF39, 0xFF10, 0xFEE2, 0xFEB1, 0xFE7B, 0xFE42,
            0xFE05, 0xFDC3, 0xFD7E, 0xFD35, 0xFCE7, 0xFC96, 0xFC41, 0xFBE8,
            0xFB8B, 0xFB2A, 0xFAC5, 0xFA5C, 0xF9EF, 0xF97E, 0xF909, 0xF891,
            0xF814, 0xF793, 0xF70F, 0xF686, 0xF5F9, 0xF569, 0xF4D4, 0xF43C,
            0xF39F, 0xF2FF, 0xF25B, 0xF1B2, 0xF106, 0xF056, 0xEFA2, 0xEEEA,
            0xEE2E, 0xED6E, 0xECA9, 0xEBE2, 0xEB16, 0xEA46, 0xE972, 0xE89A,
            0xE7BE, 0xE6DE, 0xE5FB, 0xE513, 0xE427, 0xE338, 0xE244, 0xE14D,
            0xE051, 0xDF52, 0xDE4F, 0xDD47, 0xDC3C, 0xDB2D, 0xDA19, 0xD902,
            0xD7E7, 0xD6C8, 0xD5A5, 0xD47E, 0xD353, 0xD224, 0xD0F1, 0xCFBA,
            0xCE7F, 0xCD41, 0xCBFE, 0xCAB7, 0xC96D, 0xC81E, 0xC6CB, 0xC575,
            0xC41A, 0xC2BC, 0xC159, 0xBFF3, 0xBE89, 0xBD1A, 0xBBA8, 0xBA32,
            0xB8B8, 0xB73A, 0xB5B8, 0xB432, 0xB2A7, 0xB11A, 0xAF88, 0xADF2,
            0xAC58, 0xAABA, 0xA918, 0xA772, 0xA5C9, 0xA41B, 0xA269, 0xA0B4,
            0x9EFA, 0x9D3D, 0x9B7B, 0x99B6, 0x97ED, 0x961F, 0x944E, 0x9279,
            0x909F, 0x8EC2, 0x8CE1, 0x8AFC, 0x8913, 0x8726, 0x8535, 0x8340,
        };
    }
}
