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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Malie
{
    internal interface ILibIndexReader : IDisposable
    {
        List<Entry> Dir { get; }

        bool ReadIndex ();
    }

    [Export(typeof(ArchiveFormat))]
    public class LibOpener : ArchiveFormat
    {
        public override string         Tag { get { return "LIB"; } }
        public override string Description { get { return "Malie engine resource archive"; } }
        public override uint     Signature { get { return 0x0042494C; } } // 'LIB'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public LibOpener ()
        {
            Extensions = new string[] { "lib", "sdp" };
        }

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
        public readonly IMalieDecryptor Decryptor;

        public MalieArchive (ArcView file, ArchiveFormat format, ICollection<Entry> dir, IMalieDecryptor decr)
            : base (file, format, dir)
        {
            Decryptor = decr;
        }
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
            Signatures = new uint[] { 0, 0x3F503FB1, 0xC237434E, 0x8CD11522, 0x09D411A7, 0xAAC48CAA, 0x9FC2BCB1, 0xAAC900A3 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset <= 0x10)
                return null;
            var header = new byte[0x10];
            foreach (var scheme in KnownSchemes.Values)
            {
                var decryptor = scheme.CreateDecryptor();
                ReadEncrypted (file.View, decryptor, 0, header, 0, 0x10);
                ILibIndexReader reader;
                if (Binary.AsciiEqual (header, 0, "LIBP"))
                    reader = new LibPReader (file, decryptor, header, scheme);
                else if (Binary.AsciiEqual (header, 0, "LIBU"))
                    reader = LibUReader.Create (file, decryptor);
                else
                    continue;
                using (reader)
                {
                    if (reader.ReadIndex())
                        return new MalieArchive (file, this, reader.Dir, decryptor);
                }
            }
            return null;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var march = arc as MalieArchive;
            if (null == march)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            Stream input = new EncryptedStream (march.File, march.Decryptor);
            input = new StreamRegion (input, entry.Offset, entry.Size);
            if (entry.Name.HasExtension (".txtz"))
            {
                input = new ZLibStream (input, CompressionMode.Decompress);
            }
            else if (entry.Name.HasExtension (".psbz"))
            {
                input = UnpackPsbz (input);
            }
            return input;
        }

        Stream UnpackPsbz (Stream input)
        {
            var header = new byte[8];
            input.Read (header, 0, 8);
            if (!header.AsciiEqual ("PSBZ"))
            {
                input.Position = 0;
                return input;
            }
            int unpacked_size = header.ToInt32 (4);
            var output = new MemoryStream (unpacked_size);
            using (input = new ZLibStream (input, CompressionMode.Decompress))
                input.CopyTo (output);
            output.Position = 0;
            return output;
        }

        internal abstract class LibIndexReader : ILibIndexReader
        {
            protected ArcView.Frame     m_view;
            protected readonly long     m_max_offset;
            protected IMalieDecryptor   m_dec;
            protected List<Entry>       m_dir = new List<Entry>();
            protected byte[]            m_header;

            public List<Entry> Dir { get { return m_dir; } }

            protected LibIndexReader (ArcView file, IMalieDecryptor decryptor, byte[] header)
            {
                m_view = file.View;
                m_max_offset = file.MaxOffset;
                m_dec = decryptor;
                m_header = header;
            }

            public abstract bool ReadIndex ();

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
            byte[]      m_index;
            long        m_base_offset;
            uint[]      m_offset_table;
            LibScheme   m_scheme;

            public LibPReader (ArcView file, IMalieDecryptor decryptor, byte[] header, LibScheme scheme)
                : base (file, decryptor, header)
            {
                m_base_offset = 0;
                m_scheme = scheme;
            }

            public override bool ReadIndex ()
            {
                int count = LittleEndian.ToInt32 (m_header, 4);
                if (!IsSaneCount (count))
                    return false;
                int offset_count = LittleEndian.ToInt32 (m_header, 8);

                m_index     = new byte[0x20 * count];
                var offsets = new byte[4 * offset_count];

                m_base_offset += 0x10;
                if (m_index.Length != ReadEncrypted (m_view, m_dec, m_base_offset, m_index, 0, m_index.Length))
                    return false;
                m_base_offset += m_index.Length;
                if (offsets.Length != ReadEncrypted (m_view, m_dec, m_base_offset, offsets, 0, offsets.Length))
                    return false;
                m_offset_table = new uint[offset_count];
                Buffer.BlockCopy (offsets, 0, m_offset_table, 0, offsets.Length);

                m_base_offset += offsets.Length;
                m_base_offset = m_scheme.GetAlignedOffset (m_base_offset);

                m_dir.Capacity = offset_count;
                ReadDir ("", 0, 1);
                return m_dir.Count > 0;
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
                    if (0 == (flags & 0x30000))
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

        private static int ReadEncrypted (ArcView.Frame view, IMalieDecryptor dec, long offset, byte[] buffer, int index, int length)
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
                dec.DecryptBlock (offset, aligned_buf, block);
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
}
