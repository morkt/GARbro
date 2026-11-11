//! \file       Decoder.cs
//! \date       2026-02-22
//! \brief      HUNEX General Game Engine decompression functions.
//
// Copyright (C) 2026 by morkt
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

namespace GameRes.Formats.HuneX {
    internal class HuffmanTree {
        internal class HuffmanNode {
            public int Weight;
            public int Index;
            public HuffmanNode Parent;
            public HuffmanNode Child0;
            public HuffmanNode Child1;
        }

        List<HuffmanNode> m_table;
        bool m_invert;

        public HuffmanTree(int[] weights, bool invert = false) {
            m_table = new List<HuffmanNode>(weights.Length);

            for (int i = 0; i < weights.Length; i++) {
                m_table.Add(new HuffmanNode {
                    Index = i,
                    Weight = weights[i]
                });
            }

            m_invert = invert;
        }

        public void Build(int max_entries) {
            int total_weight = m_table.Sum(x => x.Weight);
            for (int i = m_table.Count; i < max_entries; i++) {
                HuffmanNode child0 = null, child1 = null;
                for (int j = 0; j < i; j++) {
                    var node = m_table[j];
                    if (node.Weight == 0 || node.Parent != null)
                        continue;
                    if (child0 == null || node.Weight < child0.Weight) {
                        child1 = child0;
                        child0 = node;
                    }
                    else if (child1 == null || node.Weight < child1.Weight) {
                        child1 = node;
                    }
                }
                var parent = new HuffmanNode();
                if (m_invert) {
                    SetNodeRelation(parent, child1, child0);
                }
                else {
                    SetNodeRelation(parent, child0, child1);
                }
                m_table.Add(parent);
                if (parent.Weight >= total_weight)
                    break;
            }
        }

        public int DecodeSequence(IBitStream input) {
            HuffmanNode node = m_table[m_table.Count - 1];

            while (node.Child0 != null || node.Child1 != null) {
                int bit = input.GetNextBit();
                node = bit > 0 ? node.Child1 : node.Child0;
            }

            return node.Index; 
        }

        void SetNodeRelation(HuffmanNode parent, HuffmanNode child0, HuffmanNode child1) {
            if (child0 != null) {
                parent.Child0 = child0;
                child0.Parent = parent;
                parent.Weight += child0.Weight;
            }
            if (child1 != null) {
                parent.Child1 = child1;
                child1.Parent = parent;
                parent.Weight += child1.Weight;
            }
        }
    }

    internal class LenZuSettings {
        public byte HuffmanTableBitCount;
        public byte BackrefLowBitCount;
        public byte BackrefBaseDistance;
    }

    internal sealed class LenZuDecoder {
        Stream m_input;
        byte[] m_unpacked;
        LenZuSettings m_settings;

        public LenZuDecoder(byte[] buffer) {
            m_unpacked = new byte[BitConverter.ToUInt32(buffer, 0)];
            m_settings = new LenZuSettings {
                HuffmanTableBitCount = Math.Max(buffer[0x11], buffer[0x12]),
                BackrefLowBitCount = buffer[0x14],
                BackrefBaseDistance = buffer[0x15]
            };
            m_input = new MemoryStream(buffer.Skip(0x16).ToArray());
        }

        public byte[] Unpack() {
            int offset = 0;
            int first_real_entry = 1 << m_settings.HuffmanTableBitCount;
            int index_bits = (m_settings.HuffmanTableBitCount + 7) / 8;
            int index_bytes = (index_bits + 7) / 8;
            int fill_entries = ReadIntVL(index_bytes);
            if (fill_entries == 0)
                fill_entries = first_real_entry;
            var weights = new int[first_real_entry]; // idk why this can work xD
            if (first_real_entry * 4 < (index_bits + 4) * fill_entries) {
                fill_entries = first_real_entry;
                for (int i = 0; i < fill_entries; i++) {
                    weights[i] = ReadIntVL();
                }
            }
            else {
                for (int i = 0; i < fill_entries; i++) {
                    int idx = ReadIntVL(index_bytes);
                    weights[idx] = ReadIntVL();
                }
            }
            var tree = new HuffmanTree(weights, true);
            tree.Build(((first_real_entry + 1) * first_real_entry) >> 1);
            using (var input = new MsbBitStream(m_input, true)) {
                while (offset < m_unpacked.Length) {
                    int isBackRef = input.GetNextBit();
                    if (isBackRef == -1)
                        break;
                    int length = tree.DecodeSequence(input);
                    if (isBackRef > 0) {
                        length += m_settings.BackrefBaseDistance;
                        int distanceHighBits = tree.DecodeSequence(input);
                        int distanceLowBits = m_settings.BackrefLowBitCount > 0
                                            ? input.GetBits(m_settings.BackrefLowBitCount) : 0;
                        int distance = (distanceLowBits
                                     | (distanceHighBits << m_settings.BackrefLowBitCount))
                                     + m_settings.BackrefBaseDistance;
                        for (int i = 0; i < length; i++) {
                            m_unpacked[offset] = m_unpacked[offset - distance];
                            offset++;
                        }
                    }
                    else {
                        for (int i = 0; i < length + 1; i++) {
                            m_unpacked[offset++] = (byte)input.GetBits(8);
                        }
                    }
                }
                return m_unpacked;
            }
        }

        int ReadIntVL(int length = sizeof(int)) {
            var buffer = new byte[Math.Max(sizeof(int), length)];
            m_input.Read(buffer, 0, length);
            return BitConverter.ToInt32(buffer, 0);
        }
    }

    internal class RingBuffer<T> {
       private readonly T[] m_buffer;
       private int m_head;
       private int m_tail;
       private int m_count;

       public T this[int index] { get { return m_buffer[index]; } }

       public RingBuffer(int capacity) {
           m_buffer = new T[capacity];
       }

       public void Append(T item) {
           m_buffer[m_head] = item;
           m_head = (m_head + 1) % m_buffer.Length;
           if (m_count == m_buffer.Length)
               m_tail = (m_tail + 1) % m_buffer.Length;
           else
               m_count++;
       }

       public void Append(T[] items) {
           foreach (T item in items)
               Append(item);
       }
    }

    internal sealed class MzxDecoder {
        Stream m_input;
        byte[] m_unpacked;

        public MzxDecoder(byte[] buffer) {
            m_unpacked = new byte[BitConverter.ToUInt32(buffer, 0)];
            m_input = new MemoryStream(buffer.Skip(4).ToArray());
        }

        public byte[] Unpack() {
            int offset = 0;
            int counter = 0;
            var ringbuf = new RingBuffer<byte>(128);
            while (offset < m_unpacked.Length) {
                if (counter <= 0)
                    counter = 0x1000;
                byte flag = (byte)m_input.ReadByte();
                int len = flag >> 2;
                var buffer = new byte[2];
                switch (flag & 3) {
                    case 0: // RLE
                        if (counter != 0x1000) {
                            buffer[1] = m_unpacked[offset - 1];
                            buffer[0] = m_unpacked[offset - 2];
                        }
                        offset = Write2(buffer, offset, len + 1);
                        break;
                    case 1: // BACKREF
                        int k = m_input.ReadByte() * 2 + 2;
                        buffer = new byte[len * 2 + 2];
                        int pos = offset - k;
                        k = Math.Min(k, buffer.Length);
                        Buffer.BlockCopy(m_unpacked, pos, buffer, 0, k);
                        for (pos = k; pos < buffer.Length; pos += k) {
                            Buffer.BlockCopy(buffer, 0, buffer, pos, Math.Min(k, buffer.Length - pos));
                        }
                        offset = Write2(buffer, offset, 1);
                        break;
                    case 2: // RINGBUF
                        buffer[0] = ringbuf[len * 2];
                        buffer[1] = ringbuf[len * 2 + 1];
                        offset = Write2(buffer, offset, 1);
                        counter += len;
                        break;
                    case 3: // LITERAL
                        buffer = new byte[len * 2 + 2];
                        m_input.Read(buffer, 0, buffer.Length);
                        offset = Write2(buffer, offset, 1);
                        ringbuf.Append(buffer);
                        break;
                }
                counter -= len + 1;
            }
            return m_unpacked;
        }

        int Write2(byte[] buffer, int offset, int count) {
            for (int i = 0; i < count; i++) {
                int bytesToWrite = Math.Min(buffer.Length, m_unpacked.Length - offset);
                if (bytesToWrite <= 0)
                    break;
                Buffer.BlockCopy(buffer, 0, m_unpacked, offset, bytesToWrite);
                offset += bytesToWrite;
            }
            return offset;
        }
    }
}
