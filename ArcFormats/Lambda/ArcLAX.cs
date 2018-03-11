//! \file       ArcLAX.cs
//! \date       2017 Dec 31
//! \brief      Lambda engine resource archive.
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
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Lambda
{
    [Export(typeof(ArchiveFormat))]
    public class LaxOpener : ArchiveFormat
    {
        public override string         Tag { get { return "LAX"; } }
        public override string Description { get { return "Lambda engine resource archive"; } }
        public override uint     Signature { get { return 0x70614C24; } } // '$LapH__'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            long index_offset = file.MaxOffset-0x28;
            if (!file.View.AsciiEqual (index_offset, "$LapI__"))
                return null;
            int count = file.View.ReadInt32 (index_offset+8);
            if (!IsSaneCount (count))
                return null;
            uint unpacked_size = file.View.ReadUInt32 (index_offset+0x10);
            uint packed_size = file.View.ReadUInt32 (index_offset+0x14);
            index_offset = file.View.ReadUInt32 (index_offset+0xC);
            var index = new byte[unpacked_size];
            using (var input = file.CreateStream (index_offset, packed_size))
            using (var lax = new LaxStream (input))
                lax.Read (index, 0, index.Length);

            uint data_offset = 8;
            int entry_length = 0x128;
            int pos = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                if (!index.AsciiEqual (pos, "$LapF__"))
                    return null;
                var name = Binary.GetCString (index, pos+0x24, 0x104);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.UnpackedSize = index.ToUInt32 (pos+0x10);
                entry.Size         = index.ToUInt32 (pos+0x14);
                entry.Offset       = index.ToUInt32 (pos+0x18) + data_offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (name.HasAnyOfExtensions (".bmx", ".b32"))
                    entry.Type = "image";
                dir.Add (entry);
                pos += entry_length;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new LaxStream (input);
        }
    }

    internal class LaxStream : InputProxyStream
    {
        public LaxStream (Stream input, bool leave_open = false) : base (input, leave_open)
        {
        }

        byte[]  m_buffer;
        int     m_buffer_size;
        int     m_buffer_pos;

        public override int Read (byte[] buffer, int offset, int count)
        {
            int total_read = 0;
            while (count > 0)
            {
                if (m_buffer_pos == m_buffer_size)
                {
                    if (m_eof)
                        break;
                    ReadSegment();
                }
                int available = Math.Min (count, m_buffer_size - m_buffer_pos);
                Buffer.BlockCopy (m_buffer, m_buffer_pos, buffer, offset, available);
                m_buffer_pos += available;
                offset += available;
                count -= available;
                total_read += available;
            }
            return total_read;
        }

        bool m_eof = false;

        void ReadSegment ()
        {
            if (null == m_buffer)
                m_buffer = new byte[0x8000];
            m_buffer_pos = m_buffer_size = 0;
            long chunk_start = BaseStream.Position;
            if (BaseStream.Read (m_buffer, 0, 10) < 10)
            {
                m_eof = true;
                return;
            }
            if (!m_buffer.AsciiEqual ("_AF"))
                throw new InvalidFormatException ("Invalid compressed LAX stream.");
            int method = m_buffer[3];
            int chunk_size = m_buffer.ToUInt16 (4);
            int final_size = m_buffer.ToUInt16 (6);
            if (final_size != 0)
                throw new NotImplementedException ("Double compression in LAX streams not implemented.");
            int unpacked_size = m_buffer.ToUInt16 (8);
            if (unpacked_size > m_buffer.Length)
                m_buffer = new byte[unpacked_size];

            switch (method) // compression method
            {
            case '1':
                m_buffer_size = LzssUnpack (unpacked_size);
                break;

            case '2':
                m_buffer_size = HuffmanUnpack (unpacked_size);
                break;

            default:
                m_buffer_size = BaseStream.Read (m_buffer, 0, unpacked_size);
                break;
            }
            BaseStream.Position = chunk_start + chunk_size;
        }

        byte[]  m_frame = new byte[0x1000];

        int LzssUnpack (int unpacked_size)
        {
            for (int i = 0; i < m_frame.Length; ++i)
                m_frame[i] = 0;
            int frame_pos = 0xFEE;
            int bits = 2;
            int dst = 0;
            while (dst < unpacked_size)
            {
                bits >>= 1;
                if (1 == bits)
                {
                    bits = BaseStream.ReadByte();
                    if (-1 == bits)
                        break;
                    bits |= 0x100;
                }
                int lo = BaseStream.ReadByte();
                if (-1 == lo)
                    break;
                if (0 != (bits & 1))
                {
                    m_buffer[dst++] = m_frame[frame_pos++ & 0xFFF] = (byte)lo;
                }
                else
                {
                    int hi = BaseStream.ReadByte();
                    if (-1 == hi)
                        break;
                    int offset = (hi & 0xF0) << 4 | lo;
                    int count = Math.Min (3 + (hi & 0xF), unpacked_size - dst);
                    while (count --> 0)
                    {
                        byte v = m_frame[offset++ & 0xFFF];
                        m_buffer[dst++] = m_frame[frame_pos++ & 0xFFF] = v;
                    }
                }
            }
            return dst;
        }

        int HuffmanUnpack (int unpacked_size)
        {
            using (var input = new HuffmanDecompressor())
            {
                input.Initialize (BaseStream);
                return input.Continue (m_buffer, 0, unpacked_size);
            }
        }

        #region IO.Stream members
        public override bool CanSeek  { get { return false; } }
        public override long Length
        {
            get { throw new NotSupportedException ("Stream.Length property is not supported"); }
        }
        public override long Position
        {
            get { throw new NotSupportedException ("Stream.Position property is not supported"); }
            set { throw new NotSupportedException ("Stream.Position property is not supported"); }
        }

        public override void Flush()
        {
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            throw new NotSupportedException ("LzssStream.Seek method is not supported");
        }
        #endregion
    }
}
