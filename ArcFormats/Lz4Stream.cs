//! \file       Lz4Stream.cs
//! \date       Fri Jan 13 20:45:26 2017
//! \brief      LZ4 - Fast LZ compression algorithm
//
// Copyright (C) 2011-2016, Yann Collet.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are
// met:
//
//     * Redistributions of source code must retain the above copyright
// notice, this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above
// copyright notice, this list of conditions and the following disclaimer
// in the documentation and/or other materials provided with the
// distribution.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
// A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
// OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
// SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
// LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
// C# implementation Copyright (C) 2017 by morkt
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
using GameRes.Utility;

namespace GameRes.Compression
{
    public class Lz4FrameInfo
    {
        public int      BlockSize;
        public bool     IndependentBlocks;
        public bool     HasBlockChecksum;
        public bool     HasContentLength;
        public bool     HasContentChecksum;
        public bool     HasDictionary;
        public long     OriginalLength;
        public int      DictionaryId;

        public Lz4FrameInfo ()
        {
        }

        public Lz4FrameInfo (byte flags)
        {
            int version = flags >> 6;
            if (version != 1)
                throw Lz4Compressor.InvalidData();
            IndependentBlocks  = 0 != (flags & 0x20);
            HasBlockChecksum   = 0 != (flags & 0x10);
            HasContentLength   = 0 != (flags & 8);
            HasContentChecksum = 0 != (flags & 4);
            HasDictionary      = 0 != (flags & 1);
        }

        public void SetBlockSize (int code)
        {
            switch ((code >> 4) & 7)
            {
            case 4: BlockSize = 0x10000; break;
            case 5: BlockSize = 0x40000; break;
            case 6: BlockSize = 0x100000; break;
            case 7: BlockSize = 0x400000; break;
            default: throw Lz4Compressor.InvalidData();
            }
        }
    }

    public class Lz4Stream : GameRes.Formats.InputProxyStream
    {
        Lz4FrameInfo    m_info;
        readonly byte[] m_block_header;
        byte[]          m_block;
        int             m_block_size;
        byte[]          m_data;
        int             m_data_size;
        int             m_data_pos;
        bool            m_eof;

        public Lz4Stream (Stream input, Lz4FrameInfo info, bool leave_open = false) : base (input, leave_open)
        {
            if (null == info)
                throw new ArgumentNullException ("info");
            if (info.BlockSize <= 0)
                throw new ArgumentOutOfRangeException ("info.BlockSize");
            if (!info.IndependentBlocks)
                throw new NotImplementedException ("LZ4 compression with linked blocks not implemented.");
            if (info.HasDictionary)
                throw new NotImplementedException ("LZ4 compression with dictionary not implemented.");
            m_info = info;
            m_block_header = new byte[4];
            m_data = new byte[m_info.BlockSize];
            m_data_size = 0;
            m_data_pos = 0;
            m_eof = false;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int total_read = 0;
            while (count > 0)
            {
                if (m_data_pos < m_data_size)
                {
                    int available = Math.Min (m_data_size - m_data_pos, count);
                    Buffer.BlockCopy (m_data, m_data_pos, buffer, offset, available);
                    total_read += available;
                    m_data_pos += available;
                    offset += available;
                    count -= available;
                }
                else if (m_eof)
                    break;
                else
                    ReadNextBlock();
            }
            return total_read;
        }

        void ReadNextBlock ()
        {
            if (4 != BaseStream.Read (m_block_header, 0, 4))
                throw new EndOfStreamException();
            int block_size = LittleEndian.ToInt32 (m_block_header, 0);
            if (0 == block_size)
            {
                m_eof = true;
                m_data_size = 0;
                if (m_info.HasContentChecksum)
                    ReadChecksum();
            }
            else if (block_size < 0)
            {
                m_data_size = block_size & 0x7FFFFFFF;
                if (m_data_size > m_data.Length)
                    m_data = new byte[m_data_size];
                m_data_size = BaseStream.Read (m_data, 0, m_data_size);
                if (m_info.HasBlockChecksum)
                    ReadChecksum();
            }
            else
            {
                m_block_size = block_size;
                if (null == m_block || m_block_size > m_block.Length)
                    m_block = new byte[m_block_size];
                if (m_block_size != BaseStream.Read (m_block, 0, m_block_size))
                    throw new EndOfStreamException();
                m_data_size = Lz4Compressor.DecompressBlock (m_block, m_block_size, m_data, m_data.Length);
                if (m_info.HasBlockChecksum)
                    ReadChecksum();
            }
            m_data_pos = 0;
        }

        void ReadChecksum ()
        {
            if (4 != BaseStream.Read (m_block_header, 0, 4))
                throw new EndOfStreamException();
            // XXX checksum is ignored
        }

        #region Not supported IO.Stream methods
        public override bool CanSeek  { get { return false; } }
        public override long Length
        {
            get { throw new NotSupportedException ("Lz4Stream.Length property is not supported"); }
        }
        public override long Position
        {
            get { throw new NotSupportedException ("Lz4Stream.Position property is not supported"); }
            set { throw new NotSupportedException ("Lz4Stream.Position property is not supported"); }
        }

        public override void Flush()
        {
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            throw new NotSupportedException ("Lz4Stream.Seek method is not supported");
        }
        #endregion
    }

    public class Lz4Compressor
    {
        const int MinMatch          = 4;
        const int LastLiterals      = 5;
        const int MFLimit           = 12;
        const int MatchLengthBits   = 4;
        const int MatchLengthMask   = 0xF;
        const int RunMask           = 0xF;

        public static int DecompressBlock (byte[] block, int block_size, byte[] output, int output_size)
        {
            int src = 0;
            int iend = block_size;

            int dst = 0;
            int oend = output_size;

            for (;;)
            {
                /* get literal length */
                int token = block[src++];
                int length = token >> MatchLengthBits;
                if (RunMask == length)
                {
                    int n;
                    do
                    {
                        n = block[src++];
                        length += n;
                    }
                    while ((src < iend - RunMask) && (0xFF == n));
                    if (dst + length < dst || src + length < src) // overflow detection
                        throw InvalidData();
                }

                /* copy literals */
                int copy_end = dst + length;
                if ((copy_end > oend - MFLimit) || (src + length > iend - (3+LastLiterals)))
                {
                    if ((src + length != iend) || copy_end > oend)
                        throw InvalidData();
                    Buffer.BlockCopy (block, src, output, dst, length);
                    src += length;
                    dst += length;
                    break;
                }
                Buffer.BlockCopy (block, src, output, dst, length);
                src += length;
                dst = copy_end;

                /* get offset */
                int offset = LittleEndian.ToUInt16 (block, src);
                src += 2;
                int match = dst - offset;
                if (match < 0)
                    throw InvalidData();

                /* get matchlength */
                length = token & MatchLengthMask;
                if (MatchLengthMask == length)
                {
                    int n;
                    do
                    {
                        n = block[src++];
                        if (src > iend - LastLiterals)
                            throw InvalidData();
                        length += n;
                    }
                    while (0xFF == n);
                    if (dst + length < dst) // overflow detection
                        throw InvalidData();
                }
                length += MinMatch;

                /* copy match within block */
                Binary.CopyOverlapped (output, match, dst, length);
                dst += length;
            }
            return dst; // number of output bytes decoded
        }

        internal static InvalidDataException InvalidData ()
        {
            return new InvalidDataException ("Invalid LZ4 compressed stream.");
        }
    }
}
