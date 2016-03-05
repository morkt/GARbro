//! \file       LzssStream.cs
//! \date       Sat Jul 25 03:48:03 2015
//! \brief      LZSS compressed stream I/O
//
// Lempel–Ziv–Storer–Szymanski (LZSS) compression algorithm.
//
// C# implementation Copyright (C) 2014-2015 by morkt
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
using System.IO;

namespace GameRes.Compression
{
    public enum LzssMode
    {
        Decompress,
        Compress,
    };

    public class LzssSettings
    {
        public int     FrameSize { get; set; }
        public byte    FrameFill { get; set; }
        public int  FrameInitPos { get; set; }
    }

    internal sealed class LzssCoroutine : LzssSettings, IDisposable
    {
        byte[] m_buffer;
        int    m_offset;
        int    m_length;

        Stream  m_input;

        IEnumerator<int> m_unpack;

        public bool          Eof { get; private set; }

        public LzssCoroutine (Stream input)
        {
            m_input = input;

            FrameSize = 0x1000;
            FrameFill = 0;
            FrameInitPos = 0xfee;
        }

        public int Continue (byte[] buffer, int offset, int count)
        {
            m_buffer = buffer;
            m_offset = offset;
            m_length = count;
            if (null == m_unpack)
                m_unpack = Unpack();
            Eof = !m_unpack.MoveNext();
            return m_offset - offset;
        }

        private IEnumerator<int> Unpack ()
        {
            byte[] frame = new byte[FrameSize];
            int frame_pos = FrameInitPos;
            int frame_mask = FrameSize-1;
            for (;;)
            {
                int ctl = m_input.ReadByte();
                if (-1 == ctl)
                    yield break;
                for (int bit = 1; bit != 0x100; bit <<= 1)
                {
                    if (0 != (ctl & bit))
                    {
                        int b = m_input.ReadByte();
                        if (-1 == b)
                            yield break;
                        frame[frame_pos++] = (byte)b;
                        frame_pos &= frame_mask;
                        m_buffer[m_offset++] = (byte)b;
                        if (0 == --m_length)
                            yield return m_offset;
                    }
                    else
                    {
                        int lo = m_input.ReadByte();
                        if (-1 == lo)
                            yield break;
                        int hi = m_input.ReadByte();
                        if (-1 == hi)
                            yield break;
                        int offset = (hi & 0xf0) << 4 | lo;
                        for (int count = 3 + (hi & 0xF); count != 0; --count)
                        {
                            byte v = frame[offset++];
                            offset &= frame_mask;
                            frame[frame_pos++] = v;
                            frame_pos &= frame_mask;
                            m_buffer[m_offset++] = v;
                            if (0 == --m_length)
                                yield return m_offset;
                        }
                    }
                }
            }
        }

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                if (m_unpack != null)
                    m_unpack.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize (this);
        }
        #endregion
    }

    public class LzssStream : Stream
    {
        Stream          m_input;
        LzssCoroutine   m_reader;
        bool            m_should_dispose;

        public LzssStream (Stream input, LzssMode mode = LzssMode.Decompress, bool leave_open = false)
        {
            if (mode != LzssMode.Decompress)
                throw new NotImplementedException ("LzssStream compression not implemented");
            m_input = input;
            m_reader = new LzssCoroutine (input);
            m_should_dispose = !leave_open;
        }

        public LzssSettings   Config  { get { return m_reader; } }

        public override bool CanRead  { get { return m_input.CanRead; } }
        public override bool CanSeek  { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length
        {
            get { throw new NotSupportedException ("LzssStream.Length property is not supported"); }
        }
        public override long Position
        {
            get { throw new NotSupportedException ("LzssStream.Position property is not supported"); }
            set { throw new NotSupportedException ("LzssStream.Position property is not supported"); }
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            if (!m_reader.Eof && count > 0)
                return m_reader.Continue (buffer, offset, count);
            return 0;
        }

        public override void Flush()
        {
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            throw new NotSupportedException ("LzssStream.Seek method is not supported");
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("LzssStream.SetLength method is not supported");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("LzssStream.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new NotSupportedException ("LzssStream.WriteByte method is not supported");
        }

        #region IDisposable Members
        bool m_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                if (m_should_dispose && disposing)
                    m_input.Dispose();
                m_reader.Dispose();
                m_disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }

    public class LzssReader : IDisposable
    {
        BinaryReader    m_input;
        byte[]          m_output;
        int             m_size;

        public BinaryReader Input { get { return m_input; } }
        public byte[]        Data { get { return m_output; } }
        public int      FrameSize { get; set; }
        public byte     FrameFill { get; set; }
        public int   FrameInitPos { get; set; }

        public LzssReader (Stream input, int input_length, int output_length)
        {
            m_input = new BinaryReader (input, System.Text.Encoding.ASCII, true);
            m_output = new byte[output_length];
            m_size = input_length;

            FrameSize = 0x1000;
            FrameFill = 0;
            FrameInitPos = 0xfee;
        }

        public void Unpack ()
        {
            int dst = 0;
            var frame = new byte[FrameSize];
            if (FrameFill != 0)
                for (int i = 0; i < frame.Length; ++i)
                    frame[i] = FrameFill;
            int frame_pos = FrameInitPos;
            int frame_mask = FrameSize-1;
            int remaining = (int)m_size;
            while (remaining > 0)
            {
                int ctl = m_input.ReadByte();
                --remaining;
                for (int bit = 1; remaining > 0 && bit != 0x100; bit <<= 1)
                {
                    if (dst >= m_output.Length)
                        return;
                    if (0 != (ctl & bit))
                    {
                        byte b = m_input.ReadByte();
                        --remaining;
                        frame[frame_pos++] = b;
                        frame_pos &= frame_mask;
                        m_output[dst++] = b;
                    }
                    else
                    {
                        if (remaining < 2)
                            return;
                        int lo = m_input.ReadByte();
                        int hi = m_input.ReadByte();
                        remaining -= 2;
                        int offset = (hi & 0xf0) << 4 | lo;
                        for (int count = 3 + (hi & 0xF); count != 0; --count)
                        {
                            if (dst >= m_output.Length)
                                break;
                            byte v = frame[offset++];
                            offset &= frame_mask;
                            frame[frame_pos++] = v;
                            frame_pos &= frame_mask;
                            m_output[dst++] = v;
                        }
                    }
                }
            }
        }

        #region IDisposable Members
        bool disposed = false;

        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    m_input.Dispose();
                }
                disposed = true;
            }
        }
        #endregion
    }
}
