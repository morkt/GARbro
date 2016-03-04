//! \file       AudioHCA.cs
//! \date       Wed Mar 02 06:34:18 2016
//! \brief      High Compression Audio format.
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GameRes.Formats.Cri
{
    [Export(typeof(AudioFormat))]
    public class HcaAudio : AudioFormat
    {
        public override string         Tag { get { return "HCA"; } }
        public override string Description { get { return "CRI MiddleWare compressed audio"; } }
        public override uint     Signature { get { return 0x00414348; } } // 'HCA'

        public HcaAudio ()
        {
            Signatures = new uint[] { 0x00414348, 0x80C1C3C8 };
        }

        static readonly Tuple<uint, uint> DefaultKey = Tuple.Create (0x30DBE1ABu, 0xCC554639u);

        public override SoundInput TryOpen (Stream file)
        {
            return new HcaInput (file, ConversionFormat.IeeeFloat, DefaultKey);
        }
    }

    public enum ConversionFormat : ushort 
    {
        Pcm = 1,
        IeeeFloat = 3,
    };

    internal class HcaInput : SoundInput
    {
        HcaReader       m_reader;
        long            m_position;
        int             m_bitrate;
        Array[]         m_decoded_blocks;
        int             m_decoded_block_size;

        public override long Position
        {
            get { return m_position; }
            set { m_position = value; }
        }

        public override bool        CanSeek { get { return true; } }
        public override string SourceFormat { get { return "hca"; } }
        public override int   SourceBitrate { get { return m_bitrate; } }

        public HcaInput (Stream file, ConversionFormat target, Tuple<uint, uint> key) : base (file)
        {
            m_reader = new HcaReader (file, key.Item1, key.Item2);
            m_reader.InitConversion (target);
            Format = m_reader.Format;
            m_bitrate = (int)(Format.SamplesPerSecond * m_reader.BlockSize / (0x80 * Format.Channels));
            m_decoded_block_size = (int)(0x80 * Format.Channels * Format.BitsPerSample);
            PcmSize = m_reader.BlockCount * m_decoded_block_size;
            m_decoded_blocks = new Array[m_reader.BlockCount];

            InitBackgroundReader (target);
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            long block_pos;
            int block_index = (int)Math.DivRem (m_position, m_decoded_block_size, out block_pos);
            if (block_index < 0 || block_index > m_decoded_blocks.Length)
                return 0;
            int total_read = 0;
            while (block_index < m_decoded_blocks.Length && count > 0)
            {
                if (null == m_decoded_blocks[block_index])
                    FillBlock (block_index);
                if (null == m_decoded_blocks[block_index])
                    break;
                int available = Math.Min (count, m_decoded_block_size - (int)block_pos);
                Buffer.BlockCopy (m_decoded_blocks[block_index], (int)block_pos, buffer, offset, available);
                m_position += available;
                total_read += available;
                count -= available;
                if (0 == count)
                    break;
                offset += available;
                block_pos = 0;
                ++block_index;
            }
            return total_read;
        }

        void InitBackgroundReader (ConversionFormat target)
        {
            m_block_queue = new BlockingCollection<Tuple<int, Array>>();
            m_cancel_source = new CancellationTokenSource();

            var token = m_cancel_source.Token;
            if (ConversionFormat.IeeeFloat == target)
                m_conversion_task = Task.Factory.StartNew (() => m_reader.ConvertParallel (m_block_queue, HcaReader.PackSampleFloat, token), token);
            else
                m_conversion_task = Task.Factory.StartNew (() => m_reader.ConvertParallel (m_block_queue, HcaReader.PackSample16, token), token);
        }

        BlockingCollection<Tuple<int, Array>> m_block_queue;
        Task                    m_conversion_task;
        CancellationTokenSource m_cancel_source;

        void FillBlock (int block_index)
        {
            var token = m_cancel_source.Token;
            while (!m_block_queue.IsCompleted && null == m_decoded_blocks[block_index])
            {
                var block = m_block_queue.Take (token);
                if (block.Item1 >= m_decoded_blocks.Length)
                    throw new IndexOutOfRangeException();
                m_decoded_blocks[block.Item1] = block.Item2;
            }
        }

        #region IDisposable Members
        bool _hca_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!_hca_disposed)
            {
                if (disposing)
                {
                    if (m_cancel_source != null)
                    {
                        if (!m_block_queue.IsAddingCompleted)
                            m_cancel_source.Cancel();
                        try
                        {
                            m_conversion_task.Wait();
                        }
                        catch
                        {
                            // ignore exceptions
                        }
                        finally
                        {
                            m_conversion_task.Dispose();
                            m_block_queue.Dispose();
                            m_cancel_source.Dispose();
                        }
                    }
                    m_reader.Dispose();
                }
                _hca_disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }

    internal sealed class HcaReader : IDisposable
    {
        WaveFormat      m_format;
        byte[]          m_input;

        public WaveFormat Format { get { return m_format; } }
        public uint   BlockCount { get { return m_fmt_block_count.Value; } }
        public int     BlockSize { get { return m_comp.BlockSize; } }

        public HcaReader (Stream input, uint key1, uint key2)
        {
            using (var file = new BigEndianReader (input, Encoding.UTF8, true))
                ParseHeader (file);
            m_ath = new AthTable (m_ath_type.Value, m_format.SamplesPerSecond);
            m_cipher = new Cipher (m_ciph_type.Value, key1, key2);
            m_block = new ThreadLocal<byte[]> (() => new byte[m_comp.BlockSize]);

            InitBuffer (input);
        }

        delegate void SampleWriter (float f, BinaryWriter output);

        static readonly Dictionary<ConversionFormat, ushort> FormatBpsMap = new Dictionary<ConversionFormat, ushort> {
            { ConversionFormat.Pcm,         16 },
            { ConversionFormat.IeeeFloat,   32 },
        };
        static readonly Dictionary<ConversionFormat, SampleWriter> ConversionMap = new Dictionary<ConversionFormat, SampleWriter> {
            { ConversionFormat.Pcm,         (f, output) => output.Write (PackSample16 (f)) },
            { ConversionFormat.IeeeFloat,   (f, output) => output.Write (PackSampleFloat (f)) },
        };

        int             m_version;
        int             m_data_offset;
        uint?           m_fmt_block_count;
        int?            m_ciph_type;
        int?            m_ath_type;
        CompParams      m_comp;
        float           m_rva_volume = 1.0f;
        AthTable        m_ath;
        Cipher          m_cipher;

        ThreadLocal<Channel[]>  m_channel;
        ThreadLocal<byte[]>     m_block;

        public byte[] Unpack (ConversionFormat target)
        {
            InitWaveFormat (target);
            var output = new byte[BlockCount * 0x400 * m_format.BlockAlign];
            var convert_sample = ConversionMap[target];
            using (var mem = new MemoryStream (output))
            using (var writer = new BinaryWriter (mem))
                ConvertSequential (f => convert_sample (f, writer));
            return output;
        }

        public void InitConversion (ConversionFormat target)
        {
            InitWaveFormat (target);
        }

        void InitWaveFormat (ConversionFormat target)
        {
            m_format.FormatTag = (ushort)target;
            m_format.BitsPerSample = FormatBpsMap[target];
            m_format.BlockAlign = (ushort)(m_format.Channels * m_format.BitsPerSample / 8);
            m_format.AverageBytesPerSecond = m_format.SamplesPerSecond * m_format.BlockAlign;
        }

        void InitBuffer (Stream input)
        {
            var mem = input as MemoryStream;
            if (null == mem)
            {
                m_input = new byte[input.Length];
                input.Position = m_data_offset;
                input.Read (m_input, m_data_offset, m_input.Length-m_data_offset);
            }
            else
            {
                try
                {
                    m_input = mem.GetBuffer();
                }
                catch (UnauthorizedAccessException)
                {
                    m_input = mem.ToArray();
                }
            }
        }

        void ParseHeader (BigEndianReader input)
        {
            uint signature = ReadSignature (input);
            if (signature != 0x48434100) // 'HCA'
                throw new InvalidFormatException();
            m_version = input.ReadUInt16();
            m_data_offset = input.ReadUInt16();
            while (input.Position < m_data_offset)
            {
                signature = ReadSignature (input);
                switch (signature)
                {
                case 0x666D7400: // 'fmt'
                    uint format = input.ReadUInt32();
                    m_format.Channels = (byte)(format >> 24);
                    m_format.SamplesPerSecond = format & 0xFFFFFF;
                    m_fmt_block_count = input.ReadUInt32();
                    input.Skip (4);
                    continue;

                case 0x636F6D70: // 'comp'
                    m_comp = new CompParams { BlockSize = input.ReadUInt16() };
                    input.Read (m_comp.R, 0, 8);
                    input.Skip (2);
                    continue;

                case 0x6C6F6F70: // 'loop'
                    input.Skip (12);
                    continue;

                case 0x63697068: // 'ciph'
                    m_ciph_type = input.ReadUInt16();
                    continue;

                case 0x72766100: // 'rva'
                    m_rva_volume = input.ReadSingle();
                    continue;

                case 0x61746800: // 'ath'
                    m_ath_type = input.ReadUInt16();
                    continue;
                }
                break; // unknown section encountered
            }
            if (null == m_fmt_block_count || null == m_comp)
                throw new NotSupportedException ("Not supported HCA format");
            if (m_comp.BlockSize < 8)
                throw new InvalidFormatException ("Invalid HCA block size");
            if (0 == m_format.Channels || m_format.Channels > 16)
                throw new InvalidFormatException();

            if (null == m_ciph_type)
                m_ciph_type = 0;
            if (null == m_ath_type)
                m_ath_type = m_version < 0x200 ? 1 : 0;

            if (m_comp.R[7] > 0)
            {
                int t = m_comp.R[4] - (m_comp.R[5] + m_comp.R[6]);
                m_comp.R9 = t / m_comp.R[7] + ((t % m_comp.R[7]) != 0 ? 1 : 0);
            }
            else
            {
                m_comp.R9 = 0;
            }

            if (0 == m_comp.R[2])
                m_comp.R[2] = 1;
            InitChannels();
        }

        void InitChannels ()
        {
            var r = new byte[0x10];
            int step = m_format.Channels / m_comp.R[2];
            if (m_comp.R[6] != 0 && step > 1)
            {
                int c = 0;
                for (int i = 0; i < m_comp.R[2]; ++i, c += step)
                {
                    switch (step)
                    {
                    case 2:
                    case 3:
                        r[c] = 1;
                        r[c+1] = 2;
                        break;
                    case 4:
                        if (0 == m_comp.R[3])
                        {
                            r[c+2] = 1;
                            r[c+3] = 2;
                        }
                        goto case 2;
                    case 5:
                        if (m_comp.R[3] <= 2)
                        {
                            r[c+3] = 1;
                            r[c+4] = 2;
                        }
                        goto case 2;
                    case 6:
                    case 7:
                        r[c+4] = 1;
                        r[c+5] = 2;
                        goto case 2;
                    case 8:
                        r[c+6] = 1;
                        r[c+7] = 2;
                        goto case 6;
                    }
                }
            }
            m_channel = new ThreadLocal<Channel[]> (() => {
                var channels = new Channel[m_format.Channels];
                for (int i = 0; i < channels.Length; ++i)
                {
                    channels[i] = new Channel (r[i], m_comp.R[5], m_comp.R[6]);
                }
                return channels;
            });
        }

        void ConvertSequential (Action<float> pack_sample)
        {
            foreach (var block_offset in GetBlockOffsets())
            {
                if (block_offset + m_comp.BlockSize > m_input.Length)
                    throw new EndOfStreamException();
                Buffer.BlockCopy (m_input, block_offset, m_block.Value, 0, m_comp.BlockSize);
                DecodeBlock();
                for (int j = 0; j < 8; ++j)
                for (int k = 0; k < 0x80; ++k)
                for (int c = 0; c < m_format.Channels; ++c)
                {
                    float f = m_channel.Value[c].Samples[j,k] * m_rva_volume;
                    pack_sample (f);
                }
            }
        }

        public void ConvertParallel<SampleType> (BlockingCollection<Tuple<int, Array>> output, Func<float, SampleType> convert_sample, CancellationToken token)
        {
            try
            {
                // despite the fact that parallel decoding is considerably faster (roughly x[number of cores])
                // it hurts playback badly due to locks inside BlockingCollection.Take()
//                Parallel.ForEach (Enumerable.Range (0, (int)BlockCount), block_num =>
                foreach (int block_num in Enumerable.Range (0, (int)BlockCount))
                {
                    int block_offset = m_data_offset + block_num * m_comp.BlockSize;
                    if (block_offset + m_comp.BlockSize > m_input.Length)
                        throw new EndOfStreamException();
                    token.ThrowIfCancellationRequested();
                    Buffer.BlockCopy (m_input, block_offset, m_block.Value, 0, m_comp.BlockSize);
                    DecodeBlock();
                    token.ThrowIfCancellationRequested();
                    var decoded = new SampleType[0x400 * m_format.Channels];
                    int i = 0;
                    for (int j = 0; j < 8; ++j)
                    for (int k = 0; k < 0x80; ++k)
                    for (int c = 0; c < m_format.Channels; ++c)
                    {
                        float f = m_channel.Value[c].Samples[j,k] * m_rva_volume;
                        decoded[i++] = convert_sample (f);
                    }
                    output.Add (new Tuple<int, Array> (block_num, decoded), token);
                }
            }
            finally
            {
                output.CompleteAdding();
            }
        }

        IEnumerable<int> GetBlockOffsets ()
        {
            int block_offset = m_data_offset;
            for (int i = 0; i < m_fmt_block_count; ++i)
            {
                yield return block_offset;
                block_offset += m_comp.BlockSize;
            }
        }

        void DecodeBlock ()
        {
            var block = m_block.Value;
            if (CheckSum (block) != 0)
                throw new InvalidFormatException ("Data checksum mismatch");

            m_cipher.Decipher (block);
            using (var input = new MemoryStream (block, 0, block.Length-2))
            using (var bits = new HsaBitStream (input))
            {
                if (0xFFFF != bits.GetBits (16))
                    return;

                var decoder = m_channel.Value;
                int t = bits.GetBits (9) << 8;
                t -= bits.GetBits (7);
                for (int i = 0; i < decoder.Length; ++i)
                    decoder[i].Decode1 (bits, m_comp.R9, t, m_ath.Table);
                for (int i = 0; i < 8; ++i)
                {
                    for (int j = 0; j < decoder.Length; ++j)
                        decoder[j].Decode2 (bits);
                    for (int j = 0; j < decoder.Length; ++j)
                        decoder[j].Decode3 (m_comp.R9, m_comp.R[7], m_comp.R[6] + m_comp.R[5], m_comp.R[4]);
                    for (int j = 0; j < decoder.Length-1; ++j)
                        decoder[j].Decode4 (i, m_comp.R[4]-m_comp.R[5], m_comp.R[5], m_comp.R[6], decoder[j+1]);
                    for (int j = 0; j < decoder.Length; ++j)
                        decoder[j].Decode5 (i);
                }
            }
        }

        public static float PackSampleFloat (float f)
        {
            if (f > 1)
                f = 1;
            else if (f < -1)
                f = -1;
            return f;
        }

        public static short PackSample16 (float f)
        {
            int s = (int)(f * 0x7FFF);
            if (s > 0x7FFF)
                s = 0x7FFF;
            else if (s < -0x7FFF)
                s = -0x7FFF;
            return (short)s;
        }

        static uint ReadSignature (BigEndianReader input)
        {
            return input.ReadUInt32() & 0x7F7F7F7F;
        }

        static ushort CheckSum (byte[] data, ushort sum = 0)
        {
            for (int i = 0; i < data.Length; ++i)
                sum = (ushort)((sum << 8) ^ CheckSumTable[(sum >> 8) ^ data[i]]);
            return sum;
        }

        internal class CompParams
        {
            public int      BlockSize;
            public byte[]   R = new byte[8];
            public int      R9;
        }

        internal class AthTable
        {
            byte[] m_table = new byte[0x80];

            public byte[] Table { get { return m_table; } }

            public AthTable (int type, uint key)
            {
                if (1 == type)
                    Init (key);
                else if (0 != type)
                    throw new InvalidFormatException ("Unknown HCA ath type");
            }

            void Init (uint key)
            {
                uint v = 0;
                for (int i = 0; i < 0x80; i++, v += key)
                {
                    uint index = v >> 13;
                    if (index >= 0x28E)
                    {
                        for (int k = i; k < m_table.Length; ++k)
                            m_table[k] = 0xFF;
                        break;
                    }
                    m_table[i] = AthList[index];
                    v += key;
                }
            }

            static readonly byte[] AthList = {
                0x78,0x5F,0x56,0x51,0x4E,0x4C,0x4B,0x49,0x48,0x48,0x47,0x46,0x46,0x45,0x45,0x45,
                0x44,0x44,0x44,0x44,0x43,0x43,0x43,0x43,0x43,0x43,0x42,0x42,0x42,0x42,0x42,0x42,
                0x42,0x42,0x41,0x41,0x41,0x41,0x41,0x41,0x41,0x41,0x41,0x41,0x40,0x40,0x40,0x40,
                0x40,0x40,0x40,0x40,0x40,0x3F,0x3F,0x3F,0x3F,0x3F,0x3F,0x3F,0x3F,0x3F,0x3F,0x3F,
                0x3F,0x3F,0x3F,0x3E,0x3E,0x3E,0x3E,0x3E,0x3E,0x3D,0x3D,0x3D,0x3D,0x3D,0x3D,0x3D,
                0x3C,0x3C,0x3C,0x3C,0x3C,0x3C,0x3C,0x3C,0x3B,0x3B,0x3B,0x3B,0x3B,0x3B,0x3B,0x3B,
                0x3B,0x3B,0x3B,0x3B,0x3B,0x3B,0x3B,0x3B,0x3B,0x3B,0x3B,0x3B,0x3B,0x3B,0x3B,0x3B,
                0x3B,0x3B,0x3B,0x3B,0x3B,0x3B,0x3B,0x3B,0x3C,0x3C,0x3C,0x3C,0x3C,0x3C,0x3C,0x3C,
                0x3D,0x3D,0x3D,0x3D,0x3D,0x3D,0x3D,0x3D,0x3E,0x3E,0x3E,0x3E,0x3E,0x3E,0x3E,0x3F,
                0x3F,0x3F,0x3F,0x3F,0x3F,0x3F,0x3F,0x3F,0x3F,0x3F,0x3F,0x3F,0x3F,0x3F,0x3F,0x3F,
                0x3F,0x3F,0x3F,0x3F,0x40,0x40,0x40,0x40,0x40,0x40,0x40,0x40,0x40,0x40,0x40,0x40,
                0x40,0x40,0x40,0x40,0x40,0x40,0x40,0x40,0x40,0x41,0x41,0x41,0x41,0x41,0x41,0x41,
                0x41,0x41,0x41,0x41,0x41,0x41,0x41,0x41,0x41,0x41,0x41,0x41,0x41,0x41,0x41,0x41,
                0x41,0x41,0x41,0x41,0x41,0x41,0x41,0x42,0x42,0x42,0x42,0x42,0x42,0x42,0x42,0x42,
                0x42,0x42,0x42,0x42,0x42,0x42,0x42,0x42,0x42,0x42,0x42,0x42,0x42,0x43,0x43,0x43,
                0x43,0x43,0x43,0x43,0x43,0x43,0x43,0x43,0x43,0x43,0x43,0x43,0x43,0x43,0x44,0x44,
                0x44,0x44,0x44,0x44,0x44,0x44,0x44,0x44,0x44,0x44,0x44,0x44,0x45,0x45,0x45,0x45,
                0x45,0x45,0x45,0x45,0x45,0x45,0x45,0x45,0x46,0x46,0x46,0x46,0x46,0x46,0x46,0x46,
                0x46,0x46,0x47,0x47,0x47,0x47,0x47,0x47,0x47,0x47,0x47,0x47,0x48,0x48,0x48,0x48,
                0x48,0x48,0x48,0x48,0x49,0x49,0x49,0x49,0x49,0x49,0x49,0x49,0x4A,0x4A,0x4A,0x4A,
                0x4A,0x4A,0x4A,0x4A,0x4B,0x4B,0x4B,0x4B,0x4B,0x4B,0x4B,0x4C,0x4C,0x4C,0x4C,0x4C,
                0x4C,0x4D,0x4D,0x4D,0x4D,0x4D,0x4D,0x4E,0x4E,0x4E,0x4E,0x4E,0x4E,0x4F,0x4F,0x4F,
                0x4F,0x4F,0x4F,0x50,0x50,0x50,0x50,0x50,0x51,0x51,0x51,0x51,0x51,0x52,0x52,0x52,
                0x52,0x52,0x53,0x53,0x53,0x53,0x54,0x54,0x54,0x54,0x54,0x55,0x55,0x55,0x55,0x56,
                0x56,0x56,0x56,0x57,0x57,0x57,0x57,0x57,0x58,0x58,0x58,0x59,0x59,0x59,0x59,0x5A,
                0x5A,0x5A,0x5A,0x5B,0x5B,0x5B,0x5B,0x5C,0x5C,0x5C,0x5D,0x5D,0x5D,0x5D,0x5E,0x5E,
                0x5E,0x5F,0x5F,0x5F,0x60,0x60,0x60,0x61,0x61,0x61,0x61,0x62,0x62,0x62,0x63,0x63,
                0x63,0x64,0x64,0x64,0x65,0x65,0x66,0x66,0x66,0x67,0x67,0x67,0x68,0x68,0x68,0x69,
                0x69,0x6A,0x6A,0x6A,0x6B,0x6B,0x6B,0x6C,0x6C,0x6D,0x6D,0x6D,0x6E,0x6E,0x6F,0x6F,
                0x70,0x70,0x70,0x71,0x71,0x72,0x72,0x73,0x73,0x73,0x74,0x74,0x75,0x75,0x76,0x76,
                0x77,0x77,0x78,0x78,0x78,0x79,0x79,0x7A,0x7A,0x7B,0x7B,0x7C,0x7C,0x7D,0x7D,0x7E,
                0x7E,0x7F,0x7F,0x80,0x80,0x81,0x81,0x82,0x83,0x83,0x84,0x84,0x85,0x85,0x86,0x86,
                0x87,0x88,0x88,0x89,0x89,0x8A,0x8A,0x8B,0x8C,0x8C,0x8D,0x8D,0x8E,0x8F,0x8F,0x90,
                0x90,0x91,0x92,0x92,0x93,0x94,0x94,0x95,0x95,0x96,0x97,0x97,0x98,0x99,0x99,0x9A,
                0x9B,0x9B,0x9C,0x9D,0x9D,0x9E,0x9F,0xA0,0xA0,0xA1,0xA2,0xA2,0xA3,0xA4,0xA5,0xA5,
                0xA6,0xA7,0xA7,0xA8,0xA9,0xAA,0xAA,0xAB,0xAC,0xAD,0xAE,0xAE,0xAF,0xB0,0xB1,0xB1,
                0xB2,0xB3,0xB4,0xB5,0xB6,0xB6,0xB7,0xB8,0xB9,0xBA,0xBA,0xBB,0xBC,0xBD,0xBE,0xBF,
                0xC0,0xC1,0xC1,0xC2,0xC3,0xC4,0xC5,0xC6,0xC7,0xC8,0xC9,0xC9,0xCA,0xCB,0xCC,0xCD,
                0xCE,0xCF,0xD0,0xD1,0xD2,0xD3,0xD4,0xD5,0xD6,0xD7,0xD8,0xD9,0xDA,0xDB,0xDC,0xDD,
                0xDE,0xDF,0xE0,0xE1,0xE2,0xE3,0xE4,0xE5,0xE6,0xE7,0xE8,0xE9,0xEA,0xEB,0xED,0xEE,
                0xEF,0xF0,0xF1,0xF2,0xF3,0xF4,0xF5,0xF7,0xF8,0xF9,0xFA,0xFB,0xFC,0xFD,0xFF,0xFF,
            };
        }

        internal class Cipher
        {
            byte[]  m_table = new byte[0x100];

            public Cipher (int type, uint key1, uint key2)
            {
                if (0 == (key1 | key2))
                    type = 0;
                if (0 == type)
                    Init0();
                else if (1 == type)
                    Init1();
                else if (56 == type)
                    Init56 (key1, key2);
                else
                    throw new InvalidFormatException ("Unknown HCA cipher type");
            }

            public void Decipher (byte[] block)
            {
                for (int i = 0; i < block.Length; ++i)
                    block[i] = m_table[block[i]];
            }

            void Init0 ()
            {
                for (int i = 0; i < 0x100; ++i)
                    m_table[i] = (byte)i;
            }

            void Init1 ()
            {
                for (int i = 1, v = 0; i < 0xFF; ++i)
                {
                    v = (v * 13 + 11) & 0xFF;
                    if (0 == v || 0xFF == v)
                        v = (v * 13 + 11) & 0xFF;
                    m_table[i] = (byte)v;
                }
                m_table[0] = 0;
                m_table[0xFF] = 0xFF;
            }

            void Init56 (uint key1, uint key2)
            {
                throw new NotImplementedException ("Encrypted HCA streams not implemented");
            }
        }

        internal class Channel
        {
		    float[]  Block = new float[0x80];
		    float[]  Base = new float[0x80];
		    sbyte[]  Value = new sbyte[0x80];
		    sbyte[]  Scale = new sbyte[0x80];
		    sbyte[]  Value2 = new sbyte[8];
            int      Type;
            int      ScaleVersion = 1;
            int      ValuePtr; // pointer within Value
            int      Count;
            float[]  Sample1 = new float[0x80];
            float[]  Sample2 = new float[0x80];
            float[]  Sample3 = new float[0x80];
            public float[,] Samples = new float[8,0x80];

            public Channel (int type, int r06, int r07)
            {
                Type = type;
                ValuePtr = r06 + r07;
                int count = r06;
                if (type != 2)
                    count += r07;
                Count = count;
            }

            public void Decode1 (HsaBitStream bits, int a, int b, byte[] ath)
            {
                int v = bits.GetBits (3);
                if (v >= 6)
                {
                    for (int i = 0; i < Count; ++i)
                        Value[i] = (sbyte)bits.GetBits (6);
                }
                else if (v != 0)
                {
                    int v1 = bits.GetBits (6);
                    int v2 = (1 << v) - 1;
                    int v3 = v2 >> 1;
                    Value[0] = (sbyte)v1;
                    for (int i = 1; i < Count; ++i)
                    {
                        int v4 = bits.GetBits (v);
                        if (v4 != v2)
                            v1 += v4 - v3;
                        else
                            v1 = bits.GetBits (6);
                        Value[i] = (sbyte)v1;
                    }
                }
                else
                {
                    for (int i = 0; i < Value.Length; ++i)
                        Value[i] = 0;
                }
                if (2 == Type)
                {
                    v = bits.Peek (4);
                    Value2[0] = (sbyte)v;
                    if (v < 15)
                    {
                        for (int i = 0; i < 8; ++i)
                            Value2[i] = (sbyte)bits.GetBits (4);
                    }
                }
                else
                {
                    for (int i = 0; i < a; ++i)
                        Value[ValuePtr+i] = (sbyte)bits.GetBits (6);
                }
                for(int i = 0; i < Count; ++i)
                {
                    v = Value[i];
                    if (v != 0)
                    {
                        v = ath[i] + ((b + i) >> 8) - ((v * 5) >> 1) + 1;
                        if (v < 0)
                            v = 15;
                        else if (v >= 0x39)
                            v = 1;
                        else
                            v = ScaleTable[ScaleVersion, v];
                    }
                    Scale[i] = (sbyte)v;
                }
                for (int i = Count; i < 0x80; ++i)
                    Scale[i] = 0;
                for (int i = 0; i < Count; ++i)
                    Base[i] = Decode1Value[Value[i]] * Decode1Scale[Scale[i]];
            }

            public void Decode2 (HsaBitStream bits)
            {
                for (int i = 0; i < Count; ++i)
                {
                    float f;
                    int scale = Scale[i];
                    int bit_size = Decode2Table1[scale];
                    int v = bits.GetBits (bit_size);
                    if (scale < 8)
                    {
                        v += scale << 4;
                        bits.Seek (Decode2Table2[v] - bit_size);
                        f = Decode2Table3[v];
                    }
                    else
                    {
                        v = (1 - ((v & 1) << 1)) * (v >> 1);
                        if (0 == v)
                            bits.Seek (-1);
                        f = (float)v;
                    }
                    Block[i] = Base[i] * f;
                }
                for (int i = Count; i < 0x80; ++i)
                    Block[i] = 0;
            }

            public void Decode3 (int a, int b, int c, int d)
            {
                if (Type != 2 && b != 0)
                {
                    for (int i = 0, k = c, l = c - 1; i < a; ++i)
                    for(int j = 0; j < b && k < d; ++j, ++l)
                    {
                        Block[k++] = Decode3Table[Value[ValuePtr+i] - Value[l]] * Block[l];
                    }
                    Block[0x7F] = 0;
                }
            }

            public void Decode4 (int index, int a, int b, int c, Channel next)
            {
                if (1 == Type && c != 0)
                {
                    float f1 = Decode4Table[next.Value2[index]];
                    float f2 = f1 - 2.0f;
                    for (uint i = 0; i < a; ++i)
                    {
                        next.Block[b] = Block[b] * f2;
                        Block[b] *= f1;
                        ++b;
                    }
                }
            }

            public void Decode5 (int index)
            {
                int s, s1, s2;
                float[] src = Block;
                float[] dst = Sample1;
                for (int i = 0; i < 7; i++)
                {
                    int count1 = 1 << i;
                    int count2 = 0x40 >> i;
                    int d1 = 0;
                    int d2 = count2;
                    s = 0; // within src
                    for (int j = 0; j < count1; j++)
                    {
                        for (int k = 0; k < count2; k++)
                        {
                            float a = src[s++];
                            float b = src[s++];
                            dst[d1++] = a + b;
                            dst[d2++] = a - b;
                        }
                        d1 += count2;
                        d2 += count2;
                    }
                    var t = src;
                    src = dst;
                    dst = t;
                }
                src = Sample1;
                dst = Block;
                for (int i = 0; i < 7; i++)
                {
                    int count1 = 0x40 >> i;
                    int count2 = 1 << i;
                    int l1 = 0;
                    int l2 = 0;
                    s1 = 0;
                    s2 = count2; // within src
                    int d1 = 0;
                    int d2 = count2 * 2 - 1; // within dst
                    for (int j = 0; j < count1; j++)
                    {
                        for (int k = 0; k < count2; k++)
                        {
                            float a = src[s1++];
                            float b = src[s2++];
                            float c = Decode5Table1[i,l1++];
                            float d = Decode5Table2[i,l2++];
                            dst[d1++] = a * c - b * d;
                            dst[d2--] = a * d + b * c;
                        }
                        s1 += count2;
                        s2 += count2;
                        d1 += count2;
                        d2 += count2*3;
                    }
                    var t = src;
                    src = dst;
                    dst = t;
                }
                int w = 0; // within Sample2
                for (int i = 0; i < 0x80; i++)
                    Sample2[w++] = src[i];

                s = 0; // within Decode5Table3
                w = 0; // within Samples[index]
                s1 = 0x40; // within Sample2
                s2 = 0; // within Sample3
                for (int i = 0; i < 0x40; i++)
                    Samples[index,w++] = Sample2[s1++] * Decode5Table3[s++] + Sample3[s2++];
                for (int i = 0; i < 0x40; i++)
                    Samples[index,w++] = Decode5Table3[s++] * Sample2[--s1] - Sample3[s2++];
                s1 = 0x3F; // within Sample2
                s2 = 0; // within Sample3
                for (int i = 0; i < 0x40; i++)
                    Sample3[s2++] = Sample2[s1--] * Decode5Table3[--s];
                for (int i = 0; i < 0x40; i++)
                    Sample3[s2++] = Sample2[++s1] * Decode5Table3[--s];
            }

            static readonly byte[,] ScaleTable = {
                { 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0D, 0x0D,
                  0x0D, 0x0D, 0x0D, 0x0D, 0x0C, 0x0C, 0x0C, 0x0C,
                  0x0C, 0x0C, 0x0B, 0x0B, 0x0B, 0x0B, 0x0B, 0x0B,
                  0x0A, 0x0A, 0x0A, 0x0A, 0x0A, 0x0A, 0x0A, 0x09,
                  0x09, 0x09, 0x09, 0x09, 0x09, 0x08, 0x08, 0x08,
                  0x08, 0x08, 0x08, 0x07, 0x06, 0x06, 0x05, 0x04,
                  0x04, 0x04, 0x03, 0x03, 0x03, 0x02, 0x02, 0x02,
                  0x02, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                },
                { 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0D, 0x0D,
                  0x0D, 0x0D, 0x0D, 0x0D, 0x0C, 0x0C, 0x0C, 0x0C,
                  0x0C, 0x0C, 0x0B, 0x0B, 0x0B, 0x0B, 0x0B, 0x0B,
                  0x0A, 0x0A, 0x0A, 0x0A, 0x0A, 0x0A, 0x0A, 0x09,
                  0x09, 0x09, 0x09, 0x09, 0x09, 0x08, 0x08, 0x08,
                  0x08, 0x08, 0x08, 0x07, 0x06, 0x06, 0x05, 0x04,
                  0x04, 0x04, 0x03, 0x03, 0x03, 0x02, 0x02, 0x02,
                  0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                },
            };

            static readonly float[] Decode1Value = {
                1.588383e-7f, 2.116414e-7f, 2.819978e-7f, 3.757431e-7f, 5.006523e-7f, 6.670855e-7f, 8.888464e-7f, 1.184328e-6f,
                1.578037e-6f, 2.102628e-6f, 2.80161e-6f, 3.732956e-6f, 4.973912e-6f, 6.627403e-6f, 8.830567e-6f, 1.176613e-5f,
                1.567758e-5f, 2.088932e-5f, 2.783361e-5f, 3.708641e-5f, 4.941514e-5f, 6.584233e-5f, 8.773047e-5f, 0.0001168949f,
                0.0001557546f, 0.0002075325f, 0.0002765231f, 0.0003684484f, 0.0004909326f, 0.0006541346f, 0.0008715902f, 0.001161335f,
                0.001547401f, 0.002061807f, 0.002747219f, 0.003660484f, 0.004877347f, 0.006498737f, 0.008659128f, 0.0115377f,
                0.01537321f, 0.02048377f, 0.02729324f, 0.0363664f, 0.04845578f, 0.06456406f, 0.08602725f, 0.1146255f,
                0.1527307f, 0.2035034f, 0.2711546f, 0.3612952f, 0.4814015f,  0.641435f, 0.8546689f,  1.138789f,
                1.517359f,  2.021779f,  2.693884f,  3.589418f,  4.782658f,  6.372569f,  8.491017f,  11.31371f,
            };
            static readonly float[] Decode1Scale = {
                0, 2.0f/3, 2.0f/5, 2.0f/7, 2.0f/9, 2.0f/11, 2.0f/13, 2.0f/15,
                2.0f/31, 2.0f/63, 2.0f/127, 2.0f/255, 2.0f/511, 2.0f/1023, 2.0f/2047, 2.0f/4095,
            };
            static readonly byte[] Decode2Table1 = {
                0, 2, 3, 3, 4, 4, 4, 4, 5, 6, 7, 8, 9, 10, 11, 12,
            };
            static readonly byte[] Decode2Table2 = {
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                1, 1, 2, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                2, 2, 2, 2, 2, 2, 3, 3, 0, 0, 0, 0, 0, 0, 0, 0,
                2, 2, 3, 3, 3, 3, 3, 3, 0, 0, 0, 0, 0, 0, 0, 0,
                3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 4, 4,
                3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4,
                3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
                3, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
            };
            static readonly float[] Decode2Table3 = {
                0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,
                0,  0,  1, -1,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,
                0,  0,  1,  1, -1, -1,  2, -2,  0,  0,  0,  0,  0,  0,  0,  0,
                0,  0,  1, -1,  2, -2,  3, -3,  0,  0,  0,  0,  0,  0,  0,  0,
                0,  0,  1,  1, -1, -1,  2,  2, -2, -2,  3,  3, -3, -3,  4, -4,
                0,  0,  1,  1, -1, -1,  2,  2, -2, -2,  3, -3,  4, -4,  5, -5,
                0,  0,  1,  1, -1, -1,  2, -2,  3, -3,  4, -4,  5, -5,  6, -6,
                0,  0,  1, -1,  2, -2,  3, -3,  4, -4,  5, -5,  6, -6,  7, -7,
            };

            static readonly float[] Decode3Table = {
                1,          1.332433f,  1.775376f,  2.365569f,  3.151962f,  4.199776f,  5.595919f,  7.456184f,
                9.934862f,  13.23753f,  17.63812f,  23.50161f,  31.31431f,  41.72420f,  55.59468f,  74.07616f,
                98.70149f,  131.5131f,  175.2323f,  233.4852f,  311.1033f,  414.5242f,  552.3255f,  735.9365f,
                980.5858f,  1306.564f,  1740.909f,  2319.644f,  3090.769f,  4118.241f,  5487.278f,  7311.428f,
                9741.984f,  12980.54f,  17295.69f,  23045.34f,  30706.36f,  40914.16f,  54515.36f,  72638.03f,
                96785.28f,  128959.9f,  171830.3f,  228952.3f,  305063.5f,  406476.5f,  541602.6f,  721648.9f,
                961548.4f,  1281198f,   1707111f,   2274610f,   3030764f,   4038288f,   5380747f,   7169482f,
                9552851f, 1.272853e+7f, 1.695991e+7f, 2.259793e+7f, 3.011022e+7f, 4.011984e+7f, 5.345698e+7f, 0,
            };

            static readonly float[] Decode4Table = {
                2.0f,        1.857143f,  1.714286f,  1.571429f,  1.428571f,  1.285714f,  1.142857f, 1,
                0.8571429f, 0.7142857f, 0.5714286f, 0.4285714f, 0.2857143f, 0.1428571f, 0, 0,
                0, 1.870663e-8f, 2.492532e-8f, 3.321131e-8f, 4.425183e-8f, 5.896258e-8f, 7.856367e-8f, 1.046808e-7f,
                1.394801e-7f, 1.858478e-7f, 2.476297e-7f, 3.299498e-7f, 4.396359e-7f, 5.857852e-7f, 7.805192e-7f, 1.039989e-6f,
                1.385715e-6f, 1.846372e-6f, 2.460167e-6f, 3.278006e-6f, 4.367722e-6f, 5.819695e-6f, 7.754351e-6f, 1.033215e-5f,
                1.376689e-5f, 1.834346e-5f, 2.444142e-5f, 3.256654e-5f, 4.339272e-5f, 5.781787e-5f, 7.703841e-5f, 0.0001026485f,
                0.0001367722f, 0.0001822397f, 0.0002428221f, 0.0003235441f, 0.0004311007f, 0.0005744126f, 0.000765366f, 0.001019799f,
                0.001358813f, 0.001810526f, 0.002412404f, 0.003214366f, 0.004282926f, 0.00570671f, 0.007603806f, 0.01013156f,
                0.01349962f, 0.01798733f, 0.02396691f, 0.03193429f, 0.04255028f, 0.05669538f, 0.07554277f, 0.1006556f,
                0.1341169f, 0.1787017f, 0.2381079f, 0.3172627f, 0.4227312f, 0.5632608f, 0.7505071f, 0,
            };

            static readonly float[,] Decode5Table1 = {
                {
                0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f,
                0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f,
                0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f,
                0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f,
                0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f,
                0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f,
                0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f,
                0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f, 0.08166019f,
                }, {
                0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f,
                0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f,
                0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f,
                0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f,
                0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f,
                0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f,
                0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f,
                0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f, 0.9807853f, 0.8314696f,
                }, {
                0.9951847f, 0.9569404f, 0.8819213f, 0.7730104f, 0.9951847f, 0.9569404f, 0.8819213f, 0.7730104f,
                0.9951847f, 0.9569404f, 0.8819213f, 0.7730104f, 0.9951847f, 0.9569404f, 0.8819213f, 0.7730104f,
                0.9951847f, 0.9569404f, 0.8819213f, 0.7730104f, 0.9951847f, 0.9569404f, 0.8819213f, 0.7730104f,
                0.9951847f, 0.9569404f, 0.8819213f, 0.7730104f, 0.9951847f, 0.9569404f, 0.8819213f, 0.7730104f,
                0.9951847f, 0.9569404f, 0.8819213f, 0.7730104f, 0.9951847f, 0.9569404f, 0.8819213f, 0.7730104f,
                0.9951847f, 0.9569404f, 0.8819213f, 0.7730104f, 0.9951847f, 0.9569404f, 0.8819213f, 0.7730104f,
                0.9951847f, 0.9569404f, 0.8819213f, 0.7730104f, 0.9951847f, 0.9569404f, 0.8819213f, 0.7730104f,
                0.9951847f, 0.9569404f, 0.8819213f, 0.7730104f, 0.9951847f, 0.9569404f, 0.8819213f, 0.7730104f,
                }, {
                0.9987954f, 0.9891765f, 0.9700313f, 0.9415441f, 0.9039893f, 0.8577286f, 0.8032075f, 0.7409511f,
                0.9987954f, 0.9891765f, 0.9700313f, 0.9415441f, 0.9039893f, 0.8577286f, 0.8032075f, 0.7409511f,
                0.9987954f, 0.9891765f, 0.9700313f, 0.9415441f, 0.9039893f, 0.8577286f, 0.8032075f, 0.7409511f,
                0.9987954f, 0.9891765f, 0.9700313f, 0.9415441f, 0.9039893f, 0.8577286f, 0.8032075f, 0.7409511f,
                0.9987954f, 0.9891765f, 0.9700313f, 0.9415441f, 0.9039893f, 0.8577286f, 0.8032075f, 0.7409511f,
                0.9987954f, 0.9891765f, 0.9700313f, 0.9415441f, 0.9039893f, 0.8577286f, 0.8032075f, 0.7409511f,
                0.9987954f, 0.9891765f, 0.9700313f, 0.9415441f, 0.9039893f, 0.8577286f, 0.8032075f, 0.7409511f,
                0.9987954f, 0.9891765f, 0.9700313f, 0.9415441f, 0.9039893f, 0.8577286f, 0.8032075f, 0.7409511f,
                }, {
                0.9996988f, 0.9972904f, 0.9924796f, 0.9852777f, 0.9757021f, 0.9637761f, 0.9495282f, 0.9329928f,
                0.9142098f, 0.8932243f,  0.870087f, 0.8448536f, 0.8175848f, 0.7883464f, 0.7572088f, 0.7242471f,
                0.9996988f, 0.9972904f, 0.9924796f, 0.9852777f, 0.9757021f, 0.9637761f, 0.9495282f, 0.9329928f,
                0.9142098f, 0.8932243f,  0.870087f, 0.8448536f, 0.8175848f, 0.7883464f, 0.7572088f, 0.7242471f,
                0.9996988f, 0.9972904f, 0.9924796f, 0.9852777f, 0.9757021f, 0.9637761f, 0.9495282f, 0.9329928f,
                0.9142098f, 0.8932243f,  0.870087f, 0.8448536f, 0.8175848f, 0.7883464f, 0.7572088f, 0.7242471f,
                0.9996988f, 0.9972904f, 0.9924796f, 0.9852777f, 0.9757021f, 0.9637761f, 0.9495282f, 0.9329928f,
                0.9142098f, 0.8932243f,  0.870087f, 0.8448536f, 0.8175848f, 0.7883464f, 0.7572088f, 0.7242471f,
                }, {
                0.9999247f, 0.9993224f, 0.9981181f, 0.9963126f,  0.993907f, 0.9909027f, 0.9873014f, 0.9831055f,
                0.9783174f,   0.97294f, 0.9669765f, 0.9604305f,  0.953306f, 0.9456073f,  0.937339f, 0.9285061f,
                0.9191139f,  0.909168f, 0.8986745f, 0.8876396f, 0.8760701f, 0.8639728f, 0.8513552f, 0.8382247f,
                0.8245893f, 0.8104572f, 0.7958369f, 0.7807372f, 0.7651672f, 0.7491364f, 0.7326543f, 0.7157308f,
                0.9999247f, 0.9993224f, 0.9981181f, 0.9963126f,  0.993907f, 0.9909027f, 0.9873014f, 0.9831055f,
                0.9783174f,   0.97294f, 0.9669765f, 0.9604305f,  0.953306f, 0.9456073f,  0.937339f, 0.9285061f,
                0.9191139f,  0.909168f, 0.8986745f, 0.8876396f, 0.8760701f, 0.8639728f, 0.8513552f, 0.8382247f,
                0.8245893f, 0.8104572f, 0.7958369f, 0.7807372f, 0.7651672f, 0.7491364f, 0.7326543f, 0.7157308f,
                }, {
                0.9999812f, 0.9998306f, 0.9995294f, 0.9990777f, 0.9984756f,  0.997723f, 0.9968203f, 0.9957674f,
                0.9945646f, 0.9932119f, 0.9917098f, 0.9900582f, 0.9882576f, 0.9863081f, 0.9842101f, 0.9819639f,
                0.9795698f, 0.9770281f, 0.9743394f, 0.9715039f, 0.9685221f, 0.9653944f, 0.9621214f, 0.9587035f,
                0.9551412f,  0.951435f, 0.9475856f, 0.9435934f, 0.9394592f, 0.9351835f, 0.9307669f, 0.9262102f,
                0.921514f, 0.9166791f,  0.911706f, 0.9065957f, 0.9013488f, 0.8959662f, 0.8904487f, 0.8847971f,
                0.8790122f,  0.873095f, 0.8670462f,  0.860867f,  0.854558f, 0.8481203f,  0.841555f, 0.8348629f,
                0.8280451f, 0.8211025f, 0.8140363f, 0.8068476f, 0.7995372f, 0.7921066f, 0.7845566f, 0.7768885f,
                0.7691033f, 0.7612024f, 0.7531868f, 0.7450578f, 0.7368166f, 0.7284644f, 0.7200025f, 0.7114322f,
                }
            };
            static readonly float[,] Decode5Table2 = {
                {
                -0.03382476f,  0.03382476f,  0.03382476f, -0.03382476f,  0.03382476f, -0.03382476f, -0.03382476f,  0.03382476f,
                 0.03382476f, -0.03382476f, -0.03382476f,  0.03382476f, -0.03382476f,  0.03382476f,  0.03382476f, -0.03382476f,
                 0.03382476f, -0.03382476f, -0.03382476f,  0.03382476f, -0.03382476f,  0.03382476f,  0.03382476f, -0.03382476f,
                -0.03382476f,  0.03382476f,  0.03382476f, -0.03382476f,  0.03382476f, -0.03382476f, -0.03382476f,  0.03382476f,
                 0.03382476f, -0.03382476f, -0.03382476f,  0.03382476f, -0.03382476f,  0.03382476f,  0.03382476f, -0.03382476f,
                -0.03382476f,  0.03382476f,  0.03382476f, -0.03382476f,  0.03382476f, -0.03382476f, -0.03382476f,  0.03382476f,
                -0.03382476f,  0.03382476f,  0.03382476f, -0.03382476f,  0.03382476f, -0.03382476f, -0.03382476f,  0.03382476f,
                 0.03382476f, -0.03382476f, -0.03382476f,  0.03382476f, -0.03382476f,  0.03382476f,  0.03382476f, -0.03382476f,
                }, {
                -0.1950903f, -0.5555702f,  0.1950903f,  0.5555702f,  0.1950903f,  0.5555702f, -0.1950903f, -0.5555702f,
                 0.1950903f,  0.5555702f, -0.1950903f, -0.5555702f, -0.1950903f, -0.5555702f,  0.1950903f,  0.5555702f,
                 0.1950903f,  0.5555702f, -0.1950903f, -0.5555702f, -0.1950903f, -0.5555702f,  0.1950903f,  0.5555702f,
                -0.1950903f, -0.5555702f,  0.1950903f,  0.5555702f,  0.1950903f,  0.5555702f, -0.1950903f, -0.5555702f,
                 0.1950903f,  0.5555702f, -0.1950903f, -0.5555702f, -0.1950903f, -0.5555702f,  0.1950903f,  0.5555702f,
                -0.1950903f, -0.5555702f,  0.1950903f,  0.5555702f,  0.1950903f,  0.5555702f, -0.1950903f, -0.5555702f,
                -0.1950903f, -0.5555702f,  0.1950903f,  0.5555702f,  0.1950903f,  0.5555702f, -0.1950903f, -0.5555702f,
                 0.1950903f,  0.5555702f, -0.1950903f, -0.5555702f, -0.1950903f, -0.5555702f,  0.1950903f,  0.5555702f,
                }, {
                -0.09801714f, -0.2902847f, -0.4713967f, -0.6343933f,  0.09801714f,  0.2902847f,  0.4713967f,  0.6343933f,
                 0.09801714f,  0.2902847f,  0.4713967f,  0.6343933f, -0.09801714f, -0.2902847f, -0.4713967f, -0.6343933f,
                 0.09801714f,  0.2902847f,  0.4713967f,  0.6343933f, -0.09801714f, -0.2902847f, -0.4713967f, -0.6343933f,
                -0.09801714f, -0.2902847f, -0.4713967f, -0.6343933f,  0.09801714f,  0.2902847f,  0.4713967f,  0.6343933f,
                 0.09801714f,  0.2902847f,  0.4713967f,  0.6343933f, -0.09801714f, -0.2902847f, -0.4713967f, -0.6343933f,
                -0.09801714f, -0.2902847f, -0.4713967f, -0.6343933f,  0.09801714f,  0.2902847f,  0.4713967f,  0.6343933f,
                -0.09801714f, -0.2902847f, -0.4713967f, -0.6343933f,  0.09801714f,  0.2902847f,  0.4713967f,  0.6343933f,
                 0.09801714f,  0.2902847f,  0.4713967f,  0.6343933f, -0.09801714f, -0.2902847f, -0.4713967f, -0.6343933f,
                }, {
                -0.04906768f, -0.1467305f, -0.2429802f, -0.3368899f, -0.4275551f, -0.5141028f, -0.5956993f, -0.671559f,
                 0.04906768f,  0.1467305f,  0.2429802f,  0.3368899f,  0.4275551f,  0.5141028f,  0.5956993f,  0.671559f,
                 0.04906768f,  0.1467305f,  0.2429802f,  0.3368899f,  0.4275551f,  0.5141028f,  0.5956993f,  0.671559f,
                -0.04906768f, -0.1467305f, -0.2429802f, -0.3368899f, -0.4275551f, -0.5141028f, -0.5956993f, -0.671559f,
                 0.04906768f,  0.1467305f,  0.2429802f,  0.3368899f,  0.4275551f,  0.5141028f,  0.5956993f,  0.671559f,
                -0.04906768f, -0.1467305f, -0.2429802f, -0.3368899f, -0.4275551f, -0.5141028f, -0.5956993f, -0.671559f,
                -0.04906768f, -0.1467305f, -0.2429802f, -0.3368899f, -0.4275551f, -0.5141028f, -0.5956993f, -0.671559f,
                 0.04906768f,  0.1467305f,  0.2429802f,  0.3368899f,  0.4275551f,  0.5141028f,  0.5956993f,  0.671559f,
                }, {
                -0.02454123f, -0.07356457f, -0.1224107f, -0.1709619f, -0.2191012f, -0.2667128f, -0.3136818f, -0.3598951f,
                -0.4052413f,  -0.4496113f,  -0.4928982f, -0.5349976f, -0.5758082f, -0.6152316f, -0.6531729f, -0.6895406f,
                 0.02454123f,  0.07356457f,  0.1224107f,  0.1709619f,  0.2191012f,  0.2667128f,  0.3136818f,  0.3598951f,
                 0.4052413f,   0.4496113f,   0.4928982f,  0.5349976f,  0.5758082f,  0.6152316f,  0.6531729f,  0.6895406f,
                 0.02454123f,  0.07356457f,  0.1224107f,  0.1709619f,  0.2191012f,  0.2667128f,  0.3136818f,  0.3598951f,
                 0.4052413f,   0.4496113f,   0.4928982f,  0.5349976f,  0.5758082f,  0.6152316f,  0.6531729f,  0.6895406f,
                -0.02454123f, -0.07356457f, -0.1224107f, -0.1709619f, -0.2191012f, -0.2667128f, -0.3136818f, -0.3598951f,
                -0.4052413f,  -0.4496113f,  -0.4928982f, -0.5349976f, -0.5758082f, -0.6152316f, -0.6531729f, -0.6895406f,
                }, {
                -0.01227154f,-0.03680722f, -0.06132074f,-0.08579731f, -0.1102222f, -0.1345807f, -0.1588582f, -0.1830399f,
                -0.2071114f, -0.2310581f,  -0.2548656f, -0.2785197f,  -0.3020059f, -0.3253103f, -0.3484187f, -0.3713172f,
                -0.393992f,  -0.4164295f,  -0.4386162f, -0.4605387f,  -0.4821838f, -0.5035384f, -0.5245897f, -0.545325f,
                -0.5657318f, -0.5857978f,  -0.6055111f, -0.6248595f,  -0.6438316f, -0.6624158f, -0.680601f,  -0.6983762f,
                 0.01227154f, 0.03680722f,  0.06132074f, 0.08579731f,  0.1102222f,  0.1345807f,  0.1588582f,  0.1830399f,
                 0.2071114f,  0.2310581f,   0.2548656f,  0.2785197f,   0.3020059f,  0.3253103f,  0.3484187f,  0.3713172f,
                 0.393992f,   0.4164295f,   0.4386162f,  0.4605387f,   0.4821838f,  0.5035384f,  0.5245897f,  0.545325f,
                 0.5657318f,  0.5857978f,   0.6055111f,  0.6248595f,   0.6438316f,  0.6624158f,  0.680601f,   0.6983762f,
                }, {
                -0.006135885f, -0.01840673f, -0.0306748f, -0.04293826f, -0.05519525f, -0.06744392f, -0.07968244f, -0.09190895f,
                -0.1041216f, -0.1163186f, -0.1284981f, -0.1406582f, -0.1527972f, -0.1649131f, -0.1770042f, -0.1890687f,
                -0.2011046f, -0.2131103f, -0.2250839f, -0.2370236f, -0.2489276f, -0.2607941f, -0.2726214f, -0.2844075f,
                -0.2961509f, -0.3078496f, -0.319502f,  -0.3311063f, -0.3426607f, -0.3541635f, -0.365613f,  -0.3770074f,
                -0.388345f,  -0.3996242f, -0.4108432f, -0.4220003f, -0.4330938f, -0.4441221f, -0.4550836f, -0.4659765f,
                -0.4767992f, -0.4875502f, -0.4982277f, -0.5088301f, -0.519356f,  -0.5298036f, -0.5401714f, -0.550458f,
                -0.5606616f, -0.5707808f, -0.5808139f, -0.5907597f, -0.6006165f, -0.6103828f, -0.6200572f, -0.6296383f,
                -0.6391245f, -0.6485144f, -0.6578067f, -0.6669999f, -0.6760927f, -0.6850837f, -0.6939715f, -0.7027547f,
                }
            };
            static readonly float[] Decode5Table3 = {
                0.0006905338f, 0.001976235f, 0.003673865f, 0.00572424f, 0.008096703f, 0.01077318f, 0.01374252f, 0.01699786f,
                0.02053526f, 0.0243529f, 0.02845052f, 0.03282909f, 0.03749062f, 0.0424379f, 0.04767443f, 0.0532043f,
                0.05903211f, 0.06516288f, 0.07160201f, 0.07835522f, 0.08542849f, 0.09282802f, 0.1005602f, 0.1086314f,
                0.1170481f,  0.125817f, 0.1349443f, 0.1444365f, 0.1542995f, 0.1645391f, 0.1751607f, 0.1861692f,
                0.1975687f,  0.209363f, 0.2215546f, 0.2341454f,  0.247136f, 0.2605258f, 0.2743127f, 0.2884932f,
                0.3030619f, 0.3180117f, 0.3333333f, 0.3490153f, 0.3650438f, 0.3814027f, 0.3980731f, 0.4150335f,
                0.4322598f,  0.449725f, 0.4673996f, 0.4852512f, 0.5032449f, 0.5213438f, 0.5395085f, 0.5576978f,
                0.5758689f,  0.593978f, 0.6119806f, 0.6298314f,  0.647486f, 0.6649002f, 0.6820312f, 0.6988376f,

                -0.7152804f, -0.7313231f, -0.7469321f, -0.7620773f, -0.7767318f, -0.7908728f, -0.8044813f, -0.817542f,
                -0.8300441f, -0.8419802f, -0.8533467f, -0.8641438f, -0.8743748f, -0.8840462f, -0.8931671f, -0.9017491f,
                -0.9098061f, -0.9173537f, -0.924409f,  -0.9309903f, -0.937117f,  -0.942809f,  -0.9480868f, -0.9529709f,
                -0.9574819f, -0.9616405f, -0.9654669f, -0.9689808f, -0.9722016f, -0.975148f,  -0.977838f,  -0.980289f,
                -0.9825177f, -0.9845399f, -0.9863706f, -0.9880241f, -0.9895141f, -0.9908532f, -0.9920534f, -0.9931263f,
                -0.9940821f, -0.994931f,  -0.9956822f, -0.9963443f, -0.9969255f, -0.9974333f, -0.9978746f, -0.9982561f,
                -0.9985837f, -0.9988629f, -0.9990991f, -0.999297f,  -0.999461f,  -0.9995952f, -0.9997034f, -0.9997891f,
                -0.9998555f, -0.9999056f, -0.9999419f, -0.9999672f, -0.9999836f, -0.9999933f, -0.999998f,  -0.9999998f,
            };
        }

        static readonly ushort[] CheckSumTable = {
            0x0000, 0x8005, 0x800F, 0x000A, 0x801B, 0x001E, 0x0014, 0x8011,
            0x8033, 0x0036, 0x003C, 0x8039, 0x0028, 0x802D, 0x8027, 0x0022,
            0x8063, 0x0066, 0x006C, 0x8069, 0x0078, 0x807D, 0x8077, 0x0072,
            0x0050, 0x8055, 0x805F, 0x005A, 0x804B, 0x004E, 0x0044, 0x8041,
            0x80C3, 0x00C6, 0x00CC, 0x80C9, 0x00D8, 0x80DD, 0x80D7, 0x00D2,
            0x00F0, 0x80F5, 0x80FF, 0x00FA, 0x80EB, 0x00EE, 0x00E4, 0x80E1,
            0x00A0, 0x80A5, 0x80AF, 0x00AA, 0x80BB, 0x00BE, 0x00B4, 0x80B1,
            0x8093, 0x0096, 0x009C, 0x8099, 0x0088, 0x808D, 0x8087, 0x0082,
            0x8183, 0x0186, 0x018C, 0x8189, 0x0198, 0x819D, 0x8197, 0x0192,
            0x01B0, 0x81B5, 0x81BF, 0x01BA, 0x81AB, 0x01AE, 0x01A4, 0x81A1,
            0x01E0, 0x81E5, 0x81EF, 0x01EA, 0x81FB, 0x01FE, 0x01F4, 0x81F1,
            0x81D3, 0x01D6, 0x01DC, 0x81D9, 0x01C8, 0x81CD, 0x81C7, 0x01C2,
            0x0140, 0x8145, 0x814F, 0x014A, 0x815B, 0x015E, 0x0154, 0x8151,
            0x8173, 0x0176, 0x017C, 0x8179, 0x0168, 0x816D, 0x8167, 0x0162,
            0x8123, 0x0126, 0x012C, 0x8129, 0x0138, 0x813D, 0x8137, 0x0132,
            0x0110, 0x8115, 0x811F, 0x011A, 0x810B, 0x010E, 0x0104, 0x8101,
            0x8303, 0x0306, 0x030C, 0x8309, 0x0318, 0x831D, 0x8317, 0x0312,
            0x0330, 0x8335, 0x833F, 0x033A, 0x832B, 0x032E, 0x0324, 0x8321,
            0x0360, 0x8365, 0x836F, 0x036A, 0x837B, 0x037E, 0x0374, 0x8371,
            0x8353, 0x0356, 0x035C, 0x8359, 0x0348, 0x834D, 0x8347, 0x0342,
            0x03C0, 0x83C5, 0x83CF, 0x03CA, 0x83DB, 0x03DE, 0x03D4, 0x83D1,
            0x83F3, 0x03F6, 0x03FC, 0x83F9, 0x03E8, 0x83ED, 0x83E7, 0x03E2,
            0x83A3, 0x03A6, 0x03AC, 0x83A9, 0x03B8, 0x83BD, 0x83B7, 0x03B2,
            0x0390, 0x8395, 0x839F, 0x039A, 0x838B, 0x038E, 0x0384, 0x8381,
            0x0280, 0x8285, 0x828F, 0x028A, 0x829B, 0x029E, 0x0294, 0x8291,
            0x82B3, 0x02B6, 0x02BC, 0x82B9, 0x02A8, 0x82AD, 0x82A7, 0x02A2,
            0x82E3, 0x02E6, 0x02EC, 0x82E9, 0x02F8, 0x82FD, 0x82F7, 0x02F2,
            0x02D0, 0x82D5, 0x82DF, 0x02DA, 0x82CB, 0x02CE, 0x02C4, 0x82C1,
            0x8243, 0x0246, 0x024C, 0x8249, 0x0258, 0x825D, 0x8257, 0x0252,
            0x0270, 0x8275, 0x827F, 0x027A, 0x826B, 0x026E, 0x0264, 0x8261,
            0x0220, 0x8225, 0x822F, 0x022A, 0x823B, 0x023E, 0x0234, 0x8231,
            0x8213, 0x0216, 0x021C, 0x8219, 0x0208, 0x820D, 0x8207, 0x0202,
        };

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_channel.Dispose();
                m_block.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }

    /// <summary>
    /// BitStream with peeking and seeking capability.
    /// </summary>
    internal class HsaBitStream : BitStream
    {
        public HsaBitStream (Stream file, bool leave_open = false)
            : base (file, leave_open)
        {
        }

        public int Peek (int count)
        {
            while (m_cached_bits < count)
            {
                int b = m_input.ReadByte();
                if (-1 == b)
                    return -1;
                m_bits = (m_bits << 8) | b;
                m_cached_bits += 8;
            }
            int mask = (1 << count) - 1;
            return (m_bits >> (m_cached_bits - count)) & mask;
        }

        public int GetBits (int count)
        {
            var b = Peek (count);
            m_cached_bits -= count;
            return b;
        }

        public void Seek (int offset)
        {
            if (offset > 0 && offset <= m_cached_bits)
            {
                m_cached_bits -= offset;
                return;
            }
            var position = Math.Max (Input.Position * 8 - m_cached_bits + offset, 0);
            Reset();
            Input.Position = position / 8;
            int bit_pos = (int)position & 7;
            if (0 != bit_pos)
                GetBits (bit_pos);
        }
    }
}
