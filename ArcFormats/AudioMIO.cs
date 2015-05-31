//! \file       AudioMIO.cs
//! \date       Thu May 28 13:33:07 2015
//! \brief      Entis audio format implementation.
//
// Copyright (C) 2015 by morkt
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
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Threading;
using GameRes.Utility;

namespace GameRes.Formats.Entis
{
    [Export(typeof(AudioFormat))]
    public class MioAudio : AudioFormat
    {
        public override string         Tag { get { return "MIO"; } }
        public override string Description { get { return "Entis engine compressed audio format"; } }
        public override uint     Signature { get { return 0x69746e45u; } } // 'Enti'

        public override SoundInput TryOpen (Stream file)
        {
            byte[] header = new byte[0x40];
            if (header.Length != file.Read (header, 0, header.Length))
                return null;
            if (0x03000100 != LittleEndian.ToUInt32 (header, 8))
                return null;
            if (!Binary.AsciiEqual (header, 0x10, "Music Interleaved and Orthogonal transformed"))
                return null;

            return new MioInput (file);
        }
    }

    public class MioInput : SoundInput
    {
        MioInfoHeader           m_info;
        long                    m_stream_pos;
        int                     m_bitrate;
        uint                    m_total_samples;
        ERISADecodeContext      m_pmioc;
        MioDecoder              m_pmiod;
        Stream                  m_decoded_stream;

        public int   ChannelCount { get { return m_info.ChannelCount; } }
        public uint BitsPerSample { get { return m_info.BitsPerSample; } }

        public override int   SourceBitrate { get { return m_bitrate; } }
        public override string SourceFormat { get { return "mio"; } }

        #region Stream Members
        public override bool        CanSeek { get { return m_decoded_stream.CanSeek; } }

        public override long Position
        {
            get { return m_decoded_stream.Position; }
            set { m_decoded_stream.Position = value; }
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            return Read_Threaded (buffer, offset, count);
        }
        #endregion

        public MioInput (Stream file) : base (file)
        {
            file.Position = 0x40;
            using (var erif = new EriFile (file))
            {
                var section = erif.ReadSection();
                if (section.Id != "Header  " || section.Length <= 0 || section.Length > int.MaxValue)
                    throw new InvalidFormatException();
                m_stream_pos = 0x50 + section.Length;
                int header_size = (int)section.Length;
                while (header_size > 8)
                {
                    section = erif.ReadSection();
                    header_size -= 8;
                    if (section.Length <= 0 || section.Length > header_size)
                        break;
                    if ("SoundInf" == section.Id)
                    {
                        m_info = new MioInfoHeader();
                        m_info.Version        = erif.ReadInt32();
                        m_info.Transformation = (CvType)erif.ReadInt32();
                        m_info.Architecture   = (EriCode)erif.ReadInt32();
                        m_info.ChannelCount   = erif.ReadInt32();
                        m_info.SamplesPerSec  = erif.ReadUInt32();
                        m_info.BlocksetCount  = erif.ReadUInt32();
                        m_info.SubbandDegree  = erif.ReadInt32();
                        m_info.AllSampleCount = erif.ReadUInt32();
                        m_info.LappedDegree   = erif.ReadUInt32();
                        m_info.BitsPerSample  = erif.ReadUInt32();
                        break;
                    }
                    header_size -= (int)section.Length;
                    erif.BaseStream.Seek (section.Length, SeekOrigin.Current);
                }
                if (null == m_info)
                    throw new InvalidFormatException ("MIO sound header not found");

                erif.BaseStream.Position = m_stream_pos;
                var stream_size = erif.FindSection ("Stream  ");
                m_stream_pos = erif.BaseStream.Position;

                m_pmiod = new MioDecoder (m_info);
                if (EriCode.Nemesis != m_info.Architecture)
                    m_pmioc = new HuffmanDecodeContext (0x10000);
                else
                    throw new NotImplementedException ("MIO Nemesis encoding not implemented");

                int pcm_bitrate = (int)(m_info.SamplesPerSec * BitsPerSample * ChannelCount);
                var format = new GameRes.WaveFormat();
                format.FormatTag                = 1;
                format.Channels                 = (ushort)ChannelCount;
                format.SamplesPerSecond         = m_info.SamplesPerSec;
                format.BitsPerSample            = (ushort)BitsPerSample;
                format.BlockAlign               = (ushort)(BitsPerSample/8*format.Channels);
                format.AverageBytesPerSecond    = (uint)pcm_bitrate/8;
                this.Format = format;
                m_decoded_stream = LoadChunks (erif);

                if (0 != m_total_samples)
                    m_bitrate = (int)(stream_size * 8 * m_info.SamplesPerSec / m_total_samples);
                this.PcmSize = m_total_samples * ChannelCount * BitsPerSample / 8;
                m_decoded_stream.Position = 0;
            }
        }

        class MioChunk : MioDataHeader
        {
            public uint FirstSample;
            public long Position;
            public uint Size;
        }

        class ChunkStream : Stream
        {
            Stream      m_source;
            MioChunk    m_chunk;

            public ChunkStream (Stream source, MioChunk chunk)
            {
                m_source = source;
                m_chunk = chunk;
                m_source.Position = m_chunk.Position;
            }

            public override bool  CanRead { get { return true; } }
            public override bool CanWrite { get { return false; } }
            public override bool  CanSeek { get { return m_source.CanSeek; } }
            public override long   Length { get { return m_chunk.Size; } }

            public override long Position
            {
                get { return m_source.Position-m_chunk.Position; }
                set { Seek (value, SeekOrigin.Begin); }
            }

            public override long Seek (long offset, SeekOrigin origin)
            {
                if (origin == SeekOrigin.Begin)
                    offset += m_chunk.Position;
                else if (origin == SeekOrigin.Current)
                    offset += m_source.Position;
                else
                    offset += m_chunk.Position + m_chunk.Size;
                if (offset < m_chunk.Position)
                    offset = m_chunk.Position;
                m_source.Position = offset;
                return offset - m_chunk.Position;
            }

            public override void Flush()
            {
                m_source.Flush();
            }

            public override int Read (byte[] buf, int index, int count)
            {
                long remaining = (m_chunk.Position + m_chunk.Size) - m_source.Position;
                if (count > remaining)
                    count = (int)remaining;
                if (count <= 0)
                    return 0;
                return m_source.Read (buf, index, count);
            }

            public override void SetLength (long length)
            {
                throw new System.NotSupportedException ();
            }

            public override void Write (byte[] buffer, int offset, int count)
            {
                throw new System.NotSupportedException ();
            }

            public override void WriteByte (byte value)
            {
                throw new System.NotSupportedException ();
            }
        }

        private Stream LoadChunks (EriFile erif)
        {
            uint current_sample = 0;
            List<MioChunk> chunks = new List<MioChunk>();
            try
            {
                erif.BaseStream.Position = m_stream_pos;
                for (;;)
                {
                    long chunk_length = erif.FindSection ("SoundStm");
                    if (chunk_length > int.MaxValue)
                        throw new FileSizeException();
                    var chunk = new MioChunk();
                    chunk.FirstSample = current_sample;
                    chunk.Version     = erif.ReadByte();
                    chunk.Flags       = erif.ReadByte();
                    erif.ReadInt16();
                    chunk.SampleCount = erif.ReadUInt32();
                    chunk.Position    = erif.BaseStream.Position;
                    chunk.Size        = (uint)(chunk_length - 8);
                    current_sample += chunk.SampleCount;
                    chunks.Add (chunk);
                    erif.BaseStream.Seek (chunk.Size, SeekOrigin.Current);
                }
            }
            catch (EndOfStreamException) { /* ignore EOF errors */ }
            m_total_samples = current_sample;
            if (0 == m_total_samples)
            {
                m_decode_finished = true;
                return Stream.Null;
            }
            uint sample_bytes = (uint)ChannelCount * BitsPerSample / 8;
            var total_bytes = m_total_samples * sample_bytes;

            m_wait_handles = new WaitHandle[2] { m_available_chunk, m_decode_complete };
            m_chunk_queue = new ConcurrentQueue<byte[]>();
            m_worker = new BackgroundWorker();
            m_worker.WorkerSupportsCancellation = true;
            m_worker.DoWork += DoWork_Decode;
            m_worker.RunWorkerAsync (chunks);
            return new MemoryStream ((int)total_bytes);
        }

        bool                    m_decode_finished = false;
        AutoResetEvent          m_decode_complete = new AutoResetEvent (false);
        AutoResetEvent          m_available_chunk = new AutoResetEvent (false);
        WaitHandle[]            m_wait_handles;

        ConcurrentQueue<byte[]> m_chunk_queue;
        BackgroundWorker        m_worker;
        Exception               m_decode_error = null;

        private void DoWork_Decode (object sender, DoWorkEventArgs e)
        {
            try
            {
                var worker = sender as BackgroundWorker;
                var chunks = e.Argument as IEnumerable<MioChunk>;
                uint sample_bytes = (uint)ChannelCount * BitsPerSample / 8;
                foreach (var chunk in chunks)
                {
                    if (worker.CancellationPending)
                    {
                        e.Cancel = true;
                        break;
                    }
                    using (var input = new ChunkStream (Source, chunk))
                    {
                        var wave_buf = new byte[chunk.SampleCount * sample_bytes];
                        m_pmioc.AttachInputFile (input);
                        if (!m_pmiod.DecodeSound (m_pmioc, chunk, wave_buf, 0))
                            throw new InvalidFormatException();
                        m_chunk_queue.Enqueue (wave_buf);
                        m_available_chunk.Set();
                    }
                }
            }
            catch (Exception X)
            {
                Trace.WriteLine (X.Message, "[MIO]");
                m_decode_error = X;
            }
            finally
            {
                m_decode_complete.Set();
            }
        }

        private int Read_Threaded (byte[] buf, int idx, int count)
        {
            var current_pos = Position;
            int total_read = 0;
            if (current_pos < m_decoded_stream.Length)
            {
                int available_bytes = (int)(m_decoded_stream.Length - current_pos);
                int read = m_decoded_stream.Read (buf, idx, Math.Min (count, available_bytes));
                idx += read;
                count -= read;
                total_read += read;
            }
            if (count > 0 && (!m_decode_finished || m_chunk_queue.Count > 0))
            {
                current_pos = Position;
                m_decoded_stream.Seek (0, SeekOrigin.End);
                for (;;)
                {
                    byte[] wave_buf = null;
                    while (m_chunk_queue.TryDequeue (out wave_buf))
                    {
                        m_decoded_stream.Write (wave_buf, 0, wave_buf.Length);
                        if (current_pos + count <= m_decoded_stream.Length)
                            break;
                    }
                    if (m_decode_finished || (current_pos + count <= m_decoded_stream.Length))
                        break;
                    int evt = WaitHandle.WaitAny (m_wait_handles);
                    if (1 == evt)
                    {
                        m_decode_finished = true;
                        if (m_decode_error != null)
                        {
                            m_decoded_stream.Position = current_pos;
                            throw m_decode_error;
                        }
                    }
                }
                m_decoded_stream.Position = current_pos;
                total_read += m_decoded_stream.Read (buf, idx, count);
            }
            return total_read;
        }

        #region IDisposable Members
        bool _mio_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!_mio_disposed)
            {
                if (disposing)
                {
                    if (!m_decode_finished)
                    {
                        m_worker.CancelAsync();
                        m_decode_complete.WaitOne();
                    }
                    if (m_decoded_stream != null)
                        m_decoded_stream.Dispose();
                    m_decode_complete.Dispose();
                    m_available_chunk.Dispose();
                }
                _mio_disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }

/*****************************************************************************
                         E R I S A - L i b r a r y
 -----------------------------------------------------------------------------
    Copyright (C) 2002-2007 Leshade Entis, Entis-soft. All rights reserved.
 *****************************************************************************/

    internal class MioInfoHeader
    {
        public int      Version;
        public CvType   Transformation;
        public EriCode  Architecture;
        public int      ChannelCount;
        public uint     SamplesPerSec;
        public uint     BlocksetCount;
        public int      SubbandDegree;
        public uint     AllSampleCount;
        public uint     LappedDegree;
        public uint     BitsPerSample;
    }

    internal class MioDataHeader
    {
        public byte Version;
        public byte Flags;
        public uint SampleCount;
    }

    internal struct EriSinCos
    {
        public float rSin;
        public float rCos;
    }

    internal class MioDecoder
    {
        MioInfoHeader       m_mioih;

        uint                m_nBufLength = 0;
        int[]               m_ptrBuffer1;
        int[]               m_ptrBuffer2;
        sbyte[]             m_ptrBuffer3;
        byte[]              m_ptrBuffer4;
        byte[]              m_ptrDivisionTable;
        byte[]              m_ptrRevolveCode;
        int[]               m_ptrWeightCode;
        int[]               m_ptrCoefficient;

        float[]             m_ptrMatrixBuf;
        float[]             m_ptrInternalBuf;
        float[]             m_ptrWorkBuf;
        float[]             m_ptrWeightTable;
        float[]             m_ptrLastDCT;

        int                 m_ptrNextDivision;
        int                 m_ptrNextRevCode;
        int                 m_ptrNextWeight;
        int                 m_ptrNextCoefficient;
        int                 m_ptrNextSource;
        int                 m_ptrLastDCTBuf;
        int                 m_nSubbandDegree;
        int                 m_nDegreeNum;
        EriSinCos[]         m_pRevolveParam;
        readonly int[]      m_nFrequencyPoint = new int[7];

        const int MIN_DCT_DEGREE = 2;
        const int MAX_DCT_DEGREE = 12;

        static MioDecoder ()
        {
            eriInitializeMatrix();
        }

        public MioDecoder (MioInfoHeader info)
        {
            m_nBufLength = 0;
            m_mioih = info;

            if (!Initialize())
                throw new InvalidFormatException();
        }

        bool Initialize ()
        {
            if ((m_mioih.ChannelCount != 1) && (m_mioih.ChannelCount != 2))
            {
                return false;
            }

            if (m_mioih.Transformation == CvType.Lossless_ERI)
            {
                if (m_mioih.Architecture != EriCode.RunlengthHuffman)
                {
                    return false;
                }
                if ((m_mioih.BitsPerSample != 8) && (m_mioih.BitsPerSample != 16))
                {
                    return false;
                }
            }
            else if ((m_mioih.Transformation == CvType.LOT_ERI)
                    || (m_mioih.Transformation == CvType.LOT_ERI_MSS))
            {
                if ((m_mioih.Architecture != EriCode.RunlengthGamma)
                    && (m_mioih.Architecture != EriCode.RunlengthHuffman)
                    && (m_mioih.Architecture != EriCode.Nemesis))
                {
                    return false;
                }
                if (m_mioih.BitsPerSample != 16)
                {
                    return false;
                }
                if ((m_mioih.SubbandDegree < 8) || (m_mioih.SubbandDegree > MAX_DCT_DEGREE))
                {
                    return false;
                }
                if (m_mioih.LappedDegree != 1)
                {
                    return false;
                }
                int subband = (sizeof(float) << m_mioih.SubbandDegree) / sizeof(float);
                int block_size = m_mioih.ChannelCount * subband;
                m_ptrBuffer1 = new int[block_size];
                m_ptrMatrixBuf = new float[block_size];
                m_ptrInternalBuf = new float[block_size];
                m_ptrWorkBuf = new float[subband];

                m_ptrWeightTable = new float[subband];

                uint nBlocksetSamples = (uint)(m_mioih.ChannelCount << m_mioih.SubbandDegree);
                uint nLappedSamples = nBlocksetSamples * m_mioih.LappedDegree;
                if (nLappedSamples > 0)
                {
                    m_ptrLastDCT = new float[nLappedSamples];
                }
                InitializeWithDegree (m_mioih.SubbandDegree);
            }
            else
            {
                return false;
            }
            return true;
        }

        public bool DecodeSound (ERISADecodeContext context, MioDataHeader datahdr, byte[] ptrWaveBuf, int wave_pos)
        {
            context.FlushBuffer();

            if (m_mioih.Transformation == CvType.Lossless_ERI)
            {
                if (m_mioih.BitsPerSample == 8)
                {
                    return DecodeSoundPCM8 (context, datahdr, ptrWaveBuf, wave_pos);
                }
                else if (m_mioih.BitsPerSample == 16)
                {
                    return DecodeSoundPCM16 (context, datahdr, ptrWaveBuf, wave_pos);
                }
            }
            else if ((m_mioih.Transformation == CvType.LOT_ERI)
                     || (m_mioih.Transformation == CvType.LOT_ERI_MSS))
            {
                if ((m_mioih.ChannelCount != 2) || (m_mioih.Transformation == CvType.LOT_ERI))
                {
                    return DecodeSoundDCT (context, datahdr, ptrWaveBuf, wave_pos);
                }
                else
                {
                    return DecodeSoundDCT_MSS (context, datahdr, ptrWaveBuf, wave_pos);
                }
            }
            return false;
        }

        bool DecodeSoundPCM8 (ERISADecodeContext context, MioDataHeader datahdr, byte[] ptrWaveBuf, int wave_pos)
        {
            uint nSampleCount = datahdr.SampleCount;
            if (nSampleCount > m_nBufLength)
            {
                m_ptrBuffer3 = new sbyte [nSampleCount * m_mioih.ChannelCount];
                m_nBufLength = nSampleCount;
            }
            if (0 != (datahdr.Flags & MIO_LEAD_BLOCK))
            {
                (context as HuffmanDecodeContext).PrepareToDecodeERINACode();
            }
            uint nBytes = nSampleCount * (uint)m_mioih.ChannelCount;
            if (context.DecodeBytes (m_ptrBuffer3, nBytes) < nBytes)
            {
                return false;
            }
            int ptrSrcBuf = 0; // (PBYTE) m_ptrBuffer3;
            int nStep = m_mioih.ChannelCount;
            for (int i = 0; i < m_mioih.ChannelCount; i ++ )
            {
                int ptrDstBuf = wave_pos + i;
                sbyte bytValue = 0;
                for (uint j = 0; j < nSampleCount; j++)
                {
                    bytValue += m_ptrBuffer3[ptrSrcBuf++];
                    ptrWaveBuf[ptrDstBuf] = (byte)bytValue;
                    ptrDstBuf += nStep;
                }
            }
            return true;
        }

        bool DecodeSoundPCM16 (ERISADecodeContext context, MioDataHeader datahdr, byte[] ptrWaveBuf, int wave_pos)
        {
            uint nSampleCount = datahdr.SampleCount;
            uint nChannelCount = (uint)m_mioih.ChannelCount;
            uint nAllSampleCount = nSampleCount * nChannelCount;

            if (nSampleCount > m_nBufLength)
            {
                m_ptrBuffer3 = new sbyte[nAllSampleCount * sizeof(short)];
                m_ptrBuffer4 = new byte[nAllSampleCount * sizeof(short)];
                m_nBufLength = nSampleCount;
            }
            if (0 != (datahdr.Flags & MIO_LEAD_BLOCK))
            {
                (context as HuffmanDecodeContext).PrepareToDecodeERINACode();
            }
            uint nBytes = nAllSampleCount * sizeof(short);
            if (context.DecodeBytes (m_ptrBuffer3, nBytes) < nBytes)
            {
                return false;
            }
            int pbytSrcBuf1, pbytSrcBuf2, pbytDstBuf;
            for (int i = 0; i < m_mioih.ChannelCount; i++)
            {
                int nOffset = i * (int)nSampleCount * sizeof(short);
                pbytSrcBuf1 = nOffset; // ((PBYTE) m_ptrBuffer3) + nOffset;
                pbytSrcBuf2 = pbytSrcBuf1 + (int)nSampleCount; // pbytSrcBuf1 + nSampleCount;
                pbytDstBuf = nOffset; // ((PBYTE) m_ptrBuffer4) + nOffset;

                for (uint j = 0; j < nSampleCount; j ++)
                {
                    sbyte bytLow  = m_ptrBuffer3[pbytSrcBuf2 + j];
                    sbyte bytHigh = m_ptrBuffer3[pbytSrcBuf1 + j];
                    m_ptrBuffer4[pbytDstBuf + j * sizeof(short) + 0] = (byte)bytLow;
                    m_ptrBuffer4[pbytDstBuf + j * sizeof(short) + 1] = (byte)(bytHigh ^ (bytLow >> 7));
                }
            }
            int ptrSrcBuf = 0; // (SWORD*) m_ptrBuffer4;
            int nStep = m_mioih.ChannelCount;
            for (int i = 0; i < m_mioih.ChannelCount; i++)
            {
                int ptrDstBuf = wave_pos + i*sizeof(short); // (SWORD*) ptrWaveBuf;
                short wValue = 0;
                short wDelta = 0;
                for (uint j = 0; j < nSampleCount; j++)
                {
                    wDelta += LittleEndian.ToInt16 (m_ptrBuffer4, ptrSrcBuf);
                    wValue += wDelta;
                    LittleEndian.Pack (wValue, ptrWaveBuf, ptrDstBuf); // *ptrDstBuf = wValue;
                    ptrSrcBuf += sizeof(short);
                    ptrDstBuf += nStep;
                }
            }
            return true;
        }

        static readonly int[] FreqWidth = new int[7] { -6, -6, -5, -4, -3, -2, -1 };

        void InitializeWithDegree (int nSubbandDegree)
        {
            m_pRevolveParam = eriCreateRevolveParameter (nSubbandDegree);
            for (int i = 0, j = 0; i < 7; i ++)
            {
                int nFrequencyWidth = 1 << (nSubbandDegree + FreqWidth[i]);
                m_nFrequencyPoint[i] = j + (nFrequencyWidth / 2);
                j += nFrequencyWidth;
            }
            m_nSubbandDegree = nSubbandDegree;
            m_nDegreeNum = 1 << nSubbandDegree;
        }

        const uint MIO_LEAD_BLOCK = 0x01;

        bool DecodeSoundDCT (ERISADecodeContext context, MioDataHeader datahdr, byte[] ptrWaveBuf, int wave_pos)
        {
            uint i, j, k;
            uint nDegreeWidth = 1u << m_mioih.SubbandDegree;
            uint nSampleCount = (datahdr.SampleCount + nDegreeWidth - 1) & ~(nDegreeWidth - 1);
            uint nSubbandCount = (nSampleCount >> m_mioih.SubbandDegree);
            uint nChannelCount = (uint)m_mioih.ChannelCount;
            uint nAllSampleCount = nSampleCount * nChannelCount;
            uint nAllSubbandCount = nSubbandCount * nChannelCount;

            if (nSampleCount > m_nBufLength)
            {
                m_ptrBuffer2 = new int[nAllSampleCount];
                m_ptrBuffer3 = new sbyte[nAllSampleCount * sizeof(short)];
                m_ptrDivisionTable = new byte[nAllSubbandCount];
                m_ptrWeightCode = new int[nAllSubbandCount * 5];
                m_ptrCoefficient = new int[nAllSubbandCount * 5];
                m_nBufLength = nSampleCount;
            }
            if (context.GetABit() != 0)
            {
                return  false;
            }
            int[] pLastDivision = new int [nChannelCount];
            m_ptrNextDivision = 0; // within m_ptrDivisionTable;
            m_ptrNextWeight = 0; // within m_ptrWeightCode;
            m_ptrNextCoefficient = 0; // within m_ptrCoefficient;

            for (i = 0; i < nChannelCount; i++)
            {
                pLastDivision[i] = -1;
            }
            for (i = 0; i < nSubbandCount; i++)
            {
                for (j = 0; j < nChannelCount; j++)
                {
                    int nDivisionCode = (int)context.GetNBits(2);
                    m_ptrDivisionTable[m_ptrNextDivision++] = (byte)nDivisionCode;

                    if (nDivisionCode != pLastDivision[j])
                    {
                        if (i != 0)
                        {
                            m_ptrWeightCode[m_ptrNextWeight++] = (int)context.GetNBits (32);
                            m_ptrCoefficient[m_ptrNextCoefficient++] = (int)context.GetNBits (16);
                        }
                        pLastDivision[j] = nDivisionCode;
                    }

                    uint nDivisionCount = 1u << nDivisionCode;
                    for (k = 0; k < nDivisionCount; k ++)
                    {
                        m_ptrWeightCode[m_ptrNextWeight++] = (int)context.GetNBits (32);
                        m_ptrCoefficient[m_ptrNextCoefficient++] = (int)context.GetNBits (16);
                    }
                }
            }
            if (nSubbandCount > 0)
            {
                for (i = 0; i < nChannelCount; i++)
                {
                    m_ptrWeightCode[m_ptrNextWeight++] = (int)context.GetNBits (32);
                    m_ptrCoefficient[m_ptrNextCoefficient++] = (int)context.GetNBits (16);
                }
            }

            if (context.GetABit() != 0)
            {
                return  false;
            }
            if (0 != (datahdr.Flags & MIO_LEAD_BLOCK))
            {
                if (m_mioih.Architecture != EriCode.Nemesis)
                {
                    (context as HuffmanDecodeContext).PrepareToDecodeERINACode();
                }
                else
                {
                    throw new NotImplementedException ("Nemesis encoding not implemented");
//                    context.PrepareToDecodeERISACode();
                }
            }
            else if (m_mioih.Architecture == EriCode.Nemesis)
            {
                throw new NotImplementedException ("Nemesis encoding not implemented");
//                context.InitializeERISACode();
            }
            if (m_mioih.Architecture != EriCode.Nemesis)
            {
                if (context.DecodeBytes (m_ptrBuffer3, nAllSampleCount * 2 ) < nAllSampleCount * 2)
                {
                    return false;
                }
                int ptrHBuf = 0; // within m_ptrBuffer3;
                int ptrLBuf = (int)nAllSampleCount; // within m_ptrBuffer3

                for (i = 0; i < nDegreeWidth; i++)
                {
                    int ptrQuantumized = (int)i; // within (PINT) m_ptrBuffer2
                    for (j = 0; j < nAllSubbandCount; j++)
                    {
                        int nLow  = m_ptrBuffer3[ptrLBuf++];
                        int nHigh = m_ptrBuffer3[ptrHBuf++] ^ (nLow >> 8);
                        m_ptrBuffer2[ptrQuantumized] = (nLow & 0xFF) | (nHigh << 8);
                        ptrQuantumized += (int)nDegreeWidth;
                    }
                }
            }
            else
            {
                throw new NotImplementedException ("Nemesis encoding not implemented");
                /*
                if (context.DecodeERISACodeWords (m_ptrBuffer3, nAllSampleCount) < nAllSampleCount)
                {
                    return false;
                }
                for (i = 0; i < nAllSampleCount; i++)
                {
                    ((PINT)m_ptrBuffer2)[i] = ((SWORD*)m_ptrBuffer3)[i];
                }
                */
            }
            uint nSamples;
            uint[] pRestSamples = new uint [nChannelCount];
            int[] ptrDstBuf = new int [nChannelCount]; // indices within ptrWaveBuf

            m_ptrNextDivision = 0; // within m_ptrDivisionTable;
            m_ptrNextWeight = 0; // within m_ptrWeightCode;
            m_ptrNextCoefficient = 0; // within m_ptrCoefficient;
            m_ptrNextSource = 0; // within (PINT) m_ptrBuffer2;

            for (i = 0; i < nChannelCount; i++)
            {
                pLastDivision[i] = -1;
                pRestSamples[i] = datahdr.SampleCount;
                ptrDstBuf[i] = wave_pos + (int)i*sizeof(short);
            }
            int nCurrentDivision = -1;

            for (i = 0; i < nSubbandCount; i++)
            {
                for (j = 0; j < nChannelCount; j++)
                {
                    int nDivisionCode = m_ptrDivisionTable[m_ptrNextDivision++];
                    int nDivisionCount = 1 << nDivisionCode;
                    int nChannelStep = (int)(nDegreeWidth * m_mioih.LappedDegree * j);
                    m_ptrLastDCTBuf = nChannelStep; // within m_ptrLastDCT

                    bool fLeadBlock = false;
                    if (pLastDivision[j] != nDivisionCode)
                    {
                        if (i != 0)
                        {
                            if (nCurrentDivision != pLastDivision[j])
                            {
                                InitializeWithDegree (m_mioih.SubbandDegree - pLastDivision[j]);
                                nCurrentDivision = pLastDivision[j];
                            }
                            nSamples = pRestSamples[j];
                            if (nSamples > m_nDegreeNum)
                            {
                                nSamples = (uint)m_nDegreeNum;
                            }
                            DecodePostBlock (ptrWaveBuf, ptrDstBuf[j], nSamples);
                            pRestSamples[j] -= nSamples;
                            ptrDstBuf[j] += (int)(nSamples * nChannelCount * sizeof(short));
                        }
                        pLastDivision[j] = (int)nDivisionCode;
                        fLeadBlock = true;
                    }
                    if (nCurrentDivision != nDivisionCode)
                    {
                        InitializeWithDegree (m_mioih.SubbandDegree - nDivisionCode);
                        nCurrentDivision = nDivisionCode;
                    }
                    for (k = 0; k < nDivisionCount; k++)
                    {
                        if (fLeadBlock)
                        {
                            DecodeLeadBlock();
                            fLeadBlock = false;
                        }
                        else
                        {
                            nSamples = pRestSamples[j];
                            if (nSamples > m_nDegreeNum)
                            {
                                nSamples = (uint)m_nDegreeNum;
                            }
                            DecodeInternalBlock (ptrWaveBuf, ptrDstBuf[j], nSamples);
                            pRestSamples[j] -= nSamples;
                            ptrDstBuf[j] += (int)(nSamples * nChannelCount * sizeof(short));
                        }
                    }
                }
            }
            if (nSubbandCount > 0)
            {
                for (i = 0; i < nChannelCount; i ++)
                {
                    int nChannelStep = (int)(nDegreeWidth * m_mioih.LappedDegree * i);
                    m_ptrLastDCTBuf = nChannelStep; // within m_ptrLastDCT

                    if (nCurrentDivision != pLastDivision[i])
                    {
                        InitializeWithDegree (m_mioih.SubbandDegree - pLastDivision[i]);
                        nCurrentDivision = pLastDivision[i];
                    }
                    nSamples = pRestSamples[i];
                    if (nSamples > m_nDegreeNum)
                    {
                        nSamples = (uint)m_nDegreeNum;
                    }
                    DecodePostBlock (ptrWaveBuf, ptrDstBuf[i], nSamples);
                    pRestSamples[i] -= nSamples;
                    ptrDstBuf[i] += (int)(nSamples * nChannelCount * sizeof(short));
                }
            }
            return true;
        }

        void DecodeInternalBlock (byte[] ptrDst, int iDst, uint nSamples)
        {
            int nWeightCode = m_ptrWeightCode[m_ptrNextWeight++];
            int nCoefficient = m_ptrCoefficient[m_ptrNextCoefficient++];
            IQuantumize (m_ptrMatrixBuf, 0, m_ptrBuffer2, m_ptrNextSource, m_nDegreeNum, nWeightCode, nCoefficient);
            m_ptrNextSource += (int)m_nDegreeNum;

            eriOddGivensInverseMatrix (m_ptrMatrixBuf, 0, m_pRevolveParam, m_nSubbandDegree);
            eriFastIPLOT (m_ptrMatrixBuf, 0, m_nSubbandDegree);
            eriFastILOT (m_ptrWorkBuf, m_ptrLastDCT, m_ptrLastDCTBuf, m_ptrMatrixBuf, 0, m_nSubbandDegree);

            Array.Copy (m_ptrMatrixBuf, 0, m_ptrLastDCT,   m_ptrLastDCTBuf, m_nDegreeNum);
            Array.Copy (m_ptrWorkBuf,   0, m_ptrMatrixBuf, 0,               m_nDegreeNum);

            eriFastIDCT (m_ptrInternalBuf, m_ptrMatrixBuf, 0, 1, m_ptrWorkBuf, m_nSubbandDegree);
            if (nSamples != 0)
            {
                eriRoundR32ToWordArray (ptrDst, iDst, m_mioih.ChannelCount, m_ptrInternalBuf, (int)nSamples);
            }
        }

        void DecodeLeadBlock ()
        {
            int nWeightCode = m_ptrWeightCode[m_ptrNextWeight++];
            int nCoefficient = m_ptrCoefficient[m_ptrNextCoefficient++];
            uint i;
            uint nHalfDegree = (uint)m_nDegreeNum / 2;
            for (i = 0; i < nHalfDegree; i++)
            {
                m_ptrBuffer1[i * 2]   = 0;
                m_ptrBuffer1[i * 2 + 1] = m_ptrBuffer2[m_ptrNextSource++];
            }
            IQuantumize (m_ptrLastDCT, m_ptrLastDCTBuf, m_ptrBuffer1, 0, m_nDegreeNum, nWeightCode, nCoefficient);
            eriOddGivensInverseMatrix (m_ptrLastDCT, m_ptrLastDCTBuf, m_pRevolveParam, m_nSubbandDegree);
            for (i = 0; i < m_nDegreeNum; i += 2)
            {
                m_ptrLastDCT[m_ptrLastDCTBuf + i] = m_ptrLastDCT[m_ptrLastDCTBuf + i + 1];
            }
            eriFastIPLOT (m_ptrLastDCT, m_ptrLastDCTBuf, m_nSubbandDegree);
        }

        void DecodePostBlock (byte[] ptrDst, int iDst, uint nSamples)
        {
            int nWeightCode = m_ptrWeightCode[m_ptrNextWeight++];
            int nCoefficient = m_ptrCoefficient[m_ptrNextCoefficient++];
            uint i;
            uint nHalfDegree = (uint)m_nDegreeNum / 2;
            for (i = 0; i < nHalfDegree; i++)
            {
                m_ptrBuffer1[i * 2] = 0;
                m_ptrBuffer1[i * 2 + 1] = m_ptrBuffer2[m_ptrNextSource++];
            }
            IQuantumize (m_ptrMatrixBuf, 0, m_ptrBuffer1, 0, m_nDegreeNum, nWeightCode, nCoefficient);
            eriOddGivensInverseMatrix (m_ptrMatrixBuf, 0, m_pRevolveParam, m_nSubbandDegree);

            for (i = 0; i < m_nDegreeNum; i += 2)
            {
                m_ptrMatrixBuf[i] = - m_ptrMatrixBuf[i + 1];
            }

            eriFastIPLOT (m_ptrMatrixBuf, 0, m_nSubbandDegree);
            eriFastILOT (m_ptrWorkBuf, m_ptrLastDCT, m_ptrLastDCTBuf, m_ptrMatrixBuf, 0, m_nSubbandDegree);

            Array.Copy (m_ptrWorkBuf, 0, m_ptrMatrixBuf, 0, m_nDegreeNum);

            eriFastIDCT (m_ptrInternalBuf, m_ptrMatrixBuf, 0, 1, m_ptrWorkBuf, m_nSubbandDegree);
            if (nSamples != 0)
            {
                eriRoundR32ToWordArray (ptrDst, iDst, m_mioih.ChannelCount, m_ptrInternalBuf, (int)nSamples);
            }
        }

        bool DecodeSoundDCT_MSS (ERISADecodeContext context, MioDataHeader datahdr, byte[] ptrWaveBuf, int wave_pos)
        {
            uint nDegreeWidth = 1u << m_mioih.SubbandDegree;
            uint nSampleCount = (datahdr.SampleCount + nDegreeWidth - 1) & ~(nDegreeWidth - 1);
            uint nSubbandCount = (nSampleCount >> m_mioih.SubbandDegree);
            uint nChannelCount = (uint)m_mioih.ChannelCount;
            uint nAllSampleCount = nSampleCount * nChannelCount;
            uint nAllSubbandCount = nSubbandCount;

            if (nSampleCount > m_nBufLength)
            {
                m_ptrBuffer2 = new int[nAllSampleCount];
                m_ptrBuffer3 = new sbyte[nAllSampleCount * sizeof(short)];
                m_ptrDivisionTable = new byte[nAllSubbandCount];
                m_ptrRevolveCode = new byte[nAllSubbandCount * 10];
                m_ptrWeightCode = new int[nAllSubbandCount * 10];
                m_ptrCoefficient = new int[nAllSubbandCount * 10];
                m_nBufLength = nSampleCount;
            }
            if (context.GetABit() != 0)
            {
                return false;
            }

            int nLastDivision = -1;
            m_ptrNextDivision = 0; // within m_ptrDivisionTable;
            m_ptrNextRevCode = 0; // within m_ptrRevolveCode;
            m_ptrNextWeight = 0; // within m_ptrWeightCode;
            m_ptrNextCoefficient = 0; // within m_ptrCoefficient;

            uint i, j, k;
            for (i = 0; i < nSubbandCount; i ++)
            {
                int nDivisionCode = (int)context.GetNBits (2);
                m_ptrDivisionTable[m_ptrNextDivision++] = (byte)nDivisionCode;

                bool fLeadBlock = false;
                if (nDivisionCode != nLastDivision)
                {
                    if (i != 0)
                    {
                        m_ptrRevolveCode[m_ptrNextRevCode++] = (byte)context.GetNBits (2);
                        m_ptrWeightCode[m_ptrNextWeight++] = (int)context.GetNBits (32);
                        m_ptrCoefficient[m_ptrNextCoefficient++] = (int)context.GetNBits (16);
                    }
                    fLeadBlock = true;
                    nLastDivision = nDivisionCode;
                }
                uint nDivisionCount = 1u << nDivisionCode;
                for (k = 0; k < nDivisionCount; k++)
                {
                    if (fLeadBlock)
                    {
                        m_ptrRevolveCode[m_ptrNextRevCode++] = (byte)context.GetNBits (2);
                        fLeadBlock = false;
                    }
                    else
                    {
                        m_ptrRevolveCode[m_ptrNextRevCode++] = (byte)context.GetNBits (4);
                    }
                    m_ptrWeightCode[m_ptrNextWeight++] = (int)context.GetNBits (32);
                    m_ptrCoefficient[m_ptrNextCoefficient++] = (int)context.GetNBits (16);
                }
            }
            if (nSubbandCount > 0)
            {
                m_ptrRevolveCode[m_ptrNextRevCode++] = (byte)context.GetNBits (2);
                m_ptrWeightCode[m_ptrNextWeight++] = (int)context.GetNBits (32);
                m_ptrCoefficient[m_ptrNextCoefficient++] = (int)context.GetNBits (16);
            }
            if (context.GetABit() != 0)
            {
                return false;
            }
            if (0 != (datahdr.Flags & MIO_LEAD_BLOCK))
            {
                if (m_mioih.Architecture != EriCode.Nemesis)
                {
                    (context as HuffmanDecodeContext).PrepareToDecodeERINACode();
                }
                else
                {
                    throw new NotImplementedException ("Nemesis encoding not implemented");
//                    context.PrepareToDecodeERISACode( );
                }
            }
            else if (m_mioih.Architecture == EriCode.Nemesis)
            {
                throw new NotImplementedException ("Nemesis encoding not implemented");
//                context.InitializeERISACode( );
            }
            if (m_mioih.Architecture != EriCode.Nemesis)
            {
                if (context.DecodeBytes (m_ptrBuffer3, nAllSampleCount * 2) < nAllSampleCount * 2)
                {
                    return false;
                }
                int ptrHBuf = 0; // within m_ptrBuffer3;
                int ptrLBuf = (int)nAllSampleCount; // within m_ptrBuffer3

                for (i = 0; i < nDegreeWidth * 2; i++)
                {
                    int ptrQuantumized = (int)i; // within (PINT) m_ptrBuffer2
                    for (j = 0; j < nAllSubbandCount; j++)
                    {
                        int nLow  = m_ptrBuffer3[ptrLBuf++];
                        int nHigh = m_ptrBuffer3[ptrHBuf++] ^ (nLow >> 8);
                        m_ptrBuffer2[ptrQuantumized] = (nLow & 0xFF) | (nHigh << 8);
                        ptrQuantumized += (int)nDegreeWidth * 2;
                    }
                }
            }
            else
            {
                throw new NotImplementedException ("Nemesis encoding not implemented");
                /*
                if ( context.DecodeERISACodeWords
                        ( (SWORD*) m_ptrBuffer3, nAllSampleCount ) < nAllSampleCount )
                {
                    return false;
                }
                for ( i = 0; i < nAllSampleCount; i ++ )
                {
                    ((PINT)m_ptrBuffer2)[i] = ((SWORD*)m_ptrBuffer3)[i];
                }
                */
            }
            uint nSamples;
            uint nRestSamples = datahdr.SampleCount;
//            int ptrDstBuf = wave_pos; // within (SWORD*) ptrWaveBuf;

            nLastDivision = -1;
            m_ptrNextDivision = 0; // m_ptrDivisionTable;
            m_ptrNextRevCode = 0; // m_ptrRevolveCode;
            m_ptrNextWeight = 0; // m_ptrWeightCode;
            m_ptrNextCoefficient = 0; // m_ptrCoefficient;
            m_ptrNextSource = 0; // (PINT) m_ptrBuffer2;

            for (i = 0; i < nSubbandCount; i++)
            {
                int nDivisionCode = m_ptrDivisionTable[m_ptrNextDivision++];
                uint nDivisionCount = 1u << nDivisionCode;

                bool fLeadBlock = false;
                if (nLastDivision != nDivisionCode)
                {
                    if (i != 0)
                    {
                        nSamples = Math.Min (nRestSamples, (uint)m_nDegreeNum);
                        DecodePostBlock_MSS (ptrWaveBuf, wave_pos, nSamples);
                        nRestSamples -= nSamples;
                        wave_pos += (int)(nSamples * nChannelCount * sizeof(short));
                    }
                    InitializeWithDegree (m_mioih.SubbandDegree - nDivisionCode);
                    nLastDivision = nDivisionCode;
                    fLeadBlock = true;
                }
                for (k = 0; k < nDivisionCount; k++)
                {
                    if (fLeadBlock)
                    {
                        DecodeLeadBlock_MSS();
                        fLeadBlock = false;
                    }
                    else
                    {
                        nSamples = nRestSamples;
                        if (nSamples > m_nDegreeNum)
                        {
                            nSamples = (uint)m_nDegreeNum;
                        }
                        DecodeInternalBlock_MSS (ptrWaveBuf, wave_pos, nSamples);
                        nRestSamples -= nSamples;
                        wave_pos += (int)(nSamples * nChannelCount * sizeof(short));
                    }
                }
            }
            if (nSubbandCount > 0)
            {
                nSamples = nRestSamples;
                if (nSamples > m_nDegreeNum)
                {
                    nSamples = (uint)m_nDegreeNum;
                }
                DecodePostBlock_MSS (ptrWaveBuf, wave_pos, nSamples);
                nRestSamples -= nSamples;
                wave_pos += (int)(nSamples * nChannelCount) * sizeof(short);
            }
            return true;
        }

        void DecodeLeadBlock_MSS ()
        {
            uint i, j;
            uint nHalfDegree = (uint)m_nDegreeNum / 2;
            int nWeightCode = m_ptrWeightCode[m_ptrNextWeight++];
            int nCoefficient = m_ptrCoefficient[m_ptrNextCoefficient++];
            int ptrLapBuf = 0; // within m_ptrLastDCT;

            for (i = 0; i < 2; i++)
            {
                int ptrSrcBuf = 0; // within (PINT) m_ptrBuffer1;
                for (j = 0; j < nHalfDegree; j++)
                {
                    m_ptrBuffer1[ptrSrcBuf + j * 2] = 0;
                    m_ptrBuffer1[ptrSrcBuf + j * 2 + 1] = m_ptrBuffer2[m_ptrNextSource++];
                }
                IQuantumize (m_ptrLastDCT, ptrLapBuf, m_ptrBuffer1, ptrSrcBuf, m_nDegreeNum, nWeightCode, nCoefficient);
                ptrLapBuf += (int)m_nDegreeNum;
            }
            int nRevCode = m_ptrRevolveCode[m_ptrNextRevCode++];

            int ptrLapBuf1 = 0; // m_ptrLastDCT;
            int ptrLapBuf2 = (int)m_nDegreeNum; // m_ptrLastDCT 

            float rSin = (float)Math.Sin (nRevCode * Math.PI / 8);
            float rCos = (float)Math.Cos (nRevCode * Math.PI / 8);
            eriRevolve2x2 (m_ptrLastDCT, ptrLapBuf1, m_ptrLastDCT, ptrLapBuf2, rSin, rCos, 1, m_nDegreeNum);

            ptrLapBuf = 0; //m_ptrLastDCT;
            for (i = 0; i < 2; i++)
            {
                eriOddGivensInverseMatrix (m_ptrLastDCT, ptrLapBuf, m_pRevolveParam, m_nSubbandDegree);

                for (j = 0; j < m_nDegreeNum; j += 2)
                {
                    m_ptrLastDCT[ptrLapBuf + j] = m_ptrLastDCT[ptrLapBuf + j + 1];
                }
                eriFastIPLOT (m_ptrLastDCT, ptrLapBuf, m_nSubbandDegree);
                ptrLapBuf += (int)m_nDegreeNum;
            }
        }

        void DecodeInternalBlock_MSS (byte[] ptrDst, int iDst, uint nSamples)
        {
            int ptrSrcBuf = 0; // m_ptrMatrixBuf;
            int ptrLapBuf = 0; // m_ptrLastDCT;

            int nWeightCode  = m_ptrWeightCode[m_ptrNextWeight++];
            int nCoefficient = m_ptrCoefficient[m_ptrNextCoefficient++];

            for (int i = 0; i < 2; i++)
            {
                IQuantumize (m_ptrMatrixBuf, ptrSrcBuf, m_ptrBuffer2, m_ptrNextSource, m_nDegreeNum, nWeightCode, nCoefficient);
                m_ptrNextSource += m_nDegreeNum;
                ptrSrcBuf += m_nDegreeNum;
            }
            int nRevCode = m_ptrRevolveCode[m_ptrNextRevCode++];
            int nRevCode1 = (nRevCode >> 2) & 0x03;
            int nRevCode2 = (nRevCode & 0x03);

            int ptrSrcBuf1 = 0; // m_ptrMatrixBuf;
            int ptrSrcBuf2 = m_nDegreeNum; // m_ptrMatrixBuf + m_nDegreeNum;

            float rSin = (float) Math.Sin (nRevCode1 * Math.PI / 8);
            float rCos = (float) Math.Cos (nRevCode1 * Math.PI / 8);
            eriRevolve2x2 (m_ptrMatrixBuf, ptrSrcBuf1, m_ptrMatrixBuf, ptrSrcBuf2, rSin, rCos, 2, m_nDegreeNum / 2);

            rSin = (float) Math.Sin (nRevCode2 * Math.PI / 8);
            rCos = (float) Math.Cos (nRevCode2 * Math.PI / 8);
            eriRevolve2x2 (m_ptrMatrixBuf, ptrSrcBuf1 + 1, m_ptrMatrixBuf, ptrSrcBuf2 + 1, rSin, rCos, 2, m_nDegreeNum / 2);

            ptrSrcBuf = 0; // m_ptrMatrixBuf;

            for (int i = 0; i < 2; i++)
            {
                eriOddGivensInverseMatrix (m_ptrMatrixBuf, ptrSrcBuf, m_pRevolveParam, m_nSubbandDegree);
                eriFastIPLOT (m_ptrMatrixBuf, ptrSrcBuf, m_nSubbandDegree);
                eriFastILOT (m_ptrWorkBuf, m_ptrLastDCT, ptrLapBuf, m_ptrMatrixBuf, ptrSrcBuf, m_nSubbandDegree);

                Array.Copy (m_ptrMatrixBuf, ptrSrcBuf, m_ptrLastDCT,   ptrLapBuf, m_nDegreeNum);
                Array.Copy (m_ptrWorkBuf,   0,         m_ptrMatrixBuf, ptrSrcBuf, m_nDegreeNum);

                eriFastIDCT (m_ptrInternalBuf, m_ptrMatrixBuf, ptrSrcBuf, 1, m_ptrWorkBuf, m_nSubbandDegree);
                if (nSamples != 0)
                {
                    eriRoundR32ToWordArray (ptrDst, iDst + (int)i*2, 2, m_ptrInternalBuf, (int)nSamples);
                }
                ptrSrcBuf += m_nDegreeNum;
                ptrLapBuf += m_nDegreeNum;
            }
        }

        void DecodePostBlock_MSS (byte[] ptrDst, int iDst, uint nSamples)
        {
            int ptrLapBuf = 0; // m_ptrLastDCT;
            int ptrSrcBuf = 0; // m_ptrMatrixBuf;

            int i, j;
            uint nHalfDegree = (uint)m_nDegreeNum / 2u;
            int nWeightCode  = m_ptrWeightCode[m_ptrNextWeight++];
            int nCoefficient = m_ptrCoefficient[m_ptrNextCoefficient++];

            for (i = 0; i < 2; i++)
            {
                for (j = 0; j < nHalfDegree; j++)
                {
                    m_ptrBuffer1[j * 2] = 0;
                    m_ptrBuffer1[j * 2 + 1] = m_ptrBuffer2[m_ptrNextSource++];
                }
                IQuantumize (m_ptrMatrixBuf, ptrSrcBuf, m_ptrBuffer1, 0, m_nDegreeNum, nWeightCode, nCoefficient);
                ptrSrcBuf += m_nDegreeNum;
            }
            float rSin, rCos;
            int   nRevCode = m_ptrRevolveCode[m_ptrNextRevCode++];

            int ptrSrcBuf1 = 0; // m_ptrMatrixBuf;
            int ptrSrcBuf2 = m_nDegreeNum; // m_ptrMatrixBuf + m_nDegreeNum;

            rSin = (float) Math.Sin (nRevCode * Math.PI / 8);
            rCos = (float) Math.Cos (nRevCode * Math.PI / 8);
            eriRevolve2x2 (m_ptrMatrixBuf, ptrSrcBuf1, m_ptrMatrixBuf, ptrSrcBuf2, rSin, rCos, 1, m_nDegreeNum);

            ptrSrcBuf = 0; // m_ptrMatrixBuf;

            for (i = 0; i < 2; i ++)
            {
                eriOddGivensInverseMatrix (m_ptrMatrixBuf, ptrSrcBuf, m_pRevolveParam, m_nSubbandDegree);

                for (j = 0; j < m_nDegreeNum; j += 2)
                {
                    m_ptrMatrixBuf[ptrSrcBuf + j] = -m_ptrMatrixBuf[ptrSrcBuf + j + 1];
                }
                eriFastIPLOT (m_ptrMatrixBuf, ptrSrcBuf, m_nSubbandDegree);
                eriFastILOT (m_ptrWorkBuf, m_ptrLastDCT, ptrLapBuf, m_ptrMatrixBuf, ptrSrcBuf, m_nSubbandDegree);

                Array.Copy (m_ptrWorkBuf, 0, m_ptrMatrixBuf, ptrSrcBuf, m_nDegreeNum);

                eriFastIDCT (m_ptrInternalBuf, m_ptrMatrixBuf, ptrSrcBuf, 1, m_ptrWorkBuf, m_nSubbandDegree);
                if (nSamples != 0)
                {
                    eriRoundR32ToWordArray (ptrDst, iDst + (int)i*2, 2, m_ptrInternalBuf, (int)nSamples);
                }
                ptrLapBuf += m_nDegreeNum;
                ptrSrcBuf += m_nDegreeNum;
            }
        }

        void IQuantumize (float[] ptrDestination, int dst, int[] ptrQuantumized, int qsrc, int nDegreeNum, int nWeightCode, int nCoefficient)
        {
            int i, j;
            double rMatrixScale = Math.Sqrt (2.0 / nDegreeNum);
            double rCoefficient = rMatrixScale * nCoefficient;
            double[] rAvgRatio = new double[7];
            for (i = 0; i < 6; i++)
            {
                rAvgRatio[i] = 1.0 / Math.Pow (2.0, (((nWeightCode >> (i * 5)) & 0x1F) - 15) * 0.5);
            }
            rAvgRatio[6] = 1.0;
            for (i = 0; i < m_nFrequencyPoint[0]; i++)
            {
                m_ptrWeightTable[i] = (float) rAvgRatio[0];
            }
            for (j = 1; j < 7; j++)
            {
                double a = rAvgRatio[j - 1];
                double k = (rAvgRatio[j] - a) / (m_nFrequencyPoint[j] - m_nFrequencyPoint[j - 1]);
                while (i < m_nFrequencyPoint[j])
                {
                    m_ptrWeightTable[i] = (float)(k * (i - m_nFrequencyPoint[j - 1]) + a);
                    i++;
                }
            }
            while (i < nDegreeNum)
            {
                m_ptrWeightTable[i++] = (float)rAvgRatio[6];
            }
            float rOddWeight = (float)((((nWeightCode >> 30) & 0x03) + 0x02) / 2.0);
            for (i = 15; i < nDegreeNum; i += 16)
            {
                m_ptrWeightTable[i] *= rOddWeight;
            }
            m_ptrWeightTable[nDegreeNum-1] = (float) nCoefficient;
            for (i = 0; i < nDegreeNum; i++)
            {
                m_ptrWeightTable[i] = 1.0F / m_ptrWeightTable[i];
            }
            for (i = 0; i < nDegreeNum; i ++)
            {
                ptrDestination[dst + i] = (float) (rCoefficient * m_ptrWeightTable[i] * ptrQuantumized[qsrc+i]);
            }
        }

        static readonly float ERI_rCosPI4  = (float)Math.Cos (Math.PI / 4);
        static readonly float ERI_r2CosPI4 = 2 * ERI_rCosPI4;
        static readonly float[] ERI_DCTofK2 = new float[2];     // = cos( (2*i+1) / 8 )
        static readonly float[] ERI_DCTofK4 = new float[4];     // = cos( (2*i+1) / 16 )
        static readonly float[] ERI_DCTofK8 = new float[8];     // = cos( (2*i+1) / 32 )
        static readonly float[] ERI_DCTofK16 = new float[16];   // = cos( (2*i+1) / 64 )
        static readonly float[] ERI_DCTofK32 = new float[32];   // = cos( (2*i+1) / 128 )
        static readonly float[] ERI_DCTofK64 = new float[64];   // = cos( (2*i+1) / 256 )
        static readonly float[] ERI_DCTofK128 = new float[128]; // = cos( (2*i+1) / 512 )
        static readonly float[] ERI_DCTofK256 = new float[256]; // = cos( (2*i+1) / 1024 )
        static readonly float[] ERI_DCTofK512 = new float[512]; // = cos( (2*i+1) / 2048 )
        static readonly float[] ERI_DCTofK1024 = new float[1024]; // = cos( (2*i+1) / 4096 )
        static readonly float[] ERI_DCTofK2048 = new float[2048]; // = cos( (2*i+1) / 8192 )

        static readonly float[][] ERI_pMatrixDCTofK = new float[MAX_DCT_DEGREE][]
        {
            null,
            ERI_DCTofK2,
            ERI_DCTofK4,
            ERI_DCTofK8,
            ERI_DCTofK16,
            ERI_DCTofK32,
            ERI_DCTofK64,
            ERI_DCTofK128,
            ERI_DCTofK256,
            ERI_DCTofK512,
            ERI_DCTofK1024,
            ERI_DCTofK2048
        };

        static void eriInitializeMatrix ()
        {
            for (int i = 1; i < MAX_DCT_DEGREE; i++)
            {
                int     n = (1 << i);
                float[] pDCTofK = ERI_pMatrixDCTofK[i];
                double  nr = Math.PI / (4.0 * n);
                double  dr = nr + nr;
                double  ir = nr;
                for (int j = 0; j < n; j++)
                {
                    pDCTofK[j] = (float)Math.Cos (ir);
                    ir += dr;
                }
            }
        }

        static void eriRoundR32ToWordArray (byte[] ptrDst, int dst, int nStep, float[] ptrSrc, int nCount)
        {
            nStep *= 2;
            for (int i = 0; i < nCount; i++)
            {
                int nValue = eriRoundR32ToInt (ptrSrc[i]);
                if (nValue <= -0x8000)
                {
                    LittleEndian.Pack ((short)-0x8000, ptrDst, dst);
                }
                else if (nValue >= 0x7FFF)
                {
                    LittleEndian.Pack ((short)0x7FFF, ptrDst, dst);
                }
                else
                {
                    LittleEndian.Pack ((short)nValue, ptrDst, dst);
                }
                dst += nStep;
            }
        }

        static int eriRoundR32ToInt (float r)
        {
            if (r >= 0.0)
                return (int)Math.Floor (r + 0.5);
            else
                return (int)Math.Ceiling (r - 0.5);
        }

        static EriSinCos[] eriCreateRevolveParameter (int nDegreeDCT)
        {
            int nDegreeNum = 1 << nDegreeDCT;
            int lc = 1;
            for (int n = nDegreeNum / 2; n >= 8; n /= 8)
            {
                ++lc;
            }
            EriSinCos[] ptrRevolve = new EriSinCos[lc*8];

            double k = Math.PI / (nDegreeNum * 2);
            int ptrNextRev = 0;
            int nStep = 2;
            do
            {
                for (int i = 0; i < 7; i++)
                {
                    double ws = 1.0;
                    double a = 0.0;
                    for (int j = 0; j < i; j++)
                    {
                        a += nStep;
                        ws = ws * ptrRevolve[ptrNextRev+j].rSin + ptrRevolve[ptrNextRev+j].rCos * Math.Cos (a * k);
                    }
                    double r = Math.Atan2 (ws, Math.Cos ((a + nStep) * k));
                    ptrRevolve[ptrNextRev+i].rSin = (float)Math.Sin (r);
                    ptrRevolve[ptrNextRev+i].rCos = (float)Math.Cos (r);
                }
                ptrNextRev += 7;
                nStep *= 8;
            }
            while (nStep < nDegreeNum);
            return ptrRevolve;
        }

        static void eriOddGivensInverseMatrix (float[] ptrSrc, int src, EriSinCos[] ptrRevolve, int nDegreeDCT)
        {
            int nDegreeNum = 1 << nDegreeDCT;
            int index = 1;
            int nStep = 2;
            int lc = (nDegreeNum / 2) / 8;
            int resolve_idx = 0;
            for (;;)
            {
                resolve_idx += 7;
                index += nStep * 7;
                nStep *= 8;
                if (lc <= 8)
                    break;
                lc /= 8;
            }
            int k = index + nStep * (lc - 2);
            int j;
            float r1, r2;
            for (j = lc - 2; j >= 0; j--)
            {
                r1 = ptrSrc[src + k];
                r2 = ptrSrc[src + k + nStep];
                ptrSrc[src + k] = r1 * ptrRevolve[resolve_idx+j].rCos + r2 * ptrRevolve[resolve_idx+j].rSin;
                ptrSrc[src + k + nStep] = r2 * ptrRevolve[resolve_idx+j].rCos - r1 * ptrRevolve[resolve_idx+j].rSin;
                k -= nStep;
            }
            for (; lc <= (nDegreeNum / 2) / 8; lc *= 8)
            {
                resolve_idx -= 7;
                nStep /= 8;
                index -= nStep * 7;
                for (int i = 0; i < lc; i++)
                {
                    k = i * (nStep * 8) + index + nStep * 6;
                    for ( j = 6; j >= 0; j -- )
                    {
                        r1 = ptrSrc[src + k];
                        r2 = ptrSrc[src + k + nStep];
                        ptrSrc[src + k]         =
                            r1 * ptrRevolve[resolve_idx+j].rCos + r2 * ptrRevolve[resolve_idx+j].rSin;
                        ptrSrc[src + k + nStep] =
                            r2 * ptrRevolve[resolve_idx+j].rCos - r1 * ptrRevolve[resolve_idx+j].rSin;
                        k -= nStep;
                    }
                }
            }
        }

        static void eriFastIPLOT (float[] ptrSrc, int src, int nDegreeDCT)
        {
            int nDegreeNum = 1 << nDegreeDCT;
            for (int i = 0; i < nDegreeNum; i += 2)
            {
                float r1 = ptrSrc[src + i];
                float r2 = ptrSrc[src + i + 1];
                ptrSrc[src + i]     = 0.5f * (r1 + r2);
                ptrSrc[src + i + 1] = 0.5f * (r1 - r2);
            }
        }

        static void eriFastILOT (float[] ptrDst, float[] ptrSrc1, int src1, float[] ptrSrc2, int src2, int nDegreeDCT)
        {
            int nDegreeNum = 1 << nDegreeDCT;
            for (int i = 0; i < nDegreeNum; i += 2)
            {
                float r1 = ptrSrc1[src1 + i];
                float r2 = ptrSrc2[src2 + i + 1];
                ptrDst[i]     = r1 + r2;
                ptrDst[i + 1] = r1 - r2;
            }
        }

        static void eriFastDCT (float[] ptrDst, int dst, int nDstInterval, float[] ptrSrc, int src, float[] ptrWorkBuf, int work, int nDegreeDCT)
        {
            Debug.Assert ((nDegreeDCT >= MIN_DCT_DEGREE) && (nDegreeDCT <= MAX_DCT_DEGREE));

            if (nDegreeDCT == MIN_DCT_DEGREE)
            {
                float[] r32Buf = new float[4];
                r32Buf[0] = ptrSrc[src] + ptrSrc[src+3];
                r32Buf[2] = ptrSrc[src] - ptrSrc[src+3];
                r32Buf[1] = ptrSrc[src+1] + ptrSrc[src+2];
                r32Buf[3] = ptrSrc[src+1] - ptrSrc[src+2];

                ptrDst[dst]                = 0.5f * (r32Buf[0] + r32Buf[1]);
                ptrDst[dst+nDstInterval * 2] = ERI_rCosPI4 * (r32Buf[0] - r32Buf[1]);

                r32Buf[2] = ERI_DCTofK2[0] * r32Buf[2];
                r32Buf[3] = ERI_DCTofK2[1] * r32Buf[3];

                r32Buf[0] =                 r32Buf[2] + r32Buf[3];
                r32Buf[1] = ERI_r2CosPI4 * (r32Buf[2] - r32Buf[3]);

                r32Buf[1] -= r32Buf[0];

                ptrDst[dst+nDstInterval]     = r32Buf[0];
                ptrDst[dst+nDstInterval * 3] = r32Buf[1];
            }
            else
            {
                uint i;
                uint nDegreeNum = 1u << nDegreeDCT;
                uint nHalfDegree = nDegreeNum >> 1;
                for (i = 0; i < nHalfDegree; i++)
                {
                    ptrWorkBuf[work+i] = ptrSrc[src+i] + ptrSrc[src + nDegreeNum - i - 1];
                    ptrWorkBuf[work+i + nHalfDegree] = ptrSrc[src+i] - ptrSrc[src + nDegreeNum - i - 1];
                }
                int nDstStep = nDstInterval << 1;
                eriFastDCT (ptrDst, dst, nDstStep, ptrWorkBuf, work, ptrSrc, src, nDegreeDCT - 1);

                float[] pDCTofK = ERI_pMatrixDCTofK[nDegreeDCT - 1];
                src = (int)(work+nHalfDegree); // ptrSrc = ptrWorkBuf + nHalfDegree;
                dst += nDstInterval;    // ptrDst += nDstInterval;

                for (i = 0; i < nHalfDegree; i++)
                {
                    ptrWorkBuf[src + i] *= pDCTofK[i];
                }

                eriFastDCT (ptrDst, dst, nDstStep, ptrWorkBuf, src, ptrWorkBuf, work, nDegreeDCT - 1);
                // eriFastDCT (ptrDst, nDstStep, ptrSrc, ptrWorkBuf, nDegreeDCT - 1);

                int ptrNext = dst; // within ptrDst;
                for (i = 0; i < nHalfDegree; i++)
                {
                    ptrDst[ptrNext] += ptrDst[ptrNext]; // *ptrNext += *ptrNext;
                    ptrNext += nDstStep;
                }
                ptrNext = dst;
                for (i = 1; i < nHalfDegree; i ++)
                {
                    ptrDst[ptrNext + nDstStep] -= ptrDst[ptrNext];
                    ptrNext += nDstStep;
                }
            }
        }

        static void eriFastIDCT (float[] ptrDst, float[] srcBuf, int ptrSrc, int nSrcInterval, float[] ptrWorkBuf, int nDegreeDCT)
        {
            Debug.Assert ((nDegreeDCT >= MIN_DCT_DEGREE) && (nDegreeDCT <= MAX_DCT_DEGREE));

            if (nDegreeDCT == MIN_DCT_DEGREE)
            {
                float[] r32Buf1 = new float[2];
                float[] r32Buf2 = new float[4];

                r32Buf1[0] = srcBuf[ptrSrc];
                r32Buf1[1] = ERI_rCosPI4 * srcBuf[ptrSrc + nSrcInterval * 2];

                r32Buf2[0] = r32Buf1[0] + r32Buf1[1];
                r32Buf2[1] = r32Buf1[0] - r32Buf1[1];

                r32Buf1[0] = ERI_DCTofK2[0] * srcBuf[ptrSrc + nSrcInterval];
                r32Buf1[1] = ERI_DCTofK2[1] * srcBuf[ptrSrc + nSrcInterval * 3];

                r32Buf2[2] =                 r32Buf1[0] + r32Buf1[1];
                r32Buf2[3] = ERI_r2CosPI4 * (r32Buf1[0] - r32Buf1[1]);

                r32Buf2[3] -= r32Buf2[2];

                ptrDst[0] = r32Buf2[0] + r32Buf2[2];
                ptrDst[3] = r32Buf2[0] - r32Buf2[2];
                ptrDst[1] = r32Buf2[1] + r32Buf2[3];
                ptrDst[2] = r32Buf2[1] - r32Buf2[3];
            }
            else
            {
                uint nDegreeNum = 1u << nDegreeDCT;
                uint nHalfDegree = nDegreeNum >> 1;
                int nSrcStep = nSrcInterval << 1;
                eriFastIDCT (ptrDst, srcBuf, ptrSrc, nSrcStep, ptrWorkBuf, nDegreeDCT - 1);

                float[] pDCTofK = ERI_pMatrixDCTofK[nDegreeDCT - 1];
                int pOddDst = (int)nHalfDegree; // within ptrDst
                int ptrNext = ptrSrc + nSrcInterval; // within srcBuf

                uint i;
                for (i = 0; i < nHalfDegree; i++)
                {
                    ptrWorkBuf[i] = srcBuf[ptrNext] * pDCTofK[i];
                    ptrNext += nSrcStep;
                }

                eriFastDCT (ptrDst, pOddDst, 1, ptrWorkBuf, 0, ptrWorkBuf, (int)nHalfDegree, nDegreeDCT - 1);
                // eriFastDCT(pOddDst, 1, ptrWorkBuf, (ptrWorkBuf + nHalfDegree), nDegreeDCT - 1);

                for (i = 0; i < nHalfDegree; i ++)
                {
                    ptrDst[pOddDst + i] += ptrDst[pOddDst + i];
                }

                for (i = 1; i < nHalfDegree; i++)
                {
                    ptrDst[pOddDst + i] -= ptrDst[pOddDst + i - 1];
                }
                float[] r32Buf = new float[4];
                uint nQuadDegree = nHalfDegree >> 1;
                for (i = 0; i < nQuadDegree; i++)
                {
                    r32Buf[0] = ptrDst[i] + ptrDst[nHalfDegree + i];
                    r32Buf[3] = ptrDst[i] - ptrDst[nHalfDegree + i];
                    r32Buf[1] = ptrDst[nHalfDegree - 1 - i] + ptrDst[nDegreeNum - 1 - i];
                    r32Buf[2] = ptrDst[nHalfDegree - 1 - i] - ptrDst[nDegreeNum - 1 - i];

                    ptrDst[i]                   = r32Buf[0];
                    ptrDst[nHalfDegree - 1 - i] = r32Buf[1];
                    ptrDst[nHalfDegree + i]     = r32Buf[2];
                    ptrDst[nDegreeNum - 1 - i]  = r32Buf[3];
                }
            }
        }

        void eriRevolve2x2 (float[] buf1, int ptrBuf1, float[] buf2, int ptrBuf2, float rSin, float rCos, int nStep, int nCount)
        {
            for (int i = 0; i < nCount; i++)
            {
                float r1 = buf1[ptrBuf1];
                float r2 = buf2[ptrBuf2];

                buf1[ptrBuf1] = r1 * rCos - r2 * rSin;
                buf2[ptrBuf2] = r1 * rSin + r2 * rCos;

                ptrBuf1 += nStep;
                ptrBuf2 += nStep;
            }
        }
    }
}
