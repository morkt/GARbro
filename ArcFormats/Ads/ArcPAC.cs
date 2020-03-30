//! \file       ArcPAC.cs
//! \date       2020 Mar 30
//! \brief      ads engine resource archive.
//
// Copyright (C) 2020 by morkt
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

namespace GameRes.Formats.Ads
{
    internal class AdsEntry : PackedEntry
    {
        public int  CompressionMethod;
    }

    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/ADS"; } }
        public override string Description { get { return "ads engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public PacOpener ()
        {
            ContainedFormats = new[] { "TGA", "TXT" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".pac"))
                return null;
            uint index_size = file.View.ReadUInt32 (0);
            if (index_size < 0x110 || index_size >= file.MaxOffset)
                return null;
            using (var reader = new IndexReader (file, index_size))
            {
                var dir = reader.ReadIndex();
                if (null == dir)
                    return null;
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pent = entry as AdsEntry;
            if (null == pent || pent.CompressionMethod != 1)
                return input;
            return new PackedStream<RleDecompressor> (input);
        }
    }

    internal class IndexReader : IDisposable
    {
        ArcViewStream   m_input;
        List<Entry>     m_dir;
        readonly long   m_max_offset;
        
        public IndexReader (ArcView file, uint index_size)
        {
            m_input = file.CreateStream (0, index_size);
            m_dir = new List<Entry>();
            m_max_offset = file.MaxOffset;
        }

        bool ReadDir (uint dir_offset, string dir_name)
        {
            m_input.Position = dir_offset;
            int dir_count = m_input.ReadInt32();
            if (dir_count < 0 || dir_count > 0x200)
                return false;
            int file_count = m_input.ReadInt32();
            if (file_count < 0 || file_count > 0x40000)
                return false;
            if (string.IsNullOrEmpty (dir_name))
            {
                var root_name = ReadCString();
                if (string.IsNullOrWhiteSpace (root_name))
                    return false;
            }
            m_dir.Capacity = m_dir.Count + file_count;
            for (int j = 0; j < file_count; ++j)
            {
                var name = ReadCString();
                if (string.IsNullOrWhiteSpace (name))
                    return false;
                if (dir_name.Length > 0)
                    name = dir_name + '/' + name;
                var entry = new AdsEntry { Name = name };
                entry.Type = FormatCatalog.Instance.GetTypeFromName (name);
                entry.Size = m_input.ReadUInt32();
                entry.Offset = m_input.ReadUInt32();
                if (!entry.CheckPlacement (m_max_offset))
                    return false;
                entry.CompressionMethod = m_input.ReadInt32();
                entry.IsPacked = entry.CompressionMethod != 0;
                m_dir.Add (entry);
            }
            for (int j = 0; j < dir_count; ++j)
            {
                uint offset = m_input.ReadUInt32();
                if (offset >= m_input.Length)
                    return false;
                var name = ReadCString();
                if (string.IsNullOrWhiteSpace (name))
                    return false;
                if (dir_name.Length > 0)
                    name = dir_name + '/' + name;
                var current_pos = m_input.Position;
                if (!ReadDir (offset, name))
                    return false;
                m_input.Position = current_pos;
            }
            return true;
        }

        public List<Entry> ReadIndex ()
        {
            if (!ReadDir (4, ""))
                return null;
            return m_dir;
        }

        byte[]          m_buffer = new byte[0x104];

        internal string ReadCString ()
        {
            int length = m_input.Read (m_buffer, 0, 0x104);
            int end = 0;
            while (end < length && m_buffer[end] != 0)
            {
                m_buffer[end++] ^= 0xFF;
            }
            return Encodings.cp932.GetString (m_buffer, 0, end);
        }

        #region IDisposable Members
        bool m_disposed = false;

        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }

    internal sealed class RleDecompressor : Decompressor
    {
        Stream          m_input;

        public override void Initialize (Stream input)
        {
            m_input = input;
        }

        protected override IEnumerator<int> Unpack ()
        {
            var buffer = new byte[8];
            int chunk_size = 3;
            for (;;)
            {
                int ctl = m_input.ReadByte();
                if (-1 == ctl)
                    yield break;
                if (ctl != 0)
                {
                    if (m_input.Read (buffer, 4, 4) != 4)
                        yield break;
                    int count = buffer.ToInt32 (4) - 1;
                    while (count --> 0)
                    {
                        for (int i = 0; i < chunk_size; ++i)
                        {
                            m_buffer[m_pos++] = buffer[i];
                            if (0 == --m_length)
                                yield return m_pos;
                        }
                    }
                }
                else
                {
                    chunk_size = m_input.Read (buffer, 0, 3);
                    if (0 == chunk_size)
                        yield break;
                    for (int i = 0; i < chunk_size; ++i)
                    {
                        m_buffer[m_pos++] = buffer[i];
                        if (0 == --m_length)
                            yield return m_pos;
                    }
                }
            }
        }
    }
}
