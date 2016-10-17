//! \file       ArcLINK.cs
//! \date       Fri Jan 22 18:44:56 2016
//! \brief      KaGuYa archive format.
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

namespace GameRes.Formats.Kaguya
{
    [Export(typeof(ArchiveFormat))]
    public class LinkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "LINK/KAGUYA"; } }
        public override string Description { get { return "KaGuYa script engine resource archive"; } }
        public override uint     Signature { get { return 0x4B4E494C; } } // 'LINK'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public LinkOpener ()
        {
            Extensions = new string[] { "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadByte (4) - '0';
            if (version != 3)
                return null;

            long current_offset = 8;
            var dir = new List<Entry>();
            while (current_offset+4 < file.MaxOffset)
            {
                uint size = file.View.ReadUInt32 (current_offset);
                if (0 == size)
                    break;
                if (size < 0x10)
                    return null;
                bool is_compressed = file.View.ReadInt32 (current_offset+4) != 0;
                uint name_length = file.View.ReadByte (current_offset+0xD);
                var name = file.View.ReadString (current_offset+0x10, name_length);
                current_offset += 0x10 + name_length;
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset = current_offset;
                entry.Size   = size - (0x10 + name_length);
                entry.IsPacked = is_compressed && file.View.AsciiEqual (current_offset, "BMR");
                dir.Add (entry);
                current_offset += entry.Size;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
            using (var bmr = new BmrDecoder (input))
            {
                bmr.Unpack();
                return new BinMemoryStream (bmr.Data, entry.Name);
            }
        }
    }

    internal class BmrDecoder : IDisposable
    {
        byte[]          m_output;
        MsbBitStream    m_input;
        int             m_final_size;
        int             m_step;
        int             m_key;

        public byte[] Data { get { return m_output; } }

        public BmrDecoder (IBinaryStream input)
        {
            input.Position = 3;
            m_step = input.ReadUInt8();
            m_final_size = input.ReadInt32();
            m_key = input.ReadInt32();
            int unpacked_size = input.ReadInt32();
            m_output = new byte[unpacked_size];
            m_input = new MsbBitStream (input.AsStream, true);
        }

        public void Unpack ()
        {
            m_input.Input.Position = 0x14;
            UnpackHuffman();
            DescrambleOutput();
            m_output = Decode (m_output, m_key);
            if (m_step != 0)
                m_output = DecompressRLE();
        }

        byte[] DecompressRLE ()
        {
            var result = new byte[m_final_size];
            int src = 0;
            for (int i = 0; i < m_step; ++i)
            {
                byte v1 = m_output[src++];
                result[i] = v1;
                int dst = i + m_step;
                while (dst < result.Length)
                {
                    byte v2 = m_output[src++];
                    result[dst] = v2;
                    dst += m_step;
                    if (v2 == v1)
                    {
                        int count = m_output[src++];
                        if (0 != (count & 0x80))
                            count = m_output[src++] + ((count & 0x7F) << 8) + 128;
                        while (count --> 0)
                        {
                            result[dst] = v2;
                            dst += m_step;
                        }
                        if (dst < m_output.Length)
                        {
                            v2 = m_output[src++];
                            result[dst] = v2;
                            dst += m_step;
                        }
                    }
                    v1 = v2;
                }
            }
            return result;
        }


        void DescrambleOutput ()
        {
            var scramble = new byte[256];
            for (int i = 0; i < 256; ++i)
                scramble[i] = (byte)i;
            for (int i = 0; i < m_output.Length; ++i)
            {
                byte v = m_output[i];
                m_output[i] = scramble[v];
                for (int j = v; j > 0; --j)
                {
                    scramble[j] = scramble[j-1];
                }
                scramble[0] = m_output[i];
            }
        }

        byte[] Decode (byte[] input, int key)
        {
            var freq_table = new int[256];
            for (int i = 0; i < input.Length; ++i)
            {
                ++freq_table[input[i]];
            }
            for (int i = 1; i < 256; ++i)
            {
                freq_table[i] += freq_table[i-1];
            }
            var distrib_table = new int[input.Length];
            for (int i = input.Length-1; i >= 0; --i)
            {
                int v = input[i];
                int freq = freq_table[v] - 1;
                freq_table[v] = freq;
                distrib_table[freq] = i;
            }
            int pos = key;
            var copy_out = new byte[input.Length];
            for (int i = 0; i < copy_out.Length; ++i)
            {
                pos = distrib_table[pos];
                copy_out[i] = input[pos];
            }
            return copy_out;
        }

        ushort      m_token;
        ushort[,]   m_tree = new ushort[2,256];

        void UnpackHuffman ()
        {
            m_token = 256;
            ushort root = CreateHuffmanTree();
            int dst = 0;
            while (dst < m_output.Length)
            {
                ushort symbol = root;
                while (symbol >= 0x100)
                {
                    int bit = m_input.GetNextBit();
                    if (-1 == bit)
                        throw new EndOfStreamException();
                    symbol = m_tree[bit,symbol-256];
                }
                m_output[dst++] = (byte)symbol;
            }
        }

        ushort CreateHuffmanTree ()
        {
            if (0 != m_input.GetNextBit())
            {
                ushort v = m_token++;
                m_tree[0,v-256] = CreateHuffmanTree();
                m_tree[1,v-256] = CreateHuffmanTree();
                return v;
            }
            else
            {
                return (ushort)m_input.GetBits (8);
            }
        }

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
}
