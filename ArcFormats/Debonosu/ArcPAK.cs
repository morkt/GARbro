//! \file       ArcPAK.cs
//! \date       Sat Sep 19 08:51:36 2015
//! \brief      Debonosu Works resource archive.
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.IO.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Debonosu
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/Debonosu"; } }
        public override string Description { get { return "Debonosu Works resource archive"; } }
        public override uint     Signature { get { return 0x004B4150; } } // 'PAK'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public PakOpener ()
        {
            Extensions = new string[] { "pak" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_offset = file.View.ReadUInt16 (4);
            if (0 != file.View.ReadUInt16 (10))
                return null;

            uint info_size = file.View.ReadUInt32 (index_offset);
            int root_count = file.View.ReadInt32 (index_offset+8);
            uint unpacked_size = file.View.ReadUInt32 (index_offset+0xC);
            uint packed_size = file.View.ReadUInt32 (index_offset+0x10);
            using (var packed = file.CreateStream (index_offset+info_size, packed_size))
            using (var unpacked = new DeflateStream (packed, CompressionMode.Decompress))
            using (var reader = new IndexReader (unpacked, index_offset+info_size+packed_size))
            {
                var dir = reader.ReadRoot (root_count);
                if (null == dir)
                    return null;
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var packed = entry as PackedEntry;
            if (null == packed || !packed.IsPacked)
                return input;
            return new DeflateStream (input, CompressionMode.Decompress);
        }

        internal sealed class IndexReader : IDisposable
        {
            BinaryReader    m_input;
            long            m_base_offset;
            List<Entry>     m_dir = new List<Entry>();

            public IndexReader (Stream input, long base_offset)
            {
                m_input = new ArcView.Reader (input);
                m_base_offset = base_offset;
            }

            public List<Entry> ReadRoot (int root_count)
            {
                ReadDir ("", root_count);
                return m_dir;
            }

            void ReadDir (string path, int count)
            {
                for (int i = 0; i < count; ++i)
                {
                    long offset = m_input.ReadInt64();
                    long unpacked = m_input.ReadInt64();
                    long packed = m_input.ReadInt64();
                    uint flags = m_input.ReadUInt32();
                    m_input.ReadUInt64(); // ctime
                    m_input.ReadUInt64(); // atime
                    m_input.ReadUInt64(); // mtime
                    var name = Path.Combine (path, ReadName());
                    if (0 != (flags & 0x10))
                    {
                        ReadDir (name, (int)unpacked);
                    }
                    else
                    {
                        var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                        entry.Offset = m_base_offset + offset;
                        entry.Size = (uint)packed;
                        entry.UnpackedSize = (uint)unpacked;
                        entry.IsPacked = true;
                        m_dir.Add (entry);
                    }
                }
            }

            byte[] name_buffer = new byte[32];

            string ReadName ()
            {
                int size = 0;
                for (;;)
                {
                    int b = m_input.ReadByte();
                    if (0 == b)
                        break;
                    if (name_buffer.Length == size)
                    {
                        Array.Resize (ref name_buffer, checked(size/2*3));
                    }
                    name_buffer[size++] = (byte)b;
                }
                return Encodings.cp932.GetString (name_buffer, 0, size);
            }

            bool _disposed = false;
            public void Dispose ()
            {
                if (!_disposed)
                {
                    m_input.Dispose();
                    _disposed = true;
                }
            }
        }
    }
}
