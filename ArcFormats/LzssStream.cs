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

    public sealed class LzssCoroutine : Decompressor
    {
        Stream          m_input;
        LzssSettings    m_settings;

        public LzssSettings Settings { get { return m_settings; } }

        public override void Initialize (Stream input)
        {
            m_input = input;
            m_settings = new LzssSettings {
                FrameSize = 0x1000,
                FrameFill = 0,
                FrameInitPos = 0xFEE,
            };
        }

        protected override IEnumerator<int> Unpack ()
        {
            byte[] frame = new byte[Settings.FrameSize];
            if (Settings.FrameFill != 0)
                for (int i = 0; i < frame.Length; ++i)
                    frame[i] = Settings.FrameFill;
            int frame_pos = Settings.FrameInitPos;
            int frame_mask = Settings.FrameSize-1;
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
                        frame[frame_pos++ & frame_mask] = (byte)b;
                        m_buffer[m_pos++] = (byte)b;
                        if (0 == --m_length)
                            yield return m_pos;
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
                            byte v = frame[offset++ & frame_mask];
                            frame[frame_pos++ & frame_mask] = v;
                            m_buffer[m_pos++] = v;
                            if (0 == --m_length)
                                yield return m_pos;
                        }
                    }
                }
            }
        }
    }

    public class LzssStream : PackedStream<LzssCoroutine>
    {
        public LzssStream (Stream input, LzssMode mode = LzssMode.Decompress, bool leave_open = false)
            : base (input, leave_open)
        {
            if (mode != LzssMode.Decompress)
                throw new NotImplementedException ("LzssStream compression not implemented");
        }

        public LzssSettings   Config  { get { return Reader.Settings; } }
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
