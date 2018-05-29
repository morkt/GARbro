//! \file       AudioMP3.cs
//! \date       Fri May 01 22:04:37 2015
//! \brief      MP3 wrapper for GameRes.
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

using System.ComponentModel.Composition;
using System.IO;
using NAudio.Wave;

namespace GameRes.Formats
{
    public class Mp3Input : SoundInput
    {
        int             m_bitrate;
        Mp3FileReader   m_reader;

        public override long Position
        {
            get { return m_reader.Position; }
            set { m_reader.Position = value; }
        }

        public override bool CanSeek { get { return m_reader.CanSeek; } }

        public override int SourceBitrate
        {
            get { return m_bitrate; }
        }

        public override string SourceFormat { get { return "mp3"; } }

        public Mp3Input (Stream file) : base (file)
        {
            m_reader = new Mp3FileReader (file);
            m_bitrate = m_reader.Mp3WaveFormat.AverageBytesPerSecond*8;
            var format = new GameRes.WaveFormat();
            format.FormatTag                = (ushort)m_reader.WaveFormat.Encoding;
            format.Channels                 = (ushort)m_reader.WaveFormat.Channels;
            format.SamplesPerSecond         = (uint)m_reader.WaveFormat.SampleRate;
            format.BitsPerSample            = (ushort)m_reader.WaveFormat.BitsPerSample;
            format.BlockAlign               = (ushort)m_reader.BlockAlign;
            format.AverageBytesPerSecond    = (uint)m_reader.WaveFormat.AverageBytesPerSecond;
            this.Format = format;
            this.PcmSize = m_reader.Length;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return m_reader.Read (buffer, offset, count);
        }

        #region IDisposable Members
        bool _mp3_disposed;
        protected override void Dispose (bool disposing)
        {
            if (!_mp3_disposed)
            {
                if (disposing)
                {
                    m_reader.Dispose();
                }
                _mp3_disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }

    [Export(typeof(AudioFormat))]
    public class Mp3Audio : AudioFormat
    {
        public override string         Tag { get { return "MP3"; } }
        public override string Description { get { return "MPEG Layer 3 audio format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        const int SyncSearchThreshold = 0x300;

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (10).ToArray();
            long start_offset = SkipId3Tag (header);
            int sync_pos = 0;
            if (0 != start_offset)
            {
                file.Position = start_offset;
                if (4 != file.Read (header, 0, 4))
                    return null;
            }
            else if (0xFF != header[0])
            {
                file.Position = 1;
                header = file.ReadBytes (SyncSearchThreshold);
                sync_pos = System.Array.IndexOf<byte> (header, 0xFF, 1, SyncSearchThreshold-4);
                if (-1 == sync_pos)
                    return null;
            }
            if (0xFF != header[sync_pos] || 0xE2 != (header[sync_pos+1] & 0xE6) || 0xF0 == (header[sync_pos+2] & 0xF0))
                return null;
            file.Position = 0;
            return new Mp3Input (file.AsStream);
        }

        long SkipId3Tag (byte[] buffer)
        {
            long start_offset = 0;
            if (0x49 == buffer[0] && 0x44 == buffer[1] && 0x33 == buffer[2]) // 'ID3'
            {
                if (buffer[3] < 0x80 && buffer[4] < 0x80 &&
                    buffer[6] < 0x80 && buffer[7] < 0x80 && buffer[8] < 0x80 && buffer[9] < 0x80)
                {
                    int size = buffer[6] << 21 | buffer[7] << 14 | buffer[8] << 7 | buffer[9];
                    if (buffer[3] > 3 && 0 != (buffer[5] & 0x10)) // v2.4 footer present
                        size += 10;
                    start_offset = 10 + size;
                }
            }
            return start_offset;
        }
    }
}
