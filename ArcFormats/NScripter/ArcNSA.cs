//! \file       ArcNSA.cs
//! \date       Sun Jul 27 11:25:46 2014
//! \brief      NScripter NSA archives implementation.
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

using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text;
using GameRes.Formats.Strings;
using GameRes.Utility;
using ICSharpCode.SharpZipLib.BZip2;

namespace GameRes.Formats.NScripter
{
    public class NsaEntry : PackedEntry
    {
        public Compression CompressionType { get; set; }
    }

    internal class NsaEncryptedArchive : ArcFile
    {
        public readonly byte[] Key;

        public NsaEncryptedArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    public class NsaOptions : ResourceOptions
    {
        public Compression CompressionType { get; set; }
        public string             Password { get; set; }
    }

    [Serializable]
    public class NsaScheme : ResourceScheme
    {
        public Dictionary<string, string> KnownKeys;
    }

    public enum Compression
    {
        Unknown = 256,
        None    = 0,
        SPB     = 1,
        LZSS    = 2,
        NBZ     = 4,
    }

    [Export(typeof(ArchiveFormat))]
    public class NsaOpener : SarOpener
    {
        public override string Tag { get { return "NSA"; } }

        public NsaOpener ()
        {
            Extensions = new string[] { "nsa", "dat" };
        }

        public static Dictionary<string, string> KnownKeys = new Dictionary<string, string>();

        public override ResourceScheme Scheme
        {
            get { return new NsaScheme { KnownKeys = KnownKeys }; }
            set { KnownKeys = ((NsaScheme)value).KnownKeys; }
        }

        public override ArcFile TryOpen (ArcView file)
        {
            List<Entry> dir = null;
            bool zero_signature = 0 == file.View.ReadInt16 (0);
            try
            {
                using (var input = file.CreateStream())
                {
                    if (zero_signature)
                        input.Seek (2, SeekOrigin.Begin);
                    dir = ReadIndex (input);
                    if (null != dir)
                        return new ArcFile (file, this, dir);
                }
            }
            catch { /* ignore parse errors */ }
            if (zero_signature || !file.Name.HasExtension (".nsa"))
                return null;
            uint signature = file.View.ReadUInt32 (0);
            if ((signature & 0xFFFFFF) == 0x90FBFF) // looks like mp3 file
                return new WrapSingleFileArchive (file, Path.GetFileNameWithoutExtension (file.Name)+".mp3");

            var password = QueryPassword();
            if (string.IsNullOrEmpty (password))
                return null;
            var key = Encoding.ASCII.GetBytes (password);

            using (var input = new EncryptedViewStream (file, key))
            {
                dir = ReadIndex (input);
                if (null == dir)
                    return null;
                return new NsaEncryptedArchive (file, this, dir, key);
            }
        }

        protected List<Entry> ReadIndex (Stream file)
        {
            long base_offset = file.Position;
            using (var input = new ArcView.Reader (file))
            {
                int count = Binary.BigEndian (input.ReadInt16());
                if (!IsSaneCount (count))
                    return null;
                base_offset += Binary.BigEndian (input.ReadUInt32());
                if (base_offset >= file.Length || base_offset < 15 * count)
                    return null;

                var dir = new List<Entry>();
                for (int i = 0; i < count; ++i)
                {
                    if (base_offset - file.Position < 15)
                        return null;
                    var name = file.ReadCString();
                    if (base_offset - file.Position < 13 || 0 == name.Length)
                        return null;

                    var entry = FormatCatalog.Instance.Create<NsaEntry> (name);
                    byte compression_type = input.ReadByte();
                    entry.Offset = Binary.BigEndian (input.ReadUInt32()) + base_offset;
                    entry.Size   = Binary.BigEndian (input.ReadUInt32());
                    if (!entry.CheckPlacement (file.Length))
                        return null;
                    entry.UnpackedSize = Binary.BigEndian (input.ReadUInt32());
                    entry.IsPacked = compression_type != 0;
                    switch (compression_type)
                    {
                    case 0:  entry.CompressionType = Compression.None; break;
                    case 1:  entry.CompressionType = Compression.SPB; break;
                    case 2:  entry.CompressionType = Compression.LZSS; break;
                    case 4:  entry.CompressionType = Compression.NBZ; break;
                    default: entry.CompressionType = Compression.Unknown; break;
                    }
                    if (name.HasExtension (".nbz"))
                        entry.Type = "audio";
                    dir.Add (entry);
                }
                return dir;
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var nsa_arc = arc as NsaEncryptedArchive;
            if (null == nsa_arc)
            {
                var input = arc.File.CreateStream (entry.Offset, entry.Size);
                return UnpackEntry (input, entry as NsaEntry);
            }
            var encrypted = new EncryptedViewStream (arc.File, nsa_arc.Key);
            var stream = new StreamRegion (encrypted, entry.Offset, entry.Size);
            return UnpackEntry (stream, entry as NsaEntry);
        }

        protected Stream UnpackEntry (Stream input, NsaEntry nsa_entry)
        {
            if (null == nsa_entry)
                return input;
            if (nsa_entry.Name.HasExtension (".nbz") || Compression.NBZ == nsa_entry.CompressionType)
            {
                input.Position = 4;
                return new BZip2InputStream (input);
            }
            if (!(Compression.LZSS == nsa_entry.CompressionType ||
                  Compression.SPB  == nsa_entry.CompressionType))
                return input;
            using (input)
            {
                var decoder = new Unpacker (input, nsa_entry.UnpackedSize);
                if (Compression.SPB == nsa_entry.CompressionType)
                    return decoder.SpbDecodedStream();
                else
                    return decoder.LzssDecodedStream();
            }
        }

        private string QueryPassword ()
        {
            var options = Query<NsaOptions> (arcStrings.ArcEncryptedNotice);
            return options.Password;
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new NsaOptions {
                CompressionType = Properties.Settings.Default.ONSCompression,
                Password        = Properties.Settings.Default.NSAPassword,
            };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            var w = widget as GUI.WidgetNSA;
            if (null != w)
                Properties.Settings.Default.NSAPassword = w.Password.Text;
            return GetDefaultOptions();
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetNSA (KnownKeys);
        }

        public override object GetCreationWidget ()
        {
            return new GUI.CreateONSWidget();
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            var ons_options = GetOptions<NsaOptions> (options);
            var encoding = Encodings.cp932.WithFatalFallback();
            int callback_count = 0;

            var real_entry_list = new List<NsaEntry>();
            var used_names = new HashSet<string>();
            int index_size = 0;
            foreach (var entry in list)
            {
                if (!used_names.Add (entry.Name)) // duplicate name
                    continue;
                try
                {
                    index_size += encoding.GetByteCount (entry.Name) + 1;
                }
                catch (EncoderFallbackException X)
                {
                    throw new InvalidFileName (entry.Name, arcStrings.MsgIllegalCharacters, X);
                }
                var header_entry = new NsaEntry { Name = entry.Name };
                if (Compression.None != ons_options.CompressionType)
                {
                    if (entry.Name.HasExtension (".bmp"))
                        header_entry.CompressionType = ons_options.CompressionType;
                }
                index_size += 13;
                real_entry_list.Add (header_entry);
            }

            long start_offset = output.Position;
            long base_offset = 6+index_size;
            output.Seek (base_offset, SeekOrigin.Current);
            foreach (var entry in real_entry_list)
            {
                using (var input = File.OpenRead (entry.Name))
                {
                    var file_size = input.Length;
                    if (file_size > uint.MaxValue)
                        throw new FileSizeException();
                    long file_offset = output.Position - base_offset;
                    if (file_offset+file_size > uint.MaxValue)
                        throw new FileSizeException();
                    if (null != callback)
                        callback (callback_count++, entry, arcStrings.MsgAddingFile);
                    entry.Offset = file_offset;
                    entry.UnpackedSize = (uint)file_size;
                    if (Compression.LZSS == entry.CompressionType)
                    {
                        var packer = new Packer (input, output);
                        entry.Size = packer.EncodeLZSS();
                    }
                    else
                    {
                        entry.Size            = entry.UnpackedSize;
                        entry.CompressionType = Compression.None;
                        input.CopyTo (output);
                    }
                }
            }

            if (null != callback)
                callback (callback_count++, null, arcStrings.MsgWritingIndex);
            output.Position = start_offset;
            using (var writer = new BinaryWriter (output, encoding, true))
            {
                writer.Write (Binary.BigEndian ((short)real_entry_list.Count));
                writer.Write (Binary.BigEndian ((uint)base_offset));
                foreach (var entry in real_entry_list)
                {
                    writer.Write (encoding.GetBytes (entry.Name));
                    writer.Write ((byte)0);
                    writer.Write ((byte)entry.CompressionType);
                    writer.Write (Binary.BigEndian ((uint)entry.Offset));
                    writer.Write (Binary.BigEndian ((uint)entry.Size));
                    writer.Write (Binary.BigEndian ((uint)entry.UnpackedSize));
                }
            }
        }
    }

   /*
    *  ONScripter-EN decompression routines.
    *
    *  Copyright (c) 2001-2010 Ogapee. All rights reserved.
    *  (original ONScripter, of which this is a fork).
    *
    *  ogapee@aqua.dti2.ne.jp
    *
    *  Copyright (c) 2007-2010 "Uncle" Mion Sonozaki
    *
    *  UncleMion@gmail.com
    *
    */
    /* LZSS encoder-decoder  (c) Haruhiko Okumura */

    internal static class LZSS
    {
        public const int EI = 8;
        public const int EJ = 4;
        public const int P  = 1;  /* If match length <= P then output one character */
        public const int N  = (1 << EI);  /* buffer size */
        public const int F  = ((1 << EJ) + P);  /* lookahead buffer size */
    }

    internal class Unpacker : MsbBitStream
    {
        private byte[]          m_output;

        public byte[] Output { get { return m_output; } }

        public Unpacker (Stream input, uint unpacked_size) : base (input, true)
        {
            m_output = new byte[unpacked_size];
        }

        public Stream LzssDecodedStream ()
        {
            DecodeLZSS();
            return new MemoryStream (m_output);
        }

        public Stream SpbDecodedStream ()
        {
            DecodeSPB();
            return new MemoryStream (m_output);
        }

        uint DecodeLZSS ()
        {
            uint count = 0;

            byte[] decomp_buffer = new byte[LZSS.N*2];
            int r = LZSS.N - LZSS.F;
            int c;
            while (count < m_output.Length)
            {
                if (0 != GetBits (1))
                {
                    c = GetBits (8);
                    if (-1 == c)
                        break;
                    m_output[count++] = (byte)c;
                    decomp_buffer[r++] = (byte)c;
                    r &= (LZSS.N - 1);
                }
                else
                {
                    int i = GetBits (LZSS.EI);
                    if (-1 == i)
                        break;
                    int j = GetBits (LZSS.EJ);
                    if (-1 == j)
                        break;
                    for (int k = 0; k <= j + 1; k++)
                    {
                        c = decomp_buffer[(i + k) & (LZSS.N - 1)];
                        m_output[count++] = (byte)c;
                        decomp_buffer[r++] = (byte)c;
                        r &= (LZSS.N - 1);
                    }
                }
            }
            return count;
        }

        uint DecodeSPB ()
        {
            uint width   = (uint)Input.ReadByte() << 8;
            width       |= (uint)Input.ReadByte();
            uint height  = (uint)Input.ReadByte() << 8;
            height      |= (uint)Input.ReadByte();

            uint width_pad  = (4 - width * 3 % 4) % 4;
            int stride = (int)(width * 3 + width_pad);
            uint total_size = (uint)stride * height + 54;

            if ((uint)m_output.Length < total_size)
                m_output = new byte[total_size];

            /* ---------------------------------------- */
            /* Write header */
            m_output[0] = (byte)'B';
            m_output[1] = (byte)'M';
            LittleEndian.Pack (total_size, m_output, 2);
            m_output[10] = 54; // offset to the body
            m_output[14] = 40; // header size
            LittleEndian.Pack (width,  m_output, 18);
            LittleEndian.Pack (height, m_output, 22);
            m_output[26] = 1; // the number of the plane
            m_output[28] = 24; // bpp

            byte[] decomp_buffer = new byte[width*height*4];
            
            for (int i = 0; i < 3; i++)
            {
                uint count = 0;
                int c = GetBits (8);
                if (-1 == c)
                    break;
                decomp_buffer[count++] = (byte)c;
                while (count < width * height)
                {
                    int n = GetBits (3);
                    if (0 == n)
                    {
                        decomp_buffer[count++] = (byte)c;
                        decomp_buffer[count++] = (byte)c;
                        decomp_buffer[count++] = (byte)c;
                        decomp_buffer[count++] = (byte)c;
                        continue;
                    }
                    int m;
                    if (7 == n)
                        m = GetBits (1) + 1;
                    else
                        m = n + 2;

                    for (uint j = 0; j < 4; j++)
                    {
                        if (8 == m)
                        {
                            c = GetBits (8);
                        }
                        else
                        {
                            int k = GetBits (m);
                            if (0 != (k & 1))
                                c += (k>>1) + 1;
                            else
                                c -= (k>>1);
                        }
                        decomp_buffer[count++] = (byte)c;
                    }
                }

                int pbuf  = stride * (int)(height-1) + i + 54; // in m_output
                int psbuf = 0; // in decomp_buffer

                for (uint j = 0; j < height; j++)
                {
                    if (0 != (j & 1))
                    {
                        for (uint k = 0; k < width; k++, pbuf -= 3)
                            m_output[pbuf] = decomp_buffer[psbuf++];
                        pbuf -= stride - 3;
                    }
                    else
                    {
                        for (uint k = 0; k < width; k++, pbuf += 3)
                            m_output[pbuf] = decomp_buffer[psbuf++];
                        pbuf -= stride + 3;
                    }
                }
            }
            return total_size;
        }
    }

    internal class Packer
    {
        private Stream  m_input;
        private Stream  m_output;
        private uint    m_code_count = 0;

        public uint PackedSize { get { return m_code_count; } }

        public Packer (Stream input, Stream output)
        {
            m_input = input;
            m_output = output;
        }

        public uint EncodeLZSS ()
        {
            byte[] comp_buffer = new byte[LZSS.N*2];

            int i;
            for (i = LZSS.N - LZSS.F; i < LZSS.N * 2; i++)
            {
                int c = m_input.ReadByte();
                if (-1 == c)
                    break;
                comp_buffer[i] = (byte)c;
            }
            int bufferend = i;
            int r = LZSS.N - LZSS.F;
            int s = 0;
            while (r < bufferend)
            {
                int f1 = (LZSS.F <= bufferend - r) ? LZSS.F : bufferend - r;
                int x = 0;
                int y = 1;
                int c = comp_buffer[r];
                for (i = r - 1; i >= s; i--)
                {
                    if (comp_buffer[i] == c)
                    {
                        int j;
                        for (j = 1; j < f1; j++)
                            if (comp_buffer[i + j] != comp_buffer[r + j])
                                break;
                        if (j > y)
                        {
                            x = i;
                            y = j;
                        }
                    }
                }
                if (y <= LZSS.P)
                    Output1 (c);
                else
                    Output2 (x & (LZSS.N - 1), y - 2);
                r += y;
                s += y;
                if (r >= LZSS.N * 2 - LZSS.F)
                {
                    for (i = 0; i < LZSS.N; i++)
                        comp_buffer[i] = comp_buffer[i + LZSS.N];
                    bufferend -= LZSS.N;
                    r -= LZSS.N;
                    s -= LZSS.N;
                    while (bufferend < LZSS.N * 2)
                    {
                        c = m_input.ReadByte();
                        if (-1 == c)
                            break;
                        comp_buffer[bufferend++] = (byte)c;
                    }
                }
            }
            FlushBitBuffer();
            return m_code_count;
        }

        int m_bit_buffer = 0;
        int m_bit_mask = 128;

        void PutBit1 ()
        {
            m_bit_buffer |= m_bit_mask;
            if ((m_bit_mask >>= 1) == 0)
            {
                m_output.WriteByte ((byte)m_bit_buffer);
                m_bit_buffer = 0;
                m_bit_mask = 128;
                m_code_count++;
            }
        }

        void PutBit0 ()
        {
            if ((m_bit_mask >>= 1) == 0)
            {
                m_output.WriteByte ((byte)m_bit_buffer);
                m_bit_buffer = 0;
                m_bit_mask = 128;
                m_code_count++;
            }
        }

        void FlushBitBuffer ()
        {
            if (m_bit_mask != 128)
            {
                m_output.WriteByte ((byte)m_bit_buffer);
                m_code_count++;
            }
        }

        void Output1 (int c)
        {
            PutBit1();
            int mask = 256;
            while (0 != (mask >>= 1))
            {
                if (0 != (c & mask)) PutBit1();
                else PutBit0();
            }
        }

        void Output2 (int x, int y)
        {
            PutBit0();
            int mask = LZSS.N;
            while (0 != (mask >>= 1))
            {
                if (0 != (x & mask)) PutBit1();
                else PutBit0();
            }
            mask = (1 << LZSS.EJ);
            while (0 != (mask >>= 1))
            {
                if (0 != (y & mask)) PutBit1();
                else PutBit0();
            }
        }
    }
}
