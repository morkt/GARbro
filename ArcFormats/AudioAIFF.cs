//! \file       AudioAIFF.cs
//! \date       2018 Jul 04
//! \brief      Audio Interchange File Format.
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

using System.ComponentModel.Composition;
using System.IO;
using NAudio.Wave;

namespace GameRes.Formats
{
    [Export(typeof(AudioFormat))]
    public class AiffAudio : AudioFormat
    {
        public override string         Tag { get { return "AIFF"; } }
        public override string Description { get { return "Audio Interchange File Format"; } }
        public override uint     Signature { get { return 0x4D524F46; } } // 'FORM'
        public override bool      CanWrite { get { return false; } }

        public AiffAudio ()
        {
            Extensions = new string[] { "aif", "aiff" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            return new AiffInput (file.AsStream);
        }
    }

    public class AiffInput : SoundInput
    {
        int                     m_bitrate = 0;
        AiffFileReader          m_reader;

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

        public override string SourceFormat { get { return "aiff"; } }

        public AiffInput (Stream file) : base (file)
        {
            m_reader = new AiffFileReader (file);
            var format = new GameRes.WaveFormat {
                FormatTag                = (ushort)m_reader.WaveFormat.Encoding,
                Channels                 = (ushort)m_reader.WaveFormat.Channels,
                SamplesPerSecond         = (uint)m_reader.WaveFormat.SampleRate,
                BitsPerSample            = (ushort)m_reader.WaveFormat.BitsPerSample,
                BlockAlign               = (ushort)m_reader.BlockAlign,
                AverageBytesPerSecond    = (uint)m_reader.WaveFormat.AverageBytesPerSecond,
            };
            this.Format = format;
            this.PcmSize = m_reader.Length;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            return m_reader.Read (buffer, offset, count);
        }

        #region IDisposable Members
        bool m_disposed;
        protected override void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                {
                    m_reader.Dispose();
                }
                m_disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }
}
