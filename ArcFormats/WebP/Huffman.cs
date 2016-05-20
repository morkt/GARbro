//! \file       Huffman.cs
//! \date       Wed May 18 22:19:23 2016
//! \brief      Google WEBP Huffman compression implementaion.
/*
Copyright (c) 2010, Google Inc. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

  * Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.

  * Redistributions in binary form must reproduce the above copyright
    notice, this list of conditions and the following disclaimer in
    the documentation and/or other materials provided with the
    distribution.

  * Neither the name of Google nor the names of its contributors may
    be used to endorse or promote products derived from this software
    without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/
//
// C# port by morkt (C) 2016
//

namespace GameRes.Formats.Google
{
    static class Huffman
    {
        public const int CodesPerMetaCode = 5;
        public const int PackedBits = 6;
        public const int PackedTableSize = 1 << PackedBits;

        public const int DefaultCodeLength      = 8;
        public const int MaxAllowedCodeLength   = 15;

        public const int NumLiteralCodes  = 256;
        public const int NumLengthCodes   = 24;
        public const int NumDistanceCodes = 40;
        public const int CodeLengthCodes  = 19;

        public const int MinBits   = 2;  // min number of Huffman bits
        public const int MaxBits   = 9;  // max number of Huffman bits

        public const int TableBits = 8;
        public const int TableMask = (1 << TableBits) - 1;

        public const int LengthsTableBits = 7;
        public const int LengthsTableMask = (1 << LengthsTableBits) - 1;

        static uint GetNextKey (uint key, int len)
        {
            uint step = 1u << (len - 1);
            while (0 != (key & step))
                step >>= 1;
            return (key & (step - 1)) + step;
        }

        public static int BuildTable (HuffmanCode[] root_table, int index, int root_bits, int[] code_lengths, int code_lengths_size)
        {
            int table = index;  // next available space in table
            int total_size = 1 << root_bits;  // total size root table + 2nd level table
            int len;                          // current code length
            int symbol;                       // symbol index in original or sorted table
            // number of codes of each length:
            int[] count = new int[MaxAllowedCodeLength + 1];
            // offsets in sorted table for each length:
            int[] offset = new int[MaxAllowedCodeLength + 1];

            // Build histogram of code lengths.
            for (symbol = 0; symbol < code_lengths_size; ++symbol)
            {
                if (code_lengths[symbol] > MaxAllowedCodeLength)
                    return 0;
                ++count[code_lengths[symbol]];
            }

            // Error, all code lengths are zeros.
            if (count[0] == code_lengths_size)
                return 0;

            // Generate offsets into sorted symbol table by code length.
            offset[1] = 0;
            for (len = 1; len < MaxAllowedCodeLength; ++len)
            {
                if (count[len] > (1 << len))
                    return 0;
                offset[len + 1] = offset[len] + count[len];
            }

            var sorted = new int[code_lengths_size];

            // Sort symbols by length, by symbol order within each length.
            for (symbol = 0; symbol < code_lengths_size; ++symbol)
            {
                int symbol_code_length = code_lengths[symbol];
                if (code_lengths[symbol] > 0)
                    sorted[offset[symbol_code_length]++] = symbol;
            }

            // Special case code with only one value.
            if (offset[MaxAllowedCodeLength] == 1)
            {
                HuffmanCode code;
                code.bits = 0;
                code.value = (ushort)sorted[0];
                ReplicateValue (root_table, table, 1, total_size, code);
                return total_size;
            }

            int step;              // step size to replicate values in current table
            uint low = uint.MaxValue;     // low bits for current root entry
            uint mask = (uint)total_size - 1;    // mask for low bits
            uint key = 0;          // reversed prefix code
            int num_nodes = 1;     // number of Huffman tree nodes
            int num_open = 1;      // number of open branches in current tree level
            int table_bits = root_bits;        // key length of current table
            int table_size = 1 << table_bits;  // size of current table
            symbol = 0;
            // Fill in root table.
            for (len = 1, step = 2; len <= root_bits; ++len, step <<= 1)
            {
                num_open <<= 1;
                num_nodes += num_open;
                num_open -= count[len];
                if (num_open < 0)
                    return 0;
                for (; count[len] > 0; --count[len])
                {
                    HuffmanCode code;
                    code.bits = (byte)len;
                    code.value = (ushort)sorted[symbol++];
                    ReplicateValue (root_table, table + (int)key, step, table_size, code);
                    key = GetNextKey (key, len);
                }
            }

            // Fill in 2nd level tables and add pointers to root table.
            for (len = root_bits + 1, step = 2; len <= MaxAllowedCodeLength; ++len, step <<= 1)
            {
                num_open <<= 1;
                num_nodes += num_open;
                num_open -= count[len];
                if (num_open < 0)
                    return 0;
                for (; count[len] > 0; --count[len])
                {
                    HuffmanCode code;
                    if ((key & mask) != low)
                    {
                        table += table_size;
                        table_bits = NextTableBitSize (count, len, root_bits);
                        table_size = 1 << table_bits;
                        total_size += table_size;
                        low = key & mask;
                        root_table[index+low].bits = (byte)(table_bits + root_bits);
                        root_table[index+low].value = (ushort)(table - index - low);
                    }
                    code.bits = (byte)(len - root_bits);
                    code.value = (ushort)sorted[symbol++];
                    ReplicateValue (root_table, table + (int)(key >> root_bits), step, table_size, code);
                    key = GetNextKey (key, len);
                }
            }

            // Check if tree is full.
            if (num_nodes != 2 * offset[MaxAllowedCodeLength] - 1)
                return 0;

            return total_size;
        }

        static void ReplicateValue (HuffmanCode[] table, int offset, int step, int end, HuffmanCode code)
        {
            do
            {
                end -= step;
                table[offset+end] = code;
            }
            while (end > 0);
        }

        static int NextTableBitSize (int[] count, int len, int root_bits)
        {
            int left = 1 << (len - root_bits);
            while (len < MaxAllowedCodeLength)
            {
                left -= count[len];
                if (left <= 0) break;
                ++len;
                left <<= 1;
            }
            return len - root_bits;
        }
    }

    internal struct HuffmanCode
    {
        public byte bits;       // number of bits used for this symbol
        public ushort value;    // symbol value or table offset
    }

    internal struct HuffmanCode32
    {
        public int bits;    // number of bits used for this symbol,
                            // or an impossible value if not a literal code.
        public uint value;  // 32b packed ARGB value if literal,
                            // or non-literal symbol otherwise
    }

    internal class HTreeGroup
    {
        HuffmanCode[] tables;
        int[] htrees = new int[Huffman.CodesPerMetaCode];
        public bool is_trivial_literal;  // True, if huffman trees for Red, Blue & Alpha
                                        // Symbols are trivial (have a single code).
        public uint literal_arb;         // If is_trivial_literal is true, this is the
                                        // ARGB value of the pixel, with Green channel
                                        // being set to zero.
        public bool is_trivial_code;          // true if is_trivial_literal with only one code
        public bool use_packed_table;         // use packed table below for short literal code
        // table mapping input bits to a packed values, or escape case to literal code
        public HuffmanCode32[] packed_table = new HuffmanCode32[Huffman.PackedTableSize];

        public HuffmanCode[] Tables { get { return tables; } }

        public void SetMeta (int meta, int base_index)
        {
            htrees[meta] = base_index;
        }

        public int GetMeta (int meta)
        {
            return htrees[meta];
        }

        public HuffmanCode GetCode (int meta, int index)
        {
            return tables[htrees[meta] + index];
        }

        public void SetCode (int meta, int index, HuffmanCode code)
        {
            tables[htrees[meta] + index] = code;
        }

        public static HTreeGroup[] New (int num_htree_groups, int table_size)
        {
            var tables = new HuffmanCode[num_htree_groups * table_size];
            var htree_groups = new HTreeGroup[num_htree_groups];
            for (int i = 0; i < num_htree_groups; ++i)
            {
                htree_groups[i] = new HTreeGroup();
                htree_groups[i].tables = tables;
            }
            return htree_groups;
        }
    }
}
