//! \file       ArcXP3.cs
//! \date       Wed Jul 16 13:58:17 2014
//! \brief      KiriKiri engine archive implementation.
//
// Copyright (C) 2014-2017 by morkt
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
using System.Security.Cryptography;
using System.Diagnostics;
using GameRes.Compression;
using GameRes.Utility;
using GameRes.Formats.Strings;

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

        public bool          IsEncrypted { get; set; }
        public ICrypt             Cipher { get; set; }
        public List<Xp3Segment> Segments { get { return m_segments; } }
        public uint                 Hash { get; set; }
        public object              Extra { get; set; }
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
        public IDictionary<string, ICrypt>  KnownSchemes;
        public ISet<string>                 NoCryptTitles;
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
        public override bool      CanWrite { get { return true; } }

        public Xp3Opener ()
        {
            Signatures = new uint[] { 0x0d335058, 0x00905A4D, 0 };
            Extensions = new[] { "xp3", "exe" };
            ContainedFormats = new[] { "TLG", "BMP", "PNG", "JPEG", "OGG", "WAV", "TXT" };
        }
        
        static readonly byte[] s_xp3_header = {
            (byte)'X', (byte)'P', (byte)'3', 0x0d, 0x0a, 0x20, 0x0a, 0x1a, 0x8b, 0x67, 0x01
        };

        public bool ForceEncryptionQuery = true;

        internal static readonly ICrypt NoCryptAlgorithm = new NoCrypt();

        public override ArcFile TryOpen (ArcView file)
        {
            long base_offset = 0;
            if (0x5a4d == file.View.ReadUInt16 (0)) // 'MZ'
                base_offset = SkipExeHeader (file, s_xp3_header);
            if (!file.View.BytesEqual (base_offset, s_xp3_header))
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

            var crypt_algorithm = new Lazy<ICrypt> (() => QueryCryptAlgorithm (file), false);

            var dir = new List<Entry>();
            dir_offset = 0;
            using (var header = new BinaryReader (header_stream, Encoding.Unicode))
            using (var filename_map = new FilenameMap())
            {
                Dictionary<string, HxEntry> hx_entry_info = null;
                while (-1 != header.PeekChar())
                {
                    uint entry_signature = header.ReadUInt32();
                    long entry_size = header.ReadInt64();
                    if (entry_size < 0)
                        return null;
                    dir_offset += 12 + entry_size;
                    if (0x656C6946 == entry_signature) // "File"
                    {
                        var entry = new Xp3Entry();
                        while (entry_size > 0)
                        {
                            uint section = header.ReadUInt32();
                            long section_size = header.ReadInt64();
                            entry_size -= 12;
                            if (section_size > entry_size)
                            {
                                // allow "info" sections with wrong size
                                if (section != 0x6f666e69)
                                    break;
                                section_size = entry_size;
                            }
                            entry_size -= section_size;
                            long next_section_pos = header.BaseStream.Position + section_size;
                            switch (section)
                            {
                            case 0x6f666e69: // "info"
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

                                if (entry.IsEncrypted || ForceEncryptionQuery)
                                    entry.Cipher = crypt_algorithm.Value;
                                else
                                    entry.Cipher = NoCryptAlgorithm;

                                var name = entry.Cipher.ReadName (header);
                                if (null == name)
                                {
                                    goto NextEntry;
                                }
                                if (entry.Cipher.ObfuscatedIndex && ObfuscatedPathRe.IsMatch (name))
                                {
                                    goto NextEntry;
                                }
                                if (filename_map.Count > 0)
                                    name = filename_map.Get (entry.Hash, name);
                                if (name.Length > 0x100)
                                {
                                    goto NextEntry;
                                }
                                entry.Name = name;
                                entry.IsEncrypted = !(entry.Cipher is NoCrypt)
                                    && !(entry.Cipher.StartupTjsNotEncrypted && "startup.tjs" == name);
                                break;
                            case 0x6d676573: // "segm"
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
                            if (null != hx_entry_info)
                            {
                                if (hx_entry_info.TryGetValue (entry.Name, out HxEntry info))
                                {
                                    entry.Extra = info;

                                    var sb = new StringBuilder ();
                                    if (!string.IsNullOrEmpty (info.Path))
                                    {
                                        sb.Append (info.Path);
                                        if (!info.Path.EndsWith ("/") && !info.Path.EndsWith ("\\"))
                                            sb.Append ('/');
                                    }
                                    if (!string.IsNullOrEmpty (info.Name))
                                    {
                                        sb.Append (info.Name);
                                        if (sb.Length > 0)
                                            entry.Name = sb.ToString ();
                                    }
                                    else
                                    {
                                        sb.Append (entry.Name);
                                        if (sb.Length > 0)
                                            entry.Name = sb.ToString ();
                                    }
                                }
                            }
                            entry.Type = FormatCatalog.Instance.GetTypeFromName(entry.Name, ContainedFormats);
                            dir.Add (entry);
                        }
                    }
                    else if (0x3A == (entry_signature >> 24)) // "yuz:" || "sen:" || "dls:"
                    {
                        if (entry_size >= 0x10 && crypt_algorithm.Value is SenrenCxCrypt)
                        {
                            long offset = header.ReadInt64() + base_offset;
                            header.ReadUInt32(); // unpacked size
                            uint size = header.ReadUInt32();
                            if (offset > 0 && offset + size <= file.MaxOffset)
                            {
                                var yuz = file.View.ReadBytes (offset, size);
                                var crypt = crypt_algorithm.Value as SenrenCxCrypt;
                                crypt.ReadYuzNames (yuz, filename_map);
                            }
                        }
                    }
                    else if (0x34767848 == entry_signature) // "Hxv4"
                    {
                        if (crypt_algorithm.Value is HxCrypt)
                        {
                            try
                            {
                                var offset = header.ReadInt64 () + base_offset;
                                var size = header.ReadUInt32 ();
                                var flags = header.ReadUInt16 ();
                                var hx = file.View.ReadBytes (offset, size);
                                var crypt = crypt_algorithm.Value as HxCrypt;
                                hx_entry_info = crypt.ReadIndex (Path.GetFileName (file.Name), hx);
                            }
                            catch (Exception) { /* ignore parse error */ }
                        }
                    }
                    else if (entry_size > 7)
                    {
                        // 0x6E666E68 == entry_signature    // "hnfn"
                        // 0x6C696D73 == entry_signature    // "smil"
                        // 0x46696C65 == entry_signature    // "eliF"
                        // 0x757A7559 == entry_signature    // "Yuzu"
                        uint hash = header.ReadUInt32();
                        int name_size = header.ReadInt16();
                        if (name_size > 0)
                        {
                            entry_size -= 6;
                            if (name_size * 2 <= entry_size)
                            {
                                var filename = new string (header.ReadChars (name_size));
                                filename_map.Add (hash, filename);
                            }
                        }
                    }
NextEntry:
                    header.BaseStream.Position = dir_offset;
                }
            }
            if (0 == dir.Count)
                return null;
            var arc = new ArcFile (file, this, dir);
            try
            {
                if (crypt_algorithm.IsValueCreated)
                    crypt_algorithm.Value.Init (arc);
                return arc;
            }
            catch
            {
                arc.Dispose();
                throw;
            }
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

        internal static long SkipExeHeader (ArcView file, byte[] signature)
        {
            var exe = new ExeFile (file);
            if (exe.ContainsSection (".rsrc"))
            {
                var offset = exe.FindString (exe.Sections[".rsrc"], signature);
                if (offset != -1 && 0 != file.View.ReadUInt32 (offset+signature.Length))
                    return offset;
            }
            var section = exe.Overlay;
            while (section.Offset < file.MaxOffset)
            {
                var offset = exe.FindString (section, signature, 0x10);
                if (-1 == offset)
                    break;
                if (0 != file.View.ReadUInt32 (offset+signature.Length))
                    return offset;
                section.Offset = offset + 0x10;
                section.Size = (uint)(file.MaxOffset - section.Offset);
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

            return xp3_entry.Cipher.EntryReadFilter (xp3_entry, input);
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new Xp3Options {
                Version             = Properties.Settings.Default.XP3Version,
                Scheme              = GetScheme (Properties.Settings.Default.XP3Scheme),
                CompressIndex       = Properties.Settings.Default.XP3CompressHeader,
                CompressContents    = Properties.Settings.Default.XP3CompressContents,
                RetainDirs          = Properties.Settings.Default.XP3RetainStructure,
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

        ICrypt QueryCryptAlgorithm (ArcView file)
        {
            var alg = GuessCryptAlgorithm (file);
            if (null != alg)
                return alg;
            var options = Query<Xp3Options> (arcStrings.XP3EncryptedNotice);
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

            bool use_encryption = !(scheme is NoCrypt);

            using (var writer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                writer.Write (s_xp3_header);
                if (2 == xp3_options.Version || 3 == xp3_options.Version)
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
                        Cipher          = scheme,
                        IsEncrypted     = use_encryption
                                       && !(scheme.StartupTjsNotEncrypted && VFS.IsPathEqualsToFileName (name, "startup.tjs"))
                    };
                    bool compress = compress_contents && ShouldCompressFile (entry);
                    using (var file = File.Open (name, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (!xp3entry.IsEncrypted || 0 == file.Length)
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
                    if (3 == xp3_options.Version)
                    {
                        foreach (var entry in dir)
                        {
                            header.Write ((uint)0x6e666e68); // "hnfn"
                            header.Write ((long)(4+2+entry.Name.Length*2));
                            header.Write ((uint)entry.Hash);
                            header.Write ((short)entry.Name.Length);
                            foreach (char c in entry.Name)
                                header.Write (c);
                        }
                        dir_pos = header.BaseStream.Position;
                    }
                    foreach (var entry in dir)
                    {
                        var entry_name = entry.Name;
                        if (3 == xp3_options.Version)
                        {
                            using (var md5 = MD5.Create())
                            {
                                var text_bytes = Encoding.Unicode.GetBytes(entry.Name.ToLowerInvariant());
                                var hash = md5.ComputeHash(text_bytes);
                                var sb = new StringBuilder(32);
                                for (int i = 0; i < hash.Length; ++i)
                                    sb.AppendFormat("{0:x2}", hash[i]);
                                entry_name = sb.ToString();
                            }
                        }
                        header.BaseStream.Position = dir_pos;
                        header.Write ((uint)0x656c6946); // "File"
                        long header_size_pos = header.BaseStream.Position;
                        header.Write ((long)0);
                        header.Write ((uint)0x6f666e69); // "info"
                        header.Write ((long)(4+8+8+2 + entry_name.Length*2));
                        header.Write ((uint)(use_encryption ? 0x80000000 : 0));
                        header.Write ((long)entry.UnpackedSize);
                        header.Write ((long)entry.Size);

                        header.Write ((short)entry_name.Length);
                        foreach (char c in entry_name)
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
                            xp3entry.Segments.Add (segment);
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
            if (entry.Name.HasExtension (".ogg"))
                return false;
            return true;
        }

        ICrypt GuessCryptAlgorithm (ArcView file)
        {
            var title = FormatCatalog.Instance.LookupGame (file.Name);
            if (string.IsNullOrEmpty (title))
                title = FormatCatalog.Instance.LookupGame (file.Name, @"..\*.exe");
            if (string.IsNullOrEmpty (title))
                return null;
            ICrypt algorithm;
            if (!KnownSchemes.TryGetValue (title, out algorithm) && NoCryptTitles.Contains (title))
                algorithm = NoCryptAlgorithm;
            return algorithm;
        }

        static Xp3Scheme KiriKiriScheme = new Xp3Scheme
        {
            KnownSchemes = new Dictionary<string, ICrypt>(),
            NoCryptTitles = new HashSet<string>()
        };

        public static IDictionary<string, ICrypt> KnownSchemes
        {
            get { return KiriKiriScheme.KnownSchemes; }
        }

        public static ISet<string> NoCryptTitles
        {
            get { return KiriKiriScheme.NoCryptTitles; }
        }

        public override ResourceScheme Scheme
        {
            get { return KiriKiriScheme; }
            set { KiriKiriScheme = (Xp3Scheme)value; }
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

        public override bool CanRead  { get { return !disposed; } }
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
                    if (m_entry.IsEncrypted)
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
                    if (m_entry.IsEncrypted)
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

    /// <summary>
    /// Class that maps file hashes to filenames.
    /// </summary>
    internal sealed class FilenameMap : IDisposable
    {
        Dictionary<uint, string>    m_hash_map = new Dictionary<uint, string>();
        Dictionary<string, string>  m_md5_map = new Dictionary<string, string>();
        MD5             m_md5 = MD5.Create();
        StringBuilder   m_md5_str = new StringBuilder();

        public int Count { get { return m_md5_map.Count; } }

        public void Add (uint hash, string filename)
        {
            if (!m_hash_map.ContainsKey (hash))
                m_hash_map[hash] = filename;

            m_md5_map[GetMd5Hash (filename)] = filename;
        }

        public void AddShortcut (string shortcut, string filename)
        {
            m_md5_map[shortcut] = filename;
        }

        public string Get (uint hash, string md5)
        {
            string filename;
            if (m_md5_map.TryGetValue (md5, out filename))
                return filename;
            if (m_hash_map.TryGetValue (hash, out filename))
                return filename;
            return md5;
        }

        string GetMd5Hash (string text)
        {
            var text_bytes = Encoding.Unicode.GetBytes (text.ToLowerInvariant());
            var md5 = m_md5.ComputeHash (text_bytes);
            m_md5_str.Clear();
            for (int i = 0; i < md5.Length; ++i)
                m_md5_str.AppendFormat ("{0:x2}", md5[i]);
            return m_md5_str.ToString();
        }

        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_md5.Dispose();
                _disposed = true;
            }
        }
    }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "ANM")]
    [ExportMetadata("Target", "TXT")]
    public class AnmFormat : ResourceAlias { }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "ASD")]
    [ExportMetadata("Target", "TXT")]
    public class AsdFormat : ResourceAlias { }
}
