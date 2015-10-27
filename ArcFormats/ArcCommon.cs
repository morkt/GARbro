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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
            }) { Offset = offset };
        }

        private string GetName (string name)
        {
            if (null == m_res.Value)
                return name;
            var ext = m_res.Value.Extensions.FirstOrDefault();
            if (string.IsNullOrEmpty (ext))
                return name;
            return Path.ChangeExtension (name, ext);
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
        bool    m_should_dispose;

        public PrefixStream (byte[] header, Stream main, bool leave_open = false)
        {
            m_header = header;
            m_stream = main;
            m_should_dispose = !leave_open;
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
                if (m_should_dispose && disposing)
                    m_stream.Dispose();
                disposed = true;
                base.Dispose (disposing);
            }
        }
    }

    /// <summary>
    /// Represents a region within existing stream.
    /// Underlying stream should allow seeking (CanSeek == true).
    /// </summary>
    public class StreamRegion : Stream
    {
        private Stream  m_stream;
        private long    m_begin;
        private long    m_end;
        private bool    m_should_dispose;

        public StreamRegion (Stream main, long offset, long length, bool leave_open = false)
        {
            m_stream = main;
            m_begin = offset;
            m_end = Math.Min (offset + length, m_stream.Length);
            m_stream.Position = m_begin;
            m_should_dispose = !leave_open;
        }

        public StreamRegion (Stream main, long offset, bool leave_open = false)
            : this (main, offset, main.Length-offset, leave_open)
        {
        }

        public override bool CanRead  { get { return m_stream.CanRead; } }
        public override bool CanSeek  { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override long Length   { get { return m_end - m_begin; } }
        public override long Position
        {
            get { return m_stream.Position - m_begin; }
            set { m_stream.Position = Math.Max (m_begin + value, m_begin); }
        }

        public override void Flush()
        {
            m_stream.Flush();
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            if (SeekOrigin.Begin == origin)
                offset += m_begin;
            else if (SeekOrigin.Current == origin)
                offset += m_stream.Position;
            else
                offset += m_end;
            offset = Math.Max (offset, m_begin);
            m_stream.Position = offset;
            return offset - m_begin;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = 0;
            long available = m_end - m_stream.Position;
            if (available > 0)
            {
                read = m_stream.Read (buffer, offset, (int)Math.Min (count, available));
            }
            return read;
        }

        public override int ReadByte ()
        {
            if (m_stream.Position < m_end)
                return m_stream.ReadByte();
            else
                return -1;
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("StreamRegion.SetLength method is not supported");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("StreamRegion.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new NotSupportedException ("StreamRegion.WriteByte method is not supported");
        }

        bool m_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                if (m_should_dispose && disposing)
                    m_stream.Dispose();
                m_disposed = true;
                base.Dispose (disposing);
            }
        }
    }

    public class HuffmanDecoder
    {
        byte[] m_src;
        byte[] m_dst;

        ushort[] lhs = new ushort[512];
        ushort[] rhs = new ushort[512];
        ushort token = 256;

        int m_input_pos;
        int m_remaining;
        int m_cached_bits;
        int m_cache;

        public HuffmanDecoder (byte[] src, int index, int length, byte[] dst)
        {
            m_src = src;
            m_dst = dst;
            m_input_pos = index;
            m_remaining = length;
            m_cached_bits = 0;
            m_cache = 0;
        }

        public HuffmanDecoder (byte[] src, byte[] dst) : this (src, 0, src.Length, dst)
        {
        }

        public byte[] Unpack ()
        {
            int dst = 0;
            token = 256;
            ushort v3 = CreateTree();
            while (dst < m_dst.Length)
            {
                ushort symbol = v3;
                while ( symbol >= 0x100u )
                {
                    if ( 0 != GetBits (1) )
                        symbol = rhs[symbol];
                    else
                        symbol = lhs[symbol];
                }
                m_dst[dst++] = (byte)symbol;
            }
            return m_dst;
        }

        ushort CreateTree()
        {
            if ( 0 != GetBits (1) )
            {
                ushort v = token++;
                lhs[v] =  CreateTree();
                rhs[v] =  CreateTree();
                return v;
            }
            else
            {
                return (ushort)GetBits (8);
            }
        }

        uint GetBits (int n)
        {
            while (n > m_cached_bits)
            {
                if (0 == m_remaining)
                    throw new ApplicationException ("Invalid huffman-compressed stream");
                int v = m_src[m_input_pos++];
                --m_remaining;
                m_cache = v | (m_cache << 8);
                m_cached_bits += 8;
            }
            uint mask = (uint)m_cache;
            m_cached_bits -= n;
            m_cache &= ~(-1 << m_cached_bits);
            return (uint)(((-1 << m_cached_bits) & mask) >> m_cached_bits);
        }
    }

    public sealed class NotTransform : ICryptoTransform
    {
        private const int BlockSize = 256;

        public bool          CanReuseTransform { get { return true; } }
        public bool CanTransformMultipleBlocks { get { return true; } }
        public int              InputBlockSize { get { return BlockSize; } }
        public int             OutputBlockSize { get { return BlockSize; } }

        public int TransformBlock (byte[] inputBuffer, int inputOffset, int inputCount,
                                   byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; ++i)
            {
                outputBuffer[outputOffset++] = (byte)~inputBuffer[inputOffset+i];
            }
            return inputCount;
        }

        public byte[] TransformFinalBlock (byte[] inputBuffer, int inputOffset, int inputCount)
        {
            byte[] outputBuffer = new byte[inputCount];
            TransformBlock (inputBuffer, inputOffset, inputCount, outputBuffer, 0);
            return outputBuffer;
        }

        public void Dispose ()
        {
            System.GC.SuppressFinalize (this);
        }
    }

    public static class MMX
    {
        public static ulong PAddW (ulong x, ulong y)
        {
            ulong mask = 0xffff;
            ulong r = ((x & mask) + (y & mask)) & mask;
            mask <<= 16;
            r |= ((x & mask) + (y & mask)) & mask;
            mask <<= 16;
            r |= ((x & mask) + (y & mask)) & mask;
            mask <<= 16;
            r |= ((x & mask) + (y & mask)) & mask;
            return r;
        }

        public static ulong PAddD (ulong x, ulong y)
        {
            ulong mask = 0xffffffff;
            ulong r = ((x & mask) + (y & mask)) & mask;
            mask <<= 32;
            return r | ((x & mask) + (y & mask)) & mask;
        }
    }

    public static class Dump
    {
        public static string DirectoryName = Environment.GetFolderPath (Environment.SpecialFolder.UserProfile);

        [System.Diagnostics.Conditional("DEBUG")]
        public static void Write (byte[] mem, string filename = "index.dat")
        {
            using (var dump = File.Create (Path.Combine (DirectoryName, filename)))
                dump.Write (mem, 0, mem.Length);
        }
    }
}
