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
            if (!Binary.AsciiEqual (header, 0x10, "Music Interleaved"))
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
                    m_worker.Dispose();
                }
                _mio_disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }
}
