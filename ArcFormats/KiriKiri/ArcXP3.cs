//! \file       ArcXP3.cs
//! \date       Wed Jul 16 13:58:17 2014
//! \brief      KiriKiri engine archive implementation.
//
// Copyright (C) 2014-2015 by morkt
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
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using GameRes.Compression;
using GameRes.Utility;
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
        public List<Xp3Segment> Segments { get { return m_segments; } }
        public uint Hash { get; set; }
    }

    public class Xp3Options : ResourceOptions
    {
        public int              Version { get; set; }
        public ICrypt            Scheme { get; set; }
        public bool       CompressIndex { get; set; }
        public bool    CompressContents { get; set; }
        public bool          RetainDirs { get; set; }
    }

    [Serializable]
    public class Xp3Scheme : ResourceScheme
    {
        public Dictionary<string, ICrypt> KnownSchemes;
    }

    // Archive version 1: encrypt file first, then calculate checksum
    //         version 2: calculate checksum, then encrypt

    [Export(typeof(ArchiveFormat))]
    public class Xp3Opener : ArchiveFormat
    {
        public override string         Tag { get { return "XP3"; } }
        public override string Description { get { return arcStrings.XP3Description; } }
        public override uint     Signature { get { return 0x0d335058; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return true; } }

        public Xp3Opener ()
        {
            Signatures = new uint[] { 0x0d335058, 0 };
        }
        
        static readonly string SignatureBytes = "XP3\x0d\x0a\x20\x0a\x1a\x8b\x67\x01";

        static readonly byte[] s_xp3_header = {
            (byte)'X', (byte)'P', (byte)'3', 0x0d, 0x0a, 0x20, 0x0a, 0x1a, 0x8b, 0x67, 0x01
        };

        public override ResourceScheme Scheme
        {
            get { return new Xp3Scheme { KnownSchemes = KnownSchemes }; }
            set { KnownSchemes = ((Xp3Scheme)value).KnownSchemes; }
        }

        public bool ForceEncryptionQuery = true;

        private static readonly ICrypt NoCryptAlgorithm = new NoCrypt();

        public static Dictionary<string, ICrypt> KnownSchemes = new Dictionary<string, ICrypt>();

        public override ArcFile TryOpen (ArcView file)
        {
            long base_offset = 0;
            if (0x5a4d == file.View.ReadUInt16 (0)) // 'MZ'
                base_offset = SkipExeHeader (file);
            if (!file.View.AsciiEqual (base_offset, SignatureBytes))
                return null;
            long dir_offset = base_offset + file.View.ReadInt64 (base_offset+0x0b);
            if (dir_offset < 0x13 || dir_offset >= file.MaxOffset)
                return null;
            if (0x80 == file.View.ReadUInt32 (dir_offset))
            {
                dir_offset = base_offset + file.View.ReadInt64 (dir_offset+9);
                if (dir_offset < 0x13 || dir_offset >= file.MaxOffset)
                    return null;
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
                                if (file_size >= uint.MaxValue || packed_size > uint.MaxValue || packed_size > file.MaxOffset)
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
                                if (entry.IsEncrypted || ForceEncryptionQuery)
                                    entry.Cipher = crypt_algorithm.Value;
                                else
                                    entry.Cipher = NoCryptAlgorithm;
                                entry.IsEncrypted = entry.Cipher != NoCryptAlgorithm;

                                var name = new string (header.ReadChars (name_size));
                                if (entry.Cipher.ObfuscatedIndex && ObfuscatedPathRe.IsMatch (name))
                                {
                                    goto NextEntry;
                                }
                                entry.Name = name;
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
                                        long segment_offset = base_offset+header.ReadInt64();
                                        long segment_size   = header.ReadInt64();
                                        long segment_packed_size = header.ReadInt64();
                                        if (segment_offset > file.MaxOffset || segment_packed_size > file.MaxOffset)
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
                    {
                        if (entry.Cipher.ObfuscatedIndex)
                        {
                            DeobfuscateEntry (entry);
                        }
                        dir.Add (entry);
                    }
NextEntry:
                    header.BaseStream.Position = dir_offset;
                }
            }
            return new ArcFile (file, this, dir);
        }

        static readonly Regex ObfuscatedPathRe = new Regex (@"[^\\/]+[\\/]\.\.[\\/]");

        private static void DeobfuscateEntry (Xp3Entry entry)
        {
            if (entry.Segments.Count > 1)
                entry.Segments.RemoveRange (1, entry.Segments.Count-1);
            entry.IsPacked = entry.Segments[0].IsCompressed;
            entry.Size = entry.Segments[0].PackedSize;
            entry.UnpackedSize = entry.Segments[0].Size;
        }

        private long SkipExeHeader (ArcView file)
        {
            long offset = 0x10;
            long pe_offset = file.View.ReadUInt32 (0x3c);
            if (pe_offset < file.MaxOffset && 0x4550 == file.View.ReadUInt32 (pe_offset)) // 'PE'
            {
                int opt_header = file.View.ReadUInt16 (pe_offset+0x14); // SizeOfOptionalHeader
                offset = file.View.ReadUInt32 (pe_offset+0x54); // SizeOfHeaders
                long section_table = pe_offset+opt_header+0x18;
                int count = file.View.ReadUInt16 (pe_offset+6); // NumberOfSections
                if (section_table + 0x28*count < file.MaxOffset)
                {
                    for (int i = 0; i < count; ++i)
                    {
                        uint size = file.View.ReadUInt32 (section_table+0x10);
                        uint addr = file.View.ReadUInt32 (section_table+0x14);
                        if (file.View.AsciiEqual (section_table, ".rsrc\0"))
                        {
                            // look within EXE resource section
                            offset = addr;
                            break;
                        }
                        section_table += 0x28;
                        if (0 != size)
                            offset = Math.Max ((long)addr + size, offset);
                    }
                }
            }
            unsafe
            {
                while (offset < file.MaxOffset)
                {
                    uint page_size = (uint)Math.Min (0x10000L, file.MaxOffset - offset);
                    if (page_size < 0x20)
                        break;
                    using (var view = file.CreateViewAccessor (offset, page_size))
                    {
                        byte* page_begin = view.GetPointer (offset);
                        byte* page_end   = page_begin + page_size - 0x10;
                        try {
                            for (byte* ptr = page_begin; ptr != page_end; ++ptr)
                            {
                                int i = 0;
                                while (ptr[i] == s_xp3_header[i])
                                {
                                    if (++i == s_xp3_header.Length)
                                        return offset + (ptr - page_begin);
                                }
                            }
                        }
                        finally {
                            view.SafeMemoryMappedViewHandle.ReleasePointer();
                        }
                    }
                    offset += page_size - 0x10;
                }
            }
            return 0;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var xp3_entry = entry as Xp3Entry;
            if (null == xp3_entry)
                return arc.File.CreateStream (entry.Offset, entry.Size);
//            Trace.WriteLine (string.Format ("{0,-16} {3:X8} {1,11} {2,12}", xp3_entry.Name,
//                                            xp3_entry.IsEncrypted ? "[encrypted]" : "",
//                                            xp3_entry.Segments.First().IsCompressed ? "[compressed]" : "",
//                                            xp3_entry.Hash));
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

        public override ResourceOptions GetDefaultOptions ()
        {
            return new Xp3Options {
                Version             = Settings.Default.XP3Version,
                Scheme              = GetScheme (Settings.Default.XP3Scheme),
                CompressIndex       = Settings.Default.XP3CompressHeader,
                CompressContents    = Settings.Default.XP3CompressContents,
                RetainDirs          = Settings.Default.XP3RetainStructure,
            };
        }

        public override object GetCreationWidget ()
        {
            return new GUI.CreateXP3Widget();
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetXP3();
        }

        ICrypt QueryCryptAlgorithm ()
        {
            var options = Query<Xp3Options> (arcStrings.ArcEncryptedNotice);
            return options.Scheme;
        }

        public static ICrypt GetScheme (string scheme)
        {
            ICrypt algorithm;
            if (string.IsNullOrEmpty (scheme) || !KnownSchemes.TryGetValue (scheme, out algorithm))
                algorithm = NoCryptAlgorithm;
            return algorithm;
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

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            var xp3_options = GetOptions<Xp3Options> (options);

            ICrypt scheme = xp3_options.Scheme;
            bool compress_index = xp3_options.CompressIndex;
            bool compress_contents = xp3_options.CompressContents;
            bool retain_dirs = xp3_options.RetainDirs;

            bool use_encryption = scheme != NoCryptAlgorithm;

            using (var writer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                writer.Write (s_xp3_header);
                if (2 == xp3_options.Version)
                {
                    writer.Write ((long)0x17);
                    writer.Write ((int)1);
                    writer.Write ((byte)0x80);
                    writer.Write ((long)0);
                }
                long index_pos_offset = writer.BaseStream.Position;
                writer.BaseStream.Seek (8, SeekOrigin.Current);

                int callback_count = 0;
                var used_names = new HashSet<string>();
                var dir = new List<Xp3Entry>();
                long current_offset = writer.BaseStream.Position;
                foreach (var entry in list)
                {
                    if (null != callback)
                        callback (callback_count++, entry, arcStrings.MsgAddingFile);

                    string name = entry.Name;
                    if (!retain_dirs)
                        name = Path.GetFileName (name);
                    else
                        name = name.Replace (@"\", "/");
                    if (!used_names.Add (name))
                    {
                        Trace.WriteLine ("duplicate name", entry.Name);
                        continue;
                    }

                    var xp3entry = new Xp3Entry {
                        Name            = name,
                        IsEncrypted     = use_encryption,
                        Cipher          = scheme,
                    };
                    bool compress = compress_contents && ShouldCompressFile (entry);
                    using (var file = File.Open (name, FileMode.Open, FileAccess.Read))
                    {
                        if (!use_encryption || 0 == file.Length)
                            RawFileCopy (file, xp3entry, output, compress);
                        else
                            EncryptedFileCopy (file, xp3entry, output, compress);
                    }

                    dir.Add (xp3entry);
                }

                long index_pos = writer.BaseStream.Position;
                writer.BaseStream.Position = index_pos_offset;
                writer.Write (index_pos);
                writer.BaseStream.Position = index_pos;

                using (var header = new BinaryWriter (new MemoryStream (dir.Count*0x58), Encoding.Unicode))
                {
                    if (null != callback)
                        callback (callback_count++, null, arcStrings.MsgWritingIndex);

                    long dir_pos = 0;
                    foreach (var entry in dir)
                    {
                        header.BaseStream.Position = dir_pos;
                        header.Write ((uint)0x656c6946); // "File"
                        long header_size_pos = header.BaseStream.Position;
                        header.Write ((long)0);
                        header.Write ((uint)0x6f666e69); // "info"
                        header.Write ((long)(4+8+8+2 + entry.Name.Length*2));
                        header.Write ((uint)(use_encryption ? 0x80000000 : 0));
                        header.Write ((long)entry.UnpackedSize);
                        header.Write ((long)entry.Size);

                        header.Write ((short)entry.Name.Length);
                        foreach (char c in entry.Name)
                            header.Write (c);

                        header.Write ((uint)0x6d676573); // "segm"
                        header.Write ((long)0x1c);
                        var segment = entry.Segments.First();
                        header.Write ((int)(segment.IsCompressed ? 1 : 0));
                        header.Write ((long)segment.Offset);
                        header.Write ((long)segment.Size);
                        header.Write ((long)segment.PackedSize);

                        header.Write ((uint)0x726c6461); // "adlr"
                        header.Write ((long)4);
                        header.Write ((uint)entry.Hash);

                        dir_pos = header.BaseStream.Position;
                        long header_size = dir_pos - header_size_pos - 8;
                        header.BaseStream.Position = header_size_pos;
                        header.Write (header_size);
                    }

                    header.BaseStream.Position = 0;
                    writer.Write (compress_index);
                    long unpacked_dir_size = header.BaseStream.Length;
                    if (compress_index)
                    {
                        if (null != callback)
                            callback (callback_count++, null, arcStrings.MsgCompressingIndex);

                        long packed_dir_size_pos = writer.BaseStream.Position;
                        writer.Write ((long)0);
                        writer.Write (unpacked_dir_size);

                        long dir_start = writer.BaseStream.Position;
                        using (var zstream = new ZLibStream (writer.BaseStream, CompressionMode.Compress,
                                                             CompressionLevel.Level9, true))
                            header.BaseStream.CopyTo (zstream);

                        long packed_dir_size = writer.BaseStream.Position - dir_start;
                        writer.BaseStream.Position = packed_dir_size_pos;
                        writer.Write (packed_dir_size);
                    }
                    else
                    {
                        writer.Write (unpacked_dir_size);
                        header.BaseStream.CopyTo (writer.BaseStream);
                    }
                }
            }
            output.Seek (0, SeekOrigin.End);
        }

        void RawFileCopy (FileStream file, Xp3Entry xp3entry, Stream output, bool compress)
        {
            if (file.Length > uint.MaxValue)
                throw new FileSizeException();

            uint unpacked_size    = (uint)file.Length;
            xp3entry.UnpackedSize = (uint)unpacked_size;
            xp3entry.Size         = (uint)unpacked_size;
            compress = compress && unpacked_size > 0;
            var segment = new Xp3Segment {
                IsCompressed = compress,
                Offset       = output.Position,
                Size         = unpacked_size,
                PackedSize   = unpacked_size
            };
            if (compress)
            {
                var start = output.Position;
                using (var zstream = new ZLibStream (output, CompressionMode.Compress, CompressionLevel.Level9, true))
                {
                    xp3entry.Hash = CheckedCopy (file, zstream);
                }
                segment.PackedSize = (uint)(output.Position - start);
                xp3entry.Size = segment.PackedSize;
            }
            else
            {
                xp3entry.Hash = CheckedCopy (file, output);
            }
            xp3entry.Segments.Add (segment);
        }

        void EncryptedFileCopy (FileStream file, Xp3Entry xp3entry, Stream output, bool compress)
        {
            if (file.Length > int.MaxValue)
                throw new FileSizeException();

            using (var map = MemoryMappedFile.CreateFromFile (file, null, 0,
                    MemoryMappedFileAccess.Read, null, HandleInheritability.None, true))
            {
                uint unpacked_size    = (uint)file.Length;
                xp3entry.UnpackedSize = (uint)unpacked_size;
                xp3entry.Size         = (uint)unpacked_size;
                using (var view = map.CreateViewAccessor (0, unpacked_size, MemoryMappedFileAccess.Read))
                {
                    var segment = new Xp3Segment {
                        IsCompressed = compress,
                        Offset       = output.Position,
                        Size         = unpacked_size,
                        PackedSize   = unpacked_size,
                    };
                    xp3entry.Segments.Add (segment);
                    if (compress)
                    {
                        output = new ZLibStream (output, CompressionMode.Compress, CompressionLevel.Level9, true);
                    }
                    unsafe
                    {
                        byte[] read_buffer = new byte[81920];
                        byte* ptr = view.GetPointer (0);
                        try
                        {
                            var checksum = new Adler32();
                            bool hash_after_crypt = xp3entry.Cipher.HashAfterCrypt;
                            if (!hash_after_crypt)
                                xp3entry.Hash = checksum.Update (ptr, (int)unpacked_size);
                            int offset = 0;
                            int remaining = (int)unpacked_size;
                            while (remaining > 0)
                            {
                                int amount = Math.Min (remaining, read_buffer.Length);
                                remaining -= amount;
                                Marshal.Copy ((IntPtr)(ptr+offset), read_buffer, 0, amount);
                                xp3entry.Cipher.Encrypt (xp3entry, offset, read_buffer, 0, amount);
                                if (hash_after_crypt)
                                    checksum.Update (read_buffer, 0, amount);
                                output.Write (read_buffer, 0, amount);
                                offset += amount;
                            }
                            if (hash_after_crypt)
                                xp3entry.Hash = checksum.Value;
                        }
                        finally
                        {
                            view.SafeMemoryMappedViewHandle.ReleasePointer();
                            if (compress)
                            {
                                var dest = (output as ZLibStream).BaseStream;
                                output.Dispose();
                                segment.PackedSize = (uint)(dest.Position - segment.Offset);
                                xp3entry.Size = segment.PackedSize;
                            }
                        }
                    }
                }
            }
        }

        uint CheckedCopy (Stream src, Stream dst)
        {
            var checksum = new Adler32();
            var read_buffer = new byte[81920];
            for (;;)
            {
                int read = src.Read (read_buffer, 0, read_buffer.Length);
                if (0 == read)
                    break;
                checksum.Update (read_buffer, 0, read);
                dst.Write (read_buffer, 0, read);
            }
            return checksum.Value;
        }

        bool ShouldCompressFile (Entry entry)
        {
            if ("image" == entry.Type || "archive" == entry.Type)
                return false;
            if (entry.Name.EndsWith (".ogg", StringComparison.InvariantCultureIgnoreCase))
                return false;
            return true;
        }
    }

    internal class Xp3Stream : Stream
    {
        ArcView     m_file;
        Xp3Entry    m_entry;
        IEnumerator<Xp3Segment> m_segment;
        Stream      m_stream;
        long        m_offset = 0;
        bool        m_eof = false;

        public override bool CanRead  { get { return m_stream != null; } }
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
            var segment_size = segment.IsCompressed ? segment.PackedSize : segment.Size;
            m_stream = m_file.CreateStream (segment.Offset, segment_size);
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
                    m_entry.Cipher.Decrypt (m_entry, m_offset, buffer, offset, read);
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
                if (disposing && null != m_stream)
                {
                    m_stream.Dispose();
                }
                disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }

    [Serializable]
    public abstract class ICrypt
    {
        /// <summary>
        /// whether Adler32 checksum should be calculated after contents have been encrypted.
        /// </summary>
        public virtual bool HashAfterCrypt { get { return false; } }

        /// <summary>
        /// whether XP3 index is obfuscated:
        ///  - duplicate entries
        ///  - entries have additional dummy segments
        /// </summary>
        public virtual bool ObfuscatedIndex { get { return false; } }

        public virtual byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            byte[] buffer = new byte[1] { value };
            Decrypt (entry, offset, buffer, 0, 1);
            return buffer[0];
        }

        public abstract void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count);

        public virtual void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            throw new NotImplementedException (arcStrings.MsgEncNotImplemented);
        }
    }

    [Serializable]
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

    [Serializable]
    public class FateCrypt : ICrypt
    {
        public override bool HashAfterCrypt { get { return true; } }

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

    [Serializable]
    public class HashCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            return (byte)(value ^ entry.Hash);
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            byte key = (byte)entry.Hash;
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] ^= key;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }

    [Serializable]
    public class XorCrypt : ICrypt
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

    [Serializable]
    public class SwanSongCrypt : ICrypt
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

    [Serializable]
    public class SeitenCrypt : ICrypt
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

        public override void Decrypt (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                int shift;
                uint key = entry.Hash ^ (uint)offset;
                byte v = buffer[pos+i];
                if (0 != (key & 2))
                {
                    shift = (int)key & 0x18;
                    uint ebx = key >> shift;
                    shift &= 8;
                    v ^= (byte)(ebx | (key >> shift));
                }
                if (0 != (key & 4))
                {
                    v += (byte)key;
                }
                if (0 != (key & 8))
                {
                    shift = (int)key & 0x10;
                    v -= (byte)(key >> shift);
                }
                buffer[pos+i] = v;
                ++offset;
            }
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

    [Serializable]
    public class OkibaCrypt : ICrypt
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

    [Serializable]
    public class SaiminCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            byte key = (byte)entry.Hash;
            if (offset < 0x7B)
                value ^= (byte)(21 * key);
            else if (offset < 0xF6)
                value += (byte)(-32 * key);
            else if (offset < 0x171)
                value ^= (byte)(43 * key);
            else if (offset <= 0xffffffffL)
                value += (byte)(-54 * key);
            return value;
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            byte key = (byte)entry.Hash;
            for (int i = 0; i < count && offset <= 0xffffffffL; ++i, ++offset)
            {
                if (offset < 0x7B)
                    values[pos+i] ^= (byte)(21 * key);
                else if (offset < 0xF6)
                    values[pos+i] += (byte)(-32 * key);
                else if (offset >= 0x171)
                    values[pos+i] += (byte)(-54 * key);
                else
                    values[pos+i] ^= (byte)(43 * key);
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            byte key = (byte)entry.Hash;
            for (int i = 0; i < count && offset <= 0xffffffffL; ++i, ++offset)
            {
                if (offset < 0x7B)
                    values[pos+i] ^= (byte)(21 * key);
                else if (offset < 0xF6)
                    values[pos+i] -= (byte)(-32 * key);
                else if (offset >= 0x171)
                    values[pos+i] -= (byte)(-54 * key);
                else
                    values[pos+i] ^= (byte)(43 * key);
            }
        }
    }

    [Serializable]
    public class DameganeCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            if (0 != (offset & 1))
                return (byte)(value ^ entry.Hash);
            else
                return (byte)(value ^ offset);
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            for (int i = 0; i < count; ++i, ++offset)
            {
                if (0 != (offset & 1))
                    values[pos+i] ^= (byte)entry.Hash;
                else
                    values[pos+i] ^= (byte)offset;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }

    [Serializable]
    public class AlteredPinkCrypt : ICrypt
    {
        static readonly byte[] KeyTable = {
            0x43, 0xF8, 0xAD, 0x08, 0xDF, 0xB7, 0x26, 0x44, 0xF0, 0xD9, 0xE9, 0x24, 0x1A, 0xC1, 0xEE, 0xB4,
            0x11, 0x4B, 0xE4, 0xAF, 0x01, 0x5B, 0xF0, 0xAB, 0x6A, 0x70, 0x78, 0x84, 0xB0, 0x78, 0x4F, 0xED,
            0x39, 0x52, 0x69, 0xAF, 0xC4, 0x92, 0x2A, 0x21, 0xDE, 0xDC, 0x6E, 0x63, 0x9D, 0x9B, 0x63, 0xE1,
            0xB1, 0x94, 0x40, 0x6E, 0x3A, 0x52, 0x5A, 0x28, 0x08, 0x4D, 0xFB, 0x22, 0x18, 0xEB, 0xBA, 0x98,
            0x49, 0x77, 0xBF, 0xAA, 0x43, 0x75, 0xF5, 0xD3, 0x83, 0x71, 0x58, 0xA4, 0xAF, 0x1B, 0x53, 0x99,
            0x8A, 0x27, 0x5B, 0xC2, 0x7F, 0x7A, 0xCD, 0x8D, 0x33, 0x59, 0xEB, 0xA6, 0xFA, 0x7C, 0x00, 0x19,
            0xC4, 0xAA, 0x24, 0xF8, 0x84, 0xCD, 0xF7, 0x20, 0x4B, 0xAB, 0xF1, 0xD5, 0x01, 0x6F, 0x7C, 0x91,
            0x08, 0x7D, 0x8D, 0x89, 0x7C, 0x71, 0x65, 0x99, 0x9B, 0x6F, 0x3A, 0x1C, 0x49, 0xE3, 0xAF, 0x1F,
            0xC6, 0xA5, 0x79, 0xFE, 0xAE, 0xA1, 0xCA, 0x59, 0x3C, 0xEE, 0xC1, 0x02, 0xBD, 0x2B, 0x8E, 0xC5,
            0x7D, 0x38, 0x80, 0x8F, 0x72, 0xF3, 0x86, 0x5D, 0xF4, 0x20, 0x0A, 0x5B, 0xA0, 0xE3, 0x85, 0xB5,
            0x67, 0x43, 0x96, 0xBB, 0x75, 0x86, 0x8D, 0x7E, 0x7E, 0xE6, 0xAA, 0x18, 0x57, 0xC4, 0xAA, 0x87,
            0xDC, 0x74, 0x05, 0xAA, 0xBD, 0x5E, 0x4F, 0xA9, 0xB5, 0x5E, 0xC5, 0xE8, 0x11, 0x6D, 0x68, 0x89,
            0x17, 0x7C, 0x10, 0x05, 0xA2, 0xBA, 0x43, 0x01, 0xD6, 0xFD, 0x26, 0x19, 0x57, 0xFA, 0x4D, 0x01,
            0xB0, 0xED, 0x3A, 0x55, 0xEB, 0x65, 0x8E, 0xD1, 0x58, 0x27, 0xAD, 0xA1, 0x5E, 0x57, 0x3F, 0xA0,
            0xEF, 0x59, 0x3E, 0xA4, 0xEB, 0x12, 0x15, 0x60, 0xBE, 0x95, 0x61, 0x0B, 0x98, 0xF5, 0xF4, 0x12,
            0x1C, 0xD8, 0x62, 0x3F, 0xFD, 0xCF, 0x01, 0x3A, 0xE7, 0xC2, 0x19, 0x38, 0x6C, 0xC3, 0x90, 0x3E,
        };

        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            return (byte)(value ^ KeyTable[offset & 0xFF]);
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] ^= KeyTable[(offset+i) & 0xFF];
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }

    [Serializable]
    public class NatsupochiCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            return (byte)(value ^ (entry.Hash >> 3));
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            byte key = (byte)(entry.Hash >> 3);
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] ^= key;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }
}
