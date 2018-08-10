//! \file       ArcPAK.cs
//! \date       2018 Feb 27
//! \brief      AGSI resource archive.
//
// Copyright (C) 2018 by morkt
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
using System.Security.Cryptography;
using GameRes.Compression;
using GameRes.Cryptography;
using GameRes.Utility;

// Advanced Game Script Interpreter

namespace GameRes.Formats.FC01
{
    internal class AgsiEntry : PackedEntry
    {
        public int  Method;
        public bool IsEncrypted { get { return Method >= 3 && (Method <= 5 || Method == 7); } }
        public bool IsSpecial;
    }

    internal class AgsiArchive : ArcFile
    {
        public readonly byte[] Key;

        public AgsiArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Serializable]
    public class AgsiScheme : ResourceScheme
    {
        public IDictionary<string, IDictionary<string, byte[]>> KnownSchemes;
    };

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/AGSI"; } }
        public override string Description { get { return "AGSI engine resource archive"; } }
        public override uint     Signature { get { return 0x24A02028; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PakOpener ()
        {
            Signatures = new uint[] { 0x4B434150, 0x24A02028, 0 }; // 'PACK'
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var reader = IndexReader.Create (file);
            if (null == reader)
                return null;
            var dir = reader.ReadIndex();
            if (null == dir)
                return null;
            if (dir.Cast<AgsiEntry>().Any (e => e.IsEncrypted))
            {
                var scheme = QueryScheme (file);
                if (null == scheme)
                    return null;
                var arc_name = Path.GetFileName (file.Name).ToLowerInvariant();
                byte[] key;
                if (scheme.TryGetValue (arc_name, out key) && key != null)
                    return new AgsiArchive (file, this, dir, key);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var aent = (AgsiEntry)entry;
            var aarc = arc as AgsiArchive;
            Stream input;
            if (!aent.IsEncrypted)
                input = arc.File.CreateStream (entry.Offset, entry.Size);
            else if (aarc != null && aarc.Key != null)
                input = OpenEncryptedEntry (aarc, aent);
            else
                return base.OpenEntry (arc, entry);
            switch (aent.Method)
            {
            case 0: // no compression
            case 3:
                break;
            case 1: // RLE compression
            case 4:
                break;
            case 2: // LZSS bit stream
            case 5:
                input = new PackedStream<LzBitStream> (input, new LzBitStream ((int)aent.UnpackedSize));
                break;
            case 6: // LZSS compression
            case 7:
                input = new LzssStream (input);
                break;
            }
            return input;
        }

        internal Stream OpenEncryptedEntry (AgsiArchive arc, AgsiEntry entry)
        {
            uint enc_size = entry.Size;
            if (enc_size > 1024)
            {
                enc_size = 1032;
            }
            using (var des = DES.Create())
            {
                des.Key = arc.Key;
                des.Mode = CipherMode.ECB;
                des.Padding = PaddingMode.Zeros;
                using (var enc = arc.File.CreateStream (entry.Offset, enc_size))
                using (var dec = new InputCryptoStream (enc, des.CreateDecryptor()))
                {
                    var output = new byte[enc_size];
                    dec.Read (output, 0, output.Length);
                    int header_size;
                    if (!entry.IsSpecial)
                    {
                        header_size = output.ToInt32 (output.Length-4);
                        if (header_size > entry.UnpackedSize)
                            throw new InvalidEncryptionScheme();
                    }
                    else
                        header_size = (int)entry.UnpackedSize;
                    if (!entry.IsSpecial && entry.Size > enc_size)
                    {
                        var header = new byte[header_size];
                        Buffer.BlockCopy (output, 0, header, 0, header_size);
                        var input = arc.File.CreateStream (entry.Offset + enc_size, entry.Size - enc_size);
                        return new PrefixStream (header, input);
                    }
                    else
                        return new BinMemoryStream (output, 0, header_size, entry.Name);
                }
            }
        }

        protected IDictionary<string, byte[]> QueryScheme (ArcView file)
        {
            var title = FormatCatalog.Instance.LookupGame (file.Name, "*.sb")
                     ?? FormatCatalog.Instance.LookupGame (file.Name, @"..\*.sb");
            if (string.IsNullOrEmpty (title) || !KnownSchemes.ContainsKey (title))
                return null;
            return KnownSchemes[title];
        }

        static AgsiScheme DefaultScheme = new AgsiScheme
        {
            KnownSchemes = new Dictionary<string, IDictionary<string, byte[]>>()
        };

        public IDictionary<string, IDictionary<string, byte[]>> KnownSchemes
        {
            get { return DefaultScheme.KnownSchemes; }
        }

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (AgsiScheme)value; }
        }
    }

    internal class IndexReader
    {
        ArcView     m_file;
        int         m_count;
        int         m_record_size;
        uint        m_data_offset;

        public bool IsEncrypted { get; set; }

        public IndexReader (ArcView file, int count, int record_size, bool is_encrypted)
        {
            m_file = file;
            m_count = count;
            m_record_size = record_size;
            m_data_offset = (uint)(0xC + m_count * m_record_size);
            IsEncrypted = is_encrypted;
        }

        public static IndexReader Create (ArcView file)
        {
            int count, record_size;
            bool is_encrypted = false;
            if (!file.View.AsciiEqual (0, "PACK"))
            {
                var header = file.View.ReadBytes (0, 12);
                byte k1 = file.View.ReadByte (file.MaxOffset-9);
                byte k2 = file.View.ReadByte (file.MaxOffset-6);
                DecryptHeader (header, k1, k2);
                if (!header.AsciiEqual ("PACK"))
                    return null;
                count = header.ToInt32 (4);
                record_size = header.ToInt32 (8);
                is_encrypted = true;
            }
            else
            {
                count = file.View.ReadInt32 (4);
                record_size = file.View.ReadInt32 (8);
            }
            if (!ArchiveFormat.IsSaneCount (count) || record_size <= 0x10 || record_size > 0x100)
                return null;
            var reader = new IndexReader (file, count, record_size, is_encrypted);
            if (reader.m_data_offset >= file.MaxOffset)
                return null;
            return reader;
        }

        public List<Entry> ReadIndex ()
        {
            using (var index = OpenIndex())
            {
                int name_size = m_record_size - 0x10;
                var dir = new List<Entry> (m_count);
                for (int i = 0; i < m_count; ++i)
                {
                    var entry = new AgsiEntry();
                    entry.UnpackedSize = index.ReadUInt32();
                    entry.Size         = index.ReadUInt32();
                    entry.Method       = index.ReadInt32();
                    entry.Offset       = index.ReadUInt32() + m_data_offset;
                    if (!entry.CheckPlacement (m_file.MaxOffset))
                        return null;
                    var name = index.ReadCString (name_size);
                    if (string.IsNullOrEmpty (name))
                        return null;
                    entry.Name = name;
                    entry.Type = FormatCatalog.Instance.GetTypeFromName (name);
                    entry.IsPacked = entry.Method != 0 && entry.Method != 3;
                    entry.IsSpecial = name.Equals ("Copyright.Dat", StringComparison.OrdinalIgnoreCase);
                    dir.Add (entry);
                }
                return dir;
            }
        }

        IBinaryStream OpenIndex ()
        {
            int index_size = m_count * m_record_size;
            if (IsEncrypted)
            {
                var index = m_file.View.ReadBytes (12, (uint)index_size);
                DecryptIndex (index, 0, index_size, 7524u);
                return new BinMemoryStream (index);
            }
            else
                return m_file.CreateStream (12, (uint)index_size);
        }

        static void DecryptHeader (byte[] header, byte k1, byte k2)
        {
            int shift = k2 & 7;
            if (0 == shift)
                shift = 1;
            for (int i = 0; i < header.Length; ++i)
            {
                byte x = Binary.RotByteL (header[i], shift);
                header[i] = (byte)(x ^ k1++);
            }
        }

        static void DecryptIndex (byte[] data, int pos, int length, uint seed)
        {
            var rnd = new MersenneTwister (seed);
            for (int i = 0; i < length; ++i)
            {
                uint key = rnd.Rand();
                int shift = (int)key & 7;
                if (0 == shift)
                    shift = 1;
                byte x = Binary.RotByteL (data[pos+i], shift);
                data[pos+i] = (byte)(key ^ x);
            }
        }
    }

    internal sealed class LzBitStream : Decompressor
    {
        MsbBitStream    m_input;
        int             m_unpacked_size;

        public LzBitStream ()
        {
        }

        public LzBitStream (int unpacked_size)
        {
            m_unpacked_size = unpacked_size;
        }

        public override void Initialize (Stream input)
        {
            m_input = new MsbBitStream (input, true);
        }

        protected override IEnumerator<int> Unpack ()
        {
            var frame = new byte[0x1000];
            int dst = 0;
            int frame_pos = 1;
            while (dst < m_unpacked_size)
            {
                int bit = m_input.GetNextBit();
                if (bit != 0)
                {
                    if (-1 == bit)
                        yield break;
                    int v = m_input.GetBits (8);
                    if (-1 == v)
                        yield break;
                    frame[frame_pos++ & 0xFFF] = m_buffer[m_pos++] = (byte)v;
                    dst++;
                    if (0 == --m_length)
                        yield return m_pos;
                }
                else
                {
                    int offset = m_input.GetBits (12);
                    if (-1 == offset)
                        yield break;
                    int count = m_input.GetBits (4);
                    if (-1 == count)
                        yield break;
                    count += 2;
                    dst += count;
                    while (count --> 0)
                    {
                        byte v = frame[offset++ & 0xFFF];
                        frame[frame_pos++ & 0xFFF] = v;
                        m_buffer[m_pos++] = v;
                        if (0 == --m_length)
                            yield return m_pos;
                    }
                }
            }
        }

        bool m_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                {
                    m_input.Dispose();
                    m_disposed = true;
                }
                base.Dispose (disposing);
            }
        }
    }
}
