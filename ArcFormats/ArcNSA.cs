//! \file       ArcNSA.cs
//! \date       Sun Jul 27 11:25:46 2014
//! \brief      ONScripter NSA/SAR archives implementation.
//

using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.ONScripter
{
    public class NsaEntry : PackedEntry
    {
        public Compression CompressionType { get; set; }
    }

    public enum Compression
    {
        Unknown = 256,
        None    = 0,
        Spb     = 1,
        Lzss    = 2,
        Nbz     = 4,
    }

    [Export(typeof(ArchiveFormat))]
    public class NsaOpener : ArchiveFormat
    {
        public override string Tag { get { return "NSA"; } }
        public override string Description { get { return arcStrings.NSADescription; } }
        public override uint Signature { get { return 0; } }
        public override bool IsHierarchic { get { return true; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int num_of_files = Binary.BigEndian (file.View.ReadInt16 (0));
            if (num_of_files <= 0)
                return null;
            uint base_offset = Binary.BigEndian (file.View.ReadUInt32 (2));
            if (base_offset >= file.MaxOffset || base_offset < 15 * (uint)num_of_files)
                return null;

            uint cur_offset = 6;
            var dir = new List<Entry>();
            for (int i = 0; i < num_of_files; ++i)
            {
                if (base_offset - cur_offset < 15)
                    return null;
                int name_len;
                byte[] name_buffer = ReadName (file, cur_offset, base_offset-cur_offset, out name_len);
                if (0 == name_len || base_offset-cur_offset == name_len)
                    return null;
                cur_offset += (uint)(name_len + 1);
                if (base_offset - cur_offset < 13)
                    return null;

                var entry = new NsaEntry
                {
                    Name = Encodings.cp932.GetString (name_buffer, 0, name_len),
                };
                byte compression_type = file.View.ReadByte (cur_offset);
                entry.Type   = FormatCatalog.Instance.GetTypeFromName (entry.Name);
                entry.Offset = Binary.BigEndian (file.View.ReadUInt32 (cur_offset+1)) + (long)base_offset;
                entry.Size   = Binary.BigEndian (file.View.ReadUInt32 (cur_offset+5));
                entry.UnpackedSize = Binary.BigEndian (file.View.ReadUInt32 (cur_offset+9));
                switch (compression_type)
                {
                case 0:  entry.CompressionType = Compression.None; break;
                case 1:  entry.CompressionType = Compression.Spb; break;
                case 2:  entry.CompressionType = Compression.Lzss; break;
                case 4:  entry.CompressionType = Compression.Nbz; break;
                default: entry.CompressionType = Compression.Unknown; break;
                }
                cur_offset += 13;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var nsa_entry = entry as NsaEntry;
            if (null != nsa_entry
                && nsa_entry.CompressionType == Compression.Lzss
                && nsa_entry.CompressionType == Compression.Spb)
            {
                using (var input = arc.File.CreateStream (nsa_entry.Offset, nsa_entry.Size))
                {
                    var decoder = new Unpacker (input, nsa_entry.UnpackedSize);
                    switch (nsa_entry.CompressionType)
                    {
                    case Compression.Lzss:  return decoder.LzssDecodedStream();
                    case Compression.Spb:   return decoder.SpbDecodedStream();
                    }
                }
            }
            return arc.File.CreateStream (entry.Offset, entry.Size);
        }

        protected static byte[] ReadName (ArcView file, uint offset, uint limit, out int name_len)
        {
            byte[] name_buffer = new byte[40];
            for (name_len = 0; name_len < limit; ++name_len)
            {
                byte b = file.View.ReadByte (offset+name_len);
                if (0 == b)
                    break;
                if (name_buffer.Length == name_len)
                {
                    byte[] new_buffer = new byte[checked(name_len/2*3)];
                    Array.Copy (name_buffer, new_buffer, name_len);
                    name_buffer = new_buffer;
                }
                name_buffer[name_len] = b;
            }
            return name_buffer;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class SarOpener : NsaOpener
    {
        public override string Tag { get { return "SAR"; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int num_of_files = Binary.BigEndian (file.View.ReadInt16 (0));
            if (num_of_files <= 0)
                return null;
            uint base_offset = Binary.BigEndian (file.View.ReadUInt32 (2));
            if (base_offset >= file.MaxOffset || base_offset < 10 * (uint)num_of_files)
                return null;

            uint cur_offset = 6;
            var dir = new List<Entry>();
            for (int i = 0; i < num_of_files; ++i)
            {
                if (base_offset - cur_offset < 10)
                    return null;
                int name_len;
                byte[] name_buffer = ReadName (file, cur_offset, base_offset-cur_offset, out name_len);
                if (0 == name_len || base_offset-cur_offset == name_len)
                    return null;
                cur_offset += (uint)(name_len + 1);
                if (base_offset - cur_offset < 8)
                    return null;

                var entry = new NsaEntry
                {
                    Name = Encodings.cp932.GetString (name_buffer, 0, name_len),
                };
                entry.Type   = FormatCatalog.Instance.GetTypeFromName (entry.Name);
                entry.Offset = Binary.BigEndian (file.View.ReadUInt32 (cur_offset)) + (long)base_offset;
                entry.Size   = Binary.BigEndian (file.View.ReadUInt32 (cur_offset+4));
                entry.UnpackedSize = entry.Size;
                entry.CompressionType = Compression.None;
                string ext = Path.GetExtension (entry.Name);
                if (".nbz".Equals (ext, StringComparison.OrdinalIgnoreCase))
                    entry.CompressionType = Compression.Nbz;

                cur_offset += 8;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }

    internal class Unpacker
    {
        private Stream          m_input;
        private MemoryStream    m_output;
        private uint            m_unpacked_size;
        private byte[]          m_read_buf = new byte[4096];

        public Unpacker (Stream input, uint unpacked_size)
        {
            m_input = input;
            m_output = new MemoryStream ((int)unpacked_size);
            m_unpacked_size = unpacked_size;
        }

        public Stream LzssDecodedStream ()
        {
            uint size = DecodeLZSS();
            if (size != m_unpacked_size)
                System.Diagnostics.Trace.WriteLine ("Invalid compressed data", "LzssDecoder");
            m_output.Position = 0;
            return m_output;
        }

        public Stream SpbDecodedStream ()
        {
            uint size = DecodeSPB();
            if (size != m_unpacked_size)
                System.Diagnostics.Trace.WriteLine ("Invalid compressed data", "SpbDecoder");
            m_output.Position = 0;
            return m_output;
        }

        const int EI = 8;
        const int EJ = 4;
        const int P  = 1;  /* If match length <= P then output one character */
        const int N  = (1 << EI);  /* buffer size */
        const int F  = ((1 << EJ) + P);  /* lookahead buffer size */

        private int  m_getbit_mask;
        private uint m_getbit_len;
        private uint m_getbit_count;

        uint DecodeLZSS ()
        {
            uint count = 0;

            m_getbit_mask = 0;
            m_getbit_len = m_getbit_count = 0;
            byte[] decomp_buffer = new byte[N*2];
            int r = N - F;
            int c;
            while (count < m_unpacked_size)
            {
                if (0 != GetBits (1))
                {
                    c = GetBits (8);
                    if (-1 == c)
                        break;
                    m_output.WriteByte ((byte)c);
                    ++count;
                    decomp_buffer[r++] = (byte)c;
                    r &= (N - 1);
                }
                else
                {
                    int i = GetBits (EI);
                    if (-1 == i)
                        break;
                    int j = GetBits (EJ);
                    if (-1 == j)
                        break;
                    for (int k = 0; k <= j + 1; k++)
                    {
                        c = decomp_buffer[(i + k) & (N - 1)];
                        m_output.WriteByte ((byte)c);
                        ++count;
                        decomp_buffer[r++] = (byte)c;
                        r &= (N - 1);
                    }
                }
            }
            return count;
        }

        uint DecodeSPB ()
        {
            m_getbit_mask = 0;
            m_getbit_len = m_getbit_count = 0;

            uint width   = (uint)(m_input.ReadByte() << 8);
            width       |= (uint)m_input.ReadByte();
            uint height  = (uint)(m_input.ReadByte() << 8);
            height      |= (uint)m_input.ReadByte();

            uint width_pad  = (4 - width * 3 % 4) % 4;
            int stride = (int)(width * 3 + width_pad);
            uint total_size = (uint)stride * height + 54;
            m_output.SetLength (total_size);

            /* ---------------------------------------- */
            /* Write header */
            m_output.WriteByte ((byte)'B'); 
            m_output.WriteByte ((byte)'M');
            m_output.WriteByte ((byte)(total_size & 0xff));
            m_output.WriteByte ((byte)((total_size >>  8) & 0xff));
            m_output.WriteByte ((byte)((total_size >> 16) & 0xff));
            m_output.WriteByte ((byte)((total_size >> 24) & 0xff));
            m_output.Seek (4, SeekOrigin.Current);
            m_output.WriteByte (54); // offset to the body
            m_output.Seek (3, SeekOrigin.Current);
            m_output.WriteByte (40); // header size
            m_output.Seek (3, SeekOrigin.Current);
            m_output.WriteByte ((byte)(width & 0xff));
            m_output.WriteByte ((byte)((width >> 8) & 0xff));
            m_output.Seek (2, SeekOrigin.Current);
            m_output.WriteByte ((byte)(height & 0xff));
            m_output.WriteByte ((byte)((height >> 8) & 0xff));
            m_output.Seek (2, SeekOrigin.Current);
            m_output.WriteByte (1); // the number of the plane
            m_output.WriteByte (0);
            m_output.WriteByte (24); // bpp
            m_output.Seek (5, SeekOrigin.Current);
            m_output.WriteByte ((byte)(total_size - 54)); // size of the body
            m_output.Seek (54, SeekOrigin.Begin);

            byte[] buf = m_output.GetBuffer();
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

                int pbuf  = stride * (int)(height-1) + i + 54; // in buf
                int psbuf = 0; // in decomp_buffer

                for (uint j = 0; j < height; j++)
                {
                    if (0 != (j & 1))
                    {
                        for (uint k = 0; k < width; k++, pbuf -= 3)
                            buf[pbuf] = decomp_buffer[psbuf++];
                        pbuf -= stride - 3;
                    }
                    else
                    {
                        for (uint k = 0; k < width; k++, pbuf += 3)
                            buf[pbuf] = decomp_buffer[psbuf++];
                        pbuf -= stride + 3;
                    }
                }
            }
            return total_size;
        }

        private int m_getbit_buf = 0;

        int GetBits (int n)
        {
            int x = 0;
            for (int i = 0; i < n; i++)
            {
                if (0 == m_getbit_mask)
                {
                    if (m_getbit_len == m_getbit_count)
                    {
                        m_getbit_len = (uint)m_input.Read (m_read_buf, 0, m_read_buf.Length);
                        if (0 == m_getbit_len)
                            return -1;
                        m_getbit_count = 0;
                    }
                    m_getbit_buf = m_read_buf[m_getbit_count++];
                    m_getbit_mask = 128;
                }
                x <<= 1;
                if (0 != (m_getbit_buf & m_getbit_mask))
                    x |= 1;
                m_getbit_mask >>= 1;
            }
            return x;
        }
    }
}
