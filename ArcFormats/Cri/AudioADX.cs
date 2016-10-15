//! \file       AudioADX.cs
//! \date       Wed Mar 09 11:21:57 2016
//! \brief      CRI MiddleWare ADPCM audio.
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
using System.Threading;
using GameRes.Utility;

namespace GameRes.Formats.Cri
{
    [Export(typeof(AudioFormat))]
    public class AdxAudio : AudioFormat
    {
        public override string         Tag { get { return "ADX"; } }
        public override string Description { get { return "CRI MiddleWare ADPCM audio"; } }
        public override uint     Signature { get { return 0; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            uint signature = file.Signature;
            if (0x80 != (signature & 0xFFFF))
                return null;
            uint header_size = Binary.BigEndian (signature & 0xFFFF0000);
            if (header_size < 0x10 || header_size >= file.Length)
                return null;
            var header = file.ReadBytes (header_size);
            if (header.Length != header_size)
                return null;
            if (!Binary.AsciiEqual (header, header.Length-6, "(c)CRI"))
                return null;

            return new AdxInput (file.AsStream, header);
        }
    }

    internal class AdxInput : SoundInput
    {
        AdxReader       m_reader;
        int             m_data_offset;
        int             m_bitrate;
        long            m_position;
        int             m_buffered_sample;
        int             m_buffered_count;
        int             m_bytes_per_frame;
        ThreadLocal<short[]> m_frame_buffer;

        public override string SourceFormat { get { return "adx"; } }
        public override int   SourceBitrate { get { return m_bitrate; } }

        public AdxInput (Stream file, byte[] header) : base (file)
        {
            m_reader = new AdxReader (file, header);
            m_data_offset = 4 + header.Length;
            this.Format = m_reader.Format;
            this.PcmSize = m_reader.SampleCount * Format.BlockAlign;
            m_bitrate = (int)(Format.SamplesPerSecond * (file.Length-m_data_offset) * 8 / m_reader.SampleCount);
            int frame_buffer_length = m_reader.SamplesPerFrame * m_reader.Format.Channels;
            m_frame_buffer = new ThreadLocal<short[]> (() => new short[frame_buffer_length]);
            m_bytes_per_frame = frame_buffer_length * Format.BitsPerSample / 8;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int total_read = 0;
            int current_sample = (int)(m_position / Format.BlockAlign);
            bool need_refill = !(current_sample >= m_buffered_sample && current_sample < m_buffered_sample + m_buffered_count);
            int src_offset = (int)(m_position % m_bytes_per_frame);
            while (count > 0 && m_position < PcmSize)
            {
                if (need_refill)
                    FillBuffer();
                int available = Math.Min (count, m_buffered_count * Format.BlockAlign - src_offset);
                Buffer.BlockCopy (m_frame_buffer.Value, src_offset, buffer, offset, available);
                offset += available;
                count -= available;
                total_read += available;
                m_position += available;
                src_offset = 0;
                need_refill = true;
            }
            return total_read;
        }

        void FillBuffer ()
        {
            int frame_number = (int)(m_position / m_bytes_per_frame);
            m_reader.SetPosition (m_data_offset + frame_number * m_reader.FrameSize * Format.Channels);
            for (int i = 0; i < Format.Channels; ++i)
            {
                m_reader.DecodeFrame (i, m_frame_buffer.Value);
            }
            m_buffered_sample = frame_number * m_reader.SamplesPerFrame;
            m_buffered_count = Math.Min (m_reader.SampleCount - m_buffered_sample, m_reader.SamplesPerFrame);
        }

        // FIXME
        // such implementation of seek is broken since it doesn't take into account ADPCM decoder 'history',
        // therefore CanSeek returns false.

        public override bool CanSeek { get { return false; } }
        public override long Position
        {
            get { return m_position; }
            set { m_position = Math.Max (value, 0); }
        }

        #region IDisposable Members
        bool _adx_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!_adx_disposed)
            {
                if (disposing)
                {
                    m_reader.Dispose();
                    m_frame_buffer.Dispose();
                }
                _adx_disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }

    internal sealed class AdxReader : IDisposable
    {
        MsbBitStream    m_input;
        WaveFormat      m_format;
        int             m_samples_per_frame;
        int             m_prev_scale0;
        int             m_prev_scale1;
        int[][]         m_prev_samples;

        public WaveFormat   Format { get { return m_format; } }
        public int SamplesPerFrame { get { return m_samples_per_frame; } }
        public int     SampleCount { get; private set; }
        public int       FrameSize { get; private set; }
        public int   BitsPerSample { get; private set; }

        public AdxReader (Stream file, byte[] header)
        {
            FrameSize = header[1];
            BitsPerSample = header[2];
            if (header[0] != 3 || FrameSize != 0x12 || BitsPerSample != 4)
                throw new InvalidFormatException();
            m_samples_per_frame = (FrameSize - 2) * 8 / BitsPerSample;
            m_format.Channels = header[3];
            if (0 == m_format.Channels || m_format.Channels > 16)
                throw new InvalidFormatException();
            int encoding = BigEndian.ToInt16 (header, 0x0E);
            if (encoding != 0x0400)
                throw new NotSupportedException ("Not supported ADX encoding");

            m_format.FormatTag              = 1;
            m_format.SamplesPerSecond       = BigEndian.ToUInt32 (header, 4);
            m_format.BitsPerSample          = 16;
            m_format.BlockAlign             = (ushort)(m_format.BitsPerSample * m_format.Channels / 8);
            m_format.AverageBytesPerSecond  = m_format.SamplesPerSecond * m_format.BlockAlign;
            SampleCount = BigEndian.ToInt32 (header, 8);

            int lowest_freq = BigEndian.ToUInt16 (header, 12);
            var sqrt2 = Math.Sqrt (2.0);
            var x = sqrt2 - Math.Cos (2 * Math.PI * lowest_freq / m_format.SamplesPerSecond);
            var y = sqrt2 - 1;
            var z = (x - Math.Sqrt ((x + y) * (x - y))) / y;

            m_prev_scale0 = (int)Math.Floor (z * 8192);
            m_prev_scale1 = (int)Math.Floor (z * z * -4096);
            m_prev_samples = new int[m_format.Channels][];
            for (int i = 0; i < m_format.Channels; ++i)
            {
                m_prev_samples[i] = new int[2];
            }

            file.Position = 4+header.Length;
            m_input = new MsbBitStream (file, true);
        }

        /*
        public void Decode (Stream output)
        {
            int block_align = (int)m_format.BlockAlign;
            var frame = new short[m_samples_per_frame * Format.Channels];
            int sample = 0;
            while (sample < SampleCount)
            {
                int channel_offset = 0;
                for (int i = 0; i < m_format.Channels; ++i)
                {
                    DecodeFrame (i, frame);
                }
                int samples_in_frame = Math.Min (SampleCount - sample, m_samples_per_frame);
                output.Write (frame, 0, samples_in_frame * block_align); // XXX convert to byte array
                sample += samples_in_frame;
            }
        }
        */

        public void SetPosition (long position)
        {
            m_input.Input.Position = position;
            m_input.Reset();
        }

        public void DecodeFrame (int channel, short[] output)
        {
            int offset = channel;
            var prev_samples = m_prev_samples[channel];
            int scale = (short)m_input.GetBits (16) + 1;
            for (int i = 0; i < m_samples_per_frame; ++i)
            {
                int sample = NibbleToSigned (m_input.GetBits (BitsPerSample));
                int adjust = (m_prev_scale0 * prev_samples[0] + m_prev_scale1 * prev_samples[1]) >> 12;
                sample = Clamp16 (sample * scale + adjust);
                output[offset] = (short)sample;
                offset += m_format.Channels;
                prev_samples[1] = prev_samples[0];
                prev_samples[0] = sample;
            }
        }

        static int NibbleToSigned (int n)
        {
            return (n & 7) - (n & 8);
        }

        static int Clamp16 (int sample)
        {
            if (sample > 0x7FFF)
                sample = 0x7FFF;
            else if (sample < -0x8000)
                sample = -0x8000;
            return sample;
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
