//! \file       ImageSPC.cs
//! \date       Tue Mar 08 19:05:11 2016
//! \brief      CRI MiddleWare compressed multi-frame image.
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
using GameRes.Compression;

namespace GameRes.Formats.Cri
{
    [Export(typeof(ImageFormat))]
    public class SpcFormat : XtxFormat
    {
        public override string         Tag { get { return "SPC"; } }
        public override string Description { get { return "CRI MiddleWare compressed texture format"; } }
        public override uint     Signature { get { return 0; } }

        public SpcFormat ()
        {
            Signatures = new uint[] { 0 };
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            uint unpacked_size = FormatCatalog.ReadSignature (stream);
            if (unpacked_size <= 0x20 || unpacked_size > 0x5000000) // ~83MB
                return null;
            using (var lzss = new LzssStream (stream, LzssMode.Decompress, true))
            using (var input = new SeekableStream (lzss))
                return base.ReadMetaData (input);
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            stream.Position = 4;
            using (var lzss = new LzssStream (stream, LzssMode.Decompress, true))
            using (var input = new SeekableStream (lzss))
                return base.Read (input, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("SpcFormat.Write not implemented");
        }
    }

    public class SeekableStream : Stream
    {
        Stream      m_source;
        Stream      m_buffer;
        bool        m_should_dispose;
        bool        m_source_depleted;
        long        m_read_pos;

        public SeekableStream (Stream input, bool leave_open = false)
        {
            m_source = input;
            m_should_dispose = !leave_open;
            m_read_pos = 0;
            if (m_source.CanSeek)
            {
                m_buffer = m_source;
                m_source_depleted = true;
            }
            else
            {
                m_buffer = new MemoryStream();
                m_source_depleted = false;
            }
        }

        #region IO.Stream Members
        public override bool CanRead  { get { return m_buffer.CanRead; } }
        public override bool CanSeek  { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override long Length
        {
            get
            {
                if (!m_source_depleted)
                {
                    m_buffer.Seek (0, SeekOrigin.End);
                    m_source.CopyTo (m_buffer);
                    m_source_depleted = true;
                }
                return m_buffer.Length;
            }
        }
        public override long Position
        {
            get { return m_read_pos; }
            set { m_read_pos = value; }
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int total_read = 0;
            if (m_source_depleted)
            {
                m_buffer.Position = m_read_pos;
                total_read = m_buffer.Read (buffer, offset, count);
                m_read_pos += total_read;
                return total_read;
            }
            if (m_read_pos < m_buffer.Length)
            {
                int available = (int)Math.Min (m_buffer.Length-m_read_pos, count);
                m_buffer.Position = m_read_pos;
                total_read = m_buffer.Read (buffer, offset, available);
                m_read_pos += total_read;
                count -= total_read;
                if (0 == count)
                    return total_read;
                offset += total_read;
            }
            else
            {
                m_buffer.Seek (0, SeekOrigin.End);
                while (m_read_pos > m_buffer.Length)
                {
                    int b = m_source.ReadByte();
                    if (-1 == b)
                    {
                        m_source_depleted = true;
                        return 0;
                    }
                    m_buffer.WriteByte ((byte)b);
                }
            }
            int read = m_source.Read (buffer, offset, count);
            m_read_pos += read;
            m_buffer.Write (buffer, offset, read);
            return total_read + read;
        }

        public override void Flush()
        {
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            if (SeekOrigin.Begin == origin)
                m_read_pos = offset;
            else if (SeekOrigin.Current == origin)
                m_read_pos += offset;
            else
                m_read_pos = Length + offset;

            return m_read_pos;
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("SeekableStream.SetLength method is not supported");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("SeekableStream.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new NotSupportedException ("SeekableStream.WriteByte method is not supported");
        }
        #endregion

        #region IDisposable Members
        bool _disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                if (m_should_dispose)
                    m_source.Dispose();
                if (m_buffer != m_source)
                    m_buffer.Dispose();
            }
            _disposed = true;
            base.Dispose (disposing);
        }
        #endregion
    }
}
