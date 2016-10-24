//! \file       AudioWMA.cs
//! \date       Sun Oct 23 21:29:19 2016
//! \brief      Windows Media Audio format.
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
using NAudio.Wave;

namespace GameRes.Formats
{
    [Export(typeof(AudioFormat))]
    public class WmaAudio : AudioFormat
    {
        public override string         Tag { get { return "WMA"; } }
        public override string Description { get { return "Windows Media Audio format"; } }
        public override uint     Signature { get { return 0x75B22630; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            return new WmaInput (file.AsStream);
        }
    }

    public class WmaInput : SoundInput
    {
        int                     m_bitrate;
        MediaFoundationReader   m_reader;

        public override long Position
        {
            get { return m_reader.Position; }
            set { m_reader.Position = value; }
        }

        public override bool CanSeek { get { return true; } }

        public override int SourceBitrate
        {
            get { return m_bitrate; }
        }

        public override string SourceFormat { get { return "wma"; } }

        public WmaInput (Stream file) : base (file)
        {
            m_reader = new StreamMediaFoundationReader (file);
            m_bitrate = m_reader.WaveFormat.AverageBytesPerSecond * 8;
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

        public override int Read (byte[] buffer, int offset, int count)
        {
            return m_reader.Read (buffer, offset, count);
        }

        #region IDisposable Members
        bool _wma_disposed;
        protected override void Dispose (bool disposing)
        {
            if (!_wma_disposed)
            {
                if (disposing)
                {
                    m_reader.Dispose();
                }
                _wma_disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }
}
