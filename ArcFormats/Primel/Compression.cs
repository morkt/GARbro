//! \file       Compression.cs
//! \date       Mon Oct 03 12:55:45 2016
//! \brief      Primel Adventure System compression classes.
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameRes.Utility;

namespace GameRes.Formats.Primel
{
    internal abstract class PackedStream : InputProxyStream
    {
        private IEnumerator<int> m_unpacker;
        private bool             m_eof;
        private byte[]  m_buffer;
        private int     m_offset;
        private int     m_count;

        protected PackedStream (Stream input) : base (input)
        {
            m_eof = false;
        }

        protected bool YieldByte (byte c)
        {
            m_buffer[m_offset++] = c;
            return --m_count <= 0;
        }

        protected int YieldOffset { get { return m_offset; } }

        public override bool CanSeek  { get { return false; } }
        public override long Length   { get { throw new NotSupportedException(); } }
        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            if (m_eof || 0 == count)
                return 0;

            m_buffer = buffer;
            m_offset = offset;
            m_count = count;
            if (null == m_unpacker)
                m_unpacker = Unpack();
            m_eof = !m_unpacker.MoveNext();
            return m_offset - offset;
        }

        protected abstract IEnumerator<int> Unpack ();

        #region IDisposable Members
        bool m_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                if (null != m_unpacker)
                    m_unpacker.Dispose();
                m_disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }

    internal class LzssPackedStream : PackedStream
    {
        public LzssPackedStream (Stream input) : base (input)
        {
        }

        protected override IEnumerator<int> Unpack ()
        {
            int unpacked_size, frame_size;
            using (var reader = new ArcView.Reader (BaseStream))
            {
                unpacked_size = reader.ReadInt32();
                frame_size = 2 << reader.ReadUInt16();
            }
            var frame = new byte[frame_size];
            int frame_pos = 0;
            int dst = 0;
            int bits = 2;
            while (dst < unpacked_size)
            {
                bits >>= 1;
                if (1 == bits)
                {
                    bits = BaseStream.ReadByte();
                    if (-1 == bits)
                        yield break;
                    bits |= 0x100;
                }
                int c = BaseStream.ReadByte();
                if (-1 == c)
                    yield break;
                if (0 != (bits & 1))
                {
                    if (YieldByte ((byte)c))
                        yield return YieldOffset;
                    frame[frame_pos++ % frame_size] = (byte)c;
                    ++dst;
                }
                else
                {
                    int p = c | BaseStream.ReadByte() << 8;
                    int count = BaseStream.ReadByte();
                    if (-1 == count)
                        yield break;
                    count += 4;
                    p = frame_pos - p;
                    if (p < 0)
                        p += frame_size;

                    while (count --> 0)
                    {
                        byte b = frame[p++ % frame_size];
                        if (YieldByte (b))
                            yield return YieldOffset;
                        frame[frame_pos++ % frame_size] = b;
                        ++dst;
                    }
                }
            }
        }
    }

    internal class RlePackedStream : PackedStream
    {
        public RlePackedStream (Stream input) : base (input)
        {
        }

        protected override IEnumerator<int> Unpack ()
        {
            int unpacked_size;
            using (var reader = new ArcView.Reader (BaseStream))
                unpacked_size = reader.ReadInt32();
            int dst = 0;
            int prev_byte = BaseStream.ReadByte();
            while (dst+1 < unpacked_size)
            {
                int b = BaseStream.ReadByte();
                if (-1 == b)
                    break;
                if (b == prev_byte)
                {
                    int count = BaseStream.ReadByte();
                    if (-1 == count)
                        break;
                    count += 2;
                    while (count --> 0)
                    {
                        if (YieldByte ((byte)b))
                            yield return YieldOffset;
                        ++dst;
                    }
                    b = BaseStream.ReadByte();
                }
                else
                {
                    if (YieldByte ((byte)prev_byte))
                        yield return YieldOffset;
                    ++dst;
                }
                prev_byte = b;
            }
            if (dst < unpacked_size && prev_byte != -1)
            {
                YieldByte ((byte)prev_byte);
            }
        }
    }

    internal class RangePackedStream : PackedStream
    {
        public RangePackedStream (Stream input) : base (input)
        {
        }

        protected override IEnumerator<int> Unpack ()
        {
            var freq = new ushort[0x100];
            var table2 = new byte[0xFFFF00];
            var table3 = new uint[0x100];
            var table4 = new uint[0x100];
            using (var reader = new ArcView.Reader (BaseStream))
            {
                for (;;)
                {
                    int chunk_len = reader.ReadInt32();

                    byte ctl = reader.ReadByte();
                    for (int i = 0; i < 0x100; ++i)
                        freq[i] = 0;

                    switch (ctl & 0x1F)
                    {
                    case 1:
                        int count = reader.ReadByte();
                        while (count --> 0)
                        {
                            byte i = reader.ReadByte();
                            byte b = reader.ReadByte();

                            if (0 != (b & 0x80))
                                freq[i] = (ushort)(b & 0x7F);
                            else
                                freq[i] = (ushort)((reader.ReadByte() << 7) | b);
                        }
                        break;

                    case 2:
                        for (int i = 0; i < 256; i++)
                        {
                            byte b = reader.ReadByte();

                            if (0 != (b & 0x80))
                                freq[i] = (ushort)(b & 0x7F);
                            else
                                freq[i] = (ushort)((reader.ReadByte() << 7) | b);
                        }
                        break;
                    }

                    uint f = 0;
                    for (int i = 0; i < 0x100; i++)
                    {
                        table3[i] = f;
                        table4[i] = freq[i];

                        for (int j = freq[i]; j > 0; --j)
                            table2[f++] = (byte)i;
                    }

                    uint range = 0xC0000000;
                    uint high  = Binary.BigEndian (reader.ReadUInt32());

                    for (int i = 0; i < chunk_len; i++)
                    {
                        uint index = high / (range >> 12);
                        byte c = table2[index];

                        if (YieldByte (c))
                            yield return YieldOffset;

                        high -= (range >> 12) * table3[c];
                        range = (range >> 12) * table4[c];

                        while (0 == (range & 0xFF000000))
                        {
                            high = (high << 8) | reader.ReadByte();
                            range <<= 8;
                        }
                    }
                    if (0 == (ctl & 0x80))
                        break;
                }
            }
        }
    }

    internal class MtfPackedStream : PackedStream
    {
        public MtfPackedStream (Stream input) : base (input)
        {
        }

        protected override IEnumerator<int> Unpack ()
        {
            int start_index;
            using (var reader = new ArcView.Reader (BaseStream))
                start_index = reader.ReadInt32();

            byte[] table1 = Enumerable.Range (0, 256).Select (x => (byte)x).ToArray();
            var input = new List<byte>();
            for (int i = 0; ; ++i)
            {
                int b = BaseStream.ReadByte();
                if (-1 == b)
                    break;
                byte c    = table1[b];
                byte prev = table1[0];

                if (prev != c)
                {
                    for (int j = 1; ; ++j)
                    {
                        byte t = table1[j];
                        table1[j] = prev;
                        prev = t;

                        if (t == c) break;
                    }
                    table1[0] = c;
                }
                input.Add (c);
            }
            int input_length = input.Count;
            var table2 = new int[256];
            for (int i = 0; i < input_length; ++i)
                table2[input[i]]++;

            int l = input_length;
            for (int i = 255; i >= 0; --i)
            {
                l -= table2[i];
                table2[i] = l;
            }

            var order = new int[input_length];
            for (int i = 0; i < input_length; ++i)
                order[table2[input[i]]++] = i;

            int index = start_index;
            for (;;) // XXX stream is endless, should be wrapped into LimitStream
            {
                index = order[index];
                if (YieldByte (input[index]))
                    yield return YieldOffset;
            }
        }
    }
}
