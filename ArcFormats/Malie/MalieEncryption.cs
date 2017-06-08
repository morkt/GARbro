//! \file       MalieEncryption.cs
//! \date       Tue Jun 06 20:38:57 2017
//! \brief      Malie System encryption implementation.
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
using System.IO;
using GameRes.Cryptography;
using GameRes.Utility;

namespace GameRes.Formats.Malie
{
    public interface IMalieDecryptor
    {
        void DecryptBlock (long block_offset, byte[] buffer, int index);
    }

    public class CamelliaDecryptor : IMalieDecryptor
    {
        Camellia   m_enc;

        public CamelliaDecryptor (uint[] key)
        {
            m_enc = new Camellia (key);
        }

        public void DecryptBlock (long block_offset, byte[] buffer, int index)
        {
            m_enc.DecryptBlock (block_offset, buffer, index);
        }
    }

    public class CfiDecryptor : IMalieDecryptor
    {
        byte[] m_key;

        public CfiDecryptor (byte[] key)
        {
            m_key = key;
        }

        public void DecryptBlock (long block_offset, byte[] data, int index)
        {
            if (null == data)
                throw new ArgumentNullException ("data");
            if (index < 0 || index + 0x10 > data.Length)
                throw new ArgumentOutOfRangeException ("index");
            int offset = (int)block_offset;
            int o = offset & 0xF;
            byte first = data[index+o];
            for (int i = 0; i < 0x10; ++i)
            {
                if (o != i)
                    data[index+i] ^= first;
            }
            offset >>= 4;
            unsafe
            {
                fixed (byte* data8 = &data[index])
                {
                    uint* data32 = (uint*)data8;
                    uint k = Binary.RotR (0x39653542, m_key[offset & 0x1F] ^ 0xA5);
                    data32[0] = Binary.RotR (data32[0] ^ k, m_key[(offset + 12) & 0x1F] ^ 0xA5);
                    k = Binary.RotL (0x76706367, m_key[(offset + 3) & 0x1F] ^ 0xA5);
                    data32[1] = Binary.RotL (data32[1] ^ k, m_key[(offset + 15) & 0x1F] ^ 0xA5);
                    k = Binary.RotR (0x69454462, m_key[(offset + 6) & 0x1F] ^ 0xA5);
                    data32[2] = Binary.RotR (data32[2] ^ k, m_key[(offset - 14) & 0x1F] ^ 0xA5);
                    k = Binary.RotL (0x71334334, m_key[(offset + 9) & 0x1F] ^ 0xA5);
                    data32[3] = Binary.RotL (data32[3] ^ k, m_key[(offset - 11) & 0x1F] ^ 0xA5);
                }
            }
        }
    }

    internal class EncryptedStream : Stream
    {
        ArcView.Frame   m_view;
        IMalieDecryptor m_dec;
        long            m_max_offset;
        long            m_position = 0;
        byte[]          m_current_block = new byte[BlockLength];
        int             m_current_block_length = 0;
        long            m_current_block_position = 0;

        public const int BlockLength = 0x1000;

        public IMalieDecryptor Decryptor { get { return m_dec; } }

        public EncryptedStream (ArcView mmap, IMalieDecryptor decryptor)
        {
            m_view = mmap.CreateFrame();
            m_dec = decryptor;
            m_max_offset = mmap.MaxOffset;
        }

        public override int Read (byte[] buf, int index, int count)
        {
            int total_read = 0;
            bool refill_buffer = !(m_position >= m_current_block_position && m_position < m_current_block_position + m_current_block_length);
            while (count > 0 && m_position < m_max_offset)
            {
                if (refill_buffer)
                {
                    m_current_block_position = m_position & ~((long)BlockLength-1);
                    FillBuffer();
                }
                int src_offset = (int)m_position & (BlockLength-1);
                int available = Math.Min (count, m_current_block_length - src_offset);
                Buffer.BlockCopy (m_current_block, src_offset, buf, index, available);
                m_position += available;
                total_read += available;
                index += available;
                count -= available;
                refill_buffer = true;
            }
            return total_read;
        }

        private void FillBuffer ()
        {
            m_current_block_length = m_view.Read (m_current_block_position, m_current_block, 0, (uint)BlockLength);
            for (int offset = 0; offset < m_current_block_length; offset += 0x10)
            {
                m_dec.DecryptBlock (m_current_block_position+offset, m_current_block, offset);
            }
        }

        #region IO.Stream methods
        public override bool  CanRead { get { return !m_disposed; } }
        public override bool CanWrite { get { return false; } }
        public override bool  CanSeek { get { return !m_disposed; } }

        public override long Length { get { return m_max_offset; } }
        public override long Position
        {
            get { return m_position; }
            set { m_position = value; }
        }

        public override long Seek (long pos, SeekOrigin whence)
        {
            if (SeekOrigin.Current == whence)
                m_position += pos;
            else if (SeekOrigin.End == whence)
                m_position = m_max_offset + pos;
            else
                m_position = pos;
            return m_position;
        }

        public override void Write (byte[] buf, int index, int count)
        {
            throw new NotSupportedException();
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException();
        }

        public override void Flush ()
        {
        }
        #endregion

        #region IDisposable methods
        bool m_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                    m_view.Dispose();
                m_disposed = true;
                base.Dispose();
            }
        }
        #endregion
    }
}
