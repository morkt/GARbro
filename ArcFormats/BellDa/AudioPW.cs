//! \file       AudioPW.cs
//! \date       2017 Nov 24
//! \brief      Bell-Da compressed WAVE file.
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

namespace GameRes.Formats.BellDa
{
    [Export(typeof(AudioFormat))]
    public class PwAudio : AudioFormat
    {
        public override string         Tag { get { return "WAV/PW"; } }
        public override string Description { get { return "BELL-DA compressed WAVE audio"; } }
        public override uint     Signature { get { return 0x30315750; } } // 'PW10'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            if (!header.AsciiEqual (0xC, "fmt "))
                return null;
            int fmt_size = header.ToInt32 (0x10);
            header = file.ReadHeader (0x14+fmt_size+8);
            if (!header.AsciiEqual (0x14+fmt_size, "data"))
                return null;
            int pcm_size = header.ToInt32 (0x18+fmt_size);
            var pcm_data = new byte[pcm_size*2];
            for (int i = 0; i < pcm_data.Length; i += 2)
            {
                LittleEndian.Pack (PcmTable[file.ReadUInt8()], pcm_data, i);
            }
            var format = new WaveFormat {
                FormatTag       = header.ToUInt16 (0x14),
                Channels        = header.ToUInt16 (0x16),
                SamplesPerSecond = header.ToUInt32 (0x18),
                BlockAlign      = (ushort)(header.ToUInt16 (0x20) * 2),
                BitsPerSample   = 16,
            };
            format.SetBPS();
            var pcm = new MemoryStream (pcm_data);
            file.Dispose();
            return new RawPcmInput (pcm, format);
        }

        static readonly ushort[] PcmTable = {
            0x8000, 0x8001, 0x8786, 0x8E99, 0x9542, 0x9B87, 0xA16E, 0xA6FC, 
            0xAC37, 0xB123, 0xB5C5, 0xBA22, 0xBE3C, 0xC21A, 0xC5BD, 0xC929, 
            0xCC62, 0xCF6B, 0xD246, 0xD4F6, 0xD77E, 0xD9DF, 0xDC1D, 0xDE39, 
            0xE036, 0xE215, 0xE3D7, 0xE57F, 0xE70E, 0xE886, 0xE9E8, 0xEB35, 
            0xEC6E, 0xED95, 0xEEAB, 0xEFB0, 0xF0A6, 0xF18E, 0xF268, 0xF335, 
            0xF3F6, 0xF4AC, 0xF557, 0xF5F8, 0xF690, 0xF71F, 0xF7A5, 0xF823, 
            0xF89A, 0xF90A, 0xF974, 0xF9D7, 0xFA35, 0xFA8D, 0xFADF, 0xFB2D, 
            0xFB77, 0xFBBC, 0xFBFD, 0xFC3A, 0xFC74, 0xFCAA, 0xFCDD, 0xFD0D, 
            0xFD3A, 0xFD65, 0xFD8D, 0xFDB2, 0xFDD6, 0xFDF7, 0xFE17, 0xFE34, 
            0xFE50, 0xFE6A, 0xFE83, 0xFE9A, 0xFEB0, 0xFEC5, 0xFED8, 0xFEEA, 
            0xFEFC, 0xFF0C, 0xFF1B, 0xFF29, 0xFF37, 0xFF44, 0xFF4F, 0xFF5B, 
            0xFF65, 0xFF6F, 0xFF79, 0xFF81, 0xFF8A, 0xFF92, 0xFF99, 0xFFA0, 
            0xFFA6, 0xFFAD, 0xFFB2, 0xFFB8, 0xFFBD, 0xFFC2, 0xFFC6, 0xFFCB, 
            0xFFCF, 0xFFD2, 0xFFD6, 0xFFD9, 0xFFDC, 0xFFDF, 0xFFE2, 0xFFE5, 
            0xFFE7, 0xFFEA, 0xFFEC, 0xFFEE, 0xFFF0, 0xFFF2, 0xFFF3, 0xFFF5, 
            0xFFF7, 0xFFF8, 0xFFF9, 0xFFFB, 0xFFFC, 0xFFFD, 0xFFFE, 0xFFFF, 
            0x0000, 0x0001, 0x0002, 0x0003, 0x0004, 0x0005, 0x0007, 0x0008, 
            0x0009, 0x000B, 0x000D, 0x000E, 0x0010, 0x0012, 0x0014, 0x0016, 
            0x0019, 0x001B, 0x001E, 0x0021, 0x0024, 0x0027, 0x002A, 0x002E, 
            0x0031, 0x0035, 0x003A, 0x003E, 0x0043, 0x0048, 0x004E, 0x0053, 
            0x005A, 0x0060, 0x0067, 0x006E, 0x0076, 0x007F, 0x0087, 0x0091, 
            0x009B, 0x00A5, 0x00B1, 0x00BC, 0x00C9, 0x00D7, 0x00E5, 0x00F4, 
            0x0104, 0x0116, 0x0128, 0x013B, 0x0150, 0x0166, 0x017D, 0x0196, 
            0x01B0, 0x01CC, 0x01E9, 0x0209, 0x022A, 0x024E, 0x0273, 0x029B, 
            0x02C6, 0x02F3, 0x0323, 0x0356, 0x038C, 0x03C6, 0x0403, 0x0444, 
            0x0489, 0x04D3, 0x0521, 0x0573, 0x05CB, 0x0629, 0x068C, 0x06F6, 
            0x0766, 0x07DD, 0x085B, 0x08E1, 0x0970, 0x0A08, 0x0AA9, 0x0B54, 
            0x0C0A, 0x0CCB, 0x0D98, 0x0E72, 0x0F5A, 0x1050, 0x1155, 0x126B, 
            0x1392, 0x14CB, 0x1618, 0x177A, 0x18F2, 0x1A81, 0x1C29, 0x1DEB, 
            0x1FCA, 0x21C7, 0x23E3, 0x2621, 0x2882, 0x2B0A, 0x2DBA, 0x3095, 
            0x339E, 0x36D7, 0x3A43, 0x3DE6, 0x41C4, 0x45DE, 0x4A3B, 0x4EDD, 
            0x53C9, 0x5904, 0x5E92, 0x6479, 0x6ABE, 0x7167, 0x787A, 0x7FFF, 
        };
    }
}
