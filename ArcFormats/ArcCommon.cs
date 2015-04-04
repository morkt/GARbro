//! \file       ArcCommon.cs
//! \date       Tue Aug 19 09:45:38 2014
//! \brief      Classes and functions common for various resource files.
//
// Copyright (C) 2014-2015 by morkt
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
using System.IO;
using System.Linq;
using System.Text;

namespace GameRes.Formats
{
    public class AutoEntry : Entry
    {
        private Lazy<IResource> m_res;
        private Lazy<string> m_name;
        private Lazy<string> m_type;

        public override string Name
        {
            get { return m_name.Value; }
            set { m_name = new Lazy<string> (() => value); }
        }
        public override string Type
        {
            get { return m_type.Value; }
            set { m_type = new Lazy<string> (() => value); }
        }

        public AutoEntry (string name, Func<IResource> type_checker)
        {
            m_res  = new Lazy<IResource> (type_checker);
            m_name = new Lazy<string> (() => GetName (name));
            m_type = new Lazy<string> (GetEntryType);
        }

        public static AutoEntry Create (ArcView file, long offset, string base_name)
        {
            return new AutoEntry (base_name, () => {
                uint signature = file.View.ReadUInt32 (offset);
                return FormatCatalog.Instance.LookupSignature (signature).FirstOrDefault();
            });
        }

        private string GetName (string name)
        {
            if (null == m_res.Value)
                return name;
            var ext = m_res.Value.Extensions.FirstOrDefault();
            if (string.IsNullOrEmpty (ext))
                return name;
            return string.Format ("{0}.{1}", name, ext);
        }

        private string GetEntryType ()
        {
            return null == m_res.Value ? "" : m_res.Value.Type;
        }
    }

    public class PrefixStream : Stream
    {
        byte[]  m_header;
        Stream  m_stream;
        long    m_position = 0;

        public PrefixStream (byte[] header, Stream main)
        {
            m_header = header;
            m_stream = main;
        }

        public override bool CanRead  { get { return m_stream.CanRead; } }
        public override bool CanSeek  { get { return m_stream.CanSeek; } }
        public override bool CanWrite { get { return false; } }
        public override long Length   { get { return m_stream.Length + m_header.Length; } }
        public override long Position
        {
            get { return m_position; }
            set
            {
                m_position = Math.Max (value, 0);
                if (m_position > m_header.Length)
                {
                    long stream_pos = m_stream.Seek (m_position - m_header.Length, SeekOrigin.Begin);
                    m_position = m_header.Length + stream_pos;
                }
            }
        }

        public override void Flush()
        {
            m_stream.Flush();
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            if (SeekOrigin.Begin == origin)
                Position = offset;
            else if (SeekOrigin.Current == origin)
                Position = m_position + offset;
            else
                Position = Length + offset;

            return m_position;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = 0;
            if (m_position < m_header.Length)
            {
                int header_count = Math.Min (count, m_header.Length - (int)m_position);
                Buffer.BlockCopy (m_header, (int)m_position, buffer, offset, header_count);
                m_position += header_count;
                read += header_count;
                offset += header_count;
                count -= header_count;
            }
            if (count > 0)
            {
                if (m_header.Length == m_position)
                    m_stream.Position = 0;
                int stream_read = m_stream.Read (buffer, offset, count);
                m_position += stream_read;
                read += stream_read;
            }
            return read;
        }

        public override int ReadByte ()
        {
            if (m_position < m_header.Length)
                return m_header[m_position++];
            if (m_position == m_header.Length)
                m_stream.Position = 0;
            int b = m_stream.ReadByte();
            if (-1 != b)
                m_position++;
            return b;
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("PrefixStream.SetLength method is not supported");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("PrefixStream.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new NotSupportedException ("PrefixStream.WriteByte method is not supported");
        }

        bool disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!disposed)
            {
                m_stream.Dispose();
                disposed = true;
                base.Dispose (disposing);
            }
        }
    }

    public class LzssReader : IDisposable
    {
        BinaryReader    m_input;
        byte[]          m_output;
        int             m_size;

        public byte[] Data { get { return m_output; } }

        public LzssReader (Stream input, int input_length, int output_length)
        {
            m_input = new BinaryReader (input, Encoding.ASCII, true);
            m_output = new byte[output_length];
            m_size = input_length;
        }

        public void Unpack ()
        {
            int dst = 0;
            var frame = new byte[0x1000];
            int frame_pos = 0xfee;
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
                        frame_pos &= 0xfff;
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
                            offset &= 0xfff;
                            frame[frame_pos++] = v;
                            frame_pos &= 0xfff;
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
