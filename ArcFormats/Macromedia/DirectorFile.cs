//! \file       DirectorFile.cs
//! \date       2023 Aug 21
//! \brief      Macromedia Director container deserialization.
//
// Copyright (C) 2023 by morkt
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

using GameRes.Compression;
using GameRes.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace GameRes.Formats.Macromedia
{
    internal enum ByteOrder
    {
        LittleEndian, BigEndian
    }

    internal enum DataType
    {
        Null        = 0,
        Bitmap      = 1,
        FilmLoop    = 2,
        Text        = 3,
        Palette     = 4,
        Picture     = 5,
        Sound       = 6,
        Button      = 7,
        Shape       = 8,
        Movie       = 9,
        DigitalVideo = 10,
        Script      = 11,
        RTE         = 12,
    }

    internal class SerializationContext
    {
        public int          Version;
        public Encoding     Encoding;

        public SerializationContext ()
        {
            Encoding = Encodings.cp932;
        }
    }

    internal class DirectorFile
    {
        List<DirectorEntry> m_dir;
        Dictionary<int, DirectorEntry> m_index = new Dictionary<int, DirectorEntry>();
        MemoryMap       m_mmap = new MemoryMap();
        KeyTable        m_keyTable = new KeyTable();
        DirectorConfig  m_config = new DirectorConfig();
        List<Cast>      m_casts = new List<Cast>();
        Dictionary<int, byte[]> m_ilsMap = new Dictionary<int, byte[]>();
        string          m_codec;

        public string Codec => m_codec;

        public bool IsAfterBurned { get; private set; }

        public MemoryMap        MMap => m_mmap;
        public KeyTable     KeyTable => m_keyTable;
        public DirectorConfig Config => m_config;
        public List<Cast>      Casts => m_casts;
        public List<DirectorEntry>            Directory => m_dir;
        public Dictionary<int, DirectorEntry> Index     => m_index;

        public DirectorEntry Find (string four_cc) => Directory.Find (e => e.FourCC == four_cc);

        public DirectorEntry FindById (int id)
        {
            DirectorEntry entry;
            m_index.TryGetValue (id, out entry);
            return entry;
        }

        public bool Deserialize (SerializationContext context, Reader reader)
        {
            reader.Skip (8);
            m_codec = reader.ReadFourCC();
            if (m_codec == "MV93" || m_codec == "MC95")
            {
                if (!ReadMMap (context, reader))
                    return false;
            }
            else if (m_codec == "FGDC" || m_codec == "FGDM")
            {
                IsAfterBurned = true;
                if (!ReadAfterBurner (context, reader))
                    return false;
            }
            else
            {
                Trace.WriteLine (string.Format ("Unknown m_codec '{0}'", m_codec), "DXR");
                return false;
            }
            return ReadKeyTable (context, reader)
                && ReadConfig (context, reader)
                && ReadCasts (context, reader);
        }

        internal bool ReadMMap (SerializationContext context, Reader reader)
        {
            if (reader.ReadFourCC() != "imap")
                return false;
            reader.Skip (8);
            uint mmap_pos = reader.ReadU32();
            reader.Position = mmap_pos;
            if (reader.ReadFourCC() != "mmap")
                return false;
            reader.Position = mmap_pos + 8;
            MMap.Deserialize (context, reader);
            m_dir = MMap.Dir;
            for (int i = 0; i < m_dir.Count; ++i)
            {
                m_index[i] = m_dir[i];
            }
            return true;
        }

        bool ReadAfterBurner (SerializationContext context, Reader reader)
        {
            if (reader.ReadFourCC() != "Fver")
                return false;
            int length = reader.ReadVarInt();
            long next_pos = reader.Position + length;
            int version = reader.ReadVarInt();
            if (version > 0x400)
            {
                reader.ReadVarInt(); // imap version
                reader.ReadVarInt(); // director version
            }
            if (version > 0x500)
            {
                int str_len = reader.ReadU8();
                reader.Skip (str_len); // version string
            }

            reader.Position = next_pos;
            if (reader.ReadFourCC() != "Fcdr")
                return false;
            // skip compression table, assume everything is zlib-compressed
            length = reader.ReadVarInt();

            reader.Position += length;
            if (reader.ReadFourCC() != "ABMP")
                return false;
            length = reader.ReadVarInt();
            next_pos = reader.Position + length;
            reader.ReadVarInt(); // compression type, index within 'Fcdr' compression table
            int unpacked_size = reader.ReadVarInt();
            using (var abmp = new ZLibStream (reader.Source, CompressionMode.Decompress, true))
            {
                var abmp_reader = new Reader (abmp, reader.ByteOrder);
                if (!ReadABMap (context, abmp_reader))
                    return false;
            }

            reader.Position = next_pos;
            if (reader.ReadFourCC() != "FGEI")
                return false;
            reader.ReadVarInt();
            long base_offset = reader.Position;
            foreach (var entry in m_dir)
            {
                m_index[entry.Id] = entry;
                if (entry.Offset >= 0)
                    entry.Offset += base_offset;
            }
            var ils_chunk = FindById (2);
            if (null == ils_chunk)
                return false;
            using (var ils = new ZLibStream (reader.Source, CompressionMode.Decompress, true))
            {
                uint pos = 0;
                var ils_reader = new Reader (ils, reader.ByteOrder);
                while (pos < ils_chunk.UnpackedSize)
                {
                    int id = ils_reader.ReadVarInt();
                    var chunk = m_index[id];
                    m_ilsMap[id] = ils_reader.ReadBytes ((int)chunk.Size);
                    pos += ils_reader.GetVarIntLength ((uint)id) + chunk.Size;
                }
            }
            return true;
        }

        bool ReadABMap (SerializationContext context, Reader reader)
        {
            reader.ReadVarInt();
            reader.ReadVarInt();
            int count = reader.ReadVarInt();
            m_dir = new List<DirectorEntry> (count);
            for (int i = 0; i < count; ++ i)
            {
                var entry = new AfterBurnerEntry();
                entry.Deserialize (context, reader);
                m_dir.Add (entry);
            }
            return true;
        }

        Reader GetChunkReader (DirectorEntry chunk, Reader reader)
        {
            if (-1 == chunk.Offset)
            {
                byte[] chunk_data;
                if (!m_ilsMap.TryGetValue (chunk.Id, out chunk_data))
                    throw new InvalidFormatException (string.Format ("Can't find chunk {0} in ILS", chunk.FourCC));
                var input = new BinMemoryStream (chunk_data, null);
                reader = new Reader (input, reader.ByteOrder);
            }
            else
            {
                reader.Position = chunk.Offset;
            }
            return reader;
        }

        bool ReadKeyTable (SerializationContext context, Reader reader)
        {
            var key_chunk = Find ("KEY*");
            if (null == key_chunk)
                return false;
            reader = GetChunkReader (key_chunk, reader);
            KeyTable.Deserialize (context, reader);
            return true;
        }

        bool ReadConfig (SerializationContext context, Reader reader)
        {
            var config_chunk = Find ("VWCF") ?? Find ("DRCF");
            if (null == config_chunk)
                return false;
            reader = GetChunkReader (config_chunk, reader);
            Config.Deserialize (context, reader);
            context.Version = Config.Version;
            return true;
        }

        bool ReadCasts (SerializationContext context, Reader reader)
        {
            Reader cas_reader;
            if (context.Version > 1200)
            {
                var mcsl = Find ("MCsL");
                if (mcsl != null)
                {
                    var mcsl_reader = GetChunkReader (mcsl, reader);
                    var cast_list = new CastList();
                    cast_list.Deserialize (context, mcsl_reader);
                    foreach (var entry in cast_list.Entries)
                    {
                        var key_entry = KeyTable.FindByCast (entry.Id, "CAS*");
                        if (key_entry != null)
                        {
                            var cas_entry = Index[key_entry.Id];
                            cas_reader = GetChunkReader (cas_entry, reader);
                            var cast = new Cast (context, cas_reader, cas_entry);
                            if (!PopulateCast (cast, context, reader, entry))
                                return false;
                            Casts.Add (cast);
                        }
                    }
                    return true;
                }
            }
            var cas_chunk = Find ("CAS*");
            if (null == cas_chunk)
                return false;
            var new_entry = new CastListEntry { Name = "internal", Id = 0x400, MinMember = Config.MinMember };
            cas_reader = GetChunkReader (cas_chunk, reader);
            var new_cast = new Cast (context, cas_reader, cas_chunk);
            if (!PopulateCast (new_cast, context, reader, new_entry))
                return false;
            Casts.Add (new_cast);
            return true;
        }

        public bool PopulateCast (Cast cast, SerializationContext context, Reader reader, CastListEntry entry)
        {
            cast.Name = entry.Name;
            for (int i = 0; i < cast.Index.Length; ++i)
            {
                int chunk_id = cast.Index[i];
                if (chunk_id > 0)
                {
                    var chunk = this.Index[chunk_id];
                    var member = new CastMember();
                    member.Id = chunk_id;
                    var cast_reader = GetChunkReader (chunk, reader);
                    member.Deserialize (context, cast_reader);
                    cast.Members[member.Id] = member;
                }
            }
            return true;
        }
    }

    internal class CastMember
    {
        public DataType     Type;
        public CastInfo     Info = new CastInfo();
        public byte[]       SpecificData;
        public byte         Flags;
        public int          Id;

        public void Deserialize (SerializationContext context, Reader reader)
        {
            reader = reader.CloneUnless (ByteOrder.BigEndian);
            if (context.Version > 1200)
            {
                Type = (DataType)reader.ReadI32();
                int info_length = reader.ReadI32();
                int data_length = reader.ReadI32();
                if (info_length > 0)
                {
                    Info.Deserialize (context, reader);
                }
                SpecificData = reader.ReadBytes (data_length);
            }
            else
            {
                int data_length = reader.ReadU16();
                int info_length = reader.ReadI32();
                Type = (DataType)reader.ReadU8();
                --data_length;
                if (data_length > 0)
                {
                    Flags = reader.ReadU8();
                    --data_length;
                }
                SpecificData = reader.ReadBytes (data_length);
                if (info_length > 0)
                {
                    Info.Deserialize (context, reader);
                }
            }
        }
    }

    internal class CastInfo
    {
        public uint     DataOffset;
        public uint     ScriptKey;
        public uint     Flags;
        public int      ScriptId;
        public string   Name;
        public string   SourceText;
        public List<byte[]> Items = new List<byte[]>();

        public void Deserialize (SerializationContext context, Reader reader)
        {
            long base_offset = reader.Position;
            DataOffset = reader.ReadU32();
            ScriptKey = reader.ReadU32();
            reader.Skip (4);
            Flags = reader.ReadU32();
            ScriptId = reader.ReadI32();
            reader.Position = base_offset + DataOffset;
            int table_len = reader.ReadU16();
            var offsets = new int[table_len];
            for (int i = 0; i < table_len; ++i)
                offsets[i] = reader.ReadI32();

            int data_length = reader.ReadI32();
            long list_offset = reader.Position;
            Items.Clear();
            Items.Capacity = offsets.Length;
            for (int i = 0; i < offsets.Length; ++i)
            {
                int offset = offsets[i];
                int next_offset = (i + 1 < offsets.Length) ? offsets[i+1] : data_length;
                reader.Position = list_offset + offset;
                Items.Add (reader.ReadBytes (next_offset - offset));
            }

            SourceText = Items.Count > 0 ? Binary.GetCString (Items[0], 0) : string.Empty;
            Name = GetString (1, context.Encoding);
        }

        string GetString (int item_idx, Encoding enc)
        {
            if (item_idx >= Items.Count)
                return string.Empty;
            var src = Items[item_idx];
            if (src.Length <= 1 || 0 == src[0])
                return string.Empty;
            int len = src[0];
            return enc.GetString (src, 1, len);
        }
    }

    internal class Cast
    {
        public int[]    Index;
        public string   Name;
        public Dictionary<int, CastMember> Members = new Dictionary<int, CastMember>();

        public Cast (SerializationContext context, Reader reader, DirectorEntry entry)
        {
            int count = (int)(entry.Size / 4);
            Index = new int[count];
            Deserialize (context, reader);
        }

        public void Deserialize (SerializationContext context, Reader reader)
        {
            reader = reader.CloneUnless (ByteOrder.BigEndian);
            for (int i = 0; i < Index.Length; ++i)
                Index[i] = reader.ReadI32();
        }
    }

    internal class CastList
    {
        public uint DataOffset;
        public int OffsetCount;
        public int[] OffsetTable;
        public int ItemsLength;
        public int CastCount;
        public int ItemsPerCast;
        public List<byte[]> Items = new List<byte[]>();

        public readonly List<CastListEntry> Entries = new List<CastListEntry>();

        public void Deserialize (SerializationContext context, Reader reader)
        {
            long base_offset = reader.Position;
            reader = reader.CloneUnless (ByteOrder.BigEndian);
            DataOffset = reader.ReadU32();
            reader.Skip (2);
            CastCount = reader.ReadU16();
            ItemsPerCast = reader.ReadU16();
            reader.Skip (2);
            reader.Position = base_offset + DataOffset;
            OffsetCount = reader.ReadU16();
            OffsetTable = new int[OffsetCount];
            for (int i = 0; i < OffsetCount; ++i)
            {
                OffsetTable[i] = reader.ReadI32();
            }
            ItemsLength = reader.ReadI32();
            long items_offset = reader.Position;
            Items.Clear();
            Items.Capacity = OffsetCount;
            for (int i = 0; i < OffsetCount; ++i)
            {
                int offset = OffsetTable[i];
                int next_offset = (i + 1 < OffsetCount) ? OffsetTable[i + 1] : ItemsLength;
                int item_size = next_offset - offset;
                Items.Add (reader.ReadBytes (item_size));
            }

            Entries.Clear();
            Entries.Capacity = CastCount;
            int item_idx = 0;
            for (int i = 0; i < CastCount; ++i)
            {
                var entry = new CastListEntry();
                if (ItemsPerCast >= 1)
                    entry.Name = GetString (item_idx + 1, context.Encoding);
                if (ItemsPerCast >= 2)
                    entry.Path = GetString (item_idx + 2, context.Encoding);
                if (ItemsPerCast >= 3 && Items[item_idx + 3].Length >= 2)
                    entry.Flags = BigEndian.ToUInt16 (Items[item_idx + 3], 0);
                if (ItemsPerCast >= 4 && Items[item_idx + 4].Length >= 8)
                {
                    entry.MinMember = BigEndian.ToUInt16 (Items[item_idx + 4], 0);
                    entry.MaxMember = BigEndian.ToUInt16 (Items[item_idx + 4], 2);
                    entry.Id        = BigEndian.ToInt32 (Items[item_idx + 4], 4);
                }
                Entries.Add (entry);
            }
        }

        string GetString (int item_idx, Encoding enc)
        {
            var src = Items[item_idx];
            if (src.Length <= 1 || 0 == src[0])
                return string.Empty;
            int len = src[0];
            return enc.GetString (src, 1, len);
        }
    }

    internal class CastListEntry
    {
        public string   Name;
        public string   Path;
        public ushort   Flags;
        public int      MinMember;
        public int      MaxMember;
        public int      Id;
    }

    internal class DirectorConfig
    {
        public short Length;
        public short FileVersion;
        public short StageTop;
        public short StageLeft;
        public short StageBottom;
        public short StageRight;
        public short MinMember;
        public short MaxMember;
        public ushort StageColor;
        public ushort BitDepth;
        public int Version;
        public int FrameRate;
        public int Platform;
        public int Protection;
        public uint CheckSum;
        public int DefaultPalette;

        public void Deserialize (SerializationContext context, Reader reader)
        {
            long base_offset = reader.Position;
            reader = reader.CloneUnless (ByteOrder.BigEndian);

            reader.Position = base_offset + 0x24;
            Version = reader.ReadU16();
            reader.Position = base_offset;
            Length = reader.ReadI16();
            FileVersion = reader.ReadI16();
            StageTop = reader.ReadI16();
            StageLeft = reader.ReadI16();
            StageBottom = reader.ReadI16();
            StageRight = reader.ReadI16();
            MinMember = reader.ReadI16();
            MaxMember = reader.ReadI16();
            reader.Skip (0x0A);
            StageColor = reader.ReadU16();
            BitDepth = reader.ReadU16();
            reader.Skip (0x18);
            FrameRate = reader.ReadU16();
            Platform = reader.ReadI16();
            Protection = reader.ReadI16();
            reader.Skip (4);
            CheckSum = reader.ReadU32();
            if (Version > 1200)
            {
                reader.Position = base_offset + 0x4E;
            }
            else
            {
                reader.Position = base_offset + 0x46;
            }
            DefaultPalette = reader.ReadU16();
        }
    }

    internal class KeyTable
    {
        public int  EntrySize;
        public int  TotalCount;
        public int  UsedCount;
        public readonly List<KeyTableEntry> Table = new List<KeyTableEntry>();

        public KeyTableEntry this[int index] => Table[index];

        public KeyTableEntry FindByCast (int cast_id, string four_cc)
        {
            return Table.Find (e => e.CastId == cast_id && e.FourCC == four_cc);
        }

        public void Deserialize (SerializationContext context, Reader reader)
        {
            EntrySize = reader.ReadU16();
            reader.Skip(2);
            TotalCount = reader.ReadI32();
            UsedCount = reader.ReadI32();

            Table.Clear();
            Table.Capacity = TotalCount;
            for (int i = 0; i < TotalCount; ++i)
            {
                var entry = new KeyTableEntry();
                entry.Deserialize (context, reader);
                Table.Add (entry);
            }
        }
    }

    internal class KeyTableEntry
    {
        public int      Id;
        public int      CastId;
        public string   FourCC;

        public void Deserialize (SerializationContext context, Reader input)
        {
            Id     = input.ReadI32();
            CastId = input.ReadI32();
            FourCC = input.ReadFourCC();
        }
    }

    internal class MemoryMap
    {
    	public ushort   HeaderLength;
        public ushort   EntryLength;
        public int      ChunkCountMax;
        public int      ChunkCountUsed;
        public int      FreeHead;
        public readonly List<DirectorEntry> Dir = new List<DirectorEntry>();

        public DirectorEntry this[int index] => Dir[index];

        public void Deserialize (SerializationContext context, Reader reader)
        {
            long header_pos = reader.Position;
            HeaderLength = reader.ReadU16();
            if (HeaderLength < 0x18)
                throw new InvalidFormatException ("Invalid <mmap> header length.");
            EntryLength = reader.ReadU16();
            if (EntryLength < 0x14)
                throw new InvalidFormatException ("Invalid <mmap> entry length.");
            ChunkCountMax = reader.ReadI32();
            ChunkCountUsed = reader.ReadI32();
            reader.Skip (8);
            FreeHead = reader.ReadI32();

            Dir.Clear();
            Dir.Capacity = ChunkCountUsed;
            long entry_pos = header_pos + HeaderLength;
            for (int i = 0; i < ChunkCountUsed; ++i)
            {
                reader.Position = entry_pos;
                var entry = new MemoryMapEntry (i);
                entry.Deserialize (context, reader);
                Dir.Add (entry);
                entry_pos += EntryLength;
            }
        }
    }

    internal class DirectorEntry : PackedEntry
    {
        public int      Id;
        public string   FourCC;
    }

    internal class MemoryMapEntry : DirectorEntry
    {
        public ushort   Flags;

        public MemoryMapEntry (int id = 0)
        {
            Id = id;
        }

        public void Deserialize (SerializationContext context, Reader reader)
        {
            FourCC = reader.ReadFourCC();
            Size   = reader.ReadU32();
            Offset = reader.ReadU32() + 8;
            Flags  = reader.ReadU16();
            reader.ReadI32(); // next
            UnpackedSize = Size;
            IsPacked = false;
        }
    }

    internal class AfterBurnerEntry : DirectorEntry
    {
        public int      CompMethod;

        public void Deserialize (SerializationContext context, Reader reader)
        {
            Id           = reader.ReadVarInt();
            Offset       = reader.ReadVarInt();
            Size         = (uint)reader.ReadVarInt();
            UnpackedSize = (uint)reader.ReadVarInt();
            CompMethod   = reader.ReadVarInt(); // assume zlib
            FourCC       = reader.ReadFourCC();
            IsPacked     = Size != UnpackedSize;
        }
    }

    internal class Reader
    {
        Stream      m_input;
        byte[]      m_buffer = new byte[4];

        public Reader (Stream input, ByteOrder e = ByteOrder.LittleEndian) : this (input, Encodings.cp932, e)
        {
        }

        public Reader (Stream input, Encoding enc, ByteOrder e = ByteOrder.LittleEndian)
        {
            m_input = input;
            Encoding = enc;
            SetByteOrder (e);
        }

        public Stream Source => m_input;

        public ByteOrder ByteOrder { get; private set; }

        public Encoding Encoding { get; set; }

        public long Position
        {
            get => m_input.Position;
            set => m_input.Position = value;
        }

        private Func<ushort> ToU16;
        private Func<uint>   ToU32;

        public void SetByteOrder (ByteOrder e)
        {
            this.ByteOrder = e;
            if (ByteOrder.LittleEndian == e)
            {
                ToU16 = () => LittleEndian.ToUInt16 (m_buffer, 0);
                ToU32 = () => LittleEndian.ToUInt32 (m_buffer, 0);
            }
            else
            {
                ToU16 = () => BigEndian.ToUInt16 (m_buffer, 0);
                ToU32 = () => BigEndian.ToUInt32 (m_buffer, 0);
            }
        }

        static Dictionary<uint, string> KnownFourCC = new Dictionary<uint, string>();

        public string ReadFourCC ()
        {
            uint signature = ReadU32();
            string four_cc;
            if (KnownFourCC.TryGetValue (signature, out four_cc))
                return four_cc;
            BigEndian.Pack (signature, m_buffer, 0);
            return KnownFourCC[signature] = Encoding.GetString (m_buffer, 0, 4);
        }

        public void Skip (int amount) => m_input.Seek (amount, SeekOrigin.Current);

        public byte ReadU8 ()
        {
            int b = m_input.ReadByte();
            if (-1 == b)
                throw new EndOfStreamException();
            return (byte)b;
        }

        public sbyte ReadI8 () => (sbyte)ReadU8();

        public ushort ReadU16 ()
        {
            if (m_input.Read (m_buffer, 0, 2) < 2)
                throw new EndOfStreamException();
            return ToU16();
        }

        public short ReadI16 () => (short)ReadU16();

        public uint ReadU32 ()
        {
            if (m_input.Read (m_buffer, 0, 4) < 4)
                throw new EndOfStreamException();
            return ToU32();
        }

        public int ReadI32 () => (int)ReadU32();

        public byte[] ReadBytes (int length)
        {
            if (0 == length)
                return Array.Empty<byte>();
            var buffer = new byte[length];
            if (m_input.Read (buffer, 0, length) < length)
                throw new EndOfStreamException();
            return buffer;
        }

        public int ReadVarInt ()
        {
            int n = 0;
            for (int i = 0; i < 5; ++i)
            {
                int bits = m_input.ReadByte();
                if (-1 == bits)
                    throw new EndOfStreamException();
                n = n << 7 | bits & 0x7F;
                if (0 == (bits & 0x80))
                    return n;
            }
            throw new InvalidFormatException();
        }

        public uint GetVarIntLength (uint i)
        {
            uint n = 1;
            while (i > 0x7F)
            {
                i >>= 7;
                ++n;
            }
            return n;
        }

        public Reader CloneUnless (ByteOrder order)
        {
            if (this.ByteOrder != order)
                return new Reader (this.Source, this.Encoding, order);
            else
                return this;
        }
    }
}
