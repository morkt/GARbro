//! \file       AudioNWA.cs
//! \date       Mon Apr 18 15:10:59 2016
//! \brief      RealLive audio format.
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

namespace GameRes.Formats.RealLive
{
    internal class NwaMetaData
    {
        public WaveFormat Format;
        public int      Compression;
        public bool     RunLengthEncoded;
        public int      BlockCount;
        public int      PcmSize;
        public int      PackedSize;
        public int      SampleCount;
        public int      BlockSize;
        public int      FinalBlockSize;
    }

    [Export(typeof(AudioFormat))]
    public class NwaAudio : AudioFormat
    {
        public override string         Tag { get { return "NWA"; } }
        public override string Description { get { return "RealLive engine audio format"; } }
        public override uint     Signature { get { return 0; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x28);
            ushort channels = header.ToUInt16 (0);
            if (0 == channels || channels > 2)
                return null;
            ushort bps = header.ToUInt16 (2);
            if (bps != 8 && bps != 16)
                return null;
            var info = new NwaMetaData
            {
                Compression         = header.ToInt32 (8),
                RunLengthEncoded    = 0 != header.ToInt32 (0xC),
                BlockCount          = header.ToInt32 (0x10),
                PcmSize             = header.ToInt32 (0x14),
                PackedSize          = header.ToInt32 (0x18),
                SampleCount         = header.ToInt32 (0x1C),
                BlockSize           = header.ToInt32 (0x20),
                FinalBlockSize      = header.ToInt32 (0x24),
            };
            if (info.PcmSize <= 0)
                return null;
            info.Format.FormatTag = 1;
            info.Format.Channels = channels;
            info.Format.BitsPerSample = bps;
            info.Format.SamplesPerSecond = header.ToUInt32 (4);
            info.Format.BlockAlign = (ushort)(channels * bps/8);
            info.Format.AverageBytesPerSecond = info.Format.BlockAlign * info.Format.SamplesPerSecond;
            if (-1 == info.Compression)
            {
                if (info.PcmSize > file.Length - 0x2C)
                    return null;
                return new RawPcmInput (new StreamRegion (file.AsStream, 0x2C, info.PcmSize), info.Format);
            }
            if (info.Compression > 5)
                return null;
            if (info.PcmSize != info.SampleCount * bps / 8)
                return null;
            using (var decoder = new NwaDecoder (file, info))
            {
                decoder.Decode();
                var pcm = new MemoryStream (decoder.Output);
                var sound = new RawPcmInput (pcm, info.Format);
                file.Dispose();
                return sound;
            }
        }
    }

    internal sealed class NwaDecoder : IDisposable
    {
        IBinaryStream   m_input;
        byte[]          m_output;
        NwaMetaData     m_info;
        short[]         m_sample;
        LsbBitStream    m_bits;

        public byte[] Output { get { return m_output; } }


        public NwaDecoder (IBinaryStream input, NwaMetaData info)
        {
            m_input = input;
            m_info = info;
            m_output = new byte[m_info.PcmSize];
            m_sample = new short[2];
            m_bits = new LsbBitStream (input.AsStream, true);
        }

        int m_dst;

        public void Decode ()
        {
            m_input.Position = 0x2C;
            var offsets = new uint[m_info.BlockCount];
            for (int i = 0; i < offsets.Length; ++i)
                offsets[i] = m_input.ReadUInt32();

            m_dst = 0;
            for (int i = 0; i < offsets.Length-1; ++i)
            {
                m_input.Position = offsets[i];
                DecodeBlock (m_info.BlockSize);
            }
            m_input.Position = offsets[offsets.Length-1];
            if (m_info.FinalBlockSize > 0)
                DecodeBlock (m_info.FinalBlockSize);
            else
                DecodeBlock (m_info.BlockSize);
        }

        void DecodeBlock (int block_size)
        {
            int channel_count = m_info.Format.Channels;
            for (int c = 0; c < channel_count; ++c)
            {
                if (8 == m_info.Format.BitsPerSample)
                    m_sample[c] = m_input.ReadUInt8();
                else
                    m_sample[c] = m_input.ReadInt16();
            }
            m_bits.Reset();
            int channel = 0;
            int repeat_count = 0;
            for (int i = 0; i < block_size; ++i)
            {
                if (0 == repeat_count)
                {
                    int ctl = m_bits.GetBits (3);
                    if (7 == ctl)
                    {
                        if (1 == m_bits.GetNextBit())
                        {
                            m_sample[channel] = 0;
                        }
                        else
                        {
                            int bits = 8;
                            int shift = 9;
                            if (m_info.Compression < 3)
                            {
                                bits -= m_info.Compression;
                                shift += m_info.Compression;
                            }
                            int sign_bit = 1 << (bits - 1);
                            int mask = sign_bit - 1;
                            int val = m_bits.GetBits (bits);
                            if (0 != (val & sign_bit))
                                m_sample[channel] -= (short)((val & mask) << shift);
                            else
                                m_sample[channel] += (short)((val & mask) << shift);
                        }
                    }
                    else if (ctl != 0)
                    {
                        int bits, shift;
                        if (m_info.Compression < 3)
                        {
                            bits = 5 - m_info.Compression;
                            shift = 2 + ctl + m_info.Compression;
                        }
                        else
                        {
                            bits = 3 + m_info.Compression;
                            shift = 1 + ctl;
                        }
                        int sign_bit = 1 << (bits - 1);
                        int mask = sign_bit - 1;
                        int val = m_bits.GetBits (bits);
                        if (0 != (val & sign_bit))
                            m_sample[channel] -= (short)((val & mask) << shift);
                        else
                            m_sample[channel] += (short)((val & mask) << shift);
                    }
                    else if (m_info.RunLengthEncoded)
                    {
                        repeat_count = m_bits.GetNextBit();
                        if (1 == repeat_count)
                        {
                            repeat_count = m_bits.GetBits (2);
                            if (3 == repeat_count)
                                repeat_count = m_bits.GetBits (8);
                        }
                    }
                }
                else
                {
                    --repeat_count;
                }

                if (8 == m_info.Format.BitsPerSample)
                {
                    m_output[m_dst++] = (byte)m_sample[channel];
                }
                else
                {
                    LittleEndian.Pack (m_sample[channel], m_output, m_dst);
                    m_dst += 2;
                }
                if (2 == channel_count)
                    channel ^= 1;
            }
        }

        #region IDisposable Members
        public void Dispose ()
        {
        }
        #endregion
    }
}
