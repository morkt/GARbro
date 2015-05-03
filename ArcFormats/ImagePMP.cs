//! \file       ImagePMP.cs
//! \date       Thu Apr 30 20:39:26 2015
//! \brief      ScenePlayer compressed bitmap.
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
using System.Text;
using GameRes.Utility;
using ZLibNet;

namespace GameRes.Formats.ScenePlayer
{
    [Export(typeof(ImageFormat))]
    public class PmpFormat : BmpFormat
    {
        public override string         Tag { get { return "PMP"; } }
        public override string Description { get { return "ScenePlayer compressed bitmap format"; } }
        public override uint     Signature { get { return 0; } }

        public override void Write (Stream file, ImageData image)
        {
            using (var output = new XoredStream (file, 0x21, true))
            using (var zstream = new ZLibStream (output, CompressionMode.Compress, CompressionLevel.Level9))
                base.Write (zstream, image);
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            int first = stream.ReadByte() ^ 0x21;
            if (first != 0x78) // doesn't look like zlib stream
                return null;
            int flg = stream.ReadByte() ^ 0x21;
            int fcheck = first << 8 | flg;
            if (fcheck % 0x1f != 0)
                return null;

            stream.Position = 0;
            using (var input = new XoredStream (stream, 0x21, true))
            using (var zstream = new ZLibStream (input, CompressionMode.Decompress))
                return base.ReadMetaData (zstream);
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            using (var input = new XoredStream (stream, 0x21, true))
            using (var zstream = new ZLibStream (input, CompressionMode.Decompress))
                return base.Read (zstream, info);
        }
    }

    public class XoredStream : Stream
    {
        private Stream      m_stream;
        private byte        m_key;
        private bool        m_should_dispose;

        public Stream BaseStream { get { return m_stream; } }

        public override bool  CanRead { get { return m_stream.CanRead; } }
        public override bool  CanSeek { get { return m_stream.CanSeek; } }
        public override bool CanWrite { get { return m_stream.CanWrite; } }
        public override long   Length { get { return m_stream.Length; } }
        public override long Position
        {
            get { return m_stream.Position; }
            set { m_stream.Position = value; }
        }

        public XoredStream (Stream stream, byte key, bool leave_open = false)
        {
            m_stream = stream;
            m_key = key;
            m_should_dispose = !leave_open;
        }

        #region System.IO.Stream methods
        public override void Flush()
        {
            m_stream.Flush();
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            return m_stream.Seek (offset, origin);
        }

        public override void SetLength (long length)
        {
            m_stream.SetLength (length);
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = m_stream.Read (buffer, offset, count);
            for (int i = 0; i < read; ++i)
            {
                buffer[offset+i] ^= m_key;
            }
            return read;
        }

        public override int ReadByte ()
        {
            int b = m_stream.ReadByte();
            if (-1 != b)
            {
                b ^= m_key;
            }
            return b;
        }

        byte[] write_buf;

        public override void Write (byte[] buffer, int offset, int count)
        {
            if (null == write_buf || write_buf.Length < count)
                write_buf = new byte[count];
            for (int i = 0; i < count; ++i)
            {
                write_buf[i] = (byte)(buffer[offset+i] ^ m_key);
            }
            m_stream.Write (write_buf, 0, count);
        }

        public override void WriteByte (byte value)
        {
            m_stream.WriteByte ((byte)(value ^ m_key));
        }
        #endregion

        #region IDisposable Members
        bool disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!disposed)
            {
                if (disposing && m_should_dispose)
                {
                    m_stream.Dispose();
                }
                disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }
}
