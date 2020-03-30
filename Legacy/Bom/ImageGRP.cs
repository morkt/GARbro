//! \file       ImageGRP.cs
//! \date       2018 Mar 30
//! \brief      BOM image format.
//
// Copyright (C) 2018 by morkt
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Bom
{
    internal class GrpMetaData : ImageMetaData
    {
        public int  Stride;
        public int  Type;
        public uint DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class GrpFormat : ImageFormat
    {
        public override string         Tag { get { return "GRP/RG"; } }
        public override string Description { get { return "BOM image format"; } }
        public override uint     Signature { get { return 0x01004752; } } // 'RG'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x24);
            if ((header[6] & 0x80) == 0)
                return null;
            int bpp;
            switch (header[4])
            {
            case 5: bpp = 4; break;
            case 4: bpp = 8; break;
            case 3: bpp = 16; break;
            case 2: bpp = 24; break;
            case 1: bpp = 32; break;
            default: return null;
            }
            return new GrpMetaData {
                Width   = header.ToUInt16 (8),
                Height  = header.ToUInt16 (10),
                BPP     = bpp,
                Stride  = header.ToInt32 (0x18),
                Type    = header[4],
                DataOffset = header.ToUInt16 (0x22),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new GrpReader (file, (GrpMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GrpFormat.Write not implemented");
        }
    }

    internal class GrpReader
    {
        IBinaryStream   m_input;
        GrpMetaData     m_info;
        byte[]          m_output;

        public GrpReader (IBinaryStream file, GrpMetaData info)
        {
            m_input = file;
            m_info = info;
            m_output = new byte[m_info.Stride * (int)m_info.Height];
        }

        int     m_dst;

        public byte[] Unpack ()
        {
            m_input.Position = m_info.DataOffset;
            uint size = m_input.ReadUInt32();
            if ((size & 0x80000000) != 0)
            {
                m_input.Read (m_output, 0, m_output.Length);
                return m_output;
            }
            Init();
            var frame = new byte[0x1000];
            for (int i = 0; i < 0xFC0; ++i)
                frame[i] = 0x20;
            int frame_pos = 0xFC0;
            m_dst = 0;
            while (m_dst < m_output.Length)
            {
                int ctl = sub_408BE0();
                if (ctl >= 0x100)
                {
                    v5 = 0;
                    int count = ctl - 0xFD;
                    int offset = (frame_pos - sub_408E50() - 1) & 0xFFF;
                    for (int i = 0; i < count; ++i)
                    {
                        byte v = frame[(offset + i) & 0xFFF];
                        PutByte (v);
                        frame[frame_pos++ & 0xFFF] = v;
                    }
                }
                else
                {
                    PutByte (m_dst, ctl);
                    frame[frame_pos++ & 0xFFF] = (byte)ctl;
                }
            }
            return m_output;
        }

        int     dword_6FFE54;
        int     dword_6FF460;
        int     dword_709868;
        int     dword_703E64;
        int     m_cached_bits;

        void Init ()
        {
            dword_6FFE54 = -1;
            dword_6FF460 = 0;
            dword_709868 = 0;
            dword_703E64 = 0;

            int v0 = 0;
            for (int i = 0; i < 318; ++i)
            {
                dword_6FF464[i] = 1;
                dword_70A258[i] = i;
                dword_703E68[i] = i + 635;
            }
            int v1 = 0;
            int v2 = 318;
            for (int i = 0; i <= 316; ++i)
            {
                v4 = dword_6FF464[v1] + dword_6FF468[v1];
                dword_70986C[v1 + 1] = v2;
                dword_6FF95C[i] = v4;
                dword_704360[i] = v1;
                dword_70986C[v1] = v2;
                v1 += 2;
                ++v2;
            }
            dword_70A254 = 0;
            m_cached_bits = 0;
            byte_7137B4 = 0;
            m_bits = 0;
            dword_7137B0 = 0;
            dword_6FFE50 = 0xFFFF;
        }

        int[] dword_704360 = new int[317];
        int[] dword_6FF95C = new int[317];
        int[] dword_70986C = new int[634];

        int sub_408BE0 ()
        {
            for (int i = dword_704360[316]; i < 635; i = dword_703E68[GetNextBit() + i])
                ;
            int ctl = i - 635;
            sub_408C80 (ctl);
            return ctl;
        }

        int     m_bits;

        int GetNextBit ()
        {
            if (m_cached_bits <= 8)
            {
                byte v = m_input.ReadUInt8();
                m_bits |= (v << (8 - m_cached_bits));
                m_cached_bits += 8;
            }
            m_bits <<= 1;
            m_cached_bits--;
            return (m_bits >> 16) & 1;
        }

        void sub_408C80 (int a1)
        {
            unsigned int v2; // ecx@4
            unsigned int v3; // esi@4
            int *v4; // ecx@5
            unsigned int v5; // eax@6
            int v6; // eax@7
            int v7; // ecx@7
            int v8; // eax@7
            int v9; // esi@9

            if (dword_6FF95C[316] == 0x8000)
                sub_408D50();
            int v1 = dword_70A258[a1];
            do
            {
                v2 = dword_6FF464[v1] + 1;
                dword_6FF464[v1] = v2;
                v3 = v2;
                if ( v2 > dword_6FF468[v1] )
                {
                    v4 = &dword_6FF46C[v1 + 1];
                    if ( v3 > dword_6FF46C[v1] )
                    {
                        do
                        {
                            v5 = *v4;
                            ++v4;
                        }
                        while ( v3 > v5 );
                    }
                    dword_6FF464[v1] = *(v4 - 2);
                    *(v4 - 2) = v3;
                    v6 = v4 - dword_6FF464;
                    v7 = dword_703E68[v1];
                    v8 = v6 - 2;
                    dword_70986C[v7] = v8;
                    if ( v7 < 635 )
                        dword_709870[v7] = v8;
                    v9 = dword_703E68[v8];
                    dword_703E68[v8] = v7;
                    dword_70986C[v9] = v1;
                    if ( v9 < 635 )
                        dword_709870[v9] = v1;
                    dword_703E68[v1] = v9;
                    v1 = v8;
                }
                v1 = dword_70986C[v1];
            }
            while (v1 != 0);
        }

        void sub_408D50 ()
        {
            int v3; // ecx@2
            int v4; // edi@3
            int v5; // ecx@5
            int *v6; // ebx@5
            unsigned int v7; // eax@6
            int *v8; // edx@6
            int v9; // ecx@6
            int *v10; // edi@6
            unsigned int v11; // ebp@7
            int v12; // ecx@8
            int *i; // edi@8
            int *v14; // eax@10
            int *v15; // ecx@10
            int v16; // edx@13
            int *v17; // ecx@13
            int v18; // eax@14
            int v19; // [sp+10h] [bp-8h]@5
            signed int v20; // [sp+14h] [bp-4h]@5

            int v1 = 0;
            int v2 = 0;
            for (int i = 0; i < 635; ++i)
            {
                int v3 = dword_703E68[i];
                if (v3 >= 635)
                {
                    v4 = dword_6FF464[i];
                    dword_703E68[v2] = v3;
                    dword_6FF464[v2] = (int)((uint)(v4 + 1) >> 1);
                    ++v2;
                }
            }
            int v5 = 318;
            v19 = 0;
            v20 = 318;
            v6 = dword_6FF464;
            do
            {
                v7 = *v6 + v6[1];
                v8 = &dword_6FF95C[v1];
                v9 = v5 - 1;
                v10 = &dword_6FF95C[v1 - 1];
                dword_6FF95C[v1] = v7;
                if ( v7 < *v10 )
                {
                    do
                    {
                        v11 = *(v10 - 1);
                        --v10;
                        --v9;
                    }
                    while ( v7 < v11 );
                }
                v12 = v9 + 1;
                for ( i = &dword_6FF464[v12]; v8 > i; --v8 )
                    *v8 = *(v8 - 1);
                *i = v7;
                v14 = &dword_704360[v1];
                v15 = &dword_703E68[v12];
                if ( &dword_704360[v1] > v15 )
                {
                    do
                    {
                        *v14 = *(v14 - 1);
                        --v14;
                    }
                    while ( v14 > v15 );
                }
                v6 += 2;
                *v15 = v19;
                ++v1;
                v5 = v20 + 1;
                v19 += 2;
                ++v20;
            }
            while ( v1 < 317 );
            v16 = 0;
            v17 = dword_703E68;
            do
            {
                v18 = *v17;
                if ( *v17 < 635 )
                    dword_70986C[v18 + 1] = v16;
                dword_70986C[v18] = v16;
                ++v17;
                ++v16;
            }
            while ( (signed int)v17 < (signed int)&unk_704854 );
        }

        void PutByte (int a1)
        {
            if (dword_6FF460 != 0)
            {
                int v1 = (a1 & 0x7F) << (7 * dword_6FF460 - 7);
                dword_709868 += v1;
                if ((a1 & 0x80) != 0)
                {
                    ++dword_6FF460;
                }
                else
                {
                    int count = dword_709868;
                    dword_6FF460 = 0;
                    while (count --> 0 && m_dst < m_output.Length)
                    {
                        m_output[m_dst++] = (byte)dword_6FFE54;
                    }
                    dword_6FFE54 = -1;
                }
            }
            else if (dword_6FFE54 >= 0)
            {
                if (dword_6FFE54 == a1)
                {
                    dword_6FF460 = 1;
                    dword_709868 = 0;
                }
                else
                {
                    m_output[m_dst++] = (byte)dword_6FFE54;
                    dword_6FFE54 = a1;
                }
            }
            else
            {
                dword_6FF460 = 0;
                dword_6FFE54 = a1;
            }
        }
    }
}
