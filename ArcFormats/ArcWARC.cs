//! \file       ArcWARC.cs
//! \date       Fri Apr 10 03:10:42 2015
//! \brief      ShiinaRio engine archive format.
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
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using GameRes.Formats.Properties;
using GameRes.Formats.Strings;
using GameRes.Utility;
using ZLibNet;

namespace GameRes.Formats.ShiinaRio
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

    [Export(typeof(ArchiveFormat))]
    public class WarOpener : ArchiveFormat
    {
        public override string         Tag { get { return "WAR"; } }
        public override string Description { get { return "ShiinaRio engine resource archive"; } }
        public override uint     Signature { get { return 0x43524157; } } // 'WARC'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, " 1."))
                return null;
            int version = file.View.ReadByte (7) - 0x30;
            version = 100 + version * 10;
            if (170 != version)
                throw new NotSupportedException ("Not supported WARC version");
            uint index_offset = 0xf182ad82u ^ file.View.ReadUInt32 (8);
            if (index_offset >= file.MaxOffset)
                return null;

            var scheme = QueryEncryption();
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
                if (0x78 != enc_index[8]) // FIXME: check if it looks like ZLib stream
                    return null;
                var zindex = new MemoryStream (enc_index, 8, (int)index_length-8);
                index = new ZLibStream (zindex, CompressionMode.Decompress);
            }
            else
            {
                var unpacked = new byte[max_index_len];
                index_length = UnpackRNG (enc_index, unpacked);
                if (0 == index_length)
                    return null;
                index = new MemoryStream (unpacked, 0, (int)index_length);
            }
            using (var header = new BinaryReader (index))
            {
                byte[] name_buf = new byte[decoder.EntryNameSize];
                var dir = new List<Entry> ();
                while (name_buf.Length == header.Read (name_buf, 0, name_buf.Length))
                {
                    var name = Binary.GetCString (name_buf, 0, name_buf.Length);
                    var entry = new WarcEntry {
                        Name = name,
                        Type = FormatCatalog.Instance.GetTypeFromName (name)
                    };
                    entry.Offset       = header.ReadUInt32();
                    entry.Size         = header.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    entry.UnpackedSize = header.ReadUInt32();
                    entry.IsPacked     = entry.Size != entry.UnpackedSize;
                    entry.FileTime     = header.ReadInt64();
                    entry.Flags        = header.ReadUInt32();
                    if (0 != name.Length)
                        dir.Add (entry);
                }
                if (0 == dir.Count)
                    return null;
                return new WarcFile (file, this, dir, decoder);
            }
        }

        private void Dump (string name, byte[] data)
        {
            using (var dump = File.Create (name))
                dump.Write (data, 0, data.Length);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var warc = arc as WarcFile;
            var wentry = entry as WarcEntry;
            if (null == warc || null == wentry || entry.Size < 8)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var enc_data = new byte[entry.Size];
            if (entry.Size != arc.File.View.Read (entry.Offset, enc_data, 0, entry.Size))
                return Stream.Null;
            uint sig = LittleEndian.ToUInt32 (enc_data, 0);
            uint unpacked_size = LittleEndian.ToUInt32 (enc_data, 4);
            sig ^= (unpacked_size ^ 0x82AD82) & 0xffffff;

            if (0 != (wentry.Flags & 0x80000000u) && entry.Size > 8) // encrypted entry
                warc.Decoder.Decrypt (enc_data, 8, entry.Size-8);
            if (0 != (wentry.Flags & 0x20000000u) && entry.Size > 8)
                warc.Decoder.Decrypt2 (enc_data, 8, entry.Size-8);

            bool perform_post_crypt = 0 != (wentry.Flags & 0x40000000);
            byte[] unpacked;
            if (0x314859 == (sig & 0xffffff)) // 'YH1'
            {
                unpacked = new byte[unpacked_size];
                UnpackYH1 (enc_data, unpacked);
            }
            else if (0x4b5059 == (sig & 0xffffff)) // 'YPK'
            {
                unpacked = new byte[unpacked_size];
                UnpackYPK (enc_data, unpacked);
            }
            else
            {
                unpacked = enc_data;
                perform_post_crypt = false;
            }
            if (perform_post_crypt)
            {
                warc.Decoder.Decrypt2 (unpacked, 0, (uint)unpacked.Length);
                if (warc.Decoder.SchemeVersion >= 2490)
                    warc.Decoder.Decrypt3 (unpacked, 0, (uint)unpacked.Length);
            }
            return new MemoryStream (unpacked);
        }

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
                var decoder = new HuffmanReader (input, 8, input.Length-8, output);
                decoder.Unpack();
            }
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
            var src = new MemoryStream (input, 8, input.Length-8);
            using (var zlib = new ZLibStream (src, CompressionMode.Decompress))
                zlib.Read (output, 0, output.Length);
        }

        uint UnpackRNG (byte[] input, byte[] output)
        {
            var coder = new Kogado.CRangeCoder();
            coder.InitQSModel (257, 12, 2000, null, false);
            return coder.Decode (output, 0, (uint)output.Length, input, 1, (uint)input.Length-1);
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new WarOptions {
                Scheme = GetScheme (Settings.Default.WARCScheme),
            };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetWARC();
        }

        EncryptionScheme QueryEncryption ()
        {
            var options = Query<WarOptions> (arcStrings.ArcEncryptedNotice);
            return options.Scheme;
        }

        static EncryptionScheme GetScheme (string scheme)
        {
            return Decoder.KnownSchemes.Where (s => s.Name == scheme).FirstOrDefault();
        }
    }

    internal class HuffmanReader
    {
        byte[] m_src;
        byte[] m_dst;

        uint[] lhs = new uint[511];
        uint[] rhs = new uint[511];

        int m_origin;
        int m_total;
        int m_input_pos;
        int m_remaining;
        int m_curbits;
        uint m_cache;
        uint m_curindex;

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
            uint index = CreateTree();
            for (int i = 0; i < m_dst.Length; ++i)
            {
                uint idx = index;
                while (idx >= 256)
                {
                    uint is_right;

                    if (--m_curbits < 0)
                    {
                        m_curbits = 31;
                        m_cache = ReadUInt32();
                        is_right = m_cache >> 31;
                    }
                    else
                        is_right = (m_cache >> m_curbits) & 1;		

                    if (0 != is_right)
                        idx = rhs[idx];
                    else
                        idx = lhs[idx];
                }
                m_dst[i] = (byte)idx;
            }
            return m_dst;
        }

        uint ReadUInt32 ()
        {
            if (0 == m_remaining)
                throw new InvalidFormatException ("Unexpected end of file");
            uint v;
            if (m_remaining >= 4)
            {
                v = LittleEndian.ToUInt32 (m_src, m_input_pos);
                m_input_pos += 4;
                m_remaining -= 4;
            }
            else
            {
                v = m_src[m_input_pos++];
                int shift = 8;
                while (--m_remaining != 0)
                {
                    v |= (uint)(m_src[m_input_pos++] << shift);
                    shift += 8;
                }
            }
            return v;
        }

        uint GetBits (int req_bits)
        {
            uint ret_val = 0;
            if (req_bits > m_curbits)
            {
                do
                {
                    req_bits -= m_curbits;
                    ret_val |= (m_cache & ((1u << m_curbits) - 1u)) << req_bits;
                    m_cache = ReadUInt32();
                    m_curbits = 32;
                }
                while (req_bits > 32);
            }
            m_curbits -= req_bits;
            return ret_val | ((1u << req_bits) - 1u) & (m_cache >> m_curbits);
        }

        uint CreateTree ()
        {
            uint not_leaf;

            if (m_curbits-- < 1)
            {
                m_curbits = 31;
                m_cache = ReadUInt32();
                not_leaf = m_cache >> 31;
            }
            else
                not_leaf = (m_cache >> m_curbits) & 1;

            uint i;
            if (0 != not_leaf)
            {
                i = m_curindex++;
                lhs[i] = CreateTree();
                rhs[i] = CreateTree();
            }
            else
                i = GetBits (8);
            return i;
        }
    }

    internal class CachedResource
    {
        Dictionary<string, byte[]> ResourceCache = new Dictionary<string, byte[]>();
        Dictionary<string, byte[]> RegionCache   = new Dictionary<string, byte[]>();

        public static Stream Open (string name)
        {
            var assembly = typeof(CachedResource).Assembly;
            var stream = assembly.GetManifestResourceStream ("GameRes.Formats.Resources." + name);
            if (null == stream)
                throw new FileNotFoundException ("Resource not found", name);
            return stream;
        }

        public byte[] Load (string name)
        {
            byte[] res;
            if (!ResourceCache.TryGetValue (name, out res))
            {
                using (var stream = Open (name))
                {
                    res = new byte[stream.Length];
                    stream.Read (res, 0, res.Length);
                    ResourceCache[name] = res;
                }
            }
            return res;
        }

        // FIXME: this approach disregards possible differences in regions width or height
        public byte[] LoadRegion (string name, int width, int height)
        {
            byte[] region;
            if (!RegionCache.TryGetValue (name, out region))
            {
                using (var png = Open (name))
                {
                    region = new byte[width*height*4];
                    var decoder = new PngBitmapDecoder (png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    var bitmap = decoder.Frames[0];
                    width  = Math.Min (width, bitmap.PixelWidth);
                    height = Math.Min (height, bitmap.PixelHeight);
                    int stride = bitmap.PixelWidth * bitmap.Format.BitsPerPixel / 8;
                    Int32Rect rect = new Int32Rect (0, 0, width, height);
                    bitmap.CopyPixels (rect, region, stride, 0);
                }
            }
            return region;
        }
    }

    internal class EncryptionScheme
    {
        public string Name { get; set; }
        public    int Version;
        public    int EntryNameSize;
        public byte[] CryptKey;
        public uint[] HelperKey;
        public byte[] ShiinaImage;
        public byte[] Region;
        public byte[] DecodeBin;

        private static CachedResource Resource = new CachedResource();

        public static EncryptionScheme Create (string name, int version, int entry_name_size,
                                               string key1, uint[] key2,
                                               string image, string region_src, string decode_bin = null)
        {
            var scheme = new EncryptionScheme
            {
                Name = name,
                Version = version,
                EntryNameSize = entry_name_size,
                CryptKey = Encodings.cp932.GetBytes (key1),
                HelperKey = key2,
                ShiinaImage = Resource.Load (image),
                Region = Resource.LoadRegion (region_src, 48, 48),
            };
            if (null != decode_bin)
                scheme.DecodeBin = Resource.Load (decode_bin);
            return scheme;
        }
    }

    internal class Decoder
    {
        EncryptionScheme    m_scheme;

        public int   SchemeVersion { get { return m_scheme.Version; } }
        public int     WarcVersion { get; private set; }
        public uint MaxIndexLength { get; private set; }
        public int   EntryNameSize { get { return m_scheme.EntryNameSize; } }

        private uint          Rand { get; set; }

        public Decoder (int version, EncryptionScheme scheme)
        {
            m_scheme = scheme;
            WarcVersion = version;
            MaxIndexLength = GetMaxIndexLength (version);
        }

        public void Decrypt (byte[] data, int index, uint data_length)
        {
            if (data_length < 3)
                return;
            uint effective_length = Math.Min (data_length, 1024u);
            int a, b;
            uint fac = 0;
            if (WarcVersion > 120)
            {
                Rand = data_length;
                a = (sbyte)data[index]   ^ (sbyte)data_length;
                b = (sbyte)data[index+1] ^ (sbyte)(data_length / 2);
                if (data_length != MaxIndexLength)
                {
                    // ... regular entry decryption
                    int idx = (int)((double)NextRand() * (m_scheme.ShiinaImage.Length / 4294967296.0));
                    if (WarcVersion >= 160)
                    {
                        fac = Rand + m_scheme.ShiinaImage[idx];
                        fac = DecryptHelper3 (fac) & 0xfffffff;
                        if (effective_length > 0x80)
                        {
                            DecryptHelper4 (data, index+4, m_scheme.HelperKey);
                            index += 0x80;
                            effective_length -= 0x80;
                        }
                    }
                    else if (150 == WarcVersion)
                    {
                        fac = Rand + m_scheme.ShiinaImage[idx];
                        fac ^= (fac & 0xfff) * (fac & 0xfff);
                        uint v = 0;
                        for (int i = 0; i < 32; ++i)
                        {
                            uint bit = fac & 1;
                            fac >>= 1;
                            if (0 != bit)
                                v += fac;
                        }
                        fac = v;
                    }
                    else if (140 == WarcVersion)
                    {
                        fac = m_scheme.ShiinaImage[idx];
                    }
                    else if (130 == WarcVersion)
                    {
                        fac = m_scheme.ShiinaImage[idx & 0xff];
                    }
                }
            }
            else
            {
                a = data[index];
                b = data[index+1];
            }
            Rand ^= (uint)(DecryptHelper1 (a) * 100000000.0);

            double token = 0.0;
            if (0 != (a|b))
            {
                token = Math.Acos ((double)a / Math.Sqrt ((double)(a*a + b*b)));
                token = token / Math.PI * 180.0;
            }
            if (b < 0)
                token = 360.0 - token;

            uint x = (fac + (byte)DecryptHelper2 (token)) % (uint)m_scheme.CryptKey.Length;
            int n = 0;
            for (int i = 2; i < effective_length; ++i)
            {
                byte d = data[index+i];
                if (WarcVersion > 120)
                    d ^= (byte)((double)NextRand() / 16777216.0);
                else
                    d ^= (byte)((double)NextRand() / 4294967296.0); // ? effectively a no-op
                d = (byte)(((d & 1) << 7) | (d >> 1));
                d ^= (byte)(m_scheme.CryptKey[n++] ^ m_scheme.CryptKey[x]);
                data[index+i] = d;
                x = d % (uint)m_scheme.CryptKey.Length;
                if (n >= m_scheme.CryptKey.Length)
                    n = 0;
            }
        }

        public void DecryptIndex (uint index_offset, byte[] enc_index)
        {
            Decrypt (enc_index, 0, (uint)enc_index.Length);
            unsafe
            {
                fixed (byte* buf_raw = enc_index)
                {
                    uint* encoded = (uint*)buf_raw;
                    for (int i = 0; i < enc_index.Length/4; ++i)
                        encoded[i] ^= index_offset;
                    if (WarcVersion >= 170)
                    {
                        byte key = (byte)~WarcVersion;
                        for (int i = 0; i < enc_index.Length; ++i)
                            buf_raw[i] ^= key;
                    }
                }
            }
        }

        public void Decrypt2 (byte[] data, int index, uint length)
        {
            if (length < 0x400 || null == m_scheme.DecodeBin)
                return;
            uint crc = 0xffffffff;
            for (int i = 0; i < 0x100; ++i)
            {
                crc ^= (uint)data[index++] << 24;
                for (int j = 0; j < 8; ++j)
                {
                    uint bit = crc & 0x80000000u;
                    crc <<= 1;
                    if (0 != bit)
                        crc ^= 0x04c11db7;
                }
            }
            for (int i = 0; i < 0x40; ++i)
            {
                uint src = LittleEndian.ToUInt32 (data, index) & 0x1ffcu;
                src = LittleEndian.ToUInt32 (m_scheme.DecodeBin, (int)src);
                uint key = src ^ crc;
                data[index++ + 0x100] ^= (byte)key;
                data[index++ + 0x100] ^= (byte)(key >> 8);
                data[index++ + 0x100] ^= (byte)(key >> 16);
                data[index++ + 0x100] ^= (byte)(key >> 24);
            }
        }

        public void Decrypt3 (byte[] data, int index, uint length)
        {
            if (length < 0x400)
                return;
            int src = index;
            uint key = 0;
            for (uint i = (length & 0x7eu) + 1; i != 0; --i)
            {
                key ^= data[src++];
                for (int j = 0; j < 8; ++j)
                {
                    uint bit = key & 1;
                    key = bit << 15 | key >> 1;
                    if (0 == bit)
                        key ^= 0x408;
                }
            }
            data[index+0x104] ^= (byte)key;
            data[index+0x105] ^= (byte)(key >> 8);
        }

        double DecryptHelper1 (double a)
        {
            if (a < 0)
                return -DecryptHelper1 (-a);

            double v0;
            double v1;
            if (a < 18.0)
            {
                v0 = a;
                v1 = a;
                double v2 = -(a * a);

                for (int j = 3; j < 1000; j += 2)
                {
                    v1 *= v2 / (j * (j - 1));
                    v0 += v1 / j;
                    if (v0 == v2)
                        break;
                }
                return v0;
            }

            int flags = 0;
            double v0_l = 0;
            v1 = 0;
            double div = 1 / a;
            double v1_h = 2.0;
            double v0_h = 2.0;
            double v1_l = 0;
            v0 = 0;
            int i = 0;
            
            do
            {
                v0 += div;
                div *= ++i / a;
                if (v0 < v0_h)
                    v0_h = v0;
                else
                    flags |= 1;

                v1 += div;
                div *= ++i / a;
                if (v1 < v1_h)
                    v1_h = v1;
                else
                    flags |= 2;

                v0 -= div;
                div *= ++i / a;
                if (v0 > v0_l)
                    v0_l = v0;
                else
                    flags |= 4;

                v1 -= div;
                div *= ++i / a;
                if (v1 > v1_l)
                    v1_l = v1;
                else
                    flags |= 8;
            }
            while (flags != 0xf);

            return ((Math.PI - Math.Cos(a) * (v0_l + v0_h)) - (Math.Sin(a) * (v1_l + v1_h))) / 2.0;
        }

        uint DecryptHelper2 (double a)
        {
            double v0, v1, v2, v3;

            if (a > 1.0)
            {
                v0 = Math.Sqrt (a * 2 - 1);	
                for (;;)
                {
                    v1 = 1 - (double)NextRand() / 4294967296.0;
                    v2 = 2.0 * (double)NextRand() / 4294967296.0 - 1.0;
                    if (v1 * v1 + v2 * v2 > 1.0)
                        continue;

                    v2 /= v1;
                    v3 = v2 * v0 + a - 1.0;
                    if (v3 <= 0)
                        continue;

                    v1 = (a - 1.0) * Math.Log (v3 / (a - 1.0)) - v2 * v0;
                    if (v1 < -50.0)
                        continue;

                    if (((double)NextRand() / 4294967296.0) <= (Math.Exp(v1) * (v2 * v2 + 1.0)))
                        break;
                }
            }
            else
            {
                v0 = Math.Exp(1.0) / (a + Math.Exp(1.0));
                do
                {
                    v1 = (double)NextRand() / 4294967296.0;
                    v2 = (double)NextRand() / 4294967296.0;
                    if (v1 < v0)
                    {
                        v3 = Math.Pow(v2, 1.0 / a);
                        v1 = Math.Exp(-v3);
                    } else
                    {
                        v3 = 1.0 - Math.Log(v2);
                        v1 = Math.Pow(v3, a - 1.0);
                    }
                }
                while ((double)NextRand() / 4294967296.0 >= v1);
            }

            if (WarcVersion > 120)
                return (uint)(v3 * 256.0);
            else
                return (byte)((double)NextRand() / 4294967296.0);	
        }

        [StructLayout(LayoutKind.Explicit)]
        struct Union
        {
            [FieldOffset(0)]
            public int i;
            [FieldOffset (0)]
            public uint u;
            [FieldOffset(0)]
            public float f;
            [FieldOffset(0)]
            public byte b0;
            [FieldOffset(1)]
            public byte b1;
            [FieldOffset(2)]
            public byte b2;
            [FieldOffset(3)]
            public byte b3;
        }

        uint DecryptHelper3 (uint key)
        {
            var p = new Union();
            p.u = key;
            var fv = new Union();
            fv.f = (float)(1.5 * (double)p.b0 + 0.1);
            uint v0 = Binary.BigEndian (fv.u);
            fv.f = (float)(1.5 * (double)p.b1 + 0.1);
            uint v1 = (uint)fv.f;
            fv.f = (float)(1.5 * (double)p.b2 + 0.1);
            uint v2 = (uint)-fv.i;
            fv.f = (float)(1.5 * (double)p.b3 + 0.1);
            uint v3 = ~fv.u;

            return ((v0 + v1) | (v2 - v3));
        }

        void DecryptHelper4 (byte[] data, int index, uint[] key_src)
        {
            uint[] buf = new uint[0x50];
            int i;
            for (i = 0; i < 0x10; ++i)
            {
                buf[i] = BigEndian.ToUInt32 (data, index+40+4*i);
            }
            for (; i < 0x50; ++i)
            {
                uint v = buf[i-16];
                v ^= buf[i-14];
                v ^= buf[i-8];
                v ^= buf[i-3];
                v = v << 1 | v >> 31;
                buf[i] = v;
            }
            uint[] key = new uint[10];
            Array.Copy (key_src, key, 5);
            uint k0 = key[0];
            uint k1 = key[1];
            uint k2 = key[2];
            uint k3 = key[3];
            uint k4 = key[4];

            uint pc = 0;
            uint v26 = 0;
            int buf_idx = 0;
            for (int ebp = 0; ebp < 0x50; ++ebp)
            {
                if (ebp >= 0x10)
                {
                    if (ebp >= 0x20)
                    {
                        if (ebp >= 0x30)
                        {
                            uint v27 = ~k3;
                            if (ebp >= 0x40)
                            {
                                v26 = k1 ^ (k2 | v27);
                                pc = 0xA953FD4E;
                            }
                            else
                            {
                                v26 = k1 & k3 | k2 & v27;
                                pc = 0x8F1BBCDC;
                            }
                        }
                        else
                        {
                            v26 = k3 ^ (k1 | ~k2);
                            pc = 0x6ED9EBA1;
                        }
                    }
                    else
                    {
                        v26 = k1 & k2 | k3 & ~k1;
                        pc = 0x5A827999;
                    }
                }
                else
                {
                    v26 = k1 ^ k2 ^ k3;
                    pc = 0;
                }
                uint v28 = buf[buf_idx] + k4 + v26 + pc + (k0 << 5 | k0 >> 27);
                uint v29 = (k1 >> 2) | (k1 << 30);
                k1 = k0;
                k4 = k3;
                k3 = k2;
                k2 = v29;
                k0 = v28;
                ++buf_idx;
            }
            key[0] += k0;
            key[1] += k1;
            key[2] += k2;
            key[3] += k3;
            key[4] += k4;
            var ft = new FILETIME {
                DateTimeLow = key[1],
                DateTimeHigh = key[0] & 0x7FFFFFFF
            };
            var sys_time = new SYSTEMTIME (ft);
            key[5] = (uint)(sys_time.Year | sys_time.Month << 16);
            key[7] = (uint)(sys_time.Hour | sys_time.Minute << 16);
            key[8] = (uint)(sys_time.Second | sys_time.Milliseconds << 16);

            uint flags = LittleEndian.ToUInt32 (data, index+40) | 0x80000000;
//            uint rgb   = BigEndian.ToUInt32 (data, index+44) >> 8;
            uint rgb = buf[1] >> 8;
            if (0 == (flags & 0x78000000))
                flags |= 0x98000000;
            key[6] = RegionCrc32 (m_scheme.Region, flags, rgb);
            key[9] = (uint)(((int)key[2] * (long)(int)key[3]) >> 8);
            if (m_scheme.Version >= 2390)
                key[6] += key[9];
            unsafe
            {
                fixed (byte* data_fixed = data)
                {
                    uint* encoded = (uint*)(data_fixed+index);
                    for (i = 0; i < 10; ++i)
                    {
                        encoded[i] ^= key[i];
                    }
                }
            }
        }

        static readonly uint[] CustomCrcTable = InitCrcTable();

        static uint[] InitCrcTable ()
        {
            var table = new uint[0x100];
            for (uint i = 0; i != 256; ++i)
            {
                uint poly = i;
                for (int j = 0; j < 8; ++j)
                {
                    uint bit = poly & 1;
                    poly = poly >> 1 | poly << 31; // ror 1
                    if (0 == bit)
                        poly ^= 0x6DB88320;
                }
                table[i] = poly;
            }
            return table;
        }

        uint RegionCrc32 (byte[] src, uint flags, uint rgb)
        {
            int src_alpha = (int)flags & 0x1ff;
            int dst_alpha = (int)(flags >> 12) & 0x1ff;
            flags >>= 24;
            if (0 == (flags & 0x10))
                dst_alpha = 0;
            if (0 == (flags & 8))
                src_alpha = 0x100;
            int y_step = 0;
            int x_step = 4;
            int width = 48;
            int pos = 0;
            if (0 != (flags & 0x40)) // horizontal flip
            {
                y_step += width;
                pos += (width-1)*4;
                x_step = -x_step;
            }
            if (0 != (flags & 0x20)) // vertical flip
            {
                y_step -= width;
                pos += width*0x2f*4; // width*(height-1)*4;
            }
            y_step <<= 3;
            uint checksum = 0;
            for (int y = 0; y < 48; ++y)
            {
                for (int x = 0; x < 48; ++x)
                {
                    int alpha = src[pos+3] * src_alpha;
                    alpha >>= 8;
                    uint color = rgb;
                    for (int i = 0; i < 3; ++i)
                    {
                        int v = src[pos+i];
                        int c = (int)(color & 0xff); // rgb[i];
                        c -= v;
                        c = (c * dst_alpha) >> 8;
                        c = (c + v) & 0xff;
                        c = (c * alpha) >> 8;
                        checksum = (checksum >> 8) ^ CustomCrcTable[(c ^ checksum) & 0xff];
                        color >>= 8;
                    }
                    pos += x_step;
                }
                pos += y_step;
            }
            return checksum;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint DateTimeLow;
            public uint DateTimeHigh;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            [MarshalAs(UnmanagedType.U2)] public ushort Year;
            [MarshalAs(UnmanagedType.U2)] public ushort Month;
            [MarshalAs(UnmanagedType.U2)] public ushort DayOfWeek;
            [MarshalAs(UnmanagedType.U2)] public ushort Day;
            [MarshalAs(UnmanagedType.U2)] public ushort Hour;
            [MarshalAs(UnmanagedType.U2)] public ushort Minute;
            [MarshalAs(UnmanagedType.U2)] public ushort Second;
            [MarshalAs(UnmanagedType.U2)] public ushort Milliseconds;

            public SYSTEMTIME (FILETIME ft)
            {
                FileTimeToSystemTime (ref ft, out this);
            }

            [DllImport ("kernel32.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
            static extern bool FileTimeToSystemTime (ref FILETIME lpFileTime, out SYSTEMTIME lpSystemTime);
        }

        uint NextRand ()
        {
            Rand = 1566083941u * Rand + 1u;
            return Rand;
        }

        uint GetMaxIndexLength (int version)
        {
            int max_index_entries = version < 150 ? 8192 : 16384;
            return (uint)((m_scheme.EntryNameSize + 0x18) * max_index_entries);
        }

        public static EncryptionScheme[] KnownSchemes = new EncryptionScheme[]
        {
            EncryptionScheme.Create ("ShiinaRio v2.37", 2370, 0x10,
                "Crypt Type 20011002 - Copyright(C) 2000 Y.Yamada/STUDIO よしくん",
                new uint[] { 0xF182C682, 0xE882AA82, 0x718E5896, 0x8183CC82, 0xDAC98283 },
                "ShiinaRio1.png", "ShiinaRio2.png", "DecodeV1.bin"),
            EncryptionScheme.Create ("ShiinaRio v2.40", 2400, 0x20,
                "Crypt Type 20011002 - Copyright(C) 2000 Y.Yamada/STUDIO よしくん",
                new uint[] { 0x747C887C, 0xA47EA17C, 0xAF7CA77C, 0xA17C747C, 0x0000A47E },
                "ShiinaRio3.jpg", "ShiinaRio2.png", "DecodeV1.bin"),
        };
    }
}
