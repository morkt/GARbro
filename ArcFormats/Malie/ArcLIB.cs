//! \file       ArcLIB.cs
//! \date       Thu Jun 25 06:46:51 2015
//! \brief      Malie System archive implementation.
//
// Copyright (C) 2015-2016 by morkt
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
using System.Text;
using GameRes.Cryptography;
using GameRes.Utility;

namespace GameRes.Formats.Malie
{
    [Export(typeof(ArchiveFormat))]
    public class LibOpener : ArchiveFormat
    {
        public override string         Tag { get { return "LIB"; } }
        public override string Description { get { return "Malie engine resource archive"; } }
        public override uint     Signature { get { return 0x0042494C; } } // 'LIB'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            var reader = new Reader (file);
            if (reader.ReadIndex ("", 0, (uint)file.MaxOffset))
                return new ArcFile (file, this, reader.Dir);
            else
                return null;
        }

        internal class Reader
        {
            ArcView.Frame   m_view;
            List<Entry>     m_dir = new List<Entry>();

            public List<Entry> Dir { get { return m_dir; } }

            public Reader (ArcView file)
            {
                m_view = file.View;
            }

            public bool ReadIndex (string root, long base_offset, uint size)
            {
                uint signature = m_view.ReadUInt32 (base_offset);
                if (0x0042494C != signature)
                    return false;

                int count = m_view.ReadInt16 (base_offset + 8);
                if (count <= 0)
                    return false;
                long index_offset = base_offset + 0x10;
                uint index_size = (uint)(0x30 * count);
                if (index_size > size)
                    return false;
                if (index_size > m_view.Reserve (index_offset, index_size))
                    return false;
                long data_offset = index_offset + index_size;
                if (m_dir.Capacity < m_dir.Count + count)
                    m_dir.Capacity = m_dir.Count + count;
                for (int i = 0; i < count; ++i)
                {
                    string name = m_view.ReadString (index_offset, 0x24);
                    uint entry_size = m_view.ReadUInt32 (index_offset+0x24);
                    long offset = base_offset + m_view.ReadUInt32 (index_offset+0x28);
                    index_offset += 0x30;
                    string ext = Path.GetExtension (name);
                    name = Path.Combine (root, name);
                    if (string.IsNullOrEmpty (ext) && ReadIndex (name, offset, entry_size))
                    {
                        continue;
                    }
                    if (offset < data_offset || offset + entry_size > base_offset + size)
                        return false;

                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = offset;
                    entry.Size   = entry_size;
                    m_dir.Add (entry);
                }
                return true;
            }
        }
    }

    public class MalieArchive : ArcFile
    {
        public readonly Camellia Encryption;

        public MalieArchive (ArcView file, ArchiveFormat format, ICollection<Entry> dir, Camellia encryption)
            : base (file, format, dir)
        {
            Encryption = encryption;
        }
    }

    [Serializable]
    public class LibScheme
    {
        public uint     DataAlign;
        public uint[]   Key;

        public LibScheme (uint[] key) : this (0x1000, key)
        {
        }

        public LibScheme (uint align, uint[] key)
        {
            DataAlign = align;
            Key = key;
        }

        public LibScheme (string key) : this (Camellia.GenerateKey (key))
        {
        }

        public LibScheme (uint align, string key) : this (align, Camellia.GenerateKey (key))
        {
        }
    }

    [Serializable]
    public class MalieScheme : ResourceScheme
    {
        public Dictionary<string, LibScheme> KnownSchemes;
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "LIBP"; } }
        public override string Description { get { return "Malie engine encrypted archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "lib", "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset <= 0x10)
                return null;
            var header = new byte[0x10];
            foreach (var scheme in KnownSchemes.Values)
            {
                var encryption = new Camellia (scheme.Key);
                ReadEncrypted (file.View, encryption, 0, header, 0, 0x10);
                LibIndexReader reader;
                if (Binary.AsciiEqual (header, 0, "LIBP"))
                    reader = new LibPReader (file, encryption, header, scheme);
                else if (Binary.AsciiEqual (header, 0, "LIBU"))
                    reader = new LibUReader (file, encryption, header, scheme);
                else
                    continue;
                using (reader)
                {
                    var dir = reader.ReadIndex();
                    if (dir != null)
                        return new MalieArchive (file, this, dir, encryption);
                }
            }
            return null;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var march = arc as MalieArchive;
            if (null == march)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var input = new EncryptedStream (march.File, march.Encryption);
            return new StreamRegion (input, entry.Offset, entry.Size);
        }

        internal abstract class LibIndexReader : IDisposable
        {
            protected ArcView.Frame m_view;
            protected readonly long m_max_offset;
            protected Camellia      m_enc;
            protected List<Entry>   m_dir = new List<Entry>();
            protected byte[]        m_header;

            public List<Entry> Dir { get { return m_dir; } }

            protected LibIndexReader (ArcView file, Camellia encryption, byte[] header)
            {
                m_view = file.View;
                m_max_offset = file.MaxOffset;
                m_enc = encryption;
                m_header = header;
            }

            public abstract List<Entry> ReadIndex ();

            #region IDisposable Members
            public void Dispose ()
            {
                Dispose (true);
                GC.SuppressFinalize (this);
            }

            protected virtual void Dispose (bool disposing)
            {
            }
            #endregion
        }

        internal class LibPReader : LibIndexReader
        {
            byte[]        m_index;
            long          m_base_offset;
            uint          m_data_align;
            uint[]        m_offset_table;

            public LibPReader (ArcView file, Camellia encryption, byte[] header, LibScheme scheme)
                : base (file, encryption, header)
            {
                m_base_offset = 0;
                m_data_align = scheme.DataAlign - 1;
            }

            public override List<Entry> ReadIndex ()
            {
                int count = LittleEndian.ToInt32 (m_header, 4);
                if (!IsSaneCount (count))
                    return null;
                int offset_count = LittleEndian.ToInt32 (m_header, 8);

                m_index     = new byte[0x20 * count];
                var offsets = new byte[4 * offset_count];

                m_base_offset += 0x10;
                if (m_index.Length != ReadEncrypted (m_view, m_enc, m_base_offset, m_index, 0, m_index.Length))
                    return null;
                m_base_offset += m_index.Length;
                if (offsets.Length != ReadEncrypted (m_view, m_enc, m_base_offset, offsets, 0, offsets.Length))
                    return null;
                m_offset_table = new uint[offset_count];
                Buffer.BlockCopy (offsets, 0, m_offset_table, 0, offsets.Length);

                m_base_offset += offsets.Length;
                m_base_offset = (m_base_offset + m_data_align) & ~m_data_align;

                m_dir.Capacity = offset_count;
                ReadDir ("", 0, 1);
                return m_dir.Count > 0 ? m_dir : null;
            }

            private void ReadDir (string root, int entry_index, int count)
            {
                int current_offset = entry_index * 0x20;
                for (int i = 0; i < count; ++i)
                {
                    string name = Binary.GetCString (m_index, current_offset, 0x14);
                    int flags   = LittleEndian.ToInt32 (m_index, current_offset+0x14);
                    int offset  = LittleEndian.ToInt32 (m_index, current_offset+0x18);
                    uint size   = LittleEndian.ToUInt32 (m_index, current_offset+0x1c);
                    current_offset += 0x20;
                    name = Path.Combine (root, name);
                    if (0 == (flags & 0x10000))
                    {
                        if (offset > entry_index)
                            ReadDir (name, (int)offset, (int)size);
                        continue;
                    }
                    long entry_offset = m_base_offset + ((long)m_offset_table[offset] << 10);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    if (entry.CheckPlacement (m_max_offset))
                    {
                        entry.Offset = entry_offset;
                        entry.Size   = size;
                        m_dir.Add (entry);
                    }
                }
            }
        }

        internal class LibUReader : LibIndexReader
        {
            BinaryReader    m_input;

            public LibUReader (ArcView file, Camellia encryption, byte[] header, LibScheme scheme)
                : base (file, encryption, header)
            {
                var input = new EncryptedStream (file, encryption);
                m_input = new BinaryReader (input, Encoding.Unicode);
            }

            public override List<Entry> ReadIndex ()
            {
                return ReadDir ("", 0) ? m_dir : null;
            }

            bool ReadDir (string root, long base_offset)
            {
                m_input.BaseStream.Position = base_offset;
                if (0x5542494C != m_input.ReadUInt32()) // 'LIBU'
                    return false;
                m_input.ReadInt32();
                int count = m_input.ReadInt32();
                if (!IsSaneCount (count))
                    return false;
                if (m_dir.Capacity < m_dir.Count + count)
                    m_dir.Capacity = m_dir.Count + count;

                long index_pos = base_offset + 0x10;
                for (int i = 0; i < count; ++i)
                {
                    m_input.BaseStream.Position = index_pos;
                    var name = ReadName();
                    uint entry_size = m_input.ReadUInt32();
                    long entry_offset = base_offset + m_input.ReadInt64();
                    index_pos = m_input.BaseStream.Position;
                    bool has_extension = -1 != name.IndexOf ('.');
                    name = Path.Combine (root, name);
                    if (!has_extension && ReadDir (name, entry_offset))
                        continue;

                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = entry_offset;
                    entry.Size   = entry_size;
                    if (!entry.CheckPlacement (m_max_offset))
                        return false;

                    m_dir.Add (entry);
                }
                return true;
            }

            char[] m_name_buffer = new char[0x22];

            string ReadName ()
            {
                m_input.Read (m_name_buffer, 0, 0x22);
                int length = Array.IndexOf (m_name_buffer, '\0');
                if (-1 == length)
                    length = m_name_buffer.Length;
                return new string (m_name_buffer, 0, length);
            }

            #region IDisposable methods
            bool m_disposed = false;
            protected override void Dispose (bool disposing)
            {
                if (!m_disposed)
                {
                    if (disposing)
                        m_input.Dispose();
                    m_disposed = true;
                    base.Dispose();
                }
            }
            #endregion
        }

        private static int ReadEncrypted (ArcView.Frame view, Camellia enc, long offset, byte[] buffer, int index, int length)
        {
            int offset_pad  = (int)offset & 0xF;
            int aligned_len = (offset_pad + length + 0xF) & ~0xF;    
            byte[] aligned_buf;
            int block = 0;
            if (aligned_len == length)
            {
                aligned_buf = buffer;
                block = index;
            }
            else
            {
                aligned_buf = new byte[aligned_len];
            }

            int read = view.Read (offset - offset_pad, aligned_buf, block, (uint)aligned_len);
            if (read < offset_pad)
                return 0;

            for (int block_count = aligned_len / 0x10; block_count > 0; --block_count)
            {
                enc.DecryptBlock (offset, aligned_buf, block);
                block  += 0x10;
                offset += 0x10;
            }
            if (aligned_buf != buffer)
                Buffer.BlockCopy (aligned_buf, offset_pad, buffer, index, length);
            return Math.Min (length, read-offset_pad);
        }

        public static Dictionary<string, LibScheme> KnownSchemes = new Dictionary<string, LibScheme>();

        public override ResourceScheme Scheme
        {
            get { return new MalieScheme { KnownSchemes = KnownSchemes }; }
            set { KnownSchemes = ((MalieScheme)value).KnownSchemes; }
        }
    }

    internal class EncryptedStream : Stream
    {
        ArcView.Frame   m_view;
        Camellia        m_enc;
        long            m_max_offset;
        long            m_position = 0;
        byte[]          m_current_block = new byte[BlockLength];
        int             m_current_block_length = 0;
        long            m_current_block_position = 0;

        public const int BlockLength = 0x1000;

        public Camellia Encryption { get { return m_enc; } }

        public EncryptedStream (ArcView mmap, Camellia encryption)
        {
            m_view = mmap.CreateFrame();
            m_enc = encryption;
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
                m_enc.DecryptBlock (m_current_block_position+offset, m_current_block, offset);
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
