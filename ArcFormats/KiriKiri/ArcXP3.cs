//! \file       ArcXP3.cs
//! \date       Wed Jul 16 13:58:17 2014
//! \brief      KiriKiri engine archive implementation.
//
// Copyright (C) 2014-2016 by morkt
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
                                entry.IsEncrypted = !(entry.Cipher is NoCrypt);

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
                                // TODO: search every byte only when inside resource section,
                                // otherwise stick to paragraph boundary.
                                int i = 0;
                                while (ptr[i] == s_xp3_header[i])
                                {
                                    if (++i == s_xp3_header.Length)
                                    {
                                        // check whether index offset is non-zero
                                        if (0 == *(uint*)(ptr+i))
                                            break;
                                        return offset + (ptr - page_begin);
                                    }
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

            Stream input;
            if (1 == xp3_entry.Segments.Count && !xp3_entry.IsEncrypted)
            {
                var segment = xp3_entry.Segments.First();
                if (segment.IsCompressed)
                    input = new ZLibStream (arc.File.CreateStream (segment.Offset, segment.PackedSize),
                                           CompressionMode.Decompress);
                else
                    input = arc.File.CreateStream (segment.Offset, segment.Size);
            }
            else
                input = new Xp3Stream (arc.File, xp3_entry);

            if (entry.Size <= 5 || "image" == entry.Type || "audio" == entry.Type)
                return input;

            var header = new byte[5];
            input.Read (header, 0, 5);
            if (0xFE == header[0] && 0xFE == header[1] && header[2] < 3 && 0xFF == header[3] && 0xFE == header[4])
                return DecryptScript (header[2], input, xp3_entry.UnpackedSize);

            if (!input.CanSeek)
                return new PrefixStream (header, input);
            input.Position = 0;
            return input;
        }

        Stream DecryptScript (int enc_type, Stream input, uint unpacked_size)
        {
            using (var reader = new BinaryReader (input, Encoding.Unicode, true))
            {
                if (2 == enc_type)
                {
                    reader.ReadInt64(); // packed_size
                    reader.ReadInt64(); // unpacked_size
                    return new ZLibStream (input, CompressionMode.Decompress);
                }
                var output = new MemoryStream ((int)unpacked_size+2);
                using (var writer = new BinaryWriter (output, Encoding.Unicode, true))
                {
                    writer.Write ('\xFEFF'); // BOM
                    int c;
                    if (1 == enc_type)
                    {
                        while ((c = reader.Read()) != -1)
                        {
                            c = (c & 0xAAAA) >> 1 | (c & 0x5555) << 1;
                            writer.Write ((char)c);
                        }
                    }
                    else
                    {
                        while ((c = reader.Read()) != -1)
                        {
                            if (c >= 0x20)
                            {
                                c = c ^ (((c & 0xFE) << 8) ^ 1);
                                writer.Write ((char)c);
                            }
                        }
                    }
                }
                output.Position = 0;
                input.Dispose();
                return output;
            }
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
