//! \file       ArcFA2.cs
//! \date       Sun Feb 19 10:46:57 2017
//! \brief      Foster game engine resource archive.
//
// Copyright (C) 2017 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Foster
{
    [Export(typeof(ArchiveFormat))]
    public class Fa2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "FA2"; } }
        public override string Description { get { return "Foster game engine resource archive"; } }
        public override uint     Signature { get { return 0x00324146; } } // 'FA2'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0xC);
            if (!IsSaneCount (count))
                return null;
            bool is_packed = (file.View.ReadByte (4) & 1) != 0;
            uint index_offset = file.View.ReadUInt32 (8);
            byte[] index;
            using (var input = file.CreateStream (index_offset))
            {
                if (is_packed)
                    index = Decompress (input, (uint)count * 0x20);
                else
                    index = file.View.ReadBytes (index_offset, (uint)(file.MaxOffset - index_offset));
            }

            uint data_offset = 0x10;
            int index_pos = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = Binary.GetCString (index, index_pos, 0xF);
                index_pos += 0xF;
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.IsPacked = (index[index_pos] & 2) != 0;
                entry.Offset = data_offset;
                index_pos += 9;
                entry.UnpackedSize = index.ToUInt32 (index_pos);
                entry.Size         = index.ToUInt32 (index_pos+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_pos += 8;
                data_offset += (entry.Size + 0xFu) & ~0xFu;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return input;
            var data = Decompress (input, pent.UnpackedSize);
            return new BinMemoryStream (data, entry.Name);
        }

        byte[] Decompress (IBinaryStream input, uint unpacked_size)
        {
            var comp = new Fa2Compression (input, unpacked_size);
            return comp.Unpack();
        }
    }

    internal class Fa2Compression
    {
        IBinaryStream   m_input;
        byte[]          m_output;

        public Fa2Compression (IBinaryStream input, uint unpacked_size)
        {
            m_input = input;
            m_output = new byte[unpacked_size];
        }

        public byte[] Unpack ()
        {
            m_bit_count = 0;
            int dst = 0;
            while (dst < m_output.Length)
            {
                if (GetNextBit() != 0)
                {
                    m_output[dst++] = m_input.ReadUInt8();
                    continue;
                }
                int offset;
                if (GetNextBit() != 0)
                {
                    if (GetNextBit() != 0)
                    {
                        offset = m_input.ReadUInt8() << 3;
                        offset |= GetBits (3);
                        offset += 0x100;
                        if (offset >= 0x8FF)
                            break;
                    }
                    else
                    {
                        offset = m_input.ReadUInt8();
                    }
                    m_output[dst  ] = m_output[dst-offset-1];
                    m_output[dst+1] = m_output[dst-offset  ];
                    dst += 2;
                }
                else
                {
                    if (GetNextBit() != 0)
                    {
                        offset = m_input.ReadUInt8() << 1;
                        offset |= GetNextBit();
                    }
                    else
                    {
                        offset = 0x100;
                        if (GetNextBit() != 0)
                        {
                            offset |= m_input.ReadUInt8();
                            offset <<= 1;
                            offset |= GetNextBit();
                        }
                        else if (GetNextBit() != 0)
                        {
                            offset |= m_input.ReadUInt8();
                            offset <<= 2;
                            offset |= GetBits (2);
                        }
                        else if (GetNextBit() != 0)
                        {
                            offset |= m_input.ReadUInt8();
                            offset <<= 3;
                            offset |= GetBits (3);
                        }
                        else
                        {
                            offset |= m_input.ReadUInt8();
                            offset <<= 4;
                            offset |= GetBits (4);
                        }
                    }
                    int count = 0;
                    if (GetNextBit() != 0)
                    {
                        count = 3;
                    }
                    else if (GetNextBit() != 0)
                    {
                        count = 4;
                    }
                    else if (GetNextBit() != 0)
                    {
                        count = 5 + GetNextBit();
                    }
                    else if (GetNextBit() != 0)
                    {
                        count = 7 + GetBits (2);
                    }
                    else if (GetNextBit() != 0)
                    {
                        count = 11 + GetBits (4);
                    }
                    else
                    {
                        count = 27 + m_input.ReadUInt8();
                    }
                    Binary.CopyOverlapped (m_output, dst - offset - 1, dst, count);
                    dst += count;
                }
            }
            return m_output;
        }

        uint    m_bits;
        int     m_bit_count;

        void FetchBits ()
        {
            m_bits = m_input.ReadUInt32();
            m_bit_count = 32;
        }

        int GetNextBit ()
        {
            if (0 == m_bit_count)
                FetchBits();
            int bit = (int)((m_bits >> 31) & 1);
            m_bits <<= 1;
            --m_bit_count;
            return bit;
        }

        int GetBits (int count)
        {
            uint bits = 0;
            int avail_bits = Math.Min (count, m_bit_count);
            if (avail_bits > 0)
            {
                bits = m_bits >> (32 - avail_bits);
                m_bits <<= avail_bits;
                m_bit_count -= avail_bits;
                count -= avail_bits;
            }
            if (count > 0)
            {
                FetchBits();
                bits = bits << count | m_bits >> (32 - count);
                m_bits <<= count;
                m_bit_count -= count;
            }
            return (int)bits;
        }
    }
}
