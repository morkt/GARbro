//! \file       ArcTCD3.cs
//! \date       Thu Oct 08 13:14:57 2015
//! \brief      TopCat data archives (TCD)
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
using System.Linq;
using GameRes.Utility;

namespace GameRes.Formats.TopCat
{
    internal class TcdSection
    {
        public string Extension;
        public uint DataSize;
        public uint IndexOffset;
        public int  DirCount;
        public int  DirNameLength;
        public int  FileCount;
        public int  FileNameLength;

        public int  DirNamesSize;
        public int  FileNamesSize;
    }

    internal struct TcdDirEntry
    {
        public int  FileCount;
        public int  NamesOffset;
        public int  FirstIndex;
    }

    internal class TcdEntry : Entry
    {
        public int Index;
    }

    internal class TcdArchive : ArcFile
    {
        public int? Key;

        public TcdArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir)
            : base (arc, impl, dir)
        {
        }
    }

    [Serializable]
    public class TcdScheme : ResourceScheme
    {
        public Dictionary<string, int> KnownKeys;
    }

    [Export(typeof(ArchiveFormat))]
    public class TcdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "TCD"; } }
        public override string Description { get { return "TopCat data archive"; } }
        public override uint     Signature { get { return 0x33444354; } } // 'TCD3'
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public TcdOpener ()
        {
            Signatures = new uint[] { 0x32444354, 0x33444354 }; // 'TCD2', 'TCD3'
        }

        public static Dictionary<string, int> KnownKeys = new Dictionary<string, int>();

        public override ResourceScheme Scheme
        {
            get { return new TcdScheme { KnownKeys = KnownKeys }; }
            set { KnownKeys = ((TcdScheme)value).KnownKeys; }
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadByte (3) - '0';
            TcdIndexReader reader;
            if (2 == version)
                reader = new TcdReaderV2 (file);
            else
                reader = new TcdReaderV3 (file);
            using (reader)
            {
                var dir = reader.ReadIndex();
                if (null == dir)
                    return null;
                return new TcdArchive (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Name.EndsWith (".OGG", StringComparison.InvariantCultureIgnoreCase))
                return RestoreOggStream (arc, entry);
            if (entry.Name.EndsWith (".TSF", StringComparison.InvariantCultureIgnoreCase) ||
                entry.Name.EndsWith (".TCT", StringComparison.InvariantCultureIgnoreCase))
                return OpenScript (arc, entry);
            if (entry.Name.EndsWith (".SPD", StringComparison.InvariantCultureIgnoreCase))
                return OpenSpdc (arc, entry);
            return arc.File.CreateStream (entry.Offset, entry.Size);
        }

        Stream OpenSpdc (ArcFile arc, Entry entry)
        {
            int signature = arc.File.View.ReadInt32 (entry.Offset);
            if (0x43445053 == signature || 0x38445053 == signature || entry.Size <= 0x14)
                return arc.File.CreateStream (entry.Offset, entry.Size);

            var header = arc.File.View.ReadBytes (entry.Offset, 0x14);
            byte header_key = (byte)(header[0x12] + header[0x10]);
            header[0] -= header_key;
            header[1] -= header_key;
            header[2] -= header_key;
            header[3] -= header_key;
            bool spdc_entry = Binary.AsciiEqual (header, "SPD") && (header[3] == 'C' || header[3] == '8');
            if (!spdc_entry)
            {
                LittleEndian.Pack (signature, header, 0);
                var tcde = entry as TcdEntry;
                var tcda = arc as TcdArchive;
                if (tcda != null && tcde != null)
                {
                    if (null == tcda.Key)
                    {
                        foreach (var key in TcdOpener.KnownKeys.Values)
                        {
                            int first = signature + key * (tcde.Index + 3);
                            if (0x43445053 == first) // 'SPDC'
                            {
                                tcda.Key = key;
                                spdc_entry = true;
                                break;
                            }
                        }
                    }
                    else if (0x43445053 == (signature + tcda.Key.Value * (tcde.Index + 3)))
                    {
                        spdc_entry = true;
                    }
                    if (spdc_entry && 0 != tcda.Key.Value)
                    {
                        unsafe
                        {
                            fixed (byte* raw = header)
                            {
                                int* dw = (int*)raw;
                                for (int i = 0; i < 5; ++i)
                                    dw[i] += tcda.Key.Value * (tcde.Index + 3 + i);
                            }
                        }
                    }
                }
            }
            var rest = arc.File.CreateStream (entry.Offset+0x14, entry.Size-0x14);
            return new PrefixStream (header, rest);
        }

        Stream OpenScript (ArcFile arc, Entry entry)
        {
            int unpacked_size = arc.File.View.ReadInt32 (entry.Offset);
            byte[] data = new byte[unpacked_size];
            using (var input = arc.File.CreateStream (entry.Offset+4, entry.Size-4))
                UnpackLz (input, data);
            DecryptScript (data);
            return new MemoryStream (data);
        }

        void UnpackLz (Stream input, byte[] output)
        {
            int dst = 0;
            int bits = 2;
            while (dst < output.Length)
            {
                bits >>= 1;
                if (1 == bits)
                {
                    bits = input.ReadByte();
                    if (-1 == bits)
                        break;
                    bits |= 0x100;
                }
                if (0 == (bits & 1))
                {
                    int count = input.ReadByte();
                    int offset = input.ReadByte() << 4 | count >> 4;
                    count = Math.Min ((count & 0xF) + 3, output.Length - dst);
                    Binary.CopyOverlapped (output, dst-offset, dst, count);
                    dst += count;
                }
                else
                {
                    output[dst++] = (byte)input.ReadByte();
                }
            }
        }

        void DecryptScript (byte[] data)
        {
            for (int i = 0; i < data.Length; ++i)
                data[i] = Binary.RotByteR (data[i], 1);
        }

        Stream RestoreOggStream (ArcFile arc, Entry entry)
        {
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            int remaining = data.Length;
            int src = 0;
            while (remaining > 0x1B && Binary.AsciiEqual (data, src, "OggS"))
            {
                int segment_count = data[src+0x1A];
                data[src+0x16] = 0;
                data[src+0x17] = 0;
                data[src+0x18] = 0;
                data[src+0x19] = 0;
                int page_size = segment_count + 0x1B;
                if (segment_count != 0)
                {
                    if (remaining < page_size)
                        break;
                    int segment_table = src + 0x1B;
                    for (int i = 0; i < segment_count; ++i)
                        page_size += data[segment_table++];
                }
                remaining -= page_size;
                if (remaining < 0)
                    break;
                uint crc = Crc32Normal.UpdateCrc (0, data, src, page_size);
                LittleEndian.Pack (crc, data, src+0x16);
                src += page_size;
            }
            return new MemoryStream (data);
        }
    }

    internal abstract class TcdIndexReader : IDisposable
    {
        BinaryReader    m_input;
        int             m_section_count;

        public    int          Count { get; private set; }
        protected BinaryReader Input { get { return m_input; } }

        protected TcdIndexReader (ArcView file, int section_count)
        {
            Count = file.View.ReadInt32 (4);
            var input = file.CreateStream();
            m_input = new BinaryReader (input);
            m_section_count = section_count;
        }

        protected string[] Extensions = { ".TCT", ".TSF", ".SPD", ".OGG", ".WAV" };

        public List<Entry> ReadIndex ()
        {
            if (!ArchiveFormat.IsSaneCount (Count))
                return null;

            var sections = ReadSections (m_section_count);
            var list = new List<Entry> (Count);
            foreach (var section in sections)
            {
                m_input.BaseStream.Position = section.IndexOffset;
                var dir_names = m_input.ReadBytes (section.DirNamesSize);
                if (section.DirNamesSize != dir_names.Length)
                    return null;
                byte section_key = dir_names[section.DirNameLength-1];
                DecryptNames (dir_names, section_key);

                var dirs = new TcdDirEntry[section.DirCount];
                for (int i = 0; i < dirs.Length; ++i)
                {
                    dirs[i].FileCount   = m_input.ReadInt32();
                    dirs[i].NamesOffset = m_input.ReadInt32();
                    dirs[i].FirstIndex  = m_input.ReadInt32();
                    m_input.ReadInt32();
                }
                var file_names = m_input.ReadBytes (section.FileNamesSize);
                if (file_names.Length != section.FileNamesSize)
                    return null;
                DecryptNames (file_names, section_key);

                var offsets = new uint[section.FileCount + 1];
                for (int i = 0; i < offsets.Length; ++i)
                {
                    offsets[i] = m_input.ReadUInt32();
                }

                int dir_name_offset = 0;
                foreach (var dir in dirs)
                {
                    string dir_name = GetName (dir_names, section.DirNameLength, ref dir_name_offset);
                    int index = dir.FirstIndex;
                    int name_offset = dir.NamesOffset;
                    for (int i = 0; i < dir.FileCount; ++i)
                    {
                        string name = GetName (file_names, section.FileNameLength, ref name_offset);
                        name = Path.Combine (dir_name, name);
                        name = Path.ChangeExtension (name, section.Extension);
                        var entry = FormatCatalog.Instance.Create<TcdEntry> (name);
                        entry.Offset = offsets[index];
                        entry.Size = offsets[index+1] - offsets[index];
                        entry.Index = index;
                        ++index;
                        list.Add (entry);
                    }
                }
            }
            return list;
        }

        IList<TcdSection> ReadSections (int count)
        {
            var sections = new List<TcdSection> (count);
            uint current_offset = 8;
            for (int i = 0; i < count; ++i)
            {
                m_input.BaseStream.Position = current_offset;
                var section = ReadSection (i);
                if (section != null)
                    sections.Add (section);
                current_offset += 0x20;
            }
            return sections;
        }

        void DecryptNames (byte[] buffer, byte key)
        {
            for (int i = 0; i < buffer.Length; ++i)
                buffer[i] -= key;
        }

        protected abstract TcdSection ReadSection (int number);

        protected abstract string GetName (byte[] names, int name_length, ref int offset);

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }

    internal class TcdReaderV2 : TcdIndexReader
    {
        public TcdReaderV2 (ArcView file) : base (file, 4)
        {
        }

        protected override TcdSection ReadSection (int number)
        {
            uint data_size = Input.ReadUInt32();
            if (0 == data_size)
                return null;
            var section = new TcdSection { DataSize = data_size };
            section.Extension       = Extensions[number];
            section.FileCount       = Input.ReadInt32();
            section.DirCount        = Input.ReadInt32();
            section.IndexOffset     = Input.ReadUInt32();
            section.DirNameLength   = Input.ReadInt32();
            section.FileNameLength  = Input.ReadInt32();
            section.DirNamesSize    = section.DirNameLength;
            section.FileNamesSize   = section.FileNameLength;
            return section;
        }

        protected override string GetName (byte[] names, int name_length, ref int offset)
        {
            int name_end = Array.IndexOf<byte> (names, 0, offset);
            if (-1 == name_end)
                name_end = names.Length;
            name_length = name_end - offset;
            string name = Encodings.cp932.GetString (names, offset, name_length);
            offset += name_length + 1;
            return name;
        }
    }

    internal class TcdReaderV3 : TcdIndexReader
    {
        public TcdReaderV3 (ArcView file) : base (file, 5)
        {
        }

        protected override TcdSection ReadSection (int number)
        {
            uint data_size = Input.ReadUInt32();
            uint index_offset = Input.ReadUInt32();
            if (0 == index_offset)
                return null;
            var section = new TcdSection { DataSize = data_size };
            section.Extension       = Extensions[number];
            section.IndexOffset     = index_offset;
            section.DirCount        = Input.ReadInt32();
            section.DirNameLength   = Input.ReadInt32();
            section.FileCount       = Input.ReadInt32();
            section.FileNameLength  = Input.ReadInt32();
            section.DirNamesSize    = section.DirNameLength * section.DirCount;
            section.FileNamesSize   = section.FileNameLength * section.FileCount;
            return section;
        }

        protected override string GetName (byte[] names, int name_length, ref int offset)
        {
            string name = Binary.GetCString (names, offset, name_length);
            offset += name_length;
            return name;
        }
    }
}
