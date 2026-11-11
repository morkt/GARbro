//! \file       HuffmanDecoder.cs
//! \date       2024 Aug 2
//! \brief      Custom Huffman decoder for DXA archives.
//
// Copyright (C) 2017 by morkt - GetBits function
// Copyright (C) 2024 by MrSoup678
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

//Original file is Huffman.cpp. Creator: 山田 巧 Date: 2018 Dec 16


using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using GameRes.Utility;

namespace GameRes.Formats.DxLib
{
    internal class DXA8HuffmanNode
    {
        public UInt64 Weight;
        public int bitNumber;
        public byte[] bitArray; //32 bytes here
        public int Index;

        public int ParentNode; // index of parent node.
        public int[] ChildNode; //two children nodes, -1 if not existent.

        internal DXA8HuffmanNode()
        {
            bitArray = new byte[32];
            ChildNode = new int[2];
        }
        
    }

    internal sealed class HuffmanDecoder
    {
        byte[]          m_input;
        byte[]          m_output;

        int             m_src;
        byte             m_bits;
        int             m_bit_count;

        ulong m_readBytes;
        byte m_readBits;

        
        DXA8HuffmanNode[] nodes; //256+255 nodes

        ulong originalSize;
        ulong compressedSize;
        ulong headerSize;

        ulong srcSize;

        //ushort token = 256;

        public HuffmanDecoder (byte[] src,ulong srcSize)
        {
            m_input = src;
            m_output = null;

            this.srcSize = srcSize;
            m_src = 0;
            m_bit_count = 0;
            m_readBytes = 0;
            m_readBits = 0;
            originalSize = compressedSize = headerSize = 0;
            ushort[] weights = new ushort[256];
            nodes = new DXA8HuffmanNode[256+255]; //256 data nodes, then 255 hierarchy nodes.
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i] = new DXA8HuffmanNode();
            }
        }

        public byte[] Unpack ()
        {

            for (int i=0; i<nodes.Length; i++)
            {
                nodes[i].ParentNode = -1;
                nodes[i].ChildNode[0] = -1;
                nodes[i].ChildNode[1] = -1;
            }
            SetupWeights();
            //check if compressedSize and src size match.
            if (srcSize!=(compressedSize+headerSize))
            {
                throw new FileSizeException(String.Format("Supplied srcSize does not match with compressedSize+headerSize. Expected {0} got {1}",compressedSize+headerSize,srcSize));
            }
            m_output = new byte[originalSize];
            CreateTree();
            PopulateDataNodes();
            DoUnpack();
            return m_output;
        }

        private void DoUnpack()
        {
            var targetSize = originalSize;
            byte[] compressedData = new byte[compressedSize];
            Array.Copy(m_input, (long)headerSize, compressedData, 0, (long)(compressedSize));

            int PressBitCounter=0, PressBitData=0, Index=0, NodeIndex=0;
            int PressSizeCounter = 0;
            ulong DestSizeCounter = 0;
            int[] NodeIndexTable=new int[512];
            {
                ushort[] bitMask = new ushort[9];
                for (int i = 0; i < 9; i++)
                {
                    bitMask[i] = (ushort)((1<<i+1) - 1);
                }

                for (int i = 0; i < 512; i++)
                {
                    NodeIndexTable[i] = -1;

                    for (int j = 0; j < 256 + 254; j++)
                    {
                        ushort BitArrayFirstBatch;
                        if (nodes[j].bitNumber > 9) continue;

                        BitArrayFirstBatch = (ushort)(nodes[j].bitArray[0] | (nodes[j].bitArray[1] << 8));

                        if ((i & bitMask[nodes[j].bitNumber - 1]) == (BitArrayFirstBatch & bitMask[nodes[j].bitNumber-1]))
                        {
                            NodeIndexTable[i] = j;
                            break;
                        }
                    }

                }

            }
            PressBitData = compressedData[PressBitCounter];

            for (DestSizeCounter = 0;DestSizeCounter < originalSize; DestSizeCounter++)
            {
                if (DestSizeCounter>= originalSize - 17)
                {
                    NodeIndex = 510;
                }
                else
                {
                    if (PressBitCounter==8)
                    {
                        PressSizeCounter++;
                        PressBitData = compressedData[PressSizeCounter];
                        PressBitCounter = 0;
                    }

                    PressBitData = (PressBitData | (compressedData[PressSizeCounter+1]<<(8-PressBitCounter))) & 0x1ff;
                    NodeIndex = NodeIndexTable[PressBitData];
                    PressBitCounter += nodes[NodeIndex].bitNumber;
                    if (PressBitCounter >= 16)
                    {
                        PressSizeCounter += 2;
                        PressBitCounter -= 16;
                        PressBitData = compressedData[PressSizeCounter] >> PressBitCounter;
                    }
                    else if (PressBitCounter >=8)
                    {
                        PressSizeCounter ++;
                        PressBitCounter -= 8;
                        PressBitData = compressedData[PressSizeCounter] >> PressBitCounter;
                    }
                    else
                    {
                        PressBitData >>= nodes[NodeIndex].bitNumber;
                    }
                }

                while (NodeIndex > 255)
                {
                    if (PressBitCounter == 8)
                    {
                        PressSizeCounter++;
                        PressBitData = compressedData[PressSizeCounter];
                        PressBitCounter = 0;
                    }
                    Index = PressBitData & 1;
                    PressBitData >>= 1;
                    PressBitCounter++;
                    NodeIndex = nodes[NodeIndex].ChildNode[Index];
                }
                m_output[DestSizeCounter] = (byte)NodeIndex;
            }

        }

        private void PopulateDataNodes()
        {
            //The data which is populated is path from root to target node in bits.
            byte[] ScratchSpace = new byte[32];
            int TempBitIndex, TempBitCount;

            for (int i = 0; i < 256 + 254; i++) //root node is excluded.
            {
                nodes[i].bitNumber = 0;
                TempBitIndex = 0;
                TempBitCount = 0;
                ScratchSpace[TempBitIndex] = 0;

                for (int j = i; nodes[j].ParentNode!=-1;j = nodes[j].ParentNode)
                {
                    if (TempBitCount == 8)
                    {
                        TempBitCount = 0;
                        TempBitIndex++;
                        ScratchSpace[TempBitIndex] = 0;
                    }
                    ScratchSpace[TempBitIndex] <<= 1;
                    ScratchSpace[TempBitIndex] |= (byte)nodes[j].Index;
                    TempBitCount++;
                    nodes[i].bitNumber++;

                }
                //path is now backwards (target to root). Populate BitPath from root to target.
                int BitIndex=0, BitCount=0;
                nodes[i].bitArray[BitIndex] = 0;
                while (TempBitIndex >= 0)
                {
                    if (BitCount == 8)
                    {
                        BitCount = 0;
                        BitIndex++;
                        nodes[i].bitArray[BitIndex] = 0;
                    }
                    nodes[i].bitArray[BitIndex] |= (byte)((ScratchSpace[TempBitIndex] & 1) << BitCount);
                    ScratchSpace[TempBitIndex] >>= 1;
                    TempBitCount--;
                    if (TempBitCount == 0)
                    {
                        TempBitIndex--;
                        TempBitCount = 8;
                    }
                    BitCount++;
                }
            }


        }

        private void SetupWeights()
        {
            int sizeA, sizeB;
            byte BitNum;
            byte Minus;
            ushort SaveData;
            ushort[] weights = new ushort[256];
            sizeA = (int)GetBits(6) + 1;
            originalSize = GetBits(sizeA);
            sizeB = (int)GetBits(6)+1;
            compressedSize = GetBits(sizeB);

            BitNum = (byte)(((int)GetBits(3) + 1) * 2);
            Minus = (byte)GetBits(1);
            SaveData = (ushort)GetBits(BitNum);
            weights[0] = SaveData;
            for (int i = 1; i < 256; i++)
            {
                BitNum = (byte)(((int)GetBits(3) + 1) * 2);
                Minus = (byte)GetBits(1);
                SaveData = (ushort)GetBits(BitNum);
                weights[i] = (ushort)(Minus == 1 ? weights[i - 1] - SaveData : weights[i - 1] + SaveData);
            }
            headerSize = GetReadBytes();
            for (int i = 0;i < 256; i++)
            {
                nodes[i].Weight = weights[i];
            }

        }

        void CreateTree()
        {
            int NodeNum=256, DataNum=256;

            while (DataNum > 1)
            {
                int MinNode1 = -1;
                int MinNode2 = -1;
                int NodeIndex = 0;


                for (int i = 0; i < DataNum; NodeIndex++) {
                    //don't do anything if we already have a parent set.
                    if (nodes[NodeIndex].ParentNode != -1) continue;
                    i++;
                    //we need to get the two lowest numbers for parenting.
                    if (MinNode1 == -1 || nodes[MinNode1].Weight > nodes[NodeIndex].Weight)
                    {
                        {
                            MinNode2 = MinNode1;
                            MinNode1 = NodeIndex;
                        }
                    } else if (MinNode2 == -1 || nodes[MinNode2].Weight > nodes[NodeIndex].Weight)
                    {
                        MinNode2 = NodeIndex;
                    }
                }
                nodes[NodeNum].ParentNode = -1;
                nodes[NodeNum].Weight = nodes[MinNode1].Weight + nodes[MinNode2].Weight;
                nodes[NodeNum].ChildNode[0] = MinNode1;
                nodes[NodeNum].ChildNode[1] = MinNode2;
                nodes[MinNode1].Index = 0;
                nodes[MinNode2].Index = 1;
                nodes[MinNode1].ParentNode = NodeNum;
                nodes[MinNode2].ParentNode = NodeNum;

                NodeNum++;
                DataNum--;
            }
        }

        ulong GetBits (int count)
        {
            ulong bits = 0;
            for (int i = 0; i < count;i++)
            {
                if (0 == m_bit_count)
                {
                    m_bits = m_input[m_src];
                    m_src++;
                    m_bit_count = 8;
                }
                //bits are read backwards.
                bits |= ((ulong)((m_bits >> (7 - m_readBits)) & 1)) <<(count-1-i);
                --m_bit_count;
                m_readBits++;
                if (m_readBits ==8)
                {
                    m_readBits = 0;
                    m_readBytes++;
                }
            }
            return bits;
        }

        ulong GetReadBytes()
        {
            return m_readBytes + (m_readBits != 0 ? 1ul : 0ul);
        }
    }
}
