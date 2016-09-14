//! \file       ZLibStream.cs
//! \date       Tue Jul 28 04:34:13 2015
//! \brief      RFC 1950 compatible wrapper around .Net DeflateStream class.
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

using System;
using System.IO;
using System.IO.Compression;
using GameRes.Utility;

namespace GameRes.Compression
{
    public enum CompressionMode
    {
        Compress,
        Decompress
    }

    /// <summary>
    /// Enum for backwards compatibility with ZLibNet
    /// </summary>
    public enum CompressionLevel
	{
		NoCompression = 0,
		BestSpeed = 1,
		BestCompression = 9,
		Default = 6,
		Level0 = 0,
		Level1 = 1,
		Level2 = 2,
		Level3 = 3,
		Level4 = 4,
		Level5 = 5,
		Level6 = 6,
		Level7 = 7,
		Level8 = 8,
		Level9 = 9
	}

    public class ZLibStream : Stream
    {
        DeflateStream   m_stream;
        CheckedStream   m_adler;
        bool            m_should_dispose_base;
        bool            m_writing;
        int             m_total_in = 0;

        public Stream BaseStream { get { return m_stream.BaseStream; } }

        /// <summary>
        /// When compressing: returns total number of uncompressed bytes. Undefined for decompression streams.
        /// </summary>
        public int       TotalIn { get { return m_total_in; } }

        public ZLibStream (Stream stream, CompressionMode mode, bool leave_open = false)
            : this (stream, mode, CompressionLevel.Default, leave_open)
        {
        }

        public ZLibStream (Stream stream, CompressionMode mode, CompressionLevel level, bool leave_open = false)
		{
            try
            {
                if (CompressionMode.Decompress == mode)
                    InitDecompress (stream);
                else
                    InitCompress (stream, level);
                m_should_dispose_base = !leave_open;
            }
            catch
            {
                if (!leave_open)
                    stream.Dispose();
                throw;
            }
		}

        private void InitDecompress (Stream stream)
        {
            int b1 = stream.ReadByte();
            int b2 = stream.ReadByte();
            if ((0x78 != b1 && 0x58 != b1) || 0 != (b1 << 8 | b2) % 31)
                throw new InvalidDataException ("Data not recoginzed as zlib-compressed stream");
            m_stream = new DeflateStream (stream, System.IO.Compression.CompressionMode.Decompress, true);
            m_writing = false;
        }

        private void InitCompress (Stream stream, CompressionLevel level)
        {
            int flevel = (int)level;
            System.IO.Compression.CompressionLevel sys_level;
            if (0 == flevel)
            {
                sys_level = System.IO.Compression.CompressionLevel.NoCompression;
            }
            else if (flevel > 5)
            {
                sys_level = System.IO.Compression.CompressionLevel.Optimal;
                flevel = 3;
            }
            else
            {
                sys_level = System.IO.Compression.CompressionLevel.Fastest;
                flevel = 1;
            }
            int cmf = 0x7800 | flevel << 6;
            cmf = ((cmf + 30) / 31) * 31;
            stream.WriteByte ((byte)(cmf >> 8));
            stream.WriteByte ((byte)cmf);
            m_stream = new DeflateStream (stream, sys_level, true);
            m_adler  = new CheckedStream (m_stream, new Adler32());
            m_writing = true;
        }

        void WriteCheckSum (Stream output)
        {
            uint checksum = m_adler.CheckSumValue;
            output.WriteByte ((byte)(checksum >> 24));
            output.WriteByte ((byte)(checksum >> 16));
            output.WriteByte ((byte)(checksum >> 8));
            output.WriteByte ((byte)(checksum));
        }

        #region IO.Stream Members
        public override bool CanRead  { get { return !m_writing; } }
        public override bool CanSeek  { get { return false; } }
        public override bool CanWrite { get { return m_writing; } }
        public override long Length   { get { return m_stream.Length; } }
        public override long Position
        {
            get { return m_stream.Position; }
            set { m_stream.Position = value; }
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            return m_stream.Read (buffer, offset, count);
        }

        public override int ReadByte ()
        {
            return m_stream.ReadByte();
        }

        public override void Flush()
        {
            m_stream.Flush();
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            throw new NotSupportedException ("ZLibStream.Seek method not supported");
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("ZLibStream.SetLength method not supported");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            if (count > 0)
            {
                m_adler.Write (buffer, offset, count);
                m_total_in += count;
            }
        }

        public override void WriteByte (byte value)
        {
            m_adler.WriteByte (value);
            m_total_in++;
        }
        #endregion

        #region IDisposable Members
        bool m_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                try
                {
                    if (disposing)
                    {
                        var output = m_stream.BaseStream;
                        m_stream.Dispose();
                        if (m_writing)
                        {
                            WriteCheckSum (output);
                            m_adler.Dispose();
                        }
                        if (m_should_dispose_base)
                            output.Dispose();
                    }
                    m_disposed = true;
                }
                finally
                {
                    base.Dispose (disposing);
                }
            }
        }
        #endregion
    }

    public class ZLibCompressor
	{
		public static MemoryStream Compress (Stream source)
		{
			var dest = new MemoryStream();
			using (var zs = new ZLibStream (dest, CompressionMode.Compress, true))
			{
				source.CopyTo (zs);
			}
			dest.Position = 0;
			return dest;
		}

		public static MemoryStream DeCompress (Stream source)
		{
			var dest = new MemoryStream();
			using (var zs = new ZLibStream (source, CompressionMode.Decompress, true))
			{
				zs.CopyTo (dest);
			}
			dest.Position = 0;
			return dest;
		}
	}
}
