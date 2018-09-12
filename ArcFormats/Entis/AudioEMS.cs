//! \file       AudioEMS.cs
//! \date       2018 Mar 09
//! \brief      EMSAC audio format.
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

namespace GameRes.Formats.Entis
{
    internal class EmsacSoundInfo
    {
        public int      Version;            // field_00
        public CvType   Transformation;     // field_04
        public EriCode  Architecture;       // field_08
        public int      ChannelCount;       // field_0C
        public uint     SamplesPerSec;      // field_10
        public uint     BlocksetCount;      // field_14
        public int      SubbandDegree;      // field_18
        public int      AllSampleCount;     // field_1C
        public int      LappedDegree;       // field_20
    }

    [Export(typeof(AudioFormat))]
    public sealed class EmsAudio : AudioFormat
    {
        public override string         Tag { get { return "EMS"; } }
        public override string Description { get { return "EMSAC compressed audio format"; } }
        public override uint     Signature { get { return 0x69746E45; } } // 'Entis'

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x40);
            if (0x02000200 != header.ToUInt32 (8))
                return null;
            if (!header.AsciiEqual (0x10, "EMSAC-Sound-2"))
                return null;
            var decoder = new EmsacDecoder (file);
            var pcm = decoder.Decode();
            var sound = new RawPcmInput (pcm, decoder.Format);
            file.Dispose();
            return sound;
        }
    }

    internal class EmsacDecoder
    {
        Stream          m_input;
        EmsacSoundInfo  m_info;
        long            m_stream_pos;
        WaveFormat      m_format;

        public WaveFormat Format { get { return m_format; } }
        
        const ushort BitsPerSample = 16;

        public EmsacDecoder (IBinaryStream input)
        {
            m_input = input.AsStream;
        }

        public Stream Decode ()
        {
            m_input.Position = 0x40;
            using (var erif = new EriFile (m_input))
            {
                ReadHeader (erif);
                int total_bytes = m_info.AllSampleCount * BitsPerSample / 8;
                var output = new MemoryStream (total_bytes);
                try
                {
                    var buffer = new byte[0x10000];
                    var decoded = new byte[0x10000];
                    erif.BaseStream.Position = m_stream_pos;
                    while (total_bytes > 0)
                    {
                        var section = erif.ReadSection();
                        if (section.Id != "SoundStm")
                            break;
                        int stm_length = (int)section.Length;
                        if (stm_length > buffer.Length)
                            buffer = new byte[stm_length];
                        erif.Read (m_buffer, 0, stm_length);
                        int length = m_buffer.ToInt32 (4) * m_lappedSubband;
                        if (length > decoded.Length)
                            decoded = new byte[length];

                        DecodeBlock (buffer, decoded);
                        length = Math.Min (length, total_bytes);
                        output.Write (decoded, 0, length);
                        total_bytes -= length;
                    }
                }
                catch (EndOfStreamException) { /* ignore EOF errors */ }
                output.Position = 0;
                return output;
            }
        }

        void DecodeBlock (byte[] input, byte[] output)
        {
            if (m_exp.Version > 0x20100)
                throw new InvalidFormatException ("Not supported EMSAC version.");
            int channels = m_exp.ChannelCount;
            if (0 == channels || channels > 2)
                throw new InvalidFormatException ("Invalid number of channels.");
            int subband_degree = 0;
            int v84 = 1;
            bool use_dct = 0 != (m_exp.Transformation & 1);
            if (use_dct)
            {
                subband_degree = m_exp.SubbandDegree;
                v84 <<= subband_degree;
            }
            int subband = 1 << subband_degree;
            int sample_count = input.ToInt32 (4);
            int samples_per_channel = sample_count / channels;
            if (0 == samples_per_channel)
                throw new InvalidFormatException ("Invalid number of samples.");
            int v85 = samples_per_channel;
            var v73 = new int[channels][];
            var v75 = new int[subband]; // workBuf
            var v74 = new int[subband]; // weightTable
            var v13 = new int[subband * channels]; // internalBuf
            var sampleBuf = new short[sample_count];
            for (int i = 0; i < channels; ++i)
            {
                v73[i] = new int[subband];
            }
            int dst = 0;
            int src = 8;
            int v77 = src + sample_count;
            object sym_table;
            var v16 = CreateDecoderSymbolTable (input, v77, out sym_table);
            for (int i = 0; i < samples_per_channel; ++i)
            {
                for (int c = 0; c < channels; ++c)
                {
                    var v72 = v73[c];
                    int v19 = DecodeSymbols (v16, sym_table, v75, subband);
                    if (v19 < subband)
                        throw new InvalidFormatException();
                    byte code = input[src++];
                    InverseQuantumize (v74, v75, subband, code);
                    if (use_dct)
                    {
                        InverseDCT (v72, v74, 2, subband_degree);
                    }
                    else
                    {
                        Buffer.BlockCopy (v74, v72, 4 * subband);
                    }
                }
                int cur_pos = dst;
                for (int c = 0; c < channels; ++c)
                {
                    int pos = cur_pos;
                    var v32 = v73[c];
                    for (int i = 0; i < subband; ++i)
                    {
                        int v34 = v32[i];
                        int v35 = v34;
                        int v36 = v34 >> 23;
                        int v37 = v34 & 0x7FFFFF | 0x800000;
                        int v38 = v35 >> 31;
                        int v39 = -((byte)v36 - 150);
                        if (v39 > 8)
                        {
                            if (v39 <= 31)
                                v40 = v38 ^ (v38 + __CFSHR__(v37, v39) + (v37 >> v39));
                            else
                                v40 = 0;
                        }
                        else
                        {
                            v40 = v38 ^ (v38 + 0x7FFF);
                        }
                        sampleBuf[pos] = (short)v40;
                        pos += channels;
                    }
                    ++cur_pos;
                }
                dst += channels * subband;
            }
            dst = 0;
            for (int c = 0; c < channels; ++c)
            {
                int src = c;
                short diff = (short)(m_exp.CompState[c] - sampleBuf[src]);
                int pos = 0;
                for (int i = 0; i < 12; ++i)
                {
                    int sample = diff + sampleBuf[src+pos];
                    sampleBuf[src+pos] = Clamp (sample);
                    pos += channels;
                    diff >>= 1;
                }
                int v48 = samples_per_channel - 1;
                for (int i = 1; i < samples_per_channel; ++i)
                {
                    v42 = (_WORD *)((char *)v42 + (step << subbandDegree));
                    v49 = v42[step / 2] + v42[-step / 2] - v42[-step] - *v42;
                    v70 = (short)(v49 + v42[-step / 2] + *v42) >> 1;
                    v50 = (short)(v42[-step / 2] + *v42 - v49) >> 1;
                    v52 = -step;
                    v53 = (short)(v50 - v42[-step / 2]);
                    for (int j = 0; j < 12; ++j)
                    {
                        int v54 = v53 + *(_WORD *)((char *)v42 + v52);
                        *(_WORD *)((char *)v42 + v52) = Clamp (v54);
                        v52 -= step;
                        v53 >>= 1;
                    }
                    v55 = 0;
                    v57 = (short)(v70 - *v42);
                    for (int j = 0; j < 12; ++j)
                    {
                        int v58 = v57 + *(_WORD *)((char *)v42 + v55);
                        *(_WORD *)((char *)v42 + v55) = Clamp (v58);
                        v55 += step;
                        v57 >>= 1;
                    }
                }
                int v59 = 2 * *(_WORD *)((char *)v42 + (step << subbandDegree) - step)
                          - *(_WORD *)((char *)v42 + (step << subbandDegree) - 2 * step);
                m_exp.CompState[c] = Clamp (v59);
            }
            Buffer.BlockCopy (sampleBuf, 0, output, 0, sample_count * 2);
        }

        static short Clamp (int sample)
        {
            if (sample > 0x7FFF)
                return 0x7FFF;
            else if (sample < -32768)
                return -32768;
            return (short)sample;
        }

        void ReadHeader (EriFile erif)
        {
            var section = erif.ReadSection();
            if (section.Id != "Header  " || section.Length <= 0 || section.Length > int.MaxValue)
                return null;
            m_stream_pos = erif.BaseStream.Position + section.Length;
            section = erif.ReadSection();
            if (section.Id != "FileHdr" || section.Length < 8)
                return null;
            var file_hdr = new byte[section.Length];
            erif.Read (file_hdr, 0, file_hdr.Length);
            if (0 == (file_hdr[5] & 1))
                return null;
            section = erif.ReadSection();
            if (section.Id != "SoundInf" || section.Length < 0x24)
                return null;

            var info = new EmsacSoundInfo();
            info.Version        = erif.ReadInt32();
            info.Transformation = (CvType)erif.ReadInt32();
            info.Architecture   = (EriCode)erif.ReadInt32();
            info.ChannelCount   = erif.ReadInt32();
            info.SamplesPerSec  = erif.ReadUInt32();
            info.BlocksetCount  = erif.ReadUInt32();
            info.SubbandDegree  = erif.ReadInt32();
            info.AllSampleCount = erif.ReadInt32();
            info.LappedDegree   = erif.ReadInt32();
            SetSoundInfo (info);
            SetWaveFormat (info);

            erif.BaseStream.Position = m_stream_pos;
            var stream_size = erif.FindSection ("Stream  ");
            m_stream_pos = erif.BaseStream.Position;
        }

        EmsacExpansion  m_exp;
        int             m_lappedSubband;
        int             m_version;
        EriCode         m_field_98;
        int             m_field_9C;
        int             m_field_A0;
        int             m_field_A4;

        void SetSoundInfo (EmsacSoundInfo info)
        {
            m_info = info;
            m_exp = new EmsacExpansion (info);
            m_version = info.Version;
            m_field_98 = info.Architecture;
            m_field_9C = 133;
            m_field_A0 = -1;
            m_field_A4 = 0;
            int subband = 2 << info.SubbandDegree;
            m_lappedSubband = subband << info.LappedDegree;
        }

        void SetWaveFormat (EmsacSoundInfo info)
        {
            int pcm_bitrate = (int)(m_info.SamplesPerSec * 16 * m_info.ChannelCount);
            m_format.FormatTag                = 1;
            m_format.Channels                 = (ushort)ChannelCount;
            m_format.SamplesPerSecond         = m_info.SamplesPerSec;
            m_format.BitsPerSample            = (ushort)BitsPerSample;
            m_format.BlockAlign               = (ushort)(BitsPerSample/8*format.Channels);
            m_format.AverageBytesPerSecond    = (uint)pcm_bitrate/8;
        }
    }

    internal class EmsacExpansion
    {
        public int      Version;            // field_0
        public CvType   Transformation;     // field_4
        public int      ChannelCount;       // field_8
        public int      SubbandDegree;      // field_C
        public int      LappedDegree;       // field_10
        public short[]  CompState = new short[16];

        public EmsacExpansion (EmsacSoundInfo info)
        {
            Version         = info.Version;
            Transformation  = info.Transformation;
            ChannelCount    = info.ChannelCount;
            SubbandDegree   = info.SubbandDegree;
            LappedDegree    = info.LappedDegree;
        }
    }
}
