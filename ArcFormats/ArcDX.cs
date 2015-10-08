//! \file       ArcDX.cs
//! \date       Thu Oct 08 00:18:56 2015
//! \brief      Encrypted Flatz archives with 'DX' signature.
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
using GameRes.Utility;

namespace GameRes.Formats.Flat
{
    internal class DxArchive : ArcFile
    {
        public readonly byte[] Key;

        public DxArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/FLAT"; } }
        public override string Description { get { return "Flat DX resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public DatOpener ()
        {
            Signatures = new uint[] { 0x19EF8ED4 }; // the only recognized encryption scheme
        }

        static readonly string[] KnownKeys = new string[]
        {
            "omanco", // Cross Quartz
        };

        public override ArcFile TryOpen (ArcView file)
        {
            int signature = file.View.ReadUInt16 (0);
            int version   = file.View.ReadUInt16 (2);
            var key = new byte[12];
            foreach (var key_str in KnownKeys)
            {
                GetKeyBytes (key_str, key);
                int xor_id  = key[0] | (key[1] << 8);
                int xor_ver = key[2] | (key[3] << 8);
                if ((signature ^ xor_id) != 0x5844) // 'DX'
                    continue;
                xor_ver ^= version;
                if (xor_ver < 1 || xor_ver > 4)
                    continue;
                var dir = ReadIndex (file, xor_ver, key);
                if (null != dir)
                    return new DxArchive (file, this, dir, key);
                break;
            }
            return null;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            var dx_arc = arc as DxArchive;
            if (null == dx_arc)
                return input;
            input = new EncryptedStream (input, entry.Offset, dx_arc.Key);
            var dx_ent = entry as PackedEntry;
            if (null == dx_ent || !dx_ent.IsPacked)
                return input;
            using (input)
            {
                var data = Unpack (input);
                return new MemoryStream (data);
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
            var header = new byte[0x18];
            if (0x18 != file.View.Read (4, header, 0, 0x18))
                return null;
            Decrypt (header, 0, header.Length, 4, key);
            var dx = new DxHeader {
                IndexSize  = LittleEndian.ToUInt32 (header, 0),
                BaseOffset = LittleEndian.ToUInt32 (header, 4),
                IndexOffset = LittleEndian.ToUInt32 (header, 8),
                FileTable  = LittleEndian.ToUInt32 (header, 0x0c),
                DirTable   = LittleEndian.ToUInt32 (header, 0x10),
            };
            using (var encrypted = file.CreateStream (dx.IndexOffset, dx.IndexSize))
            using (var index = new EncryptedStream (encrypted, dx.IndexOffset, key))
            using (var reader = new IndexReader (dx, version, index))
            {
                return reader.Read();
            }
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

        private static void GetKeyBytes (string key, byte[] dst)
        {
            int key_length = Math.Min (dst.Length, key.Length);
            key_length = Encodings.cp932.GetBytes (key, 0, key_length, dst, 0);
            int j = 0;
            for (int i = key_length; i < dst.Length; ++i)
            {
                dst[i] = dst[j++];
            }
            dst[0] = (byte)~dst[0];
            dst[1] = (byte)((dst[1] >> 4) | (dst[1] << 4));
            dst[2] = (byte)(dst[2] ^ 0x8A);
            dst[3] = (byte)(~((dst[3] >> 4) | (dst[3] << 4)));
            dst[4] = (byte)~dst[4];
            dst[5] = (byte)(dst[5] ^ 0xAC);
            dst[6] = (byte)~dst[6];
            dst[7] = (byte)(~((dst[7] >> 3) | (dst[7] << 5)));
            dst[8] = (byte)((dst[8] >> 5) | (dst[8] << 3));
            dst[9] = (byte)(dst[9] ^ 0x7F);
            dst[10] = (byte)(((dst[10] >> 4) | (dst[10] << 4)) ^ 0xD6);
            dst[11] = (byte)(dst[11] ^ 0xCC);
        }
    }

    internal class DxHeader
    {
        public long BaseOffset;
        public long IndexOffset;
        public uint IndexSize;
        public uint FileTable;
        public uint DirTable;
    }

    internal class DxDirectory
    {
        public int DirOffset;
        public int ParentDirOffset;
        public int FileCount;
        public int FileTable;
    }

    internal sealed class IndexReader : IDisposable
    {
        readonly int    m_version;
        readonly int    m_entry_size;
        DxHeader        m_header;
        BinaryReader    m_input;
        List<Entry>     m_dir = new List<Entry>();

        public List<Entry> Dir { get { return m_dir; } }

        public IndexReader (DxHeader header, int version, Stream input)
        {
            m_version = version;
            m_entry_size = m_version >= 2 ? 0x2C : 0x28;
            m_header = header;
            m_input = new ArcView.Reader (input);
        }

        public List<Entry> Read ()
        {
            ReadFileTable ("", 0);
            return m_dir;
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

        void ReadFileTable (string root, uint table_offset)
        {
            m_input.BaseStream.Position = m_header.DirTable + table_offset;
            var dir = ReadDirEntry();
            if (dir.DirOffset != -1 && dir.ParentDirOffset != -1)
            {
                m_input.BaseStream.Position = m_header.FileTable + dir.DirOffset;
                root = root + ExtractFileName (m_input.ReadUInt32()) + '\\';
            }
            long current_pos = m_header.FileTable + dir.FileTable;
            for (int i = 0; i < dir.FileCount; ++i)
            {
                m_input.BaseStream.Position = current_pos;
                uint name_offset = m_input.ReadUInt32();
                uint attr = m_input.ReadUInt32();
                m_input.BaseStream.Seek (0x18, SeekOrigin.Current);
                uint offset = m_input.ReadUInt32();
                if (0 != (attr & 0x10)) // FILE_ATTRIBUTE_DIRECTORY
                {
                    ReadFileTable (root, offset);
                }
                else
                {
                    uint size = m_input.ReadUInt32();
                    int packed_size = -1;
                    if (m_version >= 2)
                        packed_size = m_input.ReadInt32();
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (root+ExtractFileName (name_offset));
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

        string ExtractFileName (uint table_offset)
        {
            m_input.BaseStream.Position = table_offset;
            int name_offset = m_input.ReadUInt16() * 4 + 4;
            m_input.BaseStream.Position = table_offset + name_offset;
            return m_input.BaseStream.ReadCString();
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

    internal class EncryptedStream : Stream
    {
        private Stream      m_stream;
        private int         m_base_pos;
        private byte[]      m_key;
        private bool        m_should_dispose;

        public Stream BaseStream { get { return m_stream; } }

        public override bool  CanRead { get { return m_stream.CanRead; } }
        public override bool  CanSeek { get { return m_stream.CanSeek; } }
        public override bool CanWrite { get { return m_stream.CanWrite; } }
        public override long   Length { get { return m_stream.Length; } }
        public override long Position
        {
            get { return m_stream.Position; }
            set { m_stream.Position = value; }
        }

        public EncryptedStream (Stream input, long base_position, byte[] key)
        {
            m_stream = input;
            m_key = key;
            m_base_pos = (int)(base_position % m_key.Length);
            m_should_dispose = true;
        }

        #region System.IO.Stream methods
        public override void Flush()
        {
            m_stream.Flush();
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            return m_stream.Seek (offset, origin);
        }

        public override void SetLength (long length)
        {
            m_stream.SetLength (length);
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            var key_pos = m_base_pos + Position;
            int read = m_stream.Read (buffer, offset, count);
            if (read > 0)
                DatOpener.Decrypt (buffer, offset, count, key_pos, m_key);
            return read;
        }

        public override int ReadByte ()
        {
            int key_pos = (int)((m_base_pos + Position) % m_key.Length);
            int b = m_stream.ReadByte();
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
            m_stream.Write (write_buf, 0, count);
        }

        public override void WriteByte (byte value)
        {
            int key_pos = (int)((m_base_pos + Position) % m_key.Length);
            m_stream.WriteByte ((byte)(value ^ m_key[key_pos]));
        }
        #endregion

        #region IDisposable Members
        bool disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!disposed)
            {
                if (disposing && m_should_dispose)
                {
                    m_stream.Dispose();
                }
                disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }
}
