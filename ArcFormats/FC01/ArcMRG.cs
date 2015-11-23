//! \file       ArcMRG.cs
//! \date       Mon Jul 13 03:20:13 2015
//! \brief      F&C Co. MGR archive format.
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
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.FC01
{
    internal class MrgEntry : PackedEntry
    {
        public int  Method;
    }

    [Export(typeof(ArchiveFormat))]
    public class MrgOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MRG"; } }
        public override string Description { get { return "F&C Co. engine resource archive"; } }
        public override uint     Signature { get { return 0x0047524D; } } // 'MRG'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public static readonly Tuple<byte[], byte[]> KnownKey = Tuple.Create (
            // Konata yori Kanata made
            new byte[] { 0, 0x68, 0x5F }, new byte[] { 0, 0x37 }
        );

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (12);
            if (!IsSaneCount (count))
                return null;
            int key1index = file.View.ReadUInt16 (4);
            int key2index = file.View.ReadUInt16 (6);
            if (key2index != 0 && key1index == 0)
                return null;
            uint index_size = file.View.ReadUInt32 (8) - 0x10;
            if (index_size < 0x40 || index_size >= file.MaxOffset)
                return null;
            var index = new byte[index_size];
            if (index.Length != file.View.Read (0x10, index, 0, index_size))
                return null;
            /*
            var key_src = KnownKey;
            if (key1index >= key_src.Item1.Length || key2index >= key_src.Item2.Length)
                return null;
            byte index_key = (byte)(key_src.Item1[key1index] + key_src.Item2[key2index]);
            */
            var key = GuessKey (file, index);
            if (null == key)
                throw new UnknownEncryptionScheme();
            Decrypt (index, 0, index.Length, key.Value);

            int current_offset = 0;
            uint next_offset = LittleEndian.ToUInt32 (index, current_offset+0x1C);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                string name = Binary.GetCString (index, current_offset, 0x0E);
                var entry = new MrgEntry
                {
                    Name = name,
                    Type = FormatCatalog.Instance.GetTypeFromName (name),
                    Offset = next_offset,
                    Method = index[current_offset+0x12],
                };
                next_offset = LittleEndian.ToUInt32 (index, current_offset+0x3C); 
                entry.Size = next_offset - (uint)entry.Offset;
                if (entry.Offset < index_size || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.IsPacked = entry.Method != 0;
                entry.UnpackedSize = LittleEndian.ToUInt32 (index, current_offset+0x0E);
                dir.Add (entry);
                current_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var packed_entry = entry as MrgEntry;
            if (null == packed_entry || !packed_entry.IsPacked || packed_entry.Method > 3)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            Stream input;
            if (packed_entry.Method >= 2)
            {
                if (entry.Size < 0x108)
                    return arc.File.CreateStream (entry.Offset, entry.Size);
                var data = new byte[entry.Size];
                arc.File.View.Read (entry.Offset, data, 0, entry.Size);
                var reader = new MrgDecoder (data);
                reader.Unpack();
                input = new MemoryStream (reader.Data);
            }
            else
                input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (packed_entry.Method < 3)
            {
                using (input)
                using (var reader = new MrgLzssReader (input, (int)input.Length, (int)packed_entry.UnpackedSize))
                {
                    reader.Unpack();
                    return new MemoryStream (reader.Data);
                }
            }
            return input;
        }

        static public void Decrypt (byte[] data, int index, int length, int key)
        {
            while (length > 0)
            {
                var v = data[index];
                data[index++] = (byte)((v << 1 | v >> 7) ^ key);
                key += length--;
            }
        }

        private byte? GuessKey (ArcView file, byte[] index)
        {
            uint actual_offset = (uint)file.MaxOffset;

            byte v = index[index.Length-1]; // last_offset
            v = (byte)(v << 1 | v >> 7);
            byte key = (byte)(v ^ (actual_offset >> 24));

            int remaining = 1;
            uint last_offset = (byte)(v ^ key);
            for (int i = index.Length-2; i >= index.Length-4; --i)
            {
                key -= (byte)++remaining;
                v = index[i];
                v = (byte)(v << 1 | v >> 7);
                last_offset = (last_offset << 8) | (uint)(v ^ key);
            }
            if (last_offset != actual_offset)
                return null;

            while (remaining++ < index.Length)
                key -= (byte)remaining;

            return key;
        }
    }

    /// <summary>
    /// LZSS decompression with slightly modified offset/count values encoding.
    /// </summary>
    internal sealed class MrgLzssReader : IDisposable
    {
        BinaryReader    m_input;
        byte[]          m_output;
        int             m_size;

        public byte[]        Data { get { return m_output; } }

        public MrgLzssReader (Stream input, int input_length, int output_length)
        {
            m_input = new BinaryReader (input, Encoding.ASCII, true);
            m_output = new byte[output_length];
            m_size = input_length;
        }

        public void Unpack ()
        {
            int dst = 0;
            var frame = new byte[0x1000];
            int frame_pos = 0xfee;
            int frame_mask = 0xfff;
            int remaining = m_size;
            while (remaining > 0)
            {
                int ctl = m_input.ReadByte();
                --remaining;
                for (int bit = 1; remaining > 0 && bit != 0x100; bit <<= 1)
                {
                    if (dst >= m_output.Length)
                        return;
                    if (0 != (ctl & bit))
                    {
                        byte b = m_input.ReadByte();
                        --remaining;
                        frame[frame_pos++] = b;
                        frame_pos &= frame_mask;
                        m_output[dst++] = b;
                    }
                    else
                    {
                        if (remaining < 2)
                            return;
                        int offset = m_input.ReadUInt16();
                        remaining -= 2;
                        int count = (offset >> 12) + 3;
                        for ( ; count != 0; --count)
                        {
                            if (dst >= m_output.Length)
                                break;
                            offset &= frame_mask;
                            byte v = frame[offset++];
                            frame[frame_pos++] = v;
                            frame_pos &= frame_mask;
                            m_output[dst++] = v;
                        }
                    }
                }
            }
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
            GC.SuppressFinalize (this);
        }
        #endregion
    }

    internal class MrgDecoder
    {
        byte[]      m_input;
        byte[]      m_output;
        int         m_start_index;
        int         m_src;

        public byte[] Data { get { return m_output; } }
        public byte    Key { get; set; }

        public MrgDecoder (byte[] data, int index = 0)
        {
            m_input = data;
            m_src = index;
            uint unpacked_size = LittleEndian.ToUInt32 (data, m_src);
            unpacked_size ^= LittleEndian.ToUInt32 (data, m_src+0x104);
            m_src += 4;
            m_start_index = m_src;
            m_output = new byte[unpacked_size];
        }

        public MrgDecoder (byte[] data, int index, uint unpacked_size)
        {
            m_input = data;
            m_start_index = index;
            m_src = index;
            m_output = new byte[unpacked_size];
        }

        public void ResetKey (byte key)
        {
            m_src = m_start_index;
            Key = key;
        }

        ushort[] word_10036650 = new ushort[0x200];
        byte[] byte_table = new byte[0xff00];

        public int Unpack () // sub_10026EB0
        {
            uint quant = InitTable();
            if (0 == quant || quant > 0x10000)
                throw new InvalidFormatException();
            uint mask = GetMask (quant);
            uint scale = 0x10000 / quant;
            uint b = 0;
            uint c = 0xffffffff;
            int dst = 0;
            uint a = BigEndian.ToUInt32 (m_input, m_src);
            m_src += 4;
            while (dst < m_output.Length)
            {
                c = ((c >> 8) * scale) >> 8;
                uint v = (a - b) / c;
                if (v > quant)
                    throw new InvalidFormatException();
                v = byte_table[v];
                m_output[dst++] = (byte)v;
                b += word_10036650[v*2] * c;
                c *= word_10036650[v*2+1];
                while (0 == (((c + b) ^ b) & 0xFF000000))
                {
                    if (m_src >= m_input.Length)
                        return dst;
                    a <<= 8;
                    b <<= 8;
                    c <<= 8;
                    a |= m_input[m_src++];
                }
                while (c <= mask)
                {
                    if (m_src >= m_input.Length)
                        return dst;
                    c = (~b & mask) << 8;
                    a <<= 8;
                    b <<= 8;
                    a |= m_input[m_src++];
                }
            }
            return dst;
        }

        ushort InitTable () // sub_10026E30
        {
            ushort d = 0;
            int t = 0;
            byte key = Key;
            for (int i = 0; i < 0x100; i++)
            {
                byte c = m_input[m_src++];
                if (0 != Key)
                {
                    c = (byte)(((c << 1) | (c >> 7)) ^ key);
                    key -= (byte)i;
                }
                word_10036650[i*2] = d;
                word_10036650[i*2+1] = c;
                d += c;
                for (int j = 0; j < c; ++j)
                    byte_table[t++] = (byte)i;
            }
            return d;
        }

        uint GetMask (uint d) // sub_10026DC0
        {
            d--;
            d >>= 8;
            uint result = 0xff;
            while (d > 0)
            {
                d >>= 1;
                result = (result << 1) | 1;
            }
            return result;
        }
    }
}
