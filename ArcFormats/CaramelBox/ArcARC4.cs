//! \file       ArcARC4.cs
//! \date       Wed Aug 10 15:50:21 2016
//! \brief      Caramel BOX resource archive.
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
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.CaramelBox
{
    internal class Arc4Entry : PackedEntry
    {
        public List<long>   Segments;
    }

    [Export(typeof(ArchiveFormat))]
    public class Arc4Opener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC4"; } }
        public override string Description { get { return "Caramel BOX resource archive"; } }
        public override uint     Signature { get { return 0x34435241; } } // 'ARC4'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Arc4Opener ()
        {
            Extensions = new string[] { "bin", "dat", "データ" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (0x010000 != file.View.ReadUInt32 (4))
                return null;
            int count = file.View.ReadInt32 (0x10);
            if (!IsSaneCount (count))
                return null;
            uint index_length = file.View.ReadUInt32 (8);
            uint alignment    = file.View.ReadUInt32 (0xC);
            int index_offset  = file.View.ReadInt32 (0x14);
            int names_offset  = file.View.ReadInt32 (0x1C) - index_offset;
            int segment_table = file.View.ReadInt32 (0x24) - index_offset;
            uint base_offset  = file.View.ReadUInt32 (0x2C);
            if (0 == alignment || index_offset <= 0 || names_offset <= 0 || segment_table <= 0)
                return null;
            if (!file.View.AsciiEqual (index_offset, "tZ"))
                return null;
            byte[] index;
            using (var packed = file.CreateStream (index_offset, index_length))
            using (var tz = new TzCompression (packed))
                index = tz.Unpack();

            int current_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int name_pos = ReadInt24 (index, current_offset) * 2;
                int name_length = index[current_offset+3];
                int chunk_count = index[current_offset+4];
                int offset = ReadInt24 (index, current_offset+5);
                var name = Binary.GetCString (index, names_offset + name_pos, name_length);
                var entry = FormatCatalog.Instance.Create<Arc4Entry> (name);
                entry.Segments = new List<long> (chunk_count);
                if (chunk_count > 1)
                {
                    int segment_pos = segment_table + 3 * offset;
                    for (int j = 0; j < chunk_count; ++j)
                    {
                        entry.Segments.Add (ReadInt24 (index, segment_pos) + base_offset);
                        segment_pos += 3;
                    }
                }
                else
                    entry.Segments.Add (offset + base_offset);

                for (int j = 0; j < chunk_count; ++j)
                    entry.Segments[j] *= alignment;
                dir.Add (entry);
                current_offset += 8;
            }
            foreach (Arc4Entry entry in dir)
            {
                uint size = 0;
                foreach (var segment in entry.Segments)
                {
                    size += Binary.BigEndian (file.View.ReadUInt32 (segment+4));
                }
                entry.Offset = entry.Segments[0] + 0x10;
                entry.Size = size;
                entry.IsPacked = file.View.AsciiEqual (entry.Offset, "tZ");
                if (entry.IsPacked)
                    entry.UnpackedSize = file.View.ReadUInt32 (entry.Offset+2);
                else
                    entry.UnpackedSize = size;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var xent = (Arc4Entry)entry;
            Stream input;
            if (1 == xent.Segments.Count)
                input = arc.File.CreateStream (entry.Offset, entry.Size);
            else
                input = new Arc4Stream (arc.File, xent);
            if (!xent.IsPacked)
                return input;
            using (input)
            using (var tz = new TzCompression (input))
                return new BinMemoryStream (tz.Unpack(), entry.Name);
        }

        static int ReadInt24 (byte[] data, int pos)
        {
            return data[pos] << 16 | data[pos+1] << 8 | data[pos+2];
        }
    }

    internal sealed class TzCompression : IDisposable
    {
        BinaryReader        m_input;

        public TzCompression (Stream stream)
        {
            m_input = new ArcView.Reader (stream);
        }

        public byte[] Unpack ()
        {
            int signature = m_input.ReadUInt16();
            uint unpacked_size = m_input.ReadUInt32();
            var data = new byte[unpacked_size];
            int dst = 0;
            var buffer = new byte[0x10000];
            while (dst < data.Length)
            {
                signature = m_input.ReadUInt16();
                ushort block_size = m_input.ReadUInt16();
                ushort unpacked_block_size = m_input.ReadUInt16();
                if (0 == unpacked_block_size)
                    throw new InvalidFormatException();

                ushort key = m_input.ReadUInt16();
                if (block_size != m_input.Read (buffer, 0, block_size))
                    throw new EndOfStreamException();
                DecryptBlock (buffer, 0, block_size, key);
                if (0x7453 == signature) // 'St'
                    Buffer.BlockCopy (buffer, 0, data, dst, unpacked_block_size);
                else if (0x745A == signature) // 'Zt'
                    UnpackBlock (buffer, 0, block_size, data, dst, unpacked_block_size);
                else
                    throw new InvalidFormatException();
                dst += unpacked_block_size;
            }
            return data;
        }

        unsafe void DecryptBlock (byte[] data, int pos, int count, uint key)
        {
            fixed (byte* data8 = &data[pos])
            {
                ushort* data16 = (ushort*)data8;
                for (int i = count / 2; i > 0; --i)
                {
                    key *= 0x1465D9;
                    key += 0x0FB5;
                    *data16++ -= (ushort)(key >> 16);
                }
            }
        }

        void UnpackBlock (byte[] input, int src, int input_size,
                          byte[] output, int dst, int output_size)
        {
            int src_end = src + input_size;
            while (src < src_end)
            {
                int ctl = input[src++];
                if (0 == ctl)
                    break;
                if (0 != (ctl & 0x80))
                {
                    int offset, count;
                    if (0 != (ctl & 0x40))
                    {
                        if (0 != (ctl & 0x20))
                        {
                            ctl = (ctl << 8) | input[src++];
                            ctl = (ctl << 8) | input[src++];

                            count = (ctl & 0x3F) + 4;
                            offset = (ctl >> 6) & 0x7FFF;
                        }
                        else
                        {
                            ctl = (ctl << 8) | input[src++];

                            count = (ctl & 7) + 3;
                            offset = (ctl >> 3) & 0x3FF;
                        }
                    }
                    else
                    {
                        count = (ctl & 3) + 2;
                        offset = (ctl >> 2) & 0xF;
                    }
                    ++offset;
                    Binary.CopyOverlapped (output, dst - offset, dst, count);
                    dst += count;
                }
                else
                {
                    Buffer.BlockCopy (input, src, output, dst, ctl);
                    src += ctl;
                    dst += ctl;
                }
            }
        }

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }

    internal class Arc4Stream : Stream
    {
        ArcView     m_file;
        IEnumerator<long> m_segment;
        Stream      m_stream;
        bool        m_eof = false;

        public override bool CanRead  { get { return !_disposed; } }
        public override bool CanSeek  { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length   { get { throw new NotSupportedException ("Arc4Stream.Length not supported"); } }
        public override long Position
        {
            get { throw new NotSupportedException ("Arc4Stream.Position not supported."); }
            set { throw new NotSupportedException ("Arc4Stream.Position not supported."); }
        }

        public Arc4Stream (ArcView file, Arc4Entry entry)
        {
            m_file = file;
            m_segment = entry.Segments.GetEnumerator();
            NextSegment();
        }

        private void NextSegment ()
        {
            if (!m_segment.MoveNext())
            {
                m_eof = true;
                return;
            }
            var prev_stream = m_stream;
            long offset = m_segment.Current;
            var segment_size = Binary.BigEndian (m_file.View.ReadUInt32 (offset+4));
            m_stream = m_file.CreateStream (offset+0x10, segment_size);
            if (null != prev_stream)
                prev_stream.Dispose();
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (!m_eof && count > 0)
            {
                int read = m_stream.Read (buffer, offset, count);
                if (0 != read)
                {
                    total += read;
                    offset += read;
                    count -= read;
                }
                if (0 != count)
                    NextSegment();
            }
            return total;
        }

        public override int ReadByte ()
        {
            int b = -1;
            while (!m_eof)
            {
                b = m_stream.ReadByte();
                if (-1 != b)
                    break;
                NextSegment();
            }
            return b;
        }

        public override void Flush ()
        {
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            throw new NotSupportedException ("Arc4Stream.Seek method is not supported");
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("Arc4Stream.SetLength method is not supported");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("Arc4Stream.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new NotSupportedException("Arc4Stream.WriteByte method is not supported");
        }

        #region IDisposable Members
        bool _disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (null != m_stream)
                        m_stream.Dispose();
                    m_segment.Dispose();
                }
                _disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }
}
