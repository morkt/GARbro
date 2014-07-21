//! \file       ImageHG3.cs
//! \date       Sat Jul 19 17:31:09 2014
//! \brief      Frontwing HG3 image format implementation.
//

using System;
using System.IO;
using System.ComponentModel.Composition;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using ZLibNet;
using GameRes.Utility;

namespace GameRes.Formats
{
    [Export(typeof(ImageFormat))]
    public class Hg3Format : ImageFormat
    {
        public override string Tag { get { return "HG3"; } }
        public override string Description { get { return "Frontwing proprietary image format"; } }
        public override uint Signature { get { return 0x332d4748; } }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x4c];
            if (0x4c != stream.Read (header, 0, header.Length))
                return null;
            if (LittleEndian.ToUInt32 (header, 0) != Signature)
                return null;
            if (LittleEndian.ToUInt32 (header, 4) != 0x0c)
                return null;
            if (!Binary.AsciiEqual (header, 0x14, "stdinfo\0"))
                return null;
            if (0x38 != LittleEndian.ToUInt32 (header, 0x1c))
                return null;
            if (0x20 != LittleEndian.ToUInt32 (header, 0x2c))
                return null;
            uint width  = LittleEndian.ToUInt32 (header, 0x24); // @@L0
            uint height = LittleEndian.ToUInt32 (header, 0x28);
            int pos_x   = LittleEndian.ToInt32 (header, 0x30);
            int pos_y   = LittleEndian.ToInt32 (header, 0x34);
            pos_x      -= LittleEndian.ToInt32 (header, 0x44);
            pos_y      -= LittleEndian.ToInt32 (header, 0x48);

            return new ImageMetaData
            {
                Width = width,
                Height = height,
                OffsetX = pos_x,
                OffsetY = pos_y,
                BPP = 32,
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            stream.Position = 0x40;
            bool flipped = 0 == (stream.ReadByte() & 1) || info.OffsetY < 0;
            stream.Position = 0x4c;
            var header = new byte[0x28];
            if (0x28 != stream.Read (header, 0, header.Length))
                return null;
            if (!Binary.AsciiEqual (header, "img0000\0"))
                return null;
            uint data_size = LittleEndian.ToUInt32 (header, 0x0c);
            if (data_size < 0x18)
                return null;
            if (info.Height != LittleEndian.ToUInt32 (header, 0x14))
                return null;
            uint packed2_size   = LittleEndian.ToUInt32 (header, 0x18);
            uint unpacked2_size = LittleEndian.ToUInt32 (header, 0x1c);
            uint packed1_size   = LittleEndian.ToUInt32 (header, 0x20);
            uint unpacked1_size = LittleEndian.ToUInt32 (header, 0x24);
            if (packed2_size + packed1_size < packed2_size)
                return null;
            if (unpacked2_size + unpacked1_size < unpacked2_size)
                return null;

            long data_pos = stream.Position;
            using (var unpacked2 = ZLibCompressor.DeCompress (stream))
            {
                stream.Position = data_pos + packed2_size;
                using (var unpacked1 = ZLibCompressor.DeCompress (stream))
                {
                    var decoder = new Decoder (unpacked1.GetBuffer(), unpacked1_size,
                                               unpacked2.GetBuffer(), unpacked2_size,
                                               info.Width, info.Height);
                    decoder.Unpack();
                    var pixels = decoder.Data;
                    int stride = (int)info.Width * 4;
                    var bitmap = BitmapSource.Create ((int)info.Width, (int)info.Height, 96, 96,
                        PixelFormats.Bgra32, null, pixels, stride);
                    if (flipped)
                    {
                        var flipped_bitmap = new TransformedBitmap();
                        flipped_bitmap.BeginInit();
                        flipped_bitmap.Source = bitmap;
                        flipped_bitmap.Transform = new ScaleTransform { ScaleY = -1 };
                        flipped_bitmap.EndInit();
                        bitmap = flipped_bitmap;
                    }
                    bitmap.Freeze();
                    return new ImageData (bitmap, info);
                }
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("Hg3Format.Write not implemented");
        }

        class Decoder
        {
            byte[] m_in1;
            byte[] m_in2;
            byte[] m_image;
            uint m_in1_size;
            uint m_in2_size;
            uint m_dst_size;
            uint m_width;
            uint m_height;

            public byte[] Data { get { return m_image; } }

            public Decoder (byte[] in1, uint in1_size, byte[] in2, uint in2_size,
                            uint width, uint height)
            {
                m_in1 = in1;
                m_in1_size = in1_size;
                m_in2 = in2;
                m_in2_size = in2_size;
                m_width = width;
                m_height = height;
                m_dst_size = width*height*4;
                m_image = new byte[(int)m_dst_size];
            }

            uint esi;
            uint edi;
            uint eax;
            uint ebx;
            uint ecx;
            uint edx;

            uint L0, L1, L2, L3, L4;
            uint m_plane;

            public void Unpack ()
            {
                m_plane = 0;
                edi = 0;
                esi = 0;
                ebx = 0;
                eax = m_in1_size;
                L0 = m_in2_size;
                L1 = 0;
                bool skip_first = GetNextBit();
                Proc4();
                ecx = m_dst_size;
                if (eax > m_dst_size)
                    throw new InvalidFormatException ("Underflow at Hg3Format.Decoder.Unpack()");
                m_dst_size -= eax;
                ecx >>= 2;
                L2 = eax;
                L3 = ecx;
                L4 = ecx;
                for (;;)
                {
                    if (!skip_first)
                    {
    // @@1:
                        if (0 == L2)
                            break;
                        Proc4();
                        ecx = eax;
                        if (ecx > L2)
                            throw new InvalidFormatException ("Overflow at Hg3Format.Decoder.Unpack()");
                        L2 -= ecx;
                        eax = 0;
                        do
                            Proc2();
                        while (0 != --ecx);
                    }
    // @@1a:
                    if (0 == L2)
                        break;
                    Proc4();
                    ecx = eax;
                    if (ecx > L2 || ecx > L0)
                        throw new InvalidFormatException ("Overflow (2) at Hg3Format.Decoder.Unpack()");
                    L2 -= ecx;
                    L0 -= ecx;
                    do
                    {
                        eax = m_in2[L1++];
                        Proc2();
                    }
                    while (0 != --ecx);
                    skip_first = false;
                }
    // @@7:
                ecx = m_dst_size;
                esi = 0;
                if (0 != ecx)
                {
                    eax = 0;
                    do
                        Proc2();
                    while (0 != --ecx);
                }
                Proc6 (m_width, m_height);
    // @@9:
                eax = 0;
                edx = m_in1_size;
            }

            void Proc2 () // @@2
            {
                m_image[edi] = (byte)eax;
                edi += 4;
                if (0 == --L3)
                {
                    edi = ++m_plane;
                    L3 = L4;
                }
            }

            bool GetNextBit () // @@3
            {
                bool carry = 0 != (ebx & 1);
                ebx >>= 1;
                if (0 == ebx)
                {
                    if (0 == m_in1_size--)
                        throw new InvalidFormatException ("Hg3Format.Decoder.Underflow at GetNextBit()");
                    ebx = (uint)(m_in1[esi++] | 0x100);
                    carry = 0 != (ebx & 1);
                    ebx >>= 1;
                }
                return carry;
            }

            void Proc4 () // @@4
            {
                ecx = 0;
                eax = 0;
                do
                {
                    if (ecx >= 0x20)
                        throw new InvalidFormatException ("Hg3Format.Decoder.Overflow at Proc4");
                    ++ecx;
                }
                while (!GetNextBit());
                ++eax;
                while (0 != --ecx)
                {
                    eax += eax + (uint)(GetNextBit() ? 1 : 0);
                }
            }

            void Proc6 (uint width, uint height)
            {
                uint[] table = new uint[0x100];
                ecx = 0;
                for (uint i = 0; i < 0x100; ++i)
                {
                    eax = 0xffffffff;
                    edx = i;
                    do
                    {
                        eax >>= 2;
                        eax |= (edx & 3) << 30;
                        eax >>= 6;
                        edx >>= 2;
                    }
                    while (0 != (0x80 & eax));
                    table[i] = eax;
                }
                ecx = width * height * 4;
                for (uint i = 0; i < ecx; i += 4)
                {
                    eax = m_image[i];
                    edx = table[eax];
                    edx <<= 2;
                    eax = m_image[i+1];
                    edx += table[eax];
                    edx <<= 2;
                    eax = m_image[i+2];
                    edx += table[eax];
                    edx <<= 2;
                    eax = m_image[i+3];
                    edx += table[eax];
                    m_image[i]   = (byte)(edx);
                    m_image[i+1] = (byte)(edx >> 8);
                    m_image[i+2] = (byte)(edx >> 16);
                    m_image[i+3] = (byte)(edx >> 24);
                }
                edi = 0;
                for (int i = 0; i < 4; ++i)
                {
                    eax = m_image[edi];
                    edx = (uint)(0 == (eax & 1) ? 0 : 0xff);
                    eax >>= 1;
                    eax ^= edx;
                    m_image[edi++] = (byte)eax;
                }
                ecx = width;
                if (0 != --ecx)
                {
                    ecx <<= 2;
                    do
                    {
                        eax = m_image[edi];
                        edx = (uint)(0 == (eax & 1) ? 0 : 0xff);
                        eax >>= 1;
                        eax ^= edx;
                        eax += m_image[edi-4];
                        m_image[edi++] = (byte)eax;
                    }
                    while (0 != --ecx);
                }
                ecx = height;
                if (0 != --ecx)
                {
                    uint stride = width*4;
                    ecx *= stride;
                    do
                    {
                        eax = m_image[edi];
                        edx = (uint)(0 == (eax & 1) ? 0 : 0xff);
                        eax >>= 1;
                        eax ^= edx;
                        eax += m_image[edi-stride];
                        m_image[edi++] = (byte)eax;
                    }
                    while (0 != --ecx);
                }
            }
        }
    }
}
