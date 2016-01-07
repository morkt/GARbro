//! \file       NAudio.cs
//! \date       Thu Nov 06 23:27:00 2014
//! \brief      NAudio wrappers for GameRes audio resources.
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

using NAudio.Wave;

namespace GARbro
{
    public class WaveStreamImpl : WaveStream
    {
        GameRes.SoundInput  m_input;
        WaveFormat          m_format;

        public override WaveFormat WaveFormat { get { return m_format; } }

        public override long Position
        {
            get { return m_input.Position; }
            set { m_input.Position = value; }
        }

        public override long Length { get { return m_input.Length; } }

        public WaveStreamImpl (GameRes.SoundInput input)
        {
            m_input = input;
            var format = m_input.Format;
            m_format = WaveFormat.CreateCustomFormat ((WaveFormatEncoding)format.FormatTag,
                                                      (int)format.SamplesPerSecond,
                                                      format.Channels,
                                                      (int)format.AverageBytesPerSecond,
                                                      format.BlockAlign,
                                                      format.BitsPerSample);
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            return m_input.Read (buffer, offset, count);
        }

        public override int ReadByte ()
        {
            return m_input.ReadByte();
        }

        #region IDisposable Members
        bool disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    m_input.Dispose();
                }
                disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }
}
