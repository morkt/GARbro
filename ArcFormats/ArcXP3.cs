//! \file       ArcXP3.cs
//! \date       Wed Jul 16 13:58:17 2014
//! \brief      KiriKiri engine archive implementation.
//

using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using GameRes.Utility;
using ZLibNet;
using GameRes.Formats.Strings;
using GameRes.Formats.Properties;

namespace GameRes.Formats.KiriKiri
{
    public struct Xp3Segment
    {
        public bool IsCompressed;
        public long Offset;
        public uint Size;
        public uint PackedSize;
    }

    public class Xp3Entry : PackedEntry
    {
        List<Xp3Segment> m_segments = new List<Xp3Segment>();

        public bool IsEncrypted { get; set; }
        public ICrypt Cipher { get; set; }
        public ICollection<Xp3Segment> Segments { get { return m_segments; } }
        public uint Hash { get; set; }
    }

    // Archive version 1: encrypt file first, then calculate checksum
    //         version 2: calculate checksum, then encrypt

    [Export(typeof(ArchiveFormat))]
    public class Xp3Opener : ArchiveFormat
    {
        public override string Tag { get { return "XP3"; } }
        public override string Description { get { return arcStrings.XP3Description; } }
        public override uint Signature { get { return 0x0d335058; } }
        public override bool IsHierarchic { get { return false; } }

        private static readonly ICrypt NoCryptAlgorithm = new NoCrypt();

        public static readonly Dictionary<string, ICrypt> KnownSchemes = new Dictionary<string, ICrypt> {
            { arcStrings.ArcNoEncryption, NoCryptAlgorithm },
            { "Fate/Stay Night",    new FateCrypt() },
            { "Swan Song",          new SwanSongCrypt() },
            { "Cafe Sourire",       new XorCrypt (0xcd) },
            { "Seirei Tenshou",     new SeitenCrypt() },
            { "Okiba ga Nai!",      new OkibaCrypt() },
        };

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "XP3\x0d\x0a\x20\x0a\x1a\x8b\x67\x01"))
                return null;
            long dir_offset = file.View.ReadInt64 (0x0b);
            if (0x17 == dir_offset)
            {
                if (1 != file.View.ReadUInt32 (0x13))
                    return null;
                if (0x80 != file.View.ReadUInt32 (0x17))
                    return null;
                dir_offset = file.View.ReadInt64 (0x20);
            }
            int header_type = file.View.ReadByte (dir_offset);
            if (0 != header_type && 1 != header_type)
                return null;

            Stream header_stream;
            if (0 == header_type) // read unpacked header
            {
                long header_size = file.View.ReadInt64 (dir_offset+1);
                if (header_size > uint.MaxValue)
                    return null;
                header_stream = file.CreateStream (dir_offset+9, (uint)header_size);
            }
            else // read packed header
            {
                long packed_size = file.View.ReadInt64 (dir_offset+1);
                if (packed_size > uint.MaxValue)
                    return null;
                long header_size = file.View.ReadInt64 (dir_offset+9);
                using (var input = file.CreateStream (dir_offset+17, (uint)packed_size))
                    header_stream = ZLibCompressor.DeCompress (input);
            }

            var crypt_algorithm = new Lazy<ICrypt> (QueryCryptAlgorithm);

            var dir = new List<Entry>();
            dir_offset = 0;
            using (var header = new BinaryReader (header_stream, Encoding.Unicode))
            {
                while (-1 != header.PeekChar())
                {
                    uint entry_signature = header.ReadUInt32();
                    if (0x656c6946 != entry_signature) // "File"
                    {
                        break;
                    }
                    long entry_size = header.ReadInt64();
                    dir_offset += 12 + entry_size;
                    var entry = new Xp3Entry();
                    while (entry_size > 0)
                    {
                        uint section = header.ReadUInt32();
                        long section_size = header.ReadInt64();
                        entry_size -= 12;
                        if (section_size > entry_size)
                            break;
                        entry_size -= section_size;
                        long next_section_pos = header.BaseStream.Position + section_size;
                        switch (section)
                        {
                        case 0x6f666e69: // "info"
                            {
                                if (entry.Size != 0 || !string.IsNullOrEmpty (entry.Name))
                                {
                                    goto NextEntry; // ambiguous entry, ignore
                                }
                                entry.IsEncrypted = 0 != header.ReadUInt32();
                                long file_size = header.ReadInt64();
                                long packed_size = header.ReadInt64();
                                if (file_size > uint.MaxValue || packed_size > uint.MaxValue)
                                {
                                    goto NextEntry;
                                }
                                entry.IsPacked     = file_size != packed_size;
                                entry.Size         = (uint)packed_size;
                                entry.UnpackedSize = (uint)file_size;

                                int name_size = header.ReadInt16();
                                if (name_size > 0x100 || name_size <= 0)
                                {
                                    goto NextEntry;
                                }
                                if (entry.IsEncrypted)
                                    entry.Cipher = crypt_algorithm.Value;
                                else
                                    entry.Cipher = NoCryptAlgorithm;

                                char[] name = header.ReadChars (name_size);
                                entry.Name = new string (name);
                                entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name);
                                break;
                            }
                        case 0x6d676573: // "segm"
                            {
                                int segment_count = (int)(section_size / 0x1c);
                                if (segment_count > 0)
                                {
                                    for (int i = 0; i < segment_count; ++i)
                                    {
                                        bool compressed  = 0 != header.ReadInt32();
                                        long segment_offset = header.ReadInt64();
                                        long segment_size   = header.ReadInt64();
                                        long segment_packed_size = header.ReadInt64();
                                        if (segment_offset > file.MaxOffset || segment_size > file.MaxOffset
                                            || segment_packed_size > file.MaxOffset)
                                        {
                                            goto NextEntry;
                                        }
                                        var segment = new Xp3Segment {
                                            IsCompressed = compressed,
                                            Offset       = segment_offset,
                                            Size         = (uint)segment_size,
                                            PackedSize   = (uint)segment_packed_size
                                        };
                                        entry.Segments.Add (segment);
                                    }
                                    entry.Offset = entry.Segments.First().Offset;
                                }
                                break;
                            }
                        case 0x726c6461: // "adlr"
                            if (4 == section_size)
                                entry.Hash = header.ReadUInt32();
                            break;

                        default: // unknown section
                            break;
                        }
                        header.BaseStream.Position = next_section_pos;
                    }
                    if (!string.IsNullOrEmpty (entry.Name) && entry.Segments.Any())
                        dir.Add (entry);
NextEntry:
                    header.BaseStream.Position = dir_offset;
                }
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var xp3_entry = entry as Xp3Entry;
            if (null == xp3_entry || !xp3_entry.Segments.Any())
                return arc.File.CreateStream (entry.Offset, entry.Size);
            if (1 == xp3_entry.Segments.Count && !xp3_entry.IsEncrypted)
            {
                var segment = xp3_entry.Segments.First();
                if (segment.IsCompressed)
                    return new ZLibStream (arc.File.CreateStream (segment.Offset, segment.PackedSize),
                                           CompressionMode.Decompress);
                else
                    return arc.File.CreateStream (segment.Offset, segment.Size);
            }
            return new Xp3Stream (arc.File, xp3_entry);
        }

        string m_scheme = GetDefaultScheme();

        ICrypt QueryCryptAlgorithm ()
        {
            var widget = new GUI.WidgetXP3 (m_scheme);
            var args = new ParametersRequestEventArgs
            {
                Notice = arcStrings.ArcEncryptedNotice,
                InputWidget = widget,
            };
            FormatCatalog.Instance.InvokeParametersRequest (this, args);
            if (!args.InputResult)
                throw new OperationCanceledException();

            m_scheme = widget.GetScheme();
            if (null != m_scheme)
            {
                ICrypt algorithm;
                if (KnownSchemes.TryGetValue (m_scheme, out algorithm))
                {
                    Settings.Default.XP3Scheme = m_scheme;
                    return algorithm;
                }
            }
            return NoCryptAlgorithm;
        }

        public static string GetDefaultScheme ()
        {
            string scheme = Settings.Default.XP3Scheme;
            if (!string.IsNullOrEmpty (scheme) && KnownSchemes.ContainsKey (scheme))
                return scheme;
            else
                return arcStrings.ArcNoEncryption;
        }

        static uint GetFileCheckSum (Stream src)
        {
            // compute file checksum via adler32.
            // src's file pointer should be reset to zero.
            var sum = new Adler32();
            byte[] buf = new byte[64*1024];
            for (;;)
            {
                int read = src.Read (buf, 0, buf.Length);
                if (0 == read) break;
                sum.Update (buf, 0, read);
            }
            return sum.Value;
        }
    }

    public class Xp3Stream : Stream
    {
        ArcView     m_file;
        Xp3Entry    m_entry;
        IEnumerator<Xp3Segment> m_segment;
        Stream      m_stream;
        long        m_offset = 0;
        bool        m_eof = false;

        public override bool CanRead  { get { return true; } }
        public override bool CanSeek  { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length   { get { return m_entry.UnpackedSize; } }
        public override long Position
        {
            get { return m_offset; }
            set { throw new NotSupportedException ("Xp3Stream.Position not supported."); }
        }

        public Xp3Stream (ArcView file, Xp3Entry entry)
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
            if (segment.IsCompressed)
                m_stream = new ZLibStream (m_file.CreateStream (segment.Offset, segment.PackedSize),
                                           CompressionMode.Decompress);
            else
                m_stream = m_file.CreateStream (segment.Offset, segment.Size);
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (!m_eof && count > 0)
            {
                int read = m_stream.Read (buffer, offset, count);
                m_entry.Cipher.Decrypt (m_entry, m_offset, buffer, offset, read);
                m_offset += read;
                total += read;
                offset += read;
                count -= read;
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
                {
                    b = m_entry.Cipher.Decrypt (m_entry, m_offset++, (byte)b);
                    break;
                }
                NextSegment();
            }
            return b;
        }

        public override void Flush ()
        {
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            throw new NotSupportedException ("Xp3Stream.Seek method is not supported");
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("Xp3Stream.SetLength method is not supported");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("Xp3Stream.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new NotSupportedException("Xp3Stream.WriteByte method is not supported");
        }

        #region IDisposable Members
        bool disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!disposed)
            {
                m_file = null;
                if (null != m_stream)
                {
                    m_stream.Dispose();
                    m_stream = null;
                }
                disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }

    public abstract class ICrypt
    {
        public abstract byte Decrypt (Xp3Entry entry, long offset, byte value);

        public virtual void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            for (int i = 0; i < count; ++i)
                values[pos+i] = this.Decrypt (entry, offset+i, values[pos+i]);
        }

        public virtual void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            throw new NotImplementedException ("Encryption method not implemented");
        }
    }

    public class NoCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            return value;
        }
        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            return;
        }
        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            return;
        }
    }

    internal class FateCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            byte result = (byte)(value ^ 0x36);
            if (0x13 == offset)
                result ^= 1;
            else if (0x2ea29 == offset)
                result ^= 3;
            return result;
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] ^= 0x36;
            }
            if (offset > 0x2ea29)
                return;
            if (offset + count > 0x2ea29)
                values[pos+0x2ea29-offset] ^= 3;
            if (offset > 0x13)
                return;
            if (offset + count > 0x13)
                values[pos+0x13-offset] ^= 1;
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }

    internal class XorCrypt : ICrypt
    {
        private byte m_key;

        public byte Key
        {
            get { return m_key; }
            set { m_key = value; }
        }

        public XorCrypt (uint key)
        {
            m_key = (byte)key;
        }

        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            return (byte)(value ^ m_key);
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] ^= m_key;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }

    internal class SwanSongCrypt : ICrypt
    {
        static private byte Adjust (uint hash, out int shift)
        {
            int cl = (int)(hash & 0xff);
            if (0 == cl) cl = 0x0f;
            shift = cl & 7;
            int ch = (int)((hash >> 8) & 0xff);
            if (0 == ch) ch = 0xf0;
            return (byte)ch;
        }

        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            int shift;
            byte xor = Adjust (entry.Hash, out shift);
            uint data = (uint)(value ^ xor);
            return (byte)((data >> shift) | (data << (8 - shift)));
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            int shift;
            byte xor = Adjust (entry.Hash, out shift);
            for (int i = 0; i < count; ++i)
            {
                uint data = (uint)(values[pos+i] ^ xor);
                values[pos+i] = (byte)((data >> shift) | (data << (8 - shift)));
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            int shift;
            byte xor = Adjust (entry.Hash, out shift);
            for (int i = 0; i < count; ++i)
            {
                uint data = values[pos+i];
                data = (byte)((data << shift) | (data >> (8 - shift)));
                values[pos+i] = (byte)(data ^ xor);
            }
        }
    }

    internal class SeitenCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            uint key = entry.Hash ^ (uint)offset;
            if (0 != (key & 2))
            {
                int ecx = (int)key & 0x18;
                value ^= (byte)((key >> ecx) | (key >> (ecx & 8)));
            }
            if (0 != (key & 4))
            {
                value += (byte)key;
            }
            if (0 != (key & 8))
            {
                value -= (byte)(key >> (int)(key & 0x10));
            }
            return value;
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                uint key = entry.Hash ^ (uint)offset;
                if (0 != (key & 8))
                {
                    values[pos+i] += (byte)(key >> (int)(key & 0x10));
                }
                if (0 != (key & 4))
                {
                    values[pos+i] -= (byte)key;
                }
                if (0 != (key & 2))
                {
                    int ecx = (int)key & 0x18;
                    values[pos+i] ^= (byte)((key >> ecx) | (key >> (ecx & 8)));
                }
            }
        }
    }

    internal class OkibaCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            if (offset < 0x65)
                return (byte)(value ^ (byte)(entry.Hash >> 4));
            uint key = entry.Hash;
            // 0,1,2,3 -> 1,0,3,2
            key = ((key & 0xff0000) << 8) | ((key & 0xff000000) >> 8)
                | ((key & 0xff00) >> 8)   | ((key & 0xff) << 8);
            key >>= 8 * ((int)(offset - 0x65) & 3);
            return (byte)(value ^ (byte)key);
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            int i = 0;
            if (offset < 0x65)
            {
                uint key = entry.Hash >> 4;
                int limit = Math.Min (count, (int)(0x65 - offset));
                for (; i < limit; ++i)
                {
                    values[pos+i] ^= (byte)key;
                    ++offset;
                }
            }
            if (i < count)
            {
                offset -= 0x65;
                uint key = entry.Hash;
                key = ((key & 0xff0000) << 8) | ((key & 0xff000000) >> 8)
                    | ((key & 0xff00) >> 8)   | ((key & 0xff) << 8);
                do
                {
                    values[pos+i] ^= (byte)(key >> (8 * ((int)offset & 3)));
                    ++offset;
                }
                while (++i < count);
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }
}
