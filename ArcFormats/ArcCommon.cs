//! \file       ArcCommon.cs
//! \date       Tue Aug 19 09:45:38 2014
//! \brief      Classes and functions common for various resource files.
//
// Copyright (C) 2014-2015 by morkt
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

using GameRes.Utility;
using System;
using System.IO;
using System.Linq;

namespace GameRes.Formats
{
    public class AutoEntry : Entry
    {
        private Lazy<IResource> m_res;
        private Lazy<string> m_name;
        private Lazy<string> m_type;

        public override string Name
        {
            get { return m_name.Value; }
            set { m_name = new Lazy<string> (() => value); }
        }
        public override string Type
        {
            get { return m_type.Value; }
            set { m_type = new Lazy<string> (() => value); }
        }

        public AutoEntry (string name, Func<IResource> type_checker)
        {
            m_res  = new Lazy<IResource> (type_checker);
            m_name = new Lazy<string> (() => GetName (name));
            m_type = new Lazy<string> (GetEntryType);
        }

        public static AutoEntry Create (ArcView file, long offset, string base_name)
        {
            return new AutoEntry (base_name, () => DetectFileType (file.View.ReadUInt32 (offset))) { Offset = offset };
        }

        public static IResource DetectFileType (uint signature)
        {
            if (0 == signature) return null;
            // resolve some special cases first
            if (OggAudio.Instance.Signature == signature)
                return OggAudio.Instance;
            if (AudioFormat.Wav.Signature == signature)
                return AudioFormat.Wav;
            if (0x4D42 == (signature & 0xFFFF)) // 'BM'
                return ImageFormat.Bmp;
            var res = FormatCatalog.Instance.LookupSignature (signature);
            if (!res.Any())
                return null;
            if (res.Skip (1).Any()) // type is ambiguous
                return null;
            return res.First();
        }

        private string GetName (string name)
        {
            if (null == m_res.Value)
                return name;
            var ext = m_res.Value.Extensions.FirstOrDefault();
            if (string.IsNullOrEmpty (ext))
                return name;
            return Path.ChangeExtension (name, ext);
        }

        private string GetEntryType ()
        {
            return null == m_res.Value ? "" : m_res.Value.Type;
        }

        static readonly Lazy<AudioFormat> s_OggFormat = new Lazy<AudioFormat> (() => FormatCatalog.Instance.AudioFormats.FirstOrDefault (x => x.Tag == "OGG"));
    }

    public class HuffmanDecoder
    {
        byte[] m_src;
        byte[] m_dst;

        ushort[] lhs = new ushort[512];
        ushort[] rhs = new ushort[512];
        ushort token = 256;

        int m_input_pos;
        int m_remaining;
        int m_cached_bits;
        int m_cache;

        public HuffmanDecoder (byte[] src, int index, int length, byte[] dst)
        {
            m_src = src;
            m_dst = dst;
            m_input_pos = index;
            m_remaining = length;
            m_cached_bits = 0;
            m_cache = 0;
        }

        public HuffmanDecoder (byte[] src, byte[] dst) : this (src, 0, src.Length, dst)
        {
        }

        public byte[] Unpack ()
        {
            int dst = 0;
            token = 256;
            ushort v3 = CreateTree();
            while (dst < m_dst.Length)
            {
                ushort symbol = v3;
                while ( symbol >= 0x100u )
                {
                    if ( 0 != GetBits (1) )
                        symbol = rhs[symbol];
                    else
                        symbol = lhs[symbol];
                }
                m_dst[dst++] = (byte)symbol;
            }
            return m_dst;
        }

        ushort CreateTree()
        {
            if ( 0 != GetBits (1) )
            {
                ushort v = token++;
                lhs[v] =  CreateTree();
                rhs[v] =  CreateTree();
                return v;
            }
            else
            {
                return (ushort)GetBits (8);
            }
        }

        uint GetBits (int n)
        {
            while (n > m_cached_bits)
            {
                if (0 == m_remaining)
                    throw new ApplicationException ("Invalid huffman-compressed stream");
                int v = m_src[m_input_pos++];
                --m_remaining;
                m_cache = v | (m_cache << 8);
                m_cached_bits += 8;
            }
            uint mask = (uint)m_cache;
            m_cached_bits -= n;
            m_cache &= ~(-1 << m_cached_bits);
            return (uint)(((-1 << m_cached_bits) & mask) >> m_cached_bits);
        }
    }

    /// <summary>
    /// Create stream in TGA format from the given image pixels.
    /// </summary>
    public static class TgaStream
    {
        public static Stream Create (ImageMetaData info, byte[] pixels, bool flipped = false)
        {
            var header = new byte[0x12];
            header[2] = (byte)(info.BPP > 8 ? 2 : 3);
            LittleEndian.Pack ((short)info.OffsetX, header, 8);
            LittleEndian.Pack ((short)info.OffsetY, header, 0xa);
            LittleEndian.Pack ((ushort)info.Width,  header, 0xc);
            LittleEndian.Pack ((ushort)info.Height, header, 0xe);
            header[0x10] = (byte)info.BPP;
            if (!flipped)
                header[0x11] = 0x20;
            return new PrefixStream (header, new MemoryStream (pixels));
        }

        public static Stream Create (ImageMetaData info, int stride, byte[] pixels, bool flipped = false)
        {
            int tga_stride = (int)info.Width * info.BPP / 8;
            if (stride != tga_stride)
            {
                var adjusted = new byte[tga_stride * (int)info.Height];
                int src = 0;
                int dst = 0;
                for (uint y = 0; y < info.Height; ++y)
                {
                    Buffer.BlockCopy (pixels, src, adjusted, dst, tga_stride);
                    src += stride;
                    dst += tga_stride;
                }
                pixels = adjusted;
            }
            return Create (info, pixels, flipped);
        }
    }

    public static class MMX
    {
        public static ulong PAddB (ulong x, ulong y)
        {
            ulong r = 0;
            for (ulong mask = 0xFF; mask != 0; mask <<= 8)
            {
                r |= ((x & mask) + (y & mask)) & mask;
            }
            return r;
        }

        public static uint PAddB (uint x, uint y)
        {
            uint r13 = (x & 0xFF00FF00u) + (y & 0xFF00FF00u);
            uint r02 = (x & 0x00FF00FFu) + (y & 0x00FF00FFu);
            return (r13 & 0xFF00FF00u) | (r02 & 0x00FF00FFu);
        }

        public static ulong PAddW (ulong x, ulong y)
        {
            ulong mask = 0xffff;
            ulong r = ((x & mask) + (y & mask)) & mask;
            mask <<= 16;
            r |= ((x & mask) + (y & mask)) & mask;
            mask <<= 16;
            r |= ((x & mask) + (y & mask)) & mask;
            mask <<= 16;
            r |= ((x & mask) + (y & mask)) & mask;
            return r;
        }

        public static ulong PAddD (ulong x, ulong y)
        {
            ulong mask = 0xffffffff;
            ulong r = ((x & mask) + (y & mask)) & mask;
            mask <<= 32;
            return r | ((x & mask) + (y & mask)) & mask;
        }

        public static ulong PSubB (ulong x, ulong y)
        {
            ulong r = 0;
            for (ulong mask = 0xFF; mask != 0; mask <<= 8)
            {
                r |= ((x & mask) - (y & mask)) & mask;
            }
            return r;
        }

        public static ulong PSubW (ulong x, ulong y)
        {
            ulong mask = 0xffff;
            ulong r = ((x & mask) - (y & mask)) & mask;
            mask <<= 16;
            r |= ((x & mask) - (y & mask)) & mask;
            mask <<= 16;
            r |= ((x & mask) - (y & mask)) & mask;
            mask <<= 16;
            r |= ((x & mask) - (y & mask)) & mask;
            return r;
        }

        public static ulong PSubD (ulong x, ulong y)
        {
            ulong mask = 0xffffffff;
            ulong r = ((x & mask) - (y & mask)) & mask;
            mask <<= 32;
            return r | ((x & mask) - (y & mask)) & mask;
        }

        public static ulong PSllD (ulong x, int count)
        {
            count &= 0x1F;
            ulong mask = 0xFFFFFFFFu << count;
            mask |= mask << 32;
            return (x << count) & mask;
        }
    }

    public static class Dump
    {
        public static string DirectoryName = Environment.GetFolderPath (Environment.SpecialFolder.UserProfile);

        [System.Diagnostics.Conditional("DEBUG")]
        public static void Write (byte[] mem, string filename = "index.dat")
        {
            using (var dump = File.Create (Path.Combine (DirectoryName, filename)))
                dump.Write (mem, 0, mem.Length);
        }
    }
}
