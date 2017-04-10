//! \file       AudioFSB5.cs
//! \date       Thu Apr 06 01:12:27 2017
//! \brief      FMOD Sample Bank audio file.
//
// Based on [python-fsb5](https://github.com/HearthSim/python-fsb5)
//
// Copyright (c) 2016 Simon Pinfold
//
// C# implementation Copyright (C) 2017 by morkt
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Formats.Vorbis;

namespace GameRes.Formats.Fmod
{
    [Export(typeof(AudioFormat))]
    public class Fsb5Audio : AudioFormat
    {
        public override string         Tag { get { return "FSB5"; } }
        public override string Description { get { return "FMOD Sample Bank audio format"; } }
        public override uint     Signature { get { return 0x35425346; } } // 'FSB5'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var fsb = new Fsb5Decoder (file);
            var sound = fsb.Convert();
            file.Dispose();
            return sound;
        }

        public override ResourceScheme Scheme
        {
            get { return new FmodScheme { VorbisHeaders = Fsb5Decoder.VorbisHeaders }; }
            set { Fsb5Decoder.VorbisHeaders = ((FmodScheme)value).VorbisHeaders; }
        }
    }

    enum SoundFormat
    {
        // Order is crucial.
        None,
        Pcm8,
        Pcm16,
        Pcm24,
        Pcm32,
        PcmFloat,
        GcAdpcm,
        ImaAdpcm,
        Vag,
        Hevag,
        Xma,
        Mpeg,
        Celt,
        At9,
        Xwma,
        Vorbis,
    }

    enum ChunkType
    {
        Channels = 1,
        SampleRate = 2,
        Loop = 3,
        VorbisData = 11,
    }

    internal class Sample
    {
        public int      SampleRate;
        public ushort   Channels;
        public long     DataOffset;
        public int      SampleCount;
        public byte[]   Data;
        public Dictionary<ChunkType, object> MetaData;
    }

    internal class Fsb5Decoder
    {
        IBinaryStream   m_input;
        int             m_sample_header_size;
        int             m_name_table_size;
        int             m_header_size;
        int             m_data_size;
        SoundFormat     m_format;

        static readonly HashSet<SoundFormat> Supported = new HashSet<SoundFormat> {
            SoundFormat.Pcm8,
            SoundFormat.Pcm16,
            SoundFormat.Pcm32,
            SoundFormat.PcmFloat,
            SoundFormat.Vorbis,
        };

        public Fsb5Decoder (IBinaryStream input)
        {
            m_input = input;
        }

        List<Sample> ReadSamples ()
        {
            var header = m_input.ReadHeader (0x3C);
            int version             = header.ToInt32 (4);
            int sample_count        = header.ToInt32 (8);
            m_sample_header_size    = header.ToInt32 (0xC);
            m_name_table_size       = header.ToInt32 (0x10);
            m_data_size             = header.ToInt32 (0x14);
            m_format = (SoundFormat)header.ToInt32 (0x18);
            if (!Supported.Contains (m_format))
                throw new NotSupportedException();

            if (0 == version)
                m_input.ReadInt32();
            var samples = new List<Sample> (sample_count);
            m_header_size = (int)m_input.Position;
            for (int i = 0; i < sample_count; ++i)
            {
                long raw = m_input.ReadInt64();
                bool next_chunk = 0 != (raw & 1);
                int sample_rate = (int)((raw >> 1) & 0xF);
                ushort channels = (ushort)(((raw >> 5) & 1) + 1);
                long data_offset = ((raw >> 6) & 0xFFFFFFF) * 0x10;
                int count = (int)((raw >> 34) & 0x3FFFFFFF);
                var chunks = new Dictionary<ChunkType, object>();
                while (next_chunk)
                {
                    int d = m_input.ReadInt32();
                    next_chunk = 0 != (d & 1);
                    int chunk_size = (d >> 1) & 0xFFFFFF;
                    var chunk_type = (ChunkType)((d >> 25) & 0x7F);
                    object chunk;
                    switch (chunk_type)
                    {
                    case ChunkType.Channels:
                        chunk = m_input.ReadUInt8();
                        break;
                    case ChunkType.SampleRate:
                        chunk = m_input.ReadInt32();
                        break;
                    case ChunkType.Loop:
                        int v1 = m_input.ReadInt32();
                        int v2 = m_input.ReadInt32();
                        chunk = Tuple.Create (v1, v2);
                        break;
                    case ChunkType.VorbisData:
                        chunk = new VorbisData {
                            Crc32 = m_input.ReadUInt32(),
                            Data = m_input.ReadBytes (chunk_size-4) // XXX unused
                        };
                        break;
                    default:
                        chunk = m_input.ReadBytes (chunk_size);
                        break;
                    }
                    chunks[chunk_type] = chunk;
                }
                if (chunks.ContainsKey (ChunkType.SampleRate))
                    sample_rate = (int)chunks[ChunkType.SampleRate];
                else if (SampleRates.ContainsKey (sample_rate))
                    sample_rate = SampleRates[sample_rate];
                else
                    throw new InvalidFormatException ("Invalid FSB5 sample rate.");

                var sample = new Sample {
                    SampleRate  = sample_rate,
                    Channels    = channels,
                    DataOffset  = data_offset,
                    SampleCount = count,
                    MetaData    = chunks,
                    Data        = null
                };
                samples.Add (sample);
            }
            return samples;
        }

        public SoundInput Convert ()
        {
            var samples = ReadSamples();
            var sample = samples[0];
            int data_length;
            if (samples.Count > 1)
                data_length = (int)(samples[1].DataOffset - sample.DataOffset);
            else
                data_length = m_data_size;
            m_input.Position = m_header_size + m_sample_header_size + m_name_table_size;
            sample.Data = m_input.ReadBytes (data_length);

            if (SoundFormat.Vorbis == m_format)
                return RebuildVorbis (sample);
            else
                return RebuildPcm (sample);
        }

        SoundInput RebuildPcm (Sample sample)
        {
            var format = new WaveFormat
            {
                FormatTag = (ushort)(SoundFormat.PcmFloat == m_format ? 3 : 1),
                Channels = sample.Channels,
                SamplesPerSecond = (uint)sample.SampleRate,
            };
            switch (m_format)
            {
            case SoundFormat.Pcm8:   format.BitsPerSample = 8; break;
            case SoundFormat.Pcm16:  format.BitsPerSample = 16; break;
            case SoundFormat.PcmFloat:
            case SoundFormat.Pcm32:  format.BitsPerSample = 32; break;
            default: throw new InvalidFormatException();
            }
            format.BlockAlign = (ushort)(format.Channels * format.BitsPerSample / 8);
            format.SetBPS();
            var pcm = new MemoryStream (sample.Data);
            return new RawPcmInput (pcm, format);
        }

        SoundInput RebuildVorbis (Sample sample)
        {
            if (!sample.MetaData.ContainsKey (ChunkType.VorbisData))
                throw new InvalidFormatException ("No VORBISDATA chunk in FSB5 Vorbis stream.");
            var vorbis_data = sample.MetaData[ChunkType.VorbisData] as VorbisData;
            var setup_data = GetVorbisHeader (vorbis_data.Crc32);
            var state = new OggStreamState (1);

            var id_packet = RebuildIdPacket (sample, 0x100, 0x800);
            var comment_packet = RebuildCommentPacket();
            var setup_packet = RebuildSetupPacket (setup_data);
            var info = CreateVorbisInfo (sample, setup_packet);

            var output = new MemoryStream();
            state.PacketIn (id_packet);
            state.Write (output);
            state.PacketIn (comment_packet);
            state.Write (output);
            state.PacketIn (setup_packet);
            state.Write (output);
            state.Flush (output);

            long packet_no = setup_packet.PacketNo + 1;
            long granule_pos = 0;
            int prev_block_size = 0;
            using (var input = new BinMemoryStream (sample.Data))
            {
                var packet = new OggPacket();
                int packet_size = ReadPacketSize (input);
                while (packet_size > 0)
                {
                    packet.SetPacket (packet_no++, input.ReadBytes (packet_size));
                    packet_size = ReadPacketSize (input);
                    packet.EoS = 0 == packet_size;

                    int block_size = info.PacketBlockSize (packet);
                    if (prev_block_size != 0)
                        granule_pos += (block_size + prev_block_size) / 4;
                    else
                        granule_pos = 0;
                    packet.GranulePos = granule_pos;
                    prev_block_size = block_size;

                    state.PacketIn (packet);
                    state.Write (output);
                }
            }
            output.Position = 0;
            return new OggInput (output);
        }

        VorbisInfo CreateVorbisInfo (Sample sample, OggPacket setup_packet)
        {
            var info = new VorbisInfo {
                Channels = sample.Channels,
                Rate     = sample.SampleRate,
            };
            info.CodecSetup.BlockSizes[0] = 0x100;
            info.CodecSetup.BlockSizes[1] = 0x800;
            var comment = new VorbisComment { Vendor = VorbisComment.EncodeVendorString };
            info.SynthesisHeaderin (comment, setup_packet);
            return info;
        }

        int ReadPacketSize (IBinaryStream input)
        {
            int lo = input.ReadByte();
            if (-1 == lo)
                return 0;
            int hi = input.ReadByte();
            if (-1 == hi)
                return 0;
            return hi << 8 | lo;
        }

        OggPacket RebuildIdPacket (Sample sample, uint blocksize_short, uint blocksize_long)
        {
            using (var buf = new MemoryStream())
            using (var output = new BinaryWriter (buf))
            {
                output.Write ((byte)1);
                output.Write ("vorbis".ToCharArray());
                output.Write (0);
                output.Write ((byte)sample.Channels);
                output.Write (sample.SampleRate);
                output.Write (0);
                output.Write (0);
                output.Write (0);
                int lo = VorbisInfo.CountBits (blocksize_short);
                int hi = VorbisInfo.CountBits (blocksize_long);
                int bits = hi << 4 | lo;
                output.Write ((byte)bits);
                output.Write ((byte)1);
                output.Flush();

                var packet = new OggPacket();
                packet.SetPacket (0, buf.ToArray());
                packet.BoS = true;
                return packet;
            }
        }

        OggPacket RebuildCommentPacket ()
        {
            var comment = new VorbisComment();
            var packet = new OggPacket();
            comment.HeaderOut (packet);
            return packet;
        }

        OggPacket RebuildSetupPacket (byte[] setup_packet)
        {
            var packet = new OggPacket();
            packet.SetPacket (2, setup_packet);
            return packet;
        }

        static readonly Dictionary<int, int> SampleRates = new Dictionary<int,int> {
            { 1, 8000 },
            { 2, 11000 },
            { 3, 11025 },
            { 4, 16000 },
            { 5, 22050 },
            { 6, 24000 },
            { 7, 32000 },
            { 8, 44100 },
            { 9, 48000 },
        };

        public static byte[] GetVorbisHeader (uint id)
        {
            FmodVorbisSetup setup;
            if (!VorbisHeaders.TryGetValue (id, out setup))
                throw new InvalidFormatException (string.Format ("Unknown FSB5 Vorbis encoding 0x{0:X8}.", id));
            if (null == setup.PatchData || 0 == setup.PatchData.Length)
                return setup.VorbisData;
            var data = setup.VorbisData.Clone() as byte[];
            Buffer.BlockCopy (setup.PatchData, 0, data, setup.PatchOffset, setup.PatchData.Length);
            return data;
        }

        internal static Dictionary<uint, FmodVorbisSetup> VorbisHeaders = new Dictionary<uint, FmodVorbisSetup>();
    }

    internal class VorbisData
    {
        public uint     Crc32;
        public byte[]   Data; // ignored
    }

    [Serializable]
    public class FmodVorbisSetup
    {
        public byte[]   VorbisData;
        public int      PatchOffset;
        public byte[]   PatchData;
    }

    [Serializable]
    public class FmodScheme : ResourceScheme
    {
        public Dictionary<uint, FmodVorbisSetup> VorbisHeaders;
    }
}
