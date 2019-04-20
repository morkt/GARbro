//! \file       ImageGRP.cs
//! \date       Wed Jun 24 22:14:41 2015
//! \brief      Cherry Soft compressed image format.
//
// Copyright (C) 2015-2019 by morkt
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
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;

namespace GameRes.Formats.Cherry
{
    internal class GrpMetaData : ImageMetaData
    {
        public int  PackedSize;
        public int  UnpackedSize;
        public int  Offset;
        public int  HeaderSize;
        public bool AlphaChannel;
        public bool IsEncrypted;
    }

    [Export(typeof(ImageFormat))]
    public class GrpFormat : ImageFormat
    {
        public override string         Tag { get { return "GRP/CHERRY"; } }
        public override string Description { get { return "Cherry Soft compressed image format"; } }
        public override uint     Signature { get { return 0; } }

        public GrpFormat ()
        {
            Extensions = new string[] { "grp" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x18);
            return UnpackHeader (header);
        }

        internal GrpMetaData UnpackHeader (CowArray<byte> header)
        {
            uint width  = header.ToUInt32 (0);
            uint height = header.ToUInt32 (4);
            int bpp = header.ToInt32 (8);
            int packed_size = header.ToInt32 (0x0C);
            int unpacked_size = header.ToInt32 (0x10);
            if (0 == width || 0 == height || width > 0x7fff || height > 0x7fff
                || (bpp != 24 && bpp != 8)
                || unpacked_size <= 0 || packed_size < 0)
                return null;
            return new GrpMetaData
            {
                Width = width,
                Height = height,
                BPP = bpp,
                PackedSize = packed_size,
                UnpackedSize = unpacked_size,
                Offset = header.ToInt32 (0x14),
                HeaderSize = 0x18,
                AlphaChannel = false,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new GrpReader (file.AsStream, (GrpMetaData)info);
            return reader.CreateImage();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("GrpFormat.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class Grp3Format : GrpFormat
    {
        public override string         Tag { get { return "GRP/CHERRY3"; } }
        public override string Description { get { return "Cherry Soft compressed image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x28);
            if (0xFFFF != header.ToInt32 (8))
                return null;
            int packed_size = header.ToInt32 (0);
            int unpacked_size = header.ToInt32 (4);
            uint width  = header.ToUInt32 (0x10);
            uint height = header.ToUInt32 (0x14);
            int bpp = header.ToInt32 (0x18);
            if (0 == width || 0 == height || width > 0x7fff || height > 0x7fff
                || (bpp != 32 && bpp != 24 && bpp != 8)
                || unpacked_size <= 0 || packed_size < 0)
                return null;
            return new GrpMetaData
            {
                Width = width,
                Height = height,
                BPP = bpp,
                PackedSize = packed_size,
                UnpackedSize = unpacked_size,
                Offset = 0xFFFF,
                HeaderSize = 0x28,
                AlphaChannel = header.ToInt32 (0x24) != 0,
            };
        }
    }

    [Export(typeof(ImageFormat))]
    public class GrpEncFormat : GrpFormat
    {
        public override string         Tag { get { return "GRP/ENC"; } }
        public override string Description { get { return "Cherry Soft encrypted image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            if (header[3] != 0xA5 || header[7] != 0x35)
                return null;
            header[0] ^= 0x5A; // 0xA53CC35A
            header[1] ^= 0xC3;
            header[2] ^= 0x3C;
            header[3] ^= 0xA5;
            header[4] ^= 0x05; // 0x35421005
            header[5] ^= 0x10;
            header[6] ^= 0x42;
            header[7] ^= 0x35;
            header[0x10] ^= 0x5D; // 0xCF42355D
            header[0x11] ^= 0x35;
            header[0x12] ^= 0x42;
            header[0x13] ^= 0xCF;
            var info = UnpackHeader (header);
            if (info != null)
                info.IsEncrypted = true;
            return info;
        }
    }

    internal class GrpReader
    {
        GrpMetaData     m_info;
        Stream          m_input;
        byte[]          m_image_data;
        int             m_stride;

        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }
        public byte[]         Pixels { get { return m_image_data; } }

        public GrpReader (Stream input, GrpMetaData info)
        {
            m_info = info;
            m_input = input;
            if (8 == info.BPP)
                Format = PixelFormats.Indexed8;
            else if (24 == info.BPP)
                Format = PixelFormats.Bgr24;
            else if (32 == info.BPP)
                Format = m_info.AlphaChannel ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
            else
                throw new NotSupportedException ("Not supported GRP image depth");
            m_stride = m_info.iWidth * ((Format.BitsPerPixel + 7) / 8);
        }

        public ImageData CreateImage ()
        {
            m_input.Position = m_info.HeaderSize;
            int data_size = m_info.UnpackedSize;
            if (m_info.PackedSize != 0)
                data_size = m_info.PackedSize;

            if (0x0f0f0f0f == m_info.Offset && 0x18 + data_size == m_input.Length)
                return ReadV2();
            else if (8  == m_info.BPP && 0x418 == m_info.Offset ||
                     24 == m_info.BPP && 0x018 == m_info.Offset)
                return ReadV1();
            else if (m_info.IsEncrypted)
                return ReadEncrypted();
            else if (true) // FIXME
                return ReadV3 (m_input, 0xFFFF != m_info.Offset);
            else
                throw new InvalidFormatException();
        }

        private ImageData ReadV1 ()
        {
            if (8 == m_info.BPP)
                Palette = ImageFormat.ReadPalette (m_input);

            var packed = new byte[m_info.PackedSize];
            if (packed.Length != m_input.Read (packed, 0, packed.Length))
                throw new InvalidFormatException ("Unexpected end of file");
            for (int i = 0; i < packed.Length; ++i)
                packed[i] ^= (byte)i;

            using (var input = new MemoryStream (packed))
            using (var lzs = new LzssStream (input))
            {
                m_image_data = new byte[m_info.UnpackedSize];
                // flip pixels vertically
                for (int dst = m_stride * (m_info.iHeight-1); dst >= 0; dst -= m_stride)
                {
                    lzs.Read (m_image_data, dst, m_stride);
                }
            }
            return ImageData.Create (m_info, Format, Palette, m_image_data, m_stride);
        }

        // DOUBLE
        private ImageData ReadV2 ()
        {
            if (0 != m_info.PackedSize)
                return ReadV3 (m_input, true);
            return ImageFromStream (m_input, true);
        }

        // Exile ~Blood Royal 2~      : flipped == true
        // Gakuen ~Nerawareta Chitai~ : flipped == false
        private ImageData ReadV3 (Stream input, bool flipped)
        {
            using (var lzs = new LzssStream (input, LzssMode.Decompress, true))
                return ImageFromStream (lzs, flipped);
        }

        private ImageData ImageFromStream (Stream input, bool flipped)
        {
            if (8 == m_info.BPP)
                Palette = ImageFormat.ReadPalette (input);

            m_image_data = new byte[m_stride * m_info.iHeight];
            if (m_image_data.Length != input.Read (m_image_data, 0, m_image_data.Length))
                throw new InvalidFormatException();

            if (flipped)
                return ImageData.CreateFlipped (m_info, Format, Palette, m_image_data, m_stride);
            else
                return ImageData.Create (m_info, Format, Palette, m_image_data, m_stride);
        }

        private ImageData ReadEncrypted ()
        {
            var data = new byte[m_input.Length - m_info.HeaderSize];
            m_input.Read (data, 0, data.Length);
            Pak2Opener.Decrypt (data, 0, data.Length);
            using (var input = new MemoryStream (data))
                return ReadV3 (input, true);
        }
    }
}
