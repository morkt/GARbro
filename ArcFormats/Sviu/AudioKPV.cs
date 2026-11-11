//! \file       AudioKPV.cs
//! \date       2026-09-08
//! \brief      SVIU System audio file.
//
// Copyright (C) 2026 by morkt
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

namespace GameRes.Formats.Sviu
{
    [Export(typeof(AudioFormat))]
    public class KpvAudio : AudioFormat
    {
        public override string         Tag { get { return "KPV"; } }
        public override string Description { get { return "SVIU System audio format"; } }
        public override uint     Signature { get { return 0x3056504B; } } // 'KPV0'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            using (var reader = new KpvDecoder (file))
            {
                reader.Unpack();
                var input = new MemoryStream (reader.Data);
                var sound = new RawPcmInput (input, reader.Format);
                file.Dispose();
                return sound;
            }
        }
    }

    internal class KpvDecoder : IDisposable
    {
        private IBinaryStream m_input;
        private uint          m_header_size;
        private int           m_method;

        protected WaveFormat  m_format;
        protected byte[]      m_output;

        public byte[]       Data { get { return m_output; } }
        public WaveFormat Format { get { return m_format; } }

        public KpvDecoder (IBinaryStream input)
        {
            var header = input.ReadHeader (0x24);
            m_header_size = header.ToUInt32 (4);
            m_method = header.ToInt32 (8);
            m_output = new byte[header.ToInt32 (0x10)];
            m_format = new WaveFormat {
                FormatTag             = 1,
                Channels              = header.ToUInt16 (0x16),
                SamplesPerSecond      = header.ToUInt32 (0x18),
                AverageBytesPerSecond = header.ToUInt32 (0x1C),
                BlockAlign            = header.ToUInt16 (0x20),
                BitsPerSample         = header.ToUInt16 (0x22),
            };
            m_input = input;
        }

        public void Unpack ()
        {
            switch (m_method)
            {
            case 0: UnpackRaw(); break;
            case 4: UnpackV4(); break;
            case 10:
            case 11: UnpackV10(); break;
            default: throw new NotSupportedException();
            }
        }

        void UnpackRaw ()
        {
            throw new NotImplementedException();
            /*
            m_input.Position = m_header_size;
            m_input.Read (output, 0, output.Length);
            */
        }

        void UnpackV4 ()
        {
            m_input.Position = 0x2A;
            var coef_table = new short[4];
            for (int i = 0; i < 4; i++)
                coef_table[i] = (short)m_input.ReadUInt32();

            m_input.Position = m_header_size;
            int dst = 0;
            var pred = new int[m_format.Channels];
            while (dst < m_output.Length)
            {
                for (int c = 0; c < m_format.Channels; c++)
                {
                    int b = m_input.ReadByte();
                    if (b == -1)
                        break;
                    int coef_idx = b >> 6;
                    int diff = (b & 0x3F) - ((b & 0x20) != 0 ? 64 : 0);
                    pred[c] += diff * coef_table[coef_idx];
                    short sample = Clamp (pred[c] * 4);
                    LittleEndian.Pack (sample, m_output, dst);
                    dst += 2;
                }
            }

            m_format.BlockAlign = (ushort)(m_format.Channels * m_format.BitsPerSample / 8);
            m_format.SetBPS();
        }

        void UnpackV10 ()
        {
            throw new NotImplementedException();
        }

        internal static short Clamp (int sample)
        {
            if (sample > 0x7FFF)
                return 0x7FFF;
            else if (sample < -0x8000)
                return -0x8000;
            else
                return (short)sample;
        }

        #region IDisposable Members
        public void Dispose ()
        {
            Dispose (true);
        }

        protected virtual void Dispose (bool disposing)
        {
        }
        #endregion
    }
}
