//! \file       AudioWV5.cs
//! \date       2018 Jan 21
//! \brief      MAIKA compressed audio format.
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
using GameRes.Utility;

namespace GameRes.Formats.Maika
{
    [Export(typeof(AudioFormat))]
    public class Wv5Audio : AudioFormat
    {
        public override string         Tag { get { return "WV5"; } }
        public override string Description { get { return "MAIKA compressed audio format"; } }
        public override uint     Signature { get { return 0x41355657; } } // 'WV5A'
        public override bool      CanWrite { get { return false; } }

        public Wv5Audio ()
        {
            Extensions = new string[] { "wv5", "wav" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var decoder = new Wv5Decoder (file);
            var pcm_data = decoder.Unpack();
            var pcm = new MemoryStream (pcm_data);
            var sound = new RawPcmInput (pcm, decoder.Format);
            file.Dispose();
            return sound;
        }
    }

    internal class Wv5Decoder
    {
        IBinaryStream   m_input;
        byte[]          m_output;
        int             m_chunk_count;

        public WaveFormat Format { get; private set; }

        public Wv5Decoder (IBinaryStream input)
        {
            m_input = input;
            m_input.Position = 4;
            var format = new WaveFormat {
                FormatTag           = 1,
                Channels            = m_input.ReadUInt16(),
                SamplesPerSecond    = m_input.ReadUInt32(),
                BitsPerSample       = 16,
            };
            format.BlockAlign = (ushort)(2 * format.Channels);
            format.AverageBytesPerSecond = format.BlockAlign * format.SamplesPerSecond;
            this.Format = format;
            int sample_count = m_input.ReadInt32();
            m_chunk_count = m_input.ReadInt32();
            m_output = new byte[sample_count * format.BlockAlign];
        }

        public byte[] Unpack ()
        {
            int chmask = Format.Channels - 1;
            m_input.Position = 0x12;
            var sample = new short[Format.Channels];
            int chn = 0;
            int dst = 0;
            for (int i = 0; i < m_chunk_count; ++i)
            {
                int count;
                byte ctl = m_input.ReadUInt8();
                if (ctl < 2)
                {
                    if (0 == ctl)
                        count = m_input.ReadUInt8();
                    else
                        count = 256;
                    while (count --> 0)
                    {
                        byte s = m_input.ReadUInt8();
                        int n = chn++ & chmask;
                        sample[n] += PcmTable[s];
                        LittleEndian.Pack (sample[n], m_output, dst);
                        dst += 2;
                    }
                }
                else
                {
                    if (3 == ctl)
                        count = m_input.ReadUInt16();
                    else
                        count = ctl;
                    byte s = m_input.ReadUInt8();
                    while (count --> 0)
                    {
                        int n = chn++ & chmask;
                        sample[n] += PcmTable[s];
                        LittleEndian.Pack (sample[n], m_output, dst);
                        dst += 2;
                    }
                }
            }
            return m_output;
        }

        static Wv5Decoder ()
        {
            for (int i = 0; i < 128; ++i)
                PcmTable[128+i] = (short)-PcmTable[i];
        }

        static readonly short[] PcmTable = {
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
            0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
            0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
            0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
            0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
            0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
            0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
            0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
            0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
            0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
            0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
            0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
            0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
            0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
            0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
            0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
            0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
        };
    }
}
