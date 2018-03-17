//! \file       ArcA5R.cs
//! \date       2018 Mar 16
//! \brief      Pinky Soft resource archive.
//
// Copyright (C) 2018 by morkt
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
using System.Linq;
using GameRes.Compression;

namespace GameRes.Formats.Pinky
{
    internal class A5Segment
    {
        public uint     Offset;
        public uint     Size;
        public uint     UnpackedSize;
        public byte     Type;
        public byte     Compression;

        public bool IsCompressed { get { return 3 == Compression; } }
    }

    internal class A5rEntry : PackedEntry
    {
        public IEnumerable<A5Segment>   Segments;
    }

    [Export(typeof(ArchiveFormat))]
    public class A5rOpener : ArchiveFormat
    {
        public override string         Tag { get { return "A5R"; } }
        public override string Description { get { return "Pinky Soft resource archive"; } }
        public override uint     Signature { get { return 0x53524350; } } // 'PCRS'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public A5rOpener ()
        {
            Signatures = new uint[] { 0x53524350, 0x42494C50 }; // 'PLIB'
            Extensions = new string[] { "a5r", "a5e" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint id = file.View.ReadUInt32 (0);
            if (file.View.ReadUInt32 (4) != ~id)
                return null;
            int count = file.View.ReadInt32 (0x30);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (0x34);
            if (index_offset >= file.MaxOffset)
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint next_offset = file.View.ReadUInt32 (index_offset);
            var segments = new A5Segment[count];
            for (int i = 0; i < count; ++i)
            {
                var segment = new A5Segment {
                    Offset = next_offset,
                    UnpackedSize = file.View.ReadUInt32 (index_offset+4),
                    Type = file.View.ReadByte (index_offset+8),
                    Compression = file.View.ReadByte (index_offset+9),
                };
                next_offset = file.View.ReadUInt32 (index_offset+0xA);
                if (next_offset > file.MaxOffset || next_offset < segment.Offset)
                    return null;
                segment.Size = (uint)(next_offset - segment.Offset);
                segments[i] = segment;
                index_offset += 0xA;
            }
            var dir = new List<Entry> (count);
            var riff_buffer = new byte[8];
            for (int i = 0; i < count; )
            {
                A5rEntry entry;
                var segment = segments[i];
                var name = string.Format ("{0}#{1:D5}", base_name, i);
                if (0x3C == segment.Type)
                {
                    Stream input = file.CreateStream (segment.Offset, segment.Size);
                    if (3 == segment.Compression)
                        input = new ZLibStream (input, CompressionMode.Decompress);
                    using (input)
                    {
                        if (8 == input.Read (riff_buffer, 0, 8) && riff_buffer.AsciiEqual ("RIFF"))
                        {
                            uint riff_size = riff_buffer.ToUInt32 (4);
                            entry = new A5rEntry {
                                Name = name + ".wav",
                                Type = "audio",
                                Offset = segment.Offset,
                                Size = 0,
                                UnpackedSize = 0,
                            };
                            var segment_list = new List<A5Segment>();
                            for (;;)
                            {
                                entry.Size += segment.Size;
                                entry.UnpackedSize += segment.UnpackedSize;
                                entry.IsPacked |= segment.Compression == 3;
                                segment_list.Add (segment);
                                ++i;
                                if (i >= count || entry.UnpackedSize >= riff_size)
                                    break;
                                segment = segments[i];
                                if (segment.Type != 0x3C)
                                    break;
                            }
                            entry.Segments = segment_list;
                            dir.Add (entry);
                            continue;
                        }
                    }
                }
                if (0x3E == segment.Type)
                    name += ".bmp";
                entry = new A5rEntry {
                    Name = name,
                    Type = 0x3E == segment.Type ? "image" : "",
                    Offset = segment.Offset,
                    Size = segment.Size,
                    UnpackedSize = segment.UnpackedSize,
                    IsPacked = segment.Compression == 3,
                    Segments = new A5Segment[1] { segment },
                };
                dir.Add (entry);
                ++i;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var a5ent = (A5rEntry)entry;
            Stream input;
            if (a5ent.Segments.Count() == 1)
            {
                input = arc.File.CreateStream (entry.Offset, entry.Size);
                if (a5ent.IsPacked)
                    input = new ZLibStream (input, CompressionMode.Decompress);
            }
            else
            {
                input = new A5rStream (arc.File, a5ent);
            }
            return input;
        }
    }

    internal class A5rStream : Stream
    {
        ArcView     m_file;
        A5rEntry    m_entry;
        IEnumerator<A5Segment> m_segment;
        Stream      m_stream;
        long        m_offset = 0;
        bool        m_eof = false;

        public override bool CanRead  { get { return !disposed; } }
        public override bool CanSeek  { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length   { get { return m_entry.UnpackedSize; } }
        public override long Position
        {
            get { return m_offset; }
            set { throw new NotSupportedException ("A5rStream.Position not supported."); }
        }

        public A5rStream (ArcView file, A5rEntry entry)
        {
            m_file = file;
            m_entry = entry;
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
            if (null != m_stream)
                m_stream.Dispose();
            var segment = m_segment.Current;
            m_stream = m_file.CreateStream (segment.Offset, segment.Size);
            if (segment.IsCompressed)
                m_stream = new ZLibStream (m_stream, CompressionMode.Decompress);
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (!m_eof && count > 0)
            {
                int read = m_stream.Read (buffer, offset, count);
                if (0 != read)
                {
                    m_offset += read;
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
            throw new NotSupportedException ("A5rStream.Seek method is not supported");
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("A5rStream.SetLength method is not supported");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("A5rStream.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new NotSupportedException("A5rStream.WriteByte method is not supported");
        }

        #region IDisposable Members
        bool disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    if (null != m_stream)
                        m_stream.Dispose();
                    m_segment.Dispose();
                }
                disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }
}
