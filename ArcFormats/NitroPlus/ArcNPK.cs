//! \file       ArcNPK.cs
//! \date       Sat Feb 06 06:07:52 2016
//! \brief      Mware resource archive.
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
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.NitroPlus
{
    internal class NpkEntry : PackedEntry
    {
        public readonly List<NpkSegment> Segments = new List<NpkSegment>();
    }

    internal class NpkSegment
    {
        public long Offset;
        public uint AlignedSize;
        public uint Size;
        public uint UnpackedSize;
        public bool IsCompressed { get { return Size < UnpackedSize; } }
    }

    public class Npk2Options : ResourceOptions
    {
        public byte[] Key;
    }

    internal class NpkArchive : ArcFile
    {
        public readonly Aes Encryption;

        public NpkArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, Aes enc)
            : base (arc, impl, dir)
        {
            Encryption = enc;
        }

        #region IDisposable Members
        bool _npk_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (_npk_disposed)
                return;

            if (disposing)
                Encryption.Dispose();
            _npk_disposed = true;
            base.Dispose (disposing);
        }
        #endregion
    }

    [Export(typeof(ArchiveFormat))]
    public class NpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "NPK"; } }
        public override string Description { get { return "Mware engine resource archive"; } }
        public override uint     Signature { get { return 0x324B504E; } } // 'NPK2'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return true; } }

        static Npk2Scheme DefaultScheme = new Npk2Scheme { KnownKeys = new Dictionary<string, byte[]>() };
        internal Dictionary<string, byte[]> KnownKeys { get { return DefaultScheme.KnownKeys; } }

        const uint DefaultSegmentSize = 0x10000;
        static readonly Encoding DefaultEncoding = Encoding.UTF8;

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (Npk2Scheme)value; }
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0x18);
            if (!IsSaneCount (count))
                return null;
            var key = QueryEncryption (file.Name);
            if (null == key)
                return null;
            var aes = Aes.Create();
            try
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = file.View.ReadBytes (8, 0x10);
                uint index_size = file.View.ReadUInt32 (0x1C);
                using (var decryptor = aes.CreateDecryptor())
                using (var enc_index = file.CreateStream (0x20, index_size))
                using (var dec_index = new CryptoStream (enc_index, decryptor, CryptoStreamMode.Read))
                using (var index = new ArcView.Reader (dec_index))
                {
                    var dir = ReadIndex (index, count, file.MaxOffset);
                    if (null == dir)
                        return null;
                    var arc = new NpkArchive (file, this, dir, aes);
                    aes = null; // object ownership passed to NpkArchive, don't dispose
                    return arc;
                }
            }
            finally
            {
                if (aes != null)
                    aes.Dispose();
            }
        }

        List<Entry> ReadIndex (BinaryReader index, int count, long max_offset)
        {
            var name_buffer = new byte[0x104];
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                index.ReadByte();
                int name_length = index.ReadUInt16();
                if (0 == name_length || name_length > name_buffer.Length)
                    return null;
                index.Read (name_buffer, 0, name_length);
                var name = DefaultEncoding.GetString (name_buffer, 0, name_length);
                var entry = FormatCatalog.Instance.Create<NpkEntry> (name);
                entry.UnpackedSize = index.ReadUInt32();
                index.Read (name_buffer, 0, 0x20); // skip
                int segment_count = index.ReadInt32();
                if (segment_count < 0)
                    return null;
                if (0 == segment_count)
                {
                    entry.Offset = 0;
                    dir.Add (entry);
                    continue;
                }
                entry.Segments.Capacity = segment_count;
                uint packed_size = 0;
                bool is_packed = false;
                for (int j = 0; j < segment_count; ++j)
                {
                    var segment = new NpkSegment();
                    segment.Offset = index.ReadInt64();
                    segment.AlignedSize = index.ReadUInt32();
                    segment.Size = index.ReadUInt32();
                    segment.UnpackedSize = index.ReadUInt32();
                    entry.Segments.Add (segment);
                    packed_size += segment.AlignedSize;
                    is_packed = is_packed || segment.IsCompressed;
                }
                entry.Offset = entry.Segments[0].Offset;
                entry.Size   = packed_size;
                entry.IsPacked = is_packed;
                if (!entry.CheckPlacement (max_offset))
                    return null;
                dir.Add (entry);
            }
            return dir;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (0 == entry.Size)
                return Stream.Null;
            var narc = arc as NpkArchive;
            var nent = entry as NpkEntry;
            if (null == narc || null == nent)
                return base.OpenEntry (arc, entry);

            if (1 == nent.Segments.Count && !nent.IsPacked)
            {
                var input = narc.File.CreateStream (nent.Segments[0].Offset, nent.Segments[0].AlignedSize);
                var decryptor = narc.Encryption.CreateDecryptor();
                return new InputCryptoStream (input, decryptor);
            }
            return new NpkStream (narc, nent);
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new Npk2Options { Key = GetKey (Properties.Settings.Default.NPKScheme) };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetNPK (KnownKeys.Keys);
        }

        public override object GetCreationWidget ()
        {
            return new GUI.WidgetNPK (KnownKeys.Keys);
        }

        byte[] QueryEncryption (string arc_name)
        {
            byte[] key = null;
            var title = FormatCatalog.Instance.LookupGame (arc_name);
            if (!string.IsNullOrEmpty (title))
                key = GetKey (title);
            if (null == key)
            {
                var options = Query<Npk2Options> (arcStrings.ArcEncryptedNotice);
                key = options.Key;
            }
            return key;
        }

        byte[] GetKey (string title)
        {
            byte[] key;
            KnownKeys.TryGetValue (title, out key);
            return key;
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            var npk_options = GetOptions<Npk2Options> (options);
            if (null == npk_options.Key)
                throw new InvalidEncryptionScheme();

            var enc = DefaultEncoding.WithFatalFallback();
            int index_length = 0;
            var dir = new List<NpkStoredEntry>();
            foreach (var entry in list)
            {
                var ext = Path.GetExtension (entry.Name).ToLowerInvariant();
                var npk_entry = new NpkStoredEntry
                {
                    Name = entry.Name,
                    RawName = enc.GetBytes (entry.Name.Replace ('\\', '/')),
                    IsSolid = SolidFiles.Contains (ext),
                    IsPacked = !DisableCompression.Contains (ext),
                };
                int segment_count = 1;
                if (!npk_entry.IsSolid)
                    segment_count = (int)(((long)entry.Size + DefaultSegmentSize-1) / DefaultSegmentSize);
                index_length += 3 + npk_entry.RawName.Length + 0x28 + segment_count * 0x14;
                dir.Add (npk_entry);
            }
            index_length = (index_length + 0xF) & ~0xF;

            int callback_count = 0;
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = npk_options.Key;
                aes.IV = GenerateAesIV();
                output.Position = 0x20 + index_length;
                foreach (var entry in dir)
                {
                    if (null != callback)
                        callback (callback_count++, entry, arcStrings.MsgAddingFile);

                    using (var writer = new NpkWriter (entry, output, aes))
                        writer.Write (DefaultSegmentSize);
                }
                output.Position = 0;
                var buffer = new byte[] { (byte)'N', (byte)'P', (byte)'K', (byte)'2', 1, 0, 0, 0 };
                output.Write (buffer, 0, 8);
                output.Write (aes.IV, 0, 0x10);
                LittleEndian.Pack (dir.Count, buffer, 0);
                LittleEndian.Pack (index_length, buffer, 4);
                output.Write (buffer, 0, 8);

                using (var encryptor = aes.CreateEncryptor())
                using (var proxy = new ProxyStream (output, true))
                using (var index_stream = new CryptoStream (proxy, encryptor, CryptoStreamMode.Write))
                using (var index = new BinaryWriter (index_stream))
                {
                    if (null != callback)
                        callback (callback_count++, null, arcStrings.MsgWritingIndex);
                    foreach (var entry in dir)
                    {
                        index.Write (entry.IsSolid); // 0 -> segmentation enabled, 1 -> no segmentation
                        index.Write ((short)entry.RawName.Length);
                        index.Write (entry.RawName);
                        index.Write (entry.UnpackedSize);
                        index.Write (entry.CheckSum);
                        index.Write (entry.Segments.Count);
                        foreach (var segment in entry.Segments)
                        {
                            index.Write (segment.Offset);
                            index.Write (segment.AlignedSize);
                            index.Write (segment.Size);
                            index.Write (segment.UnpackedSize);
                        }
                    }
                }
            }
        }

        byte[] GenerateAesIV ()
        {
            using (var rng = new RNGCryptoServiceProvider())
            {
                var iv = new byte[0x10];
                rng.GetBytes (iv);
                return iv;
            }
        }

        static readonly HashSet<string> SolidFiles = new HashSet<string> { ".png", ".jpg" };
        static readonly HashSet<string> DisableCompression = new HashSet<string> { ".png", ".jpg", ".ogg" };
    }

    internal class NpkStoredEntry : NpkEntry
    {
        public byte[]   RawName;
        public byte[]   CheckSum;
        public bool     IsSolid;
    }

    internal sealed class NpkWriter : IDisposable
    {
        NpkStoredEntry  m_entry;
        FileStream      m_input;
        Stream          m_archive;
        CryptoStream    m_checksum_stream;
        Aes             m_aes;
        long            m_remaining;
        byte[]          m_buffer;

        public NpkWriter (NpkStoredEntry entry, Stream archive, Aes aes)
        {
            m_input = File.OpenRead (entry.Name);
            m_archive = archive;
            m_entry = entry;
            m_aes = aes;
            m_buffer = null;
        }

        static readonly byte[] EmptyFileHash = GetDefaultHash();

        public void Write (uint segment_size)
        {
            long input_size = m_input.Length;
            if (input_size > uint.MaxValue)
                throw new FileSizeException();
            m_entry.Offset = m_archive.Position;
            m_entry.Size = m_entry.UnpackedSize = (uint)input_size;
            if (0 == m_entry.Size)
            {
                m_entry.CheckSum = EmptyFileHash;
                return;
            }
            if (!m_entry.IsSolid)
                m_buffer = new byte[segment_size];
            else if (input_size > segment_size)
                segment_size = (uint)input_size;

            using (var sha256 = SHA256.Create())
            using (m_checksum_stream = new CryptoStream (Stream.Null, sha256, CryptoStreamMode.Write))
            {
                m_remaining = input_size;
                int segment_count = (int)((input_size + segment_size - 1) / segment_size);
                m_entry.Segments.Clear();
                m_entry.Segments.Capacity = segment_count;
                for (int i = 0; i < segment_count; ++i)
                {
                    int chunk_size = (int)Math.Min (m_remaining, segment_size);
                    bool should_compress = m_entry.IsPacked && chunk_size > 2;
                    var file_pos = m_input.Position;
                    var segment = WriteSegment (chunk_size, should_compress);
                    if (should_compress && !segment.IsCompressed)
                    {
                        // compressed segment is larger than uncompressed, rewrite
                        m_input.Position = file_pos;
                        m_archive.Position = segment.Offset;
                        RewriteSegment (segment, chunk_size);
                        m_archive.SetLength (m_archive.Position);
                    }
                    m_entry.Segments.Add (segment);
                    m_remaining -= segment.UnpackedSize;
                }
                m_checksum_stream.FlushFinalBlock();
                m_entry.CheckSum = sha256.Hash;
            }
        }

        NpkSegment WriteSegment (int chunk_size, bool compress)
        {
            var segment = new NpkSegment { Offset = m_archive.Position };
            using (var proxy = new ProxyStream (m_archive, true))
            using (var encryptor = m_aes.CreateEncryptor())
            {
                Stream output = new CryptoStream (proxy, encryptor, CryptoStreamMode.Write);
                var measure = new CountedStream (output);
                output = measure;
                if (compress)
                    output = new DeflateStream (output, CompressionLevel.Optimal);
                using (output)
                {
                    if (m_remaining == chunk_size)
                    {
                        var file_pos = m_input.Position;
                        m_input.CopyTo (output);
                        m_input.Position = file_pos;
                        m_input.CopyTo (m_checksum_stream);
                    }
                    else
                    {
                        chunk_size = m_input.Read (m_buffer, 0, chunk_size);
                        output.Write (m_buffer, 0, chunk_size);
                        m_checksum_stream.Write (m_buffer, 0, chunk_size);
                    }
                }
                segment.UnpackedSize = (uint)chunk_size;
                segment.Size = (uint)measure.Count;
                segment.AlignedSize = (uint)(m_archive.Position - segment.Offset);
                return segment;
            }
        }

        void RewriteSegment (NpkSegment segment, int chunk_size)
        {
            using (var proxy = new ProxyStream (m_archive, true))
            using (var encryptor = m_aes.CreateEncryptor())
            using (var output = new CryptoStream (proxy, encryptor, CryptoStreamMode.Write))
            {
                if (m_remaining == chunk_size)
                {
                    m_input.CopyTo (output);
                }
                else
                {
                    chunk_size = m_input.Read (m_buffer, 0, chunk_size);
                    output.Write (m_buffer, 0, chunk_size);
                }
            }
            segment.UnpackedSize = segment.Size = (uint)chunk_size;
            segment.AlignedSize = (uint)(m_archive.Position - segment.Offset);
        }

        static byte[] GetDefaultHash ()
        {
            using (var sha256 = SHA256.Create())
                return sha256.ComputeHash (new byte[0]);
        }

        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize (this);
        }
    }

    internal class NpkStream : Stream
    {
        ArcView     m_file;
        Aes         m_encryption;
        IEnumerator<NpkSegment> m_segment;
        Stream      m_stream;
        bool        m_eof = false;

        public override bool CanRead  { get { return m_stream != null && m_stream.CanRead; } }
        public override bool CanSeek  { get { return false; } }
        public override bool CanWrite { get { return false; } }

        public NpkStream (NpkArchive arc, NpkEntry entry)
        {
            m_file = arc.File;
            m_encryption = arc.Encryption;
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
            m_stream = m_file.CreateStream (segment.Offset, segment.AlignedSize);
            var decryptor = m_encryption.CreateDecryptor();
            m_stream = new InputCryptoStream (m_stream, decryptor);
            if (segment.IsCompressed)
                m_stream = new DeflateStream (m_stream, CompressionMode.Decompress);
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

        #region IO.Stream members
        public override long Length
        {
            get { throw new NotSupportedException ("NpkStream.Length not supported"); }
        }
        public override long Position
        {
            get { throw new NotSupportedException ("NpkStream.Position not supported."); }
            set { throw new NotSupportedException ("NpkStream.Position not supported."); }
        }

        public override void Flush ()
        {
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            throw new NotSupportedException ("NpkStream.Seek method is not supported");
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("NpkStream.SetLength method is not supported");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("NpkStream.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new NotSupportedException("NpkStream.WriteByte method is not supported");
        }
        #endregion

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

    [Serializable]
    public class Npk2Scheme : ResourceScheme
    {
        public Dictionary<string, byte[]> KnownKeys;
    }

    /// <summary>
    /// Filter stream that counts total bytes read/written.
    /// </summary>
    public class CountedStream : ProxyStream
    {
        long    m_count;

        public long Count { get { return m_count; } }

        public CountedStream (Stream source, bool leave_open = false) : base (source, leave_open)
        {
            m_count = 0;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = BaseStream.Read (buffer, offset, count); 
            m_count += read;
            return read;
        }

        public override int ReadByte ()
        {
            int b = BaseStream.ReadByte();
            if (b != -1)
                ++m_count;
            return b;
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            BaseStream.Write (buffer, offset, count);
            m_count += count;
        }

        public override void WriteByte (byte b)
        {
            BaseStream.WriteByte (b);
            ++m_count;
        }
    }
}
