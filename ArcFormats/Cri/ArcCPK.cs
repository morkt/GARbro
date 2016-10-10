//! \file       ArcCPK.cs
//! \date       Mon Feb 29 12:39:36 2016
//! \brief      CRI Middleware resource archive.
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
using System.Linq;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Cri
{
    using TableRow = Dictionary<string, object>;

    internal class CpkEntry : PackedEntry
    {
        public int      Id;
    }

    [Export(typeof(ArchiveFormat))]
    public class CpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CPK"; } }
        public override string Description { get { return "CRI Middleware resource archive"; } }
        public override uint     Signature { get { return 0x204B5043; } } // 'CPK '
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            var reader = new IndexReader (file);
            var dir = reader.ReadIndex();
            if (null == dir || !dir.Any())
                return null;
            if (!reader.HasNames)
                DetectFileTypes (file, dir);
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Size < 0x10 || !arc.File.View.AsciiEqual (entry.Offset, "CRILAYLA"))
                return base.OpenEntry (arc, entry);

            var unpacked_size = arc.File.View.ReadInt32 (entry.Offset+8);
            var packed_size = arc.File.View.ReadUInt32 (entry.Offset+12);
            if (unpacked_size < 0 || packed_size > entry.Size - 0x10)
                return base.OpenEntry (arc, entry);
            uint prefix_size = entry.Size - (0x10+packed_size);
            var output = new byte[unpacked_size+prefix_size];
            var packed = arc.File.View.ReadBytes (entry.Offset+0x10, packed_size);
            Array.Reverse (packed);
            using (var mem = new MemoryStream (packed))
            using (var input = new MsbBitStream (mem))
            {
                byte[] sizes = { 2, 3, 5, 8 };
                int dst = (int)prefix_size;
                while (dst < output.Length)
                {
                    if (0 == input.GetNextBit())
                    {
                        output[dst++] = (byte)input.GetBits (8);
                        continue;
                    }
                    int count = 3;
                    int offset = input.GetBits (13) + 3;
                    int rank = 0;
                    int bits, step;
                    do
                    {
                        bits = sizes[rank];
                        step = input.GetBits (bits);
                        count += step;
                        if (rank < 3)
                            rank++;
                    }
                    while (((1 << bits) - 1) == step);
                    Binary.CopyOverlapped (output, dst-offset, dst, count);
                    dst += count;
                }
            }
            Array.Reverse (output, (int)prefix_size, unpacked_size);
            arc.File.View.Read (entry.Offset+0x10+packed_size, output, 0, prefix_size);
            return new MemoryStream (output);
        }

        void DetectFileTypes (ArcView file, List<Entry> dir)
        {
            foreach (var entry in dir)
            {
                var offset = entry.Offset;
                var signature = file.View.ReadUInt32 (offset);
                if (entry.Size > 0x10 && 0x4C495243 == signature) // 'CRIL'
                {
                    uint packed_size = file.View.ReadUInt32 (offset+12);
                    if (packed_size < entry.Size - 0x10)
                    {
                        signature = file.View.ReadUInt32 (offset+0x10+packed_size);
                        if (0x10 == signature)
                            signature = file.View.ReadUInt32 (offset+0x10+packed_size+signature);
                    }
                }
                var res = AutoEntry.DetectFileType (signature);
                if (null != res)
                {
                    entry.Type = res.Type;
                    entry.Name = Path.ChangeExtension (entry.Name, res.Extensions.FirstOrDefault());
                }
            }
        }
    }

    internal class IndexReader
    {
        ArcView         m_file;
        Deserializer    m_des = new Deserializer();
        long            m_content_offset;
        Dictionary<int, Entry> m_dir = new Dictionary<int, Entry>();

        public bool HasNames { get; private set; }

        public IndexReader (ArcView file)
        {
            m_file = file;
        }

        public List<Entry> ReadIndex ()
        {
            var chunk = ReadUTFChunk (4);
            var header = m_des.DeserializeUTFChunk (chunk).First();

            m_content_offset = (long)header["ContentOffset"];
            HasNames = header.ContainsKey ("TocOffset");
            if (HasNames)
            {
                ReadToc ((long)header["TocOffset"]);
            }
            if (header.ContainsKey ("ItocOffset"))
            {
                var align = (uint)(int)header["Align"];
                ReadItoc ((long)header["ItocOffset"], align);
            }
            return m_dir.Values.ToList();
        }

        void ReadToc (long toc_offset)
        {
            var base_offset = Math.Min (m_content_offset, toc_offset);
            if (!m_file.View.AsciiEqual (toc_offset, "TOC "))
                throw new InvalidFormatException();
            var chunk = ReadUTFChunk (toc_offset+4);
            var table = m_des.DeserializeUTFChunk (chunk);
            foreach (var row in table)
            {
                var entry = new CpkEntry
                {
                    Id      = (int)row["ID"],
                    Offset  = (long)row["FileOffset"] + base_offset,
                    Size    = (uint)(int)row["FileSize"],
                };
                if (row.ContainsKey ("ExtractSize"))
                    entry.UnpackedSize = (uint)(int)row["ExtractSize"];
                else
                    entry.UnpackedSize = entry.Size;
                entry.IsPacked = entry.Size != entry.UnpackedSize;
                var name = (string)row["FileName"];
                if (row.ContainsKey ("DirName"))
                    name = Path.Combine ((string)row["DirName"], name);
                entry.Name = name;
                entry.Type = FormatCatalog.Instance.GetTypeFromName (name);
                m_dir[entry.Id] = entry;
            }
        }

        void ReadItoc (long toc_offset, uint align)
        {
            if (!m_file.View.AsciiEqual (toc_offset, "ITOC"))
                throw new InvalidFormatException();
            var chunk = ReadUTFChunk (toc_offset+4);
            var itoc = m_des.DeserializeUTFChunk (chunk).FirstOrDefault();
            if (null == itoc || !itoc.ContainsKey ("DataL"))
                return;

            var dataL = m_des.DeserializeUTFChunk ((byte[])itoc["DataL"]);
            var dataH = m_des.DeserializeUTFChunk ((byte[])itoc["DataH"]);
            foreach (var row in dataL.Concat (dataH))
            {
                int id = (int)row["ID"];
                var entry = GetEntryById (id);
                entry.Size = (uint)(int)row["FileSize"];
                if (row.ContainsKey ("ExtractSize"))
                    entry.UnpackedSize = (uint)(int)row["ExtractSize"];
                else
                    entry.UnpackedSize = entry.Size;
                entry.IsPacked = entry.Size != entry.UnpackedSize;
            }
            long current_offset = m_content_offset;
            foreach (var id in m_dir.Keys.OrderBy (x => x))
            {
                var entry = m_dir[id];
                entry.Offset = current_offset;
                current_offset += entry.Size;
                if (align != 0)
                {
                    var tail = entry.Size % align;
                    if (tail > 0)
                        current_offset += align - tail;
                }
                if (string.IsNullOrEmpty (entry.Name))
                    entry.Name = id.ToString ("D5");
            }
        }

        CpkEntry GetEntryById (int id)
        {
            Entry entry;
            if (!m_dir.TryGetValue (id, out entry))
            {
                entry = new CpkEntry { Id = id };
                m_dir[id] = entry;
            }
            return entry as CpkEntry;
        }

        byte[] ReadUTFChunk (long offset)
        {
            long chunk_size = m_file.View.ReadInt64 (offset+4);
            if (chunk_size < 0 || chunk_size > int.MaxValue)
                throw new FileSizeException();
            var chunk = m_file.View.ReadBytes (offset+12, (uint)chunk_size);
            if (chunk.Length < chunk_size)
                throw new EndOfStreamException ("Unexpected end of file");
            if (!Binary.AsciiEqual (chunk, 0, "@UTF"))
                DecryptUTFChunk (chunk);
            return chunk;
        }

        internal static void DecryptUTFChunk (byte[] chunk)
        {
            int key = 0x655F;
            for (int i = 0; i < chunk.Length; i++)
            {
                chunk[i] ^= (byte)key;
                key *= 0x4115;
            }
        }
    }

    internal class Deserializer
    {
        byte[]          m_chunk;

        public List<TableRow> DeserializeUTFChunk (byte[] chunk)
        {
            m_chunk = chunk;
            if (!Binary.AsciiEqual (m_chunk, 0, "@UTF"))
                throw new InvalidFormatException();
            var chunk_length = BigEndian.ToInt32 (m_chunk, 4);
            using (var mem = new MemoryStream (m_chunk, 8, chunk_length))
            using (var input = new BigEndianReader (mem))
            {
                int rows_offset     = input.ReadInt32();
                int strings_offset  = input.ReadInt32() + 8;
                int data_offset     = input.ReadInt32() + 8;
                input.Skip (4);
                int column_count    = input.ReadInt16();
                int row_length      = input.ReadInt16();
                int row_count       = input.ReadInt32();

                var columns = new List<Column> (column_count);
                for (int i = 0; i < column_count; ++i)
                {
                    byte flags = input.ReadByte();
                    if (0 == flags)
                    {
                        input.Skip (3);
                        flags = input.ReadByte();
                    }
                    int name_offset = strings_offset + input.ReadInt32();
                    var column = new Column
                    {
                        Flags = (TableFlags)flags,
                        Name  = ReadString (name_offset),
                    };
                    columns.Add (column);
                }

                var table = new List<TableRow> (row_count);
                int next_offset = rows_offset;
                for (int i = 0; i < row_count; ++i)
                {
                    input.Position = next_offset;
                    next_offset += row_length;
                    var row = new TableRow (column_count);
                    table.Add (row);
                    foreach (var column in columns)
                    {
                        var storage = column.Flags & TableFlags.StorageMask;
                        if (TableFlags.StorageNone == storage
                            || TableFlags.StorageZero == storage
                            || TableFlags.StorageConstant == storage)
                            continue;
                        switch (column.Flags & TableFlags.TypeMask)
                        {
                        case TableFlags.TypeByte:
                            row[column.Name] = (int)input.ReadByte();
                            break;

                        case TableFlags.TypeSByte:
                            row[column.Name] = (int)input.ReadSByte();
                            break;

                        case TableFlags.TypeUInt16:
                            row[column.Name] = (int)input.ReadUInt16();
                            break;

                        case TableFlags.TypeInt16:
                            row[column.Name] = (int)input.ReadInt16();
                            break;

                        case TableFlags.TypeUInt32:
                        case TableFlags.TypeInt32:
                            row[column.Name] = input.ReadInt32();
                            break;

                        case TableFlags.TypeUInt64:
                        case TableFlags.TypeInt64:
                            row[column.Name] = input.ReadInt64();
                            break;

                        case TableFlags.TypeFloat32:
                            row[column.Name] = input.ReadSingle();
                            break;

                        case TableFlags.TypeString:
                            {
                                int offset = strings_offset + input.ReadInt32();
                                row[column.Name] = ReadString (offset);
                                break;
                            }

                        case TableFlags.TypeData:
                            {
                                int offset = data_offset + input.ReadInt32();
                                int length = input.ReadInt32();
                                row[column.Name] = m_chunk.Skip (offset).Take (length).ToArray();
                                break;
                            }

                        default:
                            throw new NotSupportedException();
                        }
                    }
                }
                return table;
            }
        }

        string ReadString (int offset)
        {
            return Binary.GetCString (m_chunk, offset, 0xFF, Encoding.UTF8);
        }
    }

    internal class Column
    {
        public TableFlags   Flags;
        public string       Name;
    }

    [Flags]
    internal enum TableFlags : byte
    {
        StorageMask     = 0xF0,
        StorageNone     = 0x00,
        StorageZero     = 0x10,
        StorageConstant = 0x30,

        TypeMask        = 0x0F,
        TypeByte        = 0x00,
        TypeSByte       = 0x01,
        TypeUInt16      = 0x02,
        TypeInt16       = 0x03,
        TypeUInt32      = 0x04,
        TypeInt32       = 0x05,
        TypeUInt64      = 0x06,
        TypeInt64       = 0x07,
        TypeFloat32     = 0x08,
        TypeFloat64     = 0x09,
        TypeString      = 0x0A,
        TypeData        = 0x0B,
    }
}
