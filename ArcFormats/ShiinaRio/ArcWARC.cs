//! \file       ArcWARC.cs
//! \date       Fri Apr 10 03:10:42 2015
//! \brief      ShiinaRio engine archive format.
//
// Copyright (C) 2015-2017 by morkt
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
using GameRes.Compression;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.ShiinaRio // 椎名里緒
{
    internal class WarOptions : ResourceOptions
    {
        public EncryptionScheme Scheme { get; set; }
    }

    internal class WarcEntry : PackedEntry
    {
        public long FileTime;
        public uint Flags;
    }

    internal class WarcFile : ArcFile
    {
        public readonly Decoder Decoder;

        public WarcFile (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, Decoder decoder)
            : base (arc, impl, dir)
        {
            this.Decoder = decoder;
        }
    }

    [Serializable]
    public class WarcScheme : ResourceScheme
    {
        public EncryptionScheme[] KnownSchemes;
    }

    [Export(typeof(ArchiveFormat))]
    public class WarOpener : ArchiveFormat
    {
        public override string         Tag { get { return "WAR"; } }
        public override string Description { get { return "ShiinaRio engine resource archive"; } }
        public override uint     Signature { get { return 0x43524157; } } // 'WARC'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ResourceScheme Scheme
        {
            get { return new WarcScheme { KnownSchemes = Decoder.KnownSchemes }; }
            set { Decoder.KnownSchemes = ((WarcScheme)value).KnownSchemes; }
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, " 1."))
                return null;
            int version = file.View.ReadByte (7) - 0x30;
            if (version < 1 || version > 7)
                return null;
            version = 100 + version * 10;
            uint index_offset = 0xF182AD82u ^ file.View.ReadUInt32 (8);
            if (index_offset >= file.MaxOffset)
                return null;

            EncryptionScheme scheme;
            if (version > 110)
                scheme = QueryEncryption (file.Name);
            else
                scheme = EncryptionScheme.Warc110;
            if (null == scheme)
                return null;
            var decoder = new Decoder (version, scheme);

            uint max_index_len = decoder.MaxIndexLength;
            uint index_length = (uint)Math.Min (max_index_len, file.MaxOffset - index_offset);
            if (index_length < 8)
                return null;
            var enc_index = new byte[max_index_len];
            if (index_length != file.View.Read (index_offset, enc_index, 0, index_length))
                return null;
            decoder.DecryptIndex (index_offset, enc_index);
            Stream index;
            if (version >= 170)
            {
                if (0x78 != enc_index[8]) // check if it looks like ZLib stream
                    return null;
                var zindex = new MemoryStream (enc_index, 8, (int)index_length-8);
                index = new ZLibStream (zindex, CompressionMode.Decompress);
            }
            else if (version >= 120)
            {
                var unpacked = new byte[max_index_len];
                index_length = UnpackRNG (enc_index, 0, index_length, unpacked);
                if (0 == index_length)
                    return null;
                index = new MemoryStream (unpacked, 0, (int)index_length);
            }
            else
            {
                index = new MemoryStream (enc_index, 0, (int)index_length);
            }
            using (var header = new BinaryReader (index))
            {
                byte[] name_buf = new byte[decoder.EntryNameSize];
                var dir = new List<Entry> ();
                var unique_names = new HashSet<string>();
                while (name_buf.Length == header.Read (name_buf, 0, name_buf.Length))
                {
                    var name = Binary.GetCString (name_buf, 0, name_buf.Length);
                    var entry = FormatCatalog.Instance.Create<WarcEntry> (name);
                    entry.Offset       = header.ReadUInt32();
                    entry.Size         = header.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    entry.UnpackedSize = header.ReadUInt32();
                    entry.IsPacked     = entry.Size != entry.UnpackedSize;
                    entry.FileTime     = header.ReadInt64();
                    entry.Flags        = header.ReadUInt32();
                    if (0 != name.Length && name_buf[0] < 0x80 && unique_names.Add (name))
                        dir.Add (entry);
                }
                if (0 == dir.Count)
                    return null;
                return new WarcFile (file, this, dir, decoder);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var warc = arc as WarcFile;
            var wentry = entry as WarcEntry;
            if (null == warc || null == wentry || entry.Size < 8)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var enc_data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            if (enc_data.Length <= 8)
                return new BinMemoryStream (enc_data, entry.Name);
            uint sig = LittleEndian.ToUInt32 (enc_data, 0);
            uint unpacked_size = LittleEndian.ToUInt32 (enc_data, 4);
            if (warc.Decoder.WarcVersion > 110)
            {
                sig ^= (unpacked_size ^ 0x82AD82) & 0xFFFFFF;
                if (0 != (wentry.Flags & 0x80000000u)) // encrypted entry
                    warc.Decoder.Decrypt (enc_data, 8, entry.Size-8);
                if (warc.Decoder.ExtraCrypt != null)
                    warc.Decoder.ExtraCrypt.Decrypt (enc_data, 8, entry.Size-8, 0x202);
                if (0 != (wentry.Flags & 0x20000000u))
                    warc.Decoder.Decrypt2 (enc_data, 8, entry.Size-8);
            }
            byte[] unpacked = enc_data;
            UnpackMethod unpack = null;
            switch (sig & 0xffffff)
            {
            case 0x314859: // 'YH1'
                unpack = UnpackYH1;
                break;
            case 0x4b5059: // 'YPK'
                unpack = UnpackYPK;
                break;
            case 0x5a4c59: // 'YLZ'
                unpack = UnpackYLZ;
                break;
            }
            if (null != unpack)
            {
                unpacked = new byte[unpacked_size];
                unpack (enc_data, unpacked);
                if (warc.Decoder.WarcVersion > 110)
                {
                    if (0 != (wentry.Flags & 0x40000000))
                        warc.Decoder.Decrypt2 (unpacked, 0, (uint)unpacked.Length);
                    if (warc.Decoder.ExtraCrypt != null)
                        warc.Decoder.ExtraCrypt.Decrypt (unpacked, 0, (uint)unpacked.Length, 0x204);
                }
            }
            return new BinMemoryStream (unpacked, entry.Name);
        }

        delegate void UnpackMethod (byte[] input, byte[] output);

        void UnpackYH1 (byte[] input, byte[] output)
        {
            if (0 != input[3])
            {
                uint key = 0x6393528e^0x4b4du; // 'KM'
                unsafe
                {
                    fixed (byte* buf_raw = input)
                    {
                        uint* encoded = (uint*)buf_raw;
                        int i;
                        for (i = 2; i < input.Length/4; ++i)
                            encoded[i] ^= key;
                    }
                }
            }
            var decoder = new HuffmanReader (input, 8, input.Length-8, output);
            decoder.Unpack();
        }

        void UnpackYPK (byte[] input, byte[] output)
        {
            if (0 != input[3])
            {
                uint key = ~0x4b4d4b4du; // 'KMKM'
                unsafe
                {
                    fixed (byte* buf_raw = input)
                    {
                        uint* encoded = (uint*)buf_raw;
                        int i;
                        for (i = 2; i < input.Length/4; ++i)
                            encoded[i] ^= key;
                        for (i *= 4; i < input.Length; ++i)
                            buf_raw[i] ^= (byte)key;
                    }
                }
            }
            if (0x78 != input[8])
                throw new ApplicationException ("Invalid decryption scheme");
            var src = new MemoryStream (input, 8, input.Length-8);
            using (var zlib = new ZLibStream (src, CompressionMode.Decompress))
                zlib.Read (output, 0, output.Length);
        }

        void UnpackYLZ (byte[] input, byte[] output)
        {
            if (0 != input[3])
            {
                uint key = 0x4b4d4b4du; // 'KMKM'
                unsafe
                {
                    fixed (byte* buf_raw = input)
                    {
                        uint* encoded = (uint*)buf_raw;
                        int i;
                        for (i = 2; i < input.Length/4; ++i)
                            encoded[i] ^= key;
                        for (i *= 4; i < input.Length; ++i)
                        {
                            buf_raw[i] ^= (byte)key;
                            key >>= 8;
                        }
                    }
                }
            }
            var decoder = new YlzReader (input, 8, output);
            decoder.Unpack();
        }

        uint UnpackRNG (byte[] input, int in_start, uint input_size, byte[] output)
        {
            var coder = new Kogado.CRangeCoder();
            coder.InitQSModel (257, 12, 2000, null, false);
            return coder.Decode (output, 0, (uint)output.Length, input, (uint)in_start, input_size);
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new WarOptions {
                Scheme = GetScheme (Properties.Settings.Default.WARCScheme),
            };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetWARC();
        }

        EncryptionScheme QueryEncryption (string arc_name)
        {
            EncryptionScheme scheme = null;
            var title = FormatCatalog.Instance.LookupGame (arc_name);
            if (!string.IsNullOrEmpty (title))
                scheme = GetScheme (title);
            if (null == scheme)
            {
                var options = Query<WarOptions> (arcStrings.ArcEncryptedNotice);
                scheme = options.Scheme;
            }
            return scheme;
        }

        static EncryptionScheme GetScheme (string scheme)
        {
            return Decoder.KnownSchemes.FirstOrDefault (s => s.Name == scheme);
        }
    }

    internal class YlzReader
    {
        byte[]  m_input;
        byte[]  m_output;
        int     m_src;
        uint    m_ctl = 0;
        uint    m_mask = 0;

        public YlzReader (byte[] input, int src_offset, byte[] output)
        {
            m_input = input;
            m_src = src_offset;
            m_output = output;
        }

        bool GetCtlBit ()
        {
            bool bit = 0 != (m_ctl & m_mask);
            m_mask >>= 1;
            if (0 == m_mask)
            {
                m_ctl = LittleEndian.ToUInt32 (m_input, m_src);
                m_src += 4;
                m_mask = 0x80000000;
            }
            return bit;
        }

        int GetBits (int n)
        {
            int v = 0;
            for (int i = 0; i < n; ++i)
            {
                v <<= 1;
                if (GetCtlBit())
                    v |= 1;
            }
            return v;
        }

        public void Unpack ()
        {
            GetCtlBit();
            int dst = 0;
            while (dst < m_output.Length)
            {
                if (GetCtlBit())
                {
                    m_output[dst++] = m_input[m_src++];
                    continue;
                }
                bool next_bit = GetCtlBit();
                int offset = m_input[m_src++] | ~0xffff;
                int ah = 0xff;
                int count = 0;
                if (next_bit) // 5e
                {
                    if (GetCtlBit()) // 10d
                    {
                        ah = (ah << 1) | GetBits (1);
                    }
                    else if (GetCtlBit()) // 13e
                    {
                        ah = (ah << 1) | GetBits (1);
                        offset -= 0x200;
                    }
                    else if (GetCtlBit()) // 174
                    {
                        ah = (ah << 2) | GetBits (2);
                        offset -= 0x400;
                    }
                    else if (GetCtlBit()) // 1c0
                    {
                        ah = (ah << 3) | GetBits (3);
                        offset -= 0x800;
                    }
                    else
                    {
                        ah = (ah << 4) | GetBits (4);
                        offset -= 0x1000;
                    }

                    if (GetCtlBit()) // 296
                    {
                        count = 3;
                    }
                    else if (GetCtlBit()) // 2a2
                    {
                        count = 4;
                    }
                    else if (GetCtlBit()) // 2c2
                    {
                        count = 5 + GetBits (1);
                    }
                    else if (GetCtlBit()) // 2f8
                    {
                        count = 7 + GetBits (2);
                    }
                    else if (GetCtlBit()) // 33f
                    {
                        count = 0x0b + GetBits (3);
                    }
                    else
                    {
                        count = 0x13 + m_input[m_src++];
                    }
                }
                else if (GetCtlBit()) // 94
                {
                    ah <<= 3; // b2
                    ah |= GetBits (3);
                    ah = (ah - 1) & 0xff;
                    count = 2;
                }
                else if (0xff == (offset & 0xff))
                {
                    return;
                }
                else
                {
                    count = 2;
                }
                offset += (ah & 0xff) << 8; // 280
                Binary.CopyOverlapped (m_output, dst + offset, dst, count);
                dst += count;
            }
        }
    }

    internal class HuffmanReader
    {
        byte[] m_src;
        byte[] m_dst;

        ushort[,] m_tree = new ushort[2,511];

        int m_origin;
        int m_total;
        int m_input_pos;
        int m_remaining;
        int m_curbits;
        uint m_cache;
        ushort m_curindex;

        public HuffmanReader (byte[] src, int index, int length, byte[] dst)
        {
            m_src = src;
            m_dst = dst;
            m_origin = index;
            m_total = length;
        }

        public HuffmanReader (byte[] src, byte[] dst) : this (src, 0, src.Length, dst)
        {
        }

        public byte[] Unpack ()
        {
            m_input_pos = m_origin;
            m_remaining = m_total;
            m_curbits = 0;
            m_curindex = 256;
            ushort root = CreateTree();
            for (int i = 0; i < m_dst.Length; ++i)
            {
                ushort symbol = root;
                while (symbol >= 256)
                {
                    symbol = m_tree[GetBits(1), symbol];
                }
                m_dst[i] = (byte)symbol;
            }
            return m_dst;
        }

        uint ReadUInt32 ()
        {
            uint v;
            if (m_remaining >= 4)
            {
                v = LittleEndian.ToUInt32 (m_src, m_input_pos);
                m_input_pos += 4;
                m_remaining -= 4;
            }
            else if (m_remaining > 0)
            {
                v = m_src[m_input_pos++];
                int shift = 8;
                while (--m_remaining != 0)
                {
                    v |= (uint)(m_src[m_input_pos++] << shift);
                    shift += 8;
                }
            }
            else
                throw new InvalidFormatException ("Unexpected end of file");
            return v;
        }

        uint GetBits (int req_bits)
        {
            uint ret_val = 0;
            if (req_bits > m_curbits)
            {
                req_bits -= m_curbits;
                ret_val |= (m_cache & ((1u << m_curbits) - 1u)) << req_bits;
                m_cache = ReadUInt32();
                m_curbits = 32;
            }
            m_curbits -= req_bits;
            return ret_val | ((1u << req_bits) - 1u) & (m_cache >> m_curbits);
        }

        ushort CreateTree ()
        {
            ushort i;
            if (0 != GetBits (1))
            {
                i = m_curindex++;
                m_tree[0,i] = CreateTree();
                m_tree[1,i] = CreateTree();
            }
            else
                i = (ushort)GetBits (8);
            return i;
        }
    }
}
