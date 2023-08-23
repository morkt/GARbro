//! \file       ImageNP.cs
//! \date       2022 Jun 26
//! \brief      Paprika image format.
//
// Copyright (C) 2022 by morkt
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
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Paprika
{
    public class NpMetaData : ImageMetaData
    {
        public uint DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class NpFormat : ImageFormat
    {
        public override string         Tag { get { return "PIC/NP"; } }
        public override string Description { get { return "Paprika image format"; } }
        public override uint     Signature { get { return 0x0001504E; } } // 'NP'

        public NpFormat ()
        {
            Signatures = new[] { 0x0001504Eu, 0u };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            if (!header.AsciiEqual (0, "NP"))
                return null;
            int frame_count = header.ToUInt16 (2);
            if (frame_count == 0)
                return null;
            return new NpMetaData {
                Width  = header.ToUInt32 (4),
                Height = header.ToUInt32 (8),
                BPP = 24,
                DataOffset = header.ToUInt32 (12),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new NpReader (file, (NpMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, PixelFormats.Bgr24, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("NpFormat.Write not implemented");
        }
    }

    internal class NpReader
    {
        IBinaryStream   m_input;
        NpMetaData      m_info;
        byte[]          m_output;

        public NpReader (IBinaryStream input, NpMetaData info)
        {
            m_input = input;
            m_info = info;
        }

        int     bitCount;
        uint    bits;

        static readonly int[]   bitMap = new int[] {
            2, 0x00, 3, 0x04, 3, 0x0C, 4, 0x14, 4, 0x24, 4, 0x34, 4, 0x44, 4, 0x54,
            4, 0x64, 4, 0x74, 4, 0x84, 4, 0x94, 4, 0xA4, 5, 0xB4, 5, 0xD4, 5, 0xF4,
        };

        static readonly ushort[] wordList = new ushort[] {
            0x100, 0x101, 0x102, 0x103, 0x104, 0x105, 0x106, 0x107,
            0x108, 0x109, 0x10A, 0x10B, 0x10C, 0x10D, 0x10E, 0x10F,
            0x00, 0x20, 0x30, 0xFF,
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
            0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F,
            0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x2F,
            0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F,
            0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F,
            0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F,
            0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F,
            0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x7B, 0x7C, 0x7D, 0x7E, 0x7F,
            0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89, 0x8A, 0x8B, 0x8C, 0x8D, 0x8E, 0x8F,
            0x90, 0x91, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0x9B, 0x9C, 0x9D, 0x9E, 0x9F,
            0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xAB, 0xAC, 0xAD, 0xAE, 0xAF,
            0xB0, 0xB1, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF,
            0xC0, 0xC1, 0xC2, 0xC3, 0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xCB, 0xCC, 0xCD, 0xCE, 0xCF,
            0xD0, 0xD1, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xDB, 0xDC, 0xDD, 0xDE, 0xDF,
            0xE0, 0xE1, 0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xEB, 0xEC, 0xED, 0xEE, 0xEF,
            0xF0, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8, 0xF9, 0xFA, 0xFB, 0xFC, 0xFD, 0xFE,
            0x110, 0x111,
        };

        byte[]      field_0 = new byte[0x10000];
        int         field_4;
        ushort[]    field_8;
        int[]       field_C;
        ushort[]    field_8C;

        public byte[] Unpack ()
        {
            m_input.Position = m_info.DataOffset;
            int unpacked_size = m_input.ReadInt32();
            int packed_size = m_input.ReadInt32();
            m_output = new byte[unpacked_size];

            field_4 = 0;
            field_8  = new ushort[0x112];
            field_C  = bitMap.Clone() as int[];
            field_8C = wordList.Clone() as ushort[];
            int dst = 0;
            while (dst < unpacked_size && UnpackBits (ref dst))
                ;
            var pixels = new byte[unpacked_size - 0x1C];
            Buffer.BlockCopy (m_output, 0x1C, pixels, 0, pixels.Length);
            return pixels;
        }

        ushort[] v74 = new ushort[0x225];

        bool UnpackBits (ref int dst)
        {
            bitCount = 0;
            while (m_input.PeekByte() != -1)
            {
                int v9 = GetBits (4) * 2;
                int v12 = field_C[v9];
                int word;
                if (0 == v12)
                {
                    word = field_8C[field_C[v9 + 1]];
                }
                else
                {
                    int v17 = GetBits (v12) + field_C[v9 + 1];
                    if (v17 >= 274)
                        return false;
                    word = field_8C[v17];
                }
                ++field_8[word];
                int count = 0;
                if (word < 256)
                {
                    if (dst >= m_output.Length)
                        return false;
                    m_output[dst++] = (byte)word;
                    field_0[field_4++ & 0xFFFF] = (byte)word;
                    continue;
                }
                else if (word == 272)
                {
                    sub_416E80 (v74);
                    int j = 0;
                    for (int i = 0; i < 0x112; ++i)
                    {
                        field_8C[i] = v74[j];
                        j += 2;
                    }
                    int v22 = 0;
                    int v23 = 1; // &this->field_C[1];
                    int v69 = 0;
                    for (int v65 = 16; v65 != 0; --v65)
                    {
                        int n;
                        for (n = 0; ; ++n)
                        {
                            int v26 = GetBits (1);
                            if (v26 != 0)
                                break;
                        }
                        v22 += n;
                        field_C[v23 - 1] = v22;
                        field_C[v23] = v69;
                        v23 += 2;
                        v69 += 1 << v22;
                    }
                    continue;
                }
                else if (word == 273)
                {
                    return true;
                }
                else if (word < 264)
                {
                    count = word - 256;
                }
                else
                {
                    int v29 = dword_4257CC[2 * (word - 264)];
                    int v31 = GetBits (v29);
                    count = v31 + dword_4257CC[2 * (word - 264) + 1];
                }
                int v70 = GetBits (3);
                int v64 = 0;
                int v37 = dword_425810[2 * v70] + 9;
                if (v37 > 8)
                {
                    v37 -= 8;
                    int v39 = GetBits (8);
                    v64 = v39 << v37;
                }
                int v43 = GetBits (v37) | v64;
                int v46 = (dword_425810[2 * v70 + 1] << 9) + v43;
                count += 4;
                int v45 = dst; // output;
                int dst_next = dst + count + 4; // &output[count + 4];
                if (dst_next > m_output.Length)
                    break;
                int src = field_4 - v46;
                if (count >= v46)
                {
                    int v49 = src & 0xFFFF;
                    int v51, v52;
                    if (v49 + v46 <= 0x10000)
                    {
                        v52 = v46;
                        v51 = v49; // &this->field_0[v49];
                    }
                    else
                    {
                        int v50 = 0x10000 - (src & 0xFFFF);
                        Buffer.BlockCopy (field_0, src & 0xFFFF, m_output, dst, v50);
//                        qmemcpy(output, &this->field_0[src & 0xFFFF], v50);
                        v51 = 0; // this->field_0;
                        v52 = v46 - v50;
                        v45 += v50; // &output[v50];
                    }
                    Buffer.BlockCopy (field_0, v51, m_output, v45, v52);
//                    qmemcpy(v45, v51, v52);
                    int v54 = count - v46;
                    if (v54 > 0)
                    {
                        Binary.CopyOverlapped (m_output, dst, dst + v46, v54);
                        /*
                        int v53 = 0;
                        int v55 = dst + v46; // &output[v46];
                        do
                        {
                            m_output[v55 + v53] = m_output[dst + v53];
                            ++v53;
                        }
                        while (v53 < v54);
                        */
                    }
                }
                else if ((src & 0xFFFF) + count <= 0x10000)
                {
                    Buffer.BlockCopy (field_0, src & 0xFFFF, m_output, dst, count);
//                    qmemcpy(output, &this->field_0[(unsigned __int16)src], count);
                }
                else
                {
                    for (int i = 0; i < count; ++i)
                    {
                        m_output[dst + i] = field_0[(src + i) & 0xFFFF];
                    }
                }
                /*
                int v56 = field_4 & 0xFFFF;
                if (v56 + count <= 0x10000)
                {
                    v60 = output;
                    v59 = count;
                    v58 = &this->field_0[v56];
                }
                else
                {
                    v57 = 0x10000 - v56;
                    qmemcpy(&this->field_0[v56], output, 0x10000 - v56);
                    v58 = this->field_0;
                    v59 = count - v57;
                    v60 = &output[v57];
                }
                qmemcpy(v58, v60, v59);
                */
                for (int i = 0; i < count; ++i)
                {
                    field_0[(field_4 + i) & 0xFFFF] = m_output[dst];
                }
                dst = dst_next;
                field_4 += count;
            }
            return false;
        }

        int GetBits (int count)
        {
            if (bitCount < count)
            {
                bits |= (uint)m_input.ReadUInt8() << (24 - bitCount);
                bitCount += 8;
            }
            uint b = bits >> (32 - count);
            bits <<= count;
            bitCount -= count;
            return (int)b;
        }

        int sub_416E80 (ushort[] a2)
        {
            int v2 = 0; // a2;
            int v3 = 0;
            ushort v4 = 0;
            do
            {
                a2[v2] = v4;
                a2[v2 + 1] = field_8[v4];
                v2 += 2;
                v3 += field_8[v4];
                field_8[v4] >>= 1;
                ++v4;
            }
            while (v4 < 0x112);
            sub_416DC0 (a2, -2, 0x112);
            return v3;
        }

        byte[] dword0 = new byte[4];
        byte[] dword1 = new byte[4];

        void sub_416DC0 (ushort[] w, int a1, int count)
        {
            int v2 = a1;
            int v3 = count;
            int v4 = 40;
            do
            {
                int v5 = v4 + 1;
                int v10 = v4 + 1;
                if (v5 <= v3)
                {
                    do
                    {
                        int v6 = 4 * v5;
                        int v9 = v5;
//                        v11 = *(_DWORD *)&v2[v6];
                        Buffer.BlockCopy (w, v2 + v6, dword0, 0, 4);
                        if (v5 > v4)
                        {
                            int v7 = 4 * v4;
                            int v8;
                            do
                            {
//                                v8 = *(_WORD *)&v2[v6 - v7 + 2] - SHIWORD(v11);
                                Buffer.BlockCopy (w, v2 + v6 - v7, dword1, 0, 4);
                                v8 = LittleEndian.ToInt16 (dword1, 2) - LittleEndian.ToInt16 (dword0, 2);
                                if (v8 == 0)
                                {
//                                    v8 = *(_WORD *)&v2[v6 - v7] - (signed __int16)v11;
                                    v8 = LittleEndian.ToInt16 (dword1, 0) - LittleEndian.ToInt16 (dword0, 0);
                                }
                                if (v8 >= 0)
                                    break;
//                                *(_DWORD *)&v2[v6] = *(_DWORD *)&v2[v6 - v7];
                                Buffer.BlockCopy (w, v2 + v6 - v7, w, v2 + v6, 4);
                                v6 -= v7;
                                v9 -= v4;
                            }
                            while (v9 > v4);
                            v5 = v10;
                        }
                        ++v5;
//                        *(_DWORD *)&v2[4 * v9] = v11;
                        Buffer.BlockCopy (dword0, 0, w, v2 + v9 * 4, 4);
                        v3 = count;
                        v10 = v5;
                    }
                    while (v5 <= count);
                }
                v4 /= 3;
            }
            while (v4 > 0);
        }

        static readonly int[] dword_425810 = new int[] {
            0, 0, 0, 1, 1, 2, 2, 4, 3, 8, 4, 0x10, 5, 0x20, 6, 0x40
        };
        static readonly int[] dword_4257CC = new int[] {
            1, 8, 2, 0x0A, 3, 0x0E, 4, 0x16, 5, 0x26, 6, 0x46, 7, 0x86, 8, 0x106,
        };
    }
}
