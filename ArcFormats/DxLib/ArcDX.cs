//! \file       ArcDX.cs
//! \date       Thu Oct 08 00:18:56 2015
//! \brief      DxLib engine archives with 'DX' signature.
//
// Copyright (C) 2015-2018 by morkt
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
using System.Diagnostics;
using System.IO;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.DxLib
{
    internal class DxArchive : ArcFile
    {
        public readonly IDxKey Encryption;
        public readonly int Version;

        public DxArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, IDxKey enc, int version)
            : base (arc, impl, dir)
        {
            Encryption = enc;
            Version = version;
        }
    }

    [Serializable]
    public class DxScheme : ResourceScheme
    {
        public IList<IDxKey> KnownKeys;
    }

    [Export(typeof(ArchiveFormat))]
    public class DxOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DXA"; } }
        public override string Description { get { return "DxLib engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public DxOpener ()
        {
            Extensions = new string[] { "dxa", "hud", "usi", "med", "dat", "bin", "bcx", "wolf" };
            Signatures = new uint[] {
                0x19EF8ED4, 0xA9FCCEDD, 0x0AEE0FD3, 0x5523F211, 0x5524F211, 0x69FC5FE4, 0x09E19ED9, 0x7DCC5D83,
                0xC55D4473, 0
            };
        }

        DxScheme DefaultScheme = new DxScheme { KnownKeys = new List<IDxKey>() };

        public IList<IDxKey> KnownKeys { get { return DefaultScheme.KnownKeys; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset < 0x1C)
                return null;
            uint signature = file.View.ReadUInt32 (0);
            foreach (var enc in KnownKeys)
            {
                var key = enc.Key;
                uint sig_key = LittleEndian.ToUInt32 (key, 0);
                uint sig_test = signature ^ sig_key;
                int version = (int)(sig_test >> 16);
                if (0x5844 == (sig_test & 0xFFFF) && version <= 7) // 'DX'
                {
                    var dir = ReadIndex (file, version, key);
                    if (null != dir)
                    {
                        if (KnownKeys[0] != enc)
                        {
                            // move last used key to the top of the known keys list
                            KnownKeys.Remove (enc);
                            KnownKeys.Insert (0, enc);
                        }
                        return new DxArchive (file, this, dir, enc, version);
                    }
                    return null;
                }
            }
            var arc = GuessKey (file);
            if (arc != null)
            {
                var encryption = arc.Encryption;
                KnownKeys.Insert (0, encryption);
                Trace.WriteLine (string.Format ("Restored key '{0}'", encryption.Password, "[DXA]"));
            }
            return arc;
        }

        DxArchive GuessKey (ArcView file)
        {
            if (file.MaxOffset > uint.MaxValue)
                return null;
            var key = GuessKeyV6 (file);
            if (key != null)
            {
                var dir = ReadIndex (file, 6, key);
                if (dir != null)
                    return new DxArchive (file, this, dir, new DxKey (key), 6);
            }
            key = new byte[12];
            for (short version = 4; version >= 1; --version)
            {
                file.View.Read (0, key, 0, 12);
                key[0] ^= (byte)'D'; 
                key[1] ^= (byte)'X';
                key[2] ^= (byte)version;
                int base_offset = version > 3 ? 0x1C : 0x18;
                key[8] ^= (byte)base_offset;
                uint key0 = LittleEndian.ToUInt32 (key, 0);
                uint index_offset = file.View.ReadUInt32 (12) ^ key0;
                if (index_offset <= base_offset || index_offset >= file.MaxOffset)
                    continue;
                uint index_size = (uint)(file.MaxOffset - index_offset);
                if (index_size > 0xFFFFFF)
                    continue;
                key[4] ^= (byte)index_size;
                key[5] ^= (byte)(index_size >> 8);
                key[6] ^= (byte)(index_size >> 16);
                try
                {
                    var dir = ReadIndex (file, version, key);
                    if (null != dir)
                        return new DxArchive (file, this, dir, new DxKey (key), version);
                }
                catch { /* ignore parse errors */ }
            }
            return null;
        }

        byte[] GuessKeyV6 (ArcView file)
        {
            var header = file.View.ReadBytes (0, 0x30);
            header[0] ^= (byte)'D'; 
            header[1] ^= (byte)'X';
            header[2] ^= 6;
            uint key0 = header.ToUInt32 (0);
            header[8] ^= (byte)0x30;
            uint data_offset_hi = header.ToUInt32 (12) ^ key0;
            if (data_offset_hi != 0)
                return null;
            uint key2 = header.ToUInt32 (8);
            uint key1 = header.ToUInt32 (0x1C);
            long index_offset = header.ToInt64 (0x10) ^ key1 ^ ((long)key2 << 32);
            if (index_offset <= 0x30 || index_offset >= file.MaxOffset)
                return null;
            var key = new byte[12];
            LittleEndian.Pack (key0, key, 0);
            LittleEndian.Pack (key1, key, 4);
            LittleEndian.Pack (key2, key, 8);
            return key;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            var dx_arc = arc as DxArchive;
            if (null == dx_arc)
                return input;
            var dx_ent = (PackedEntry)entry;
            long dec_offset = entry.Offset;
            if (dx_arc.Version > 5)
            {
                dec_offset = dx_ent.UnpackedSize;
            }
            var key = dx_arc.Encryption.GetEntryKey (dx_ent.Name);
            input = new EncryptedStream (input, dec_offset, key);
            if (!dx_ent.IsPacked)
                return input;
            using (input)
            {
                var data = Unpack (input);
                return new BinMemoryStream (data, entry.Name);
            }
        }

        byte[] Unpack (Stream stream)
        {
            using (var input = new ArcView.Reader (stream))
            {
                uint unpacked_size = input.ReadUInt32();
                int remaining = input.ReadInt32() - 9;
                var output = new byte[unpacked_size];
                byte control_code = input.ReadByte();
                int dst = 0;
                while (remaining > 0)
                {
                    byte b = input.ReadByte();
                    --remaining;
                    if (b != control_code)
                    {
                        output[dst++] = b;
                        continue;
                    }
                    b = input.ReadByte();
                    --remaining;
                    if (b == control_code)
                    {
                        output[dst++] = b;
                        continue;
                    }
                    if (b > control_code)
                        --b;
                    int count = b >> 3;
                    if (0 != (b & 4))
                    {
                        count |= input.ReadByte() << 5;
                        --remaining;
                    }
                    count += 4;
                    int offset;
                    switch (b & 3)
                    {
                    case 0:
                        offset = input.ReadByte();
                        --remaining;
                        break;

                    case 1:
                        offset = input.ReadUInt16();
                        remaining -= 2;
                        break;

                    case 2:
                        offset = input.ReadUInt16();
                        offset |= input.ReadByte() << 16;
                        remaining -= 3;
                        break;

                    default:
                        throw new InvalidFormatException ("DX decompression failed");
                    }
                    ++offset;
                    Binary.CopyOverlapped (output, dst - offset, dst, count);
                    dst += count;
                }
                return output;
            }
        }

        List<Entry> ReadIndex (ArcView file, int version, byte[] key)
        {
            DxHeader dx = null;
            if (version <= 4)
                dx = ReadArcHeaderV4 (file, version, key);
            else if (version >= 6)
                dx = ReadArcHeaderV6 (file, version, key);
            if (null == dx || dx.DirTable >= dx.IndexSize || dx.FileTable >= dx.IndexSize)
                return null;
            using (var encrypted = file.CreateStream (dx.IndexOffset, dx.IndexSize))
            using (var index = new EncryptedStream (encrypted, version >= 6 ? 0 : dx.IndexOffset, key))
            using (var reader = IndexReader.Create (dx, version, index))
            {
                return reader.Read();
            }
        }

        DxHeader ReadArcHeaderV4 (ArcView file, int version, byte[] key)
        {
            var header = file.View.ReadBytes (4, 0x18);
            if (0x18 != header.Length)
                return null;
            Decrypt (header, 0, header.Length, 4, key);
            return new DxHeader {
                IndexSize  = LittleEndian.ToUInt32 (header, 0),
                BaseOffset = LittleEndian.ToUInt32 (header, 4),
                IndexOffset = LittleEndian.ToUInt32 (header, 8),
                FileTable  = LittleEndian.ToUInt32 (header, 0x0C),
                DirTable   = LittleEndian.ToUInt32 (header, 0x10),
                CodePage   = 932,
            };
        }

        DxHeader ReadArcHeaderV6 (ArcView file, int version, byte[] key)
        {
            var header = file.View.ReadBytes (4, 0x2C);
            if (0x2C != header.Length)
                return null;
            Decrypt (header, 0, header.Length, 4, key);
            return new DxHeader {
                IndexSize  = LittleEndian.ToUInt32 (header, 0),
                BaseOffset = LittleEndian.ToInt64 (header, 4),
                IndexOffset = LittleEndian.ToInt64 (header, 0x0C),
                FileTable  = (uint)LittleEndian.ToInt64 (header, 0x14),
                DirTable   = (uint)LittleEndian.ToInt64 (header, 0x1C),
                CodePage   = LittleEndian.ToInt32 (header, 0x24),
            };
        }

        internal static void Decrypt (byte[] data, int index, int count, long offset, byte[] key)
        {
            int key_pos = (int)(offset % key.Length);
            for (int i = 0; i < count; ++i)
            {
                data[index+i] ^= key[key_pos++];
                if (key.Length == key_pos)
                    key_pos = 0;
            }
        }

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (DxScheme)value; }
        }
    }

    internal class DxHeader
    {
        public long BaseOffset;
        public long IndexOffset;
        public uint IndexSize;
        public uint FileTable;
        public uint DirTable;
        public int  CodePage;
    }

    internal abstract class IndexReader : IDisposable
    {
        protected readonly int  m_version;
        protected DxHeader      m_header;
        protected BinaryStream  m_input;
        protected Encoding      m_encoding;
        protected List<Entry>   m_dir = new List<Entry>();

        internal int Version { get { return m_version; } }

        protected IndexReader (DxHeader header, int version, Stream input)
        {
            m_header = header;
            m_version = version;
            m_input = new BinaryStream (input, "");
            m_encoding = Encoding.GetEncoding (header.CodePage);
        }

        public static IndexReader Create (DxHeader header, int version, Stream input)
        {
            if (version <= 4)
                return new IndexReaderV2 (header, version, input);
            else if (version >= 6)
                return new IndexReaderV6 (header, version, input);
            else
                throw new InvalidFormatException ("Not supported DX archive version.");
        }

        public List<Entry> Read ()
        {
            ReadFileTable ("", 0);
            return m_dir;
        }

        protected abstract void ReadFileTable (string root, long table_offset);

        protected string ExtractFileName (long table_offset)
        {
            m_input.Position = table_offset;
            int name_offset = m_input.ReadUInt16() * 4 + 4;
            m_input.Position = table_offset + name_offset;
            return m_input.ReadCString (m_encoding);
        }

        #region IDisposable Members
        bool disposed = false;
        public void Dispose ()
        {
            if (!disposed)
            {
                m_input.Dispose();
                disposed = true;
            }
        }
        #endregion
    }

    internal sealed class IndexReaderV2 : IndexReader
    {
        readonly int    m_entry_size;

        public IndexReaderV2 (DxHeader header, int version, Stream input) : base (header, version, input)
        {
            m_entry_size = Version >= 2 ? 0x2C : 0x28;
        }

        private class DxDirectory
        {
            public int DirOffset;
            public int ParentDirOffset;
            public int FileCount;
            public int FileTable;
        }

        DxDirectory ReadDirEntry ()
        {
            var dir = new DxDirectory();
            dir.DirOffset = m_input.ReadInt32();
            dir.ParentDirOffset = m_input.ReadInt32();
            dir.FileCount = m_input.ReadInt32();
            dir.FileTable = m_input.ReadInt32();
            return dir;
        }

        protected override void ReadFileTable (string root, long table_offset)
        {
            m_input.Position = m_header.DirTable + table_offset;
            var dir = ReadDirEntry();
            if (dir.DirOffset != -1 && dir.ParentDirOffset != -1)
            {
                m_input.Position = m_header.FileTable + dir.DirOffset;
                root = Path.Combine (root, ExtractFileName (m_input.ReadUInt32()));
            }
            long current_pos = m_header.FileTable + dir.FileTable;
            for (int i = 0; i < dir.FileCount; ++i)
            {
                m_input.Position = current_pos;
                uint name_offset = m_input.ReadUInt32();
                uint attr = m_input.ReadUInt32();
                m_input.Seek (0x18, SeekOrigin.Current);
                uint offset = m_input.ReadUInt32();
                if (0 != (attr & 0x10)) // FILE_ATTRIBUTE_DIRECTORY
                {
                    if (0 == offset || table_offset == offset)
                        throw new InvalidFormatException ("Infinite recursion in DXA directory index");
                    ReadFileTable (root, offset);
                }
                else
                {
                    uint size = m_input.ReadUInt32();
                    int packed_size = -1;
                    if (Version >= 2)
                        packed_size = m_input.ReadInt32();
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (Path.Combine (root, ExtractFileName (name_offset)));
                    entry.Offset = m_header.BaseOffset + offset;
                    entry.UnpackedSize = size;
                    entry.IsPacked = -1 != packed_size;
                    if (entry.IsPacked)
                        entry.Size = (uint)packed_size;
                    else
                        entry.Size = size;
                    m_dir.Add (entry);
                }
                current_pos += m_entry_size;
            }
        }
    }

    internal sealed class IndexReaderV6 : IndexReader
    {
        readonly int    m_entry_size;

        public IndexReaderV6 (DxHeader header, int version, Stream input) : base (header, version, input)
        {
            m_entry_size = 0x40;
        }

        private class DxDirectory
        {
            public long DirOffset;
            public long ParentDirOffset;
            public int  FileCount;
            public long FileTable;
        }

        DxDirectory ReadDirEntry ()
        {
            var dir = new DxDirectory();
            dir.DirOffset = m_input.ReadInt64();
            dir.ParentDirOffset = m_input.ReadInt64();
            dir.FileCount = (int)m_input.ReadInt64();
            dir.FileTable = m_input.ReadInt64();
            return dir;
        }

        protected override void ReadFileTable (string root, long table_offset)
        {
            m_input.Position = m_header.DirTable + table_offset;
            var dir = ReadDirEntry();
            if (dir.DirOffset != -1 && dir.ParentDirOffset != -1)
            {
                m_input.Position = m_header.FileTable + dir.DirOffset;
                root = Path.Combine (root, ExtractFileName (m_input.ReadInt64()));
            }
            long current_pos = m_header.FileTable + dir.FileTable;
            for (int i = 0; i < dir.FileCount; ++i)
            {
                m_input.Position = current_pos;
                var name_offset = m_input.ReadInt64();
                uint attr = (uint)m_input.ReadInt64();
                m_input.Seek (0x18, SeekOrigin.Current);
                var offset = m_input.ReadInt64();
                if (0 != (attr & 0x10)) // FILE_ATTRIBUTE_DIRECTORY
                {
                    if (0 == offset || table_offset == offset)
                        throw new InvalidFormatException ("Infinite recursion in DXA directory index");
                    ReadFileTable (root, offset);
                }
                else
                {
                    var size = m_input.ReadInt64();
                    var packed_size = m_input.ReadInt64();
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (Path.Combine (root, ExtractFileName (name_offset)));
                    entry.Offset = m_header.BaseOffset + offset;
                    entry.UnpackedSize = (uint)size;
                    entry.IsPacked = -1 != packed_size;
                    if (entry.IsPacked)
                        entry.Size = (uint)packed_size;
                    else
                        entry.Size = (uint)size;
                    m_dir.Add (entry);
                }
                current_pos += m_entry_size;
            }
        }
    }

    internal class EncryptedStream : ProxyStream
    {
        private int         m_base_pos;
        private byte[]      m_key;

        public EncryptedStream (Stream stream, long base_position, byte[] key, bool leave_open = false)
            : base (stream, leave_open)
        {
            m_key = key;
            m_base_pos = (int)(base_position % m_key.Length);
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            var key_pos = m_base_pos + Position;
            int read = BaseStream.Read (buffer, offset, count);
            if (read > 0)
                DxOpener.Decrypt (buffer, offset, count, key_pos, m_key);
            return read;
        }

        public override int ReadByte ()
        {
            int key_pos = (int)((m_base_pos + Position) % m_key.Length);
            int b = BaseStream.ReadByte();
            if (-1 != b)
            {
                b ^= m_key[key_pos];
            }
            return b;
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            int key_pos = (int)((m_base_pos + Position) % m_key.Length);
            byte[] write_buf = new byte[count];
            for (int i = 0; i < count; ++i)
            {
                write_buf[i] = (byte)(buffer[offset+i] ^ m_key[key_pos++]);
                if (m_key.Length == key_pos)
                    key_pos = 0;
            }
            BaseStream.Write (write_buf, 0, count);
        }

        public override void WriteByte (byte value)
        {
            int key_pos = (int)((m_base_pos + Position) % m_key.Length);
            BaseStream.WriteByte ((byte)(value ^ m_key[key_pos]));
        }
    }
}
