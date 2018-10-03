//! \file       BundleStream.cs
//! \date       Wed Apr 05 13:30:19 2017
//! \brief      Stream representing Unity bundle.
//
// Copyright (C) 2017 by morkt
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
using System.Diagnostics;
using System.IO;
using GameRes.Compression;
using LZMA = SevenZip.Compression.LZMA;

namespace GameRes.Formats.Unity
{
    /// <summary>
    /// Stream representing Unity asset bundle.
    /// </summary>
    internal class BundleStream : Stream
    {
        readonly ArcViewStream  m_input;
        readonly long           m_length;
        IList<BundleSegment>    m_segments;
        long                    m_position;
        int                     m_current_segment;
        byte[]                  m_buffer;
        int                     m_buffer_pos;
        int                     m_buffer_len;
        byte[]                  m_packed;

        public BundleStream (ArcView file, IList<BundleSegment> segments)
        {
            if (null == segments || 0 == segments.Count)
                throw new ArgumentException ("Segments list is empty.", "segments");
            m_input = file.CreateStream();
            m_segments = segments;
            var last_segment = m_segments[m_segments.Count-1];
            m_length = last_segment.UnpackedOffset + last_segment.UnpackedSize;
            m_position = 0;
            m_current_segment = 0;
            m_input.Position = m_segments[0].Offset;
        }

        public override bool CanRead  { get { return !m_disposed; } }
        public override bool CanSeek  { get { return !m_disposed; } }
        public override bool CanWrite { get { return false; } }
        public override long Length   { get { return m_length; } }
        public override long Position
        {
            get { return m_position; }
            set {
                if (value == m_position)
                    return;
                if (value < 0)
                    throw new ArgumentOutOfRangeException ("value", "Stream position is out of range.");
                m_position = value;
                int segment_index = 0;
                for (int i = 1; i < m_segments.Count; ++i)
                {
                    if (m_segments[i].UnpackedOffset > value)
                        break;
                    ++segment_index;
                }
                var segment = m_segments[segment_index];
                if (segment_index != m_current_segment)
                {
                    m_current_segment = segment_index;
                    m_buffer_len = 0;
                }
                if (segment.IsCompressed)
                {
                    m_buffer_pos = (int)(m_position - segment.UnpackedOffset);
                }
                else
                {
                    m_buffer_pos = 0;
                    m_input.Position = segment.Offset + (m_position - segment.UnpackedOffset);
                }
            }
        }

        byte[] PrepareBuffer (uint length)
        {
            if (null == m_buffer || length > m_buffer.Length)
                m_buffer = new byte[length];
            return m_buffer;
        }

        void ReadCompressedSegment (BundleSegment segment)
        {
            m_input.Position = segment.Offset;
            int method = segment.Compression & 0x3F;
            if (1 == method)
            {
                m_buffer_len = LzmaDecompressBlock (segment.PackedSize, segment.UnpackedSize);
                return;
            }
            if (null == m_packed || segment.PackedSize > m_packed.Length)
                m_packed = new byte[segment.PackedSize];
            int packed_size = m_input.Read (m_packed, 0, (int)segment.PackedSize);
            var output = PrepareBuffer (segment.UnpackedSize);
            if (3 == method)
                m_buffer_len = Lz4Compressor.DecompressBlock (m_packed, packed_size, output, (int)segment.UnpackedSize);
            else
                throw new NotImplementedException ("Not supported Unity asset bundle compression.");
        }

        int LzmaDecompressBlock (uint packed_size, uint unpacked_size)
        {
            var decoder = new LZMA.Decoder();
            var props = m_input.ReadBytes (5);
            decoder.SetDecoderProperties (props);
            var buffer = PrepareBuffer (unpacked_size);
            using (var output = new MemoryStream (buffer))
            {
                decoder.Code (m_input, output, packed_size-5, unpacked_size, null);
                return (int)output.Length;
            }
        }

        int ReadFromSegment (BundleSegment segment, byte[] buffer, int offset, int count)
        {
            Debug.Assert (m_position >= segment.UnpackedOffset && m_position <= segment.UnpackedOffset + segment.UnpackedSize);
            if (!segment.IsCompressed)
            {
                int available = (int)Math.Min (count, (segment.UnpackedOffset + segment.UnpackedSize) - m_position);
                return m_input.Read (buffer, offset, available);
            }
            else
            {
                if (0 == m_buffer_len)
                    ReadCompressedSegment (segment);
                int available = Math.Min (count, m_buffer_len - m_buffer_pos);
                Buffer.BlockCopy (m_buffer, m_buffer_pos, buffer, offset, available);
                m_buffer_pos += available;
                return available;
            }
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            if (m_position >= m_length)
                return 0;
            int total_read = 0;
            while (count > 0)
            {
                var segment = m_segments[m_current_segment];
                int read = ReadFromSegment (segment, buffer, offset, count);
                m_position += read;
                total_read += read;
                offset += read;
                count -= read;
                if (count > 0)
                {
                    if (m_current_segment+1 == m_segments.Count)
                        break;
                    ++m_current_segment;
                    m_buffer_len = m_buffer_pos = 0;
                    m_input.Position = m_segments[m_current_segment].Offset;
                    Debug.Assert (m_position == m_segments[m_current_segment].UnpackedOffset);
                }
            }
            return total_read;
        }

        public override void Flush()
        {
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            switch (origin)
            {
            case SeekOrigin.Current:    offset += m_position; break;
            case SeekOrigin.End:        offset += m_length; break;
            }
            Position = offset;
            return offset;
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("Stream.SetLength method is not supported.");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("Stream.Write method is not supported.");
        }

        public override void WriteByte (byte value)
        {
            throw new NotSupportedException ("Stream.WriteByte method is not supported.");
        }

        bool m_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                    m_input.Dispose();
                m_disposed = true;
                base.Dispose (disposing);
            }
        }
    }
}
