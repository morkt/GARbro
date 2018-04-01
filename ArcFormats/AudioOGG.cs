//! \file       AudioOGG.cs
//! \date       Fri Nov 07 00:32:15 2014
//! \brief      NVorbis wrapper for GameRes.
//
// Copyright (C) 2014 by morkt
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
using System.Linq;
using GameRes.Utility;
using NVorbis;

namespace GameRes.Formats
{
    public class OggInput : SoundInput
    {
        VorbisReader    m_reader;

        public override long Position
        {
            get
            {
                return (long)(m_reader.DecodedTime.TotalSeconds * m_reader.SampleRate * m_reader.Channels * sizeof(float));
            }
            set
            {
                if (value < 0 || value > Length) throw new ArgumentOutOfRangeException("value");

                m_reader.DecodedTime = TimeSpan.FromSeconds((double)value / m_reader.SampleRate / m_reader.Channels / sizeof(float));
            }
        }

        public override bool CanSeek { get { return Source.CanSeek; } }

        public override int SourceBitrate
        {
            get { return m_reader.NominalBitrate; }
        }

        public override string SourceFormat { get { return "ogg"; } }

        public OggInput (Stream file) : base (file)
        {
            m_reader = new VorbisReader (Source, false);
            var format = new GameRes.WaveFormat();
            format.FormatTag                = 3; // WAVE_FORMAT_IEEE_FLOAT
            format.Channels                 = (ushort)m_reader.Channels;
            format.SamplesPerSecond         = (uint)m_reader.SampleRate;
            format.BitsPerSample            = 32;
            format.BlockAlign               = (ushort)(4 * format.Channels);
            format.AverageBytesPerSecond    = format.SamplesPerSecond * format.BlockAlign;
            this.Format = format;
            this.PcmSize = (long)(m_reader.TotalTime.TotalSeconds * format.SamplesPerSecond * format.Channels * sizeof(float));
        }

        public override void Reset ()
        {
            m_reader.DecodedTime = TimeSpan.FromSeconds (0);
        }

        // This buffer can be static because it can only be used by 1 instance per thread
        [ThreadStatic]
        static float[] _conversionBuffer = null;

        public override int Read(byte[] buffer, int offset, int count)
        {
            // adjust count so it is in floats instead of bytes
            count /= sizeof(float);

            // make sure we don't have an odd count
            count -= count % m_reader.Channels;

            // get the buffer, creating a new one if none exists or the existing one is too small
            var cb = _conversionBuffer ?? (_conversionBuffer = new float[count]);
            if (cb.Length < count)
            {
                cb = (_conversionBuffer = new float[count]);
            }

            // let ReadSamples(float[], int, int) do the actual reading; adjust count back to bytes
            int cnt = m_reader.ReadSamples (cb, 0, count) * sizeof(float);

            // move the data back to the request buffer
            Buffer.BlockCopy (cb, 0, buffer, offset, cnt);

            // done!
            return cnt;
        }

        #region IDisposable Members
        bool _ogg_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!_ogg_disposed)
            {
                if (disposing)
                {
                    m_reader.Dispose();
                }
                _ogg_disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }

    [Export(typeof(AudioFormat))]
    public sealed class OggAudio : AudioFormat
    {
        public override string         Tag { get { return "OGG"; } }
        public override string Description { get { return "Ogg/Vorbis audio format"; } }
        public override uint     Signature { get { return 0x5367674f; } } // 'OggS'
        public override bool      CanWrite { get { return false; } }

        LocalResourceSetting FixCrc = new LocalResourceSetting ("OGGFixCrc");

        public OggAudio ()
        {
            Signatures = new uint[] { 0x5367674F, 0 };
            Settings = new[] { FixCrc };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            Stream input = file.AsStream;
            if (file.Signature == Wav.Signature)
            {
                var header = file.ReadHeader (0x14);
                if (!header.AsciiEqual (8, "WAVEfmt "))
                    return null;
                uint fmt_size = header.ToUInt32 (0x10);
                long fmt_pos = file.Position;
                ushort format = file.ReadUInt16();
                if (format != 0x676F && format != 0x6770 && format != 0x6771 && format != 0x674F)
                    return null;
                // interpret WAVE 'data' section as Ogg stream
                file.Position = fmt_pos + ((fmt_size + 1) & ~1);
                for (;;) // ended by end-of-stream exception
                {
                    uint section_id = file.ReadUInt32();
                    uint section_size = file.ReadUInt32();
                    if (section_id == 0x61746164) // 'data'
                    {
                        long ogg_pos = file.Position;
                        uint id = file.ReadUInt32();
                        if (id != Signature)
                            return null;
                        input = new StreamRegion (input, ogg_pos, section_size);
                        break;
                    }
                    file.Seek ((section_size + 1) & ~1u, SeekOrigin.Current);
                }
            }
            else if (file.Signature != this.Signature)
                return null;
            if (FixCrc.Get<bool>())
                input = new SeekableStream (new OggRestoreStream (input));
            return new OggInput (input);
        }

        public static AudioFormat Instance { get { return s_OggFormat.Value; } }

        static readonly ResourceInstance<AudioFormat> s_OggFormat = new ResourceInstance<AudioFormat> ("OGG");
    }

    /// <summary>
    /// Restore CRC checksums of the OGG stream pages.
    /// </summary>
    internal class OggRestoreStream : InputProxyStream
    {
        bool                m_eof;
        bool                m_ogg_ended;

        byte[]              m_page = new byte[0x10000];
        int                 m_page_pos = 0;
        int                 m_page_length = 0;

        public override bool CanSeek { get { return false; } }

        public OggRestoreStream (Stream input) : base (input)
        {
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int total_read = 0;
            while (count > 0 && !m_eof)
            {
                if (m_page_pos == m_page_length)
                {
                    if (m_ogg_ended)
                    {
                        int read = BaseStream.Read (buffer, offset, count);
                        total_read += read;
                        m_eof = 0 == read;
                        break;
                    }
                    NextPage();
                }
                if (m_eof)
                    break;
                int available = Math.Min (m_page_length - m_page_pos, count);
                Buffer.BlockCopy (m_page, m_page_pos, buffer, offset, available);
                m_page_pos += available;
                offset += available;
                count -= available;
                total_read += available;
            }
            return total_read;
        }

        void NextPage ()
        {
            m_page_pos = 0;
            m_page_length = BaseStream.Read (m_page, 0, 0x1B);
            if (0 == m_page_length)
            {
                m_eof = true;
                return;
            }
            if (m_page_length < 0x1B || !m_page.AsciiEqual ("OggS"))
            {
                m_ogg_ended = true;
                return;
            }
            int segment_count = m_page[0x1A];
            m_page[0x16] = 0;
            m_page[0x17] = 0;
            m_page[0x18] = 0;
            m_page[0x19] = 0;
            if (segment_count != 0)
            {
                int table_length = BaseStream.Read (m_page, 0x1B, segment_count);
                m_page_length += table_length;
                if (table_length != segment_count)
                {
                    m_ogg_ended = true;
                    return;
                }
                int segments_length = 0;
                int segment_table = 0x1B;
                for (int i = 0; i < segment_count; ++i)
                    segments_length += m_page[segment_table++];
                m_page_length += BaseStream.Read (m_page, 0x1B+segment_count, segments_length);
            }
            uint crc = Crc32Normal.UpdateCrc (0, m_page, 0, m_page_length);
            LittleEndian.Pack (crc, m_page, 0x16);
        }
    }
}
