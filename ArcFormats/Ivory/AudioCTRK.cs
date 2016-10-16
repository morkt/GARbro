//! \file       AudioCTRK.cs
//! \date       Mon Sep 12 13:17:22 2016
//! \brief      cTRK audio format.
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

namespace GameRes.Formats.Ivory
{
    [Export(typeof(AudioFormat))]
    public class PxAudio : AudioFormat
    {
        public override string         Tag { get { return "PX/cTRK"; } }
        public override string Description { get { return "Ivory audio format"; } }
        public override uint     Signature { get { return 0x20585066; } } // 'fPX '

        public PxAudio ()
        {
            Extensions = new string[] { "px", "trk" };
            Signatures = new uint[] { 0x20585066, 0x4B525463 };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = new byte[0x24];
            if (8 != file.Read (header, 0, 8))
                return null;
            int start_offset = 0;
            if (Binary.AsciiEqual (header, "fPX "))
            {
                start_offset = 8;
                file.Read (header, 0, 0x24);
            }
            else
            {
                file.Read (header, 8, 0x1C);
            }
            if (!Binary.AsciiEqual (header, "cTRK"))
                return null;
            int header_length = LittleEndian.ToInt32 (header, 0xC);
            if (header_length < 0x20)
                return null;
            int data_length = LittleEndian.ToInt32 (header, 4) - header_length;
            start_offset += header_length;
            int type = LittleEndian.ToUInt16 (header, 0x1E);
            if (0 == type)
            {
                var format = new WaveFormat
                {
                    FormatTag           = 1,
                    Channels            = LittleEndian.ToUInt16 (header, 0x1A),
                    SamplesPerSecond    = LittleEndian.ToUInt32 (header, 0x14),
                    BitsPerSample       = LittleEndian.ToUInt16 (header, 0x1C),
                };
                format.BlockAlign = (ushort)(format.BitsPerSample * format.Channels / 8);
                format.AverageBytesPerSecond = format.SamplesPerSecond * format.BlockAlign;
                var input = new StreamRegion (file.AsStream, start_offset, data_length);
                return new RawPcmInput (input, format);
            }
            else if (2 == type)
            {
                using (var decoder = new TrkDecoder (file.AsStream, header, start_offset, data_length))
                {
                    var pcm = decoder.Decode();
                    return new RawPcmInput (new MemoryStream (pcm), decoder.Format);
                }
            }
            else if (3 == type)
            {
                var input = new StreamRegion (file.AsStream, start_offset, data_length);
                return new OggInput (input);
            }
            else
                throw new InvalidFormatException (string.Format ("Unknown cTRK format ({0})", type));
        }
    }

    internal sealed class TrkDecoder : IDisposable
    {
        MsbBitStream    m_input;
        WaveFormat      m_format;
        byte[]          m_output;
        int             m_start;
        int             m_end;
        int             m_sample_count;

        public WaveFormat Format { get { return m_format; } }
        public byte[]       Data { get { return m_output; } }

        public TrkDecoder (Stream input, byte[] header, int start_offset, int data_length)
        {
            m_format.FormatTag          = 1;
            m_format.Channels           = LittleEndian.ToUInt16 (header, 0x1A);
            m_format.SamplesPerSecond   = LittleEndian.ToUInt32 (header, 0x14);
            m_format.BitsPerSample      = 16;
            m_format.BlockAlign         = (ushort)(2 * m_format.Channels);
            m_format.AverageBytesPerSecond = m_format.SamplesPerSecond * m_format.BlockAlign;
            m_sample_count = LittleEndian.ToInt32 (header, 0x10);
            m_input = new MsbBitStream (input, true);
            m_start = start_offset;
            m_end = start_offset + data_length;
            m_output = new byte[m_sample_count * m_format.BlockAlign];
            ref_sample = new int[m_format.Channels];
        }

        int[] ref_sample;

        public byte[] Decode () // decode_audio_420170
        {
            int dst_block = 0;
            int step = m_format.BlockAlign;
            m_input.Input.Position = m_start;
            int remaining = m_sample_count;
            while (remaining > 0)
            {
                int block_count = Math.Min (remaining, 28);
                remaining -= block_count;
                for (int c = 0; c < m_format.Channels; ++c)
                {
                    int sample = ref_sample[c];
                    int first = m_input.GetBits (8);
                    int shift = m_input.GetBits (4);
                    int first_shift = m_input.GetBits (4);
                    int diff = first << ((first_shift & 7) + 1);
                    if (0 != (first_shift & 8))
                    {
                        sample -= diff;
                        if (sample < -32768)
                            sample = -32768;
                    }
                    else
                    {
                        sample += diff;
                        if (sample > 0x7FFF)
                            sample = 0x7FFF;
                    }
                    int dst = dst_block + 2 * c;
                    for (int i = 0; i < block_count; ++i)
                    {
                        int bits = m_input.GetBits (4);
                        if (-1 == bits)
                            throw new EndOfStreamException();
                        if (0 != (bits & 8))
                        {
                            sample -= (((bits & 7 ^ 7) + 1) & 0xFF) << shift;
                            if (sample < -32768)
                                sample = -32768;
                        }
                        else
                        {
                            sample += (bits & 7) << shift;
                            if (sample > 0x7FFF)
                                sample = 0x7FFF;
                        }
                        LittleEndian.Pack ((ushort)sample, m_output, dst);
                        dst += step;
                    }
                    ref_sample[c] = sample;
                }
                dst_block += block_count * step;
            }
            return m_output;
        }

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }
}
