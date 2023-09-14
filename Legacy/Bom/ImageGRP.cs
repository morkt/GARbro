//! \file       ImageGRP.cs
//! \date       2018 Mar 30
//! \brief      BOM image format.
//
// Copyright (C) 2018-2023 by morkt
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

// [020705][BOM] Ikihaji@
// [021025][Crystal Vision] Confession ~Hajimete no Kokuhaku~

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
            return reader.Unpack();
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

        public PixelFormat Format { get; private set; }

        public GrpReader (IBinaryStream file, GrpMetaData info)
        {
            m_input = file;
            m_info = info;
            m_output = new byte[m_info.Stride * (int)m_info.Height];
            Format = info.BPP == 16 ? PixelFormats.Bgr555
                   : info.BPP == 24 ? PixelFormats.Bgr24
                   : info.BPP == 32 ? PixelFormats.Bgra32
                                    : PixelFormats.Gray8;
        }

        int     m_dst;

        public ImageData Unpack ()
        {
            m_input.Position = m_info.DataOffset;
            uint size = m_input.ReadUInt32();
            if ((size & 0x80000000) != 0)
            {
                m_input.Read (m_output, 0, m_output.Length);
            }
            else
            {
                UnpackLz();
            }
            return ImageData.Create (m_info, Format, null, m_output, m_info.Stride);
        }

        void UnpackLz ()
        {
            Init();
            var frame = new byte[0x1000];
            for (int i = 0; i < 0xFC0; ++i)
                frame[i] = 0x20;
            int frame_pos = 0xFC0;
            m_dst = 0;
            while (m_dst < m_output.Length)
            {
                int ctl = GetControlWord();
                if (ctl >= 0x100)
                {
                    int count = ctl - 0xFD;
                    int offset = (frame_pos - GetOffset() - 1) & 0xFFF;
                    for (int i = 0; i < count; ++i)
                    {
                        byte v = frame[(offset + i) & 0xFFF];
                        PutByte (v);
                        frame[frame_pos++ & 0xFFF] = v;
                    }
                }
                else
                {
                    PutByte (ctl);
                    frame[frame_pos++ & 0xFFF] = (byte)ctl;
                }
            }
        }

        int     dword_6FF460;
        int     dword_6FFE54;
        int     dword_709868;

        int[]   dword_6FF464 = new int[636];
        int[]   dword_703E68 = new int[635];
        int[]   dword_70986C = new int[953];

        void Init ()
        {
            dword_6FFE54 = -1;
            dword_6FF460 = 0;
            dword_709868 = 0;

            for (int i = 0; i < 318; ++i)
            {
                dword_6FF464[i] = 1;
                dword_70986C[i + 635] = i;
                dword_703E68[i] = i + 635;
            }
            int v1 = 0;
            int v2 = 318;
            for (int i = 0; i <= 316; ++i)
            {
                int v4 = dword_6FF464[v1] + dword_6FF464[v1 + 1];
                dword_70986C[v1] = v2;
                dword_70986C[v1 + 1] = v2;
                dword_6FF464[i + 318] = v4;
                dword_703E68[i + 318] = v1;
                v1 += 2;
                ++v2;
            }
            dword_70986C[634] = 0;
            m_cached_bits = 0;
            m_bits = 0;
            dword_6FF464[635] = 0xFFFF;
        }

        int GetControlWord ()
        {
            int ctl;
            for (ctl = dword_703E68[634]; ctl < 635; ctl = dword_703E68[GetNextBit() + ctl])
                ;
            ctl -= 635;
            sub_408C80 (ctl);
            return ctl;
        }

        int GetOffset ()
        {
            int v0 = GetByte();
            int v1 = byte_438D10[v0] - 2;
            int v2 = byte_438C10[v0] << 6;
            int bits = GetBits (v1);
            return v2 | (((v0 << v1) & 0xFF) | (bits & 0xFF)) & 0x3F;
        }

        static readonly byte[] byte_438C10 = new byte[] {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4, 5, 5, 5, 5, 5, 5, 5, 5,
            6, 6, 6, 6, 6, 6, 6, 6, 7, 7, 7, 7, 7, 7, 7, 7, 8, 8, 8, 8, 8, 8, 8, 8, 9, 9, 9, 9, 9, 9, 9, 9,
            0x0A, 0x0A, 0x0A, 0x0A, 0x0A, 0x0A, 0x0A, 0x0A, 0x0B, 0x0B, 0x0B, 0x0B, 0x0B, 0x0B, 0x0B, 0x0B,
            0x0C, 0x0C, 0x0C, 0x0C, 0x0D, 0x0D, 0x0D, 0x0D, 0x0E, 0x0E, 0x0E, 0x0E, 0x0F, 0x0F, 0x0F, 0x0F,
            0x10, 0x10, 0x10, 0x10, 0x11, 0x11, 0x11, 0x11, 0x12, 0x12, 0x12, 0x12, 0x13, 0x13, 0x13, 0x13,
            0x14, 0x14, 0x14, 0x14, 0x15, 0x15, 0x15, 0x15, 0x16, 0x16, 0x16, 0x16, 0x17, 0x17, 0x17, 0x17,
            0x18, 0x18, 0x19, 0x19, 0x1A, 0x1A, 0x1B, 0x1B, 0x1C, 0x1C, 0x1D, 0x1D, 0x1E, 0x1E, 0x1F, 0x1F,
            0x20, 0x20, 0x21, 0x21, 0x22, 0x22, 0x23, 0x23, 0x24, 0x24, 0x25, 0x25, 0x26, 0x26, 0x27, 0x27,
            0x28, 0x28, 0x29, 0x29, 0x2A, 0x2A, 0x2B, 0x2B, 0x2C, 0x2C, 0x2D, 0x2D, 0x2E, 0x2E, 0x2F, 0x2F,
            0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F,
        };
        static readonly byte[] byte_438D10 = new byte[] {
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
            4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5,
            5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5,
            5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6,
            6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6,
            7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
            7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        };

        int     m_bits;
        int     m_cached_bits;

        int GetNextBit ()
        {
            FillBitCache();
            m_bits <<= 1;
            m_cached_bits--;
            return (m_bits >> 16) & 1;
        }

        int GetByte()
        {
            FillBitCache();
            int result = m_bits >> 8;
            m_bits <<= 8;
            m_cached_bits -= 8;
            return result & 0xFF;
        }

        int GetBits (int n)
        {
            FillBitCache();
            int bits = m_bits << n;
            m_cached_bits -= n;
            int result = m_bits >> BitShiftTable[n];
            m_bits = bits;
            return result & BitMaskTable[n];
        }

        void FillBitCache ()
        {
            if (m_cached_bits <= 8)
            {
                int v = m_input.ReadByte();
                if (-1 == v)
                    v = 0;
                m_bits |= v << (8 - m_cached_bits);
                m_cached_bits += 8;
            }
        }

        static readonly ushort[] BitMaskTable = new ushort[] {
            0, 1, 3, 7, 0x0F, 0x1F, 0x3F, 0x7F, 0x0FF, 0x1FF, 0x3FF, 0x7FF, 0x0FFF, 0x1FFF, 0x3FFF, 0x0FFF, 0xFFFF
        };
        static readonly byte[] BitShiftTable = new byte[] {
            0x10, 0x0F, 0x0E, 0x0D, 0x0C, 0x0B, 0x0A, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0
        };

        void sub_408C80 (int a1)
        {
            if (dword_6FF464[634] == 0x8000)
                sub_408D50();
            int v1 = dword_70986C[a1 + 635];
            do
            {
                int v2 = ++dword_6FF464[v1];
                int v3 = v2;
                if (v2 > dword_6FF464[v1 + 1])
                {
                    int v4 = v1 + 2; // &dword_6FF464[v1 + 2];
                    while (v3 > dword_6FF464[v4++])
                        ;
                    dword_6FF464[v1] = dword_6FF464[v4 - 2];
                    dword_6FF464[v4 - 2] = v3;
                    int v6 = v4; // v4 - dword_6FF464;
                    int v7 = dword_703E68[v1];
                    int v8 = v6 - 2;
                    dword_70986C[v7] = v8;
                    if (v7 < 635)
                        dword_70986C[v7 + 1] = v8;
                    int v9 = dword_703E68[v8];
                    dword_703E68[v8] = v7;
                    dword_70986C[v9] = v1;
                    if (v9 < 635)
                        dword_70986C[v9 + 1] = v1;
                    dword_703E68[v1] = v9;
                    v1 = v8;
                }
                v1 = dword_70986C[v1];
            }
            while (v1 != 0);
        }

        void sub_408D50 ()
        {
            int v2 = 0;
            for (int i = 0; i < 635; ++i)
            {
                int v3 = dword_703E68[i];
                if (v3 >= 635)
                {
                    int v4 = dword_6FF464[i];
                    dword_703E68[v2] = v3;
                    dword_6FF464[v2] = (v4 + 1) >> 1;
                    ++v2;
                }
            }
            int v5 = 318;
            int v19 = 0;
            int v6 = 0; // dword_6FF464;
            int v1 = 0;
            while (v1 < 317)
            {
                int v7 = dword_6FF464[v6] + dword_6FF464[v6 + 1];
                int v9 = v5;
                int v10 = v1 + 317; // &dword_6FF464[v1 + 317];
                dword_6FF464[v1 + 318] = v7;
                while (v7 < dword_6FF464[v10])
                {
                    --v10;
                    --v9;
                }
                int p = v9; // &dword_6FF464[v9]
                for (int v8 = v1 + 318; v8 > p; --v8)
                    dword_6FF464[v8] = dword_6FF464[v8 - 1];
                dword_6FF464[p] = v7;

                int v14 = v1 + 318; // &dword_703E68[v1 + 318];
                int v15 = v9; // &dword_703E68[v9];
                while (v14 > v15)
                {
                    dword_703E68[v14] = dword_703E68[v14 - 1];
                    --v14;
                }
                v6 += 2;
                dword_703E68[v15] = v19;
                v19 += 2;
                ++v5;
                ++v1;
            }
            int v16 = 0;
            for (int i = 0; i < 635; ++i)
            {
                int v18 = dword_703E68[i];
                if (v18 < 635)
                    dword_70986C[v18 + 1] = v16;
                dword_70986C[v18] = v16;
                ++v16;
            }
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
                    if (m_dst < m_output.Length)
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
