//! \file       ImageAKB.cs
//! \date       Sat Nov 28 13:09:20 2015
//! \brief      Ai6Win engine image format.
//
// Copyright (C) 2015 by morkt
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

using GameRes.Compression;
using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Silky
{
    internal class AkbMetaData : ImageMetaData
    {
        public byte[]   Background;
        public int      InnerWidth;
        public int      InnerHeight;
        public uint     Flags;
    }

    [Export(typeof(ImageFormat))]
    public class AkbFormat : ImageFormat
    {
        public override string         Tag { get { return "AKB"; } }
        public override string Description { get { return "AI6WIN engine image format"; } }
        public override uint     Signature { get { return 0x20424B41; } } // 'AKB '

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.ReadInt32();
            var info = new AkbMetaData();
            info.Width = file.ReadUInt16();
            info.Height = file.ReadUInt16();
            info.Flags = file.ReadUInt32();
            info.BPP = 0 == (info.Flags & 0x40000000) ? 32 : 24;
            info.Background = file.ReadBytes (4);
            info.OffsetX = file.ReadInt32();
            info.OffsetY = file.ReadInt32();
            info.InnerWidth = file.ReadInt32() - info.OffsetX;
            info.InnerHeight = file.ReadInt32() - info.OffsetY;
            if (info.InnerWidth > info.Width || info.InnerHeight > info.Height)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new AkbReader (file.AsStream, (AkbMetaData)info);
            var image = reader.Unpack();
            return ImageData.Create (info, reader.Format, null, image, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AkbFormat.Write not implemented");
        }
    }

    internal class AkbReader
    {
        Stream          m_input;
        AkbMetaData     m_info;
        int             m_pixel_size;

        public PixelFormat Format { get; private set; }
        public int         Stride { get; private set; }

        public AkbReader (Stream input, AkbMetaData info)
        {
            m_input = input;
            m_info = info;
            m_pixel_size = m_info.BPP / 8;
            Stride = (int)m_info.Width * m_pixel_size;
            if (24 == m_info.BPP)
                Format = PixelFormats.Bgr24;
            else if (0 == (m_info.Flags & 0x80000000))
                Format = PixelFormats.Bgr32;
            else
                Format = PixelFormats.Bgra32;
        }

        public byte[] Unpack ()
        {
            if (0 == m_info.InnerWidth || 0 == m_info.InnerHeight)
                return CreateBackground();

            m_input.Position = 0x20;
            int inner_stride = m_info.InnerWidth * m_pixel_size;
            var pixels = new byte[m_info.InnerHeight * inner_stride];
            using (var lz = new LzssStream (m_input, LzssMode.Decompress, true))
            {
                for (int pos = pixels.Length - inner_stride; pos >= 0; pos -= inner_stride)
                {
                    if (inner_stride != lz.Read (pixels, pos, inner_stride))
                        throw new InvalidFormatException();
                }
            }
            RestoreDelta (pixels, inner_stride);
            if (m_info.InnerWidth == m_info.Width && m_info.InnerHeight == m_info.Height)
                return pixels;

            var image = CreateBackground();
            int src = 0;
            int dst = m_info.OffsetY * Stride + m_info.OffsetX * m_pixel_size;
            for (int y = 0; y < m_info.InnerHeight; ++y)
            {
                Buffer.BlockCopy (pixels, src, image, dst, inner_stride);
                dst += Stride;
                src += inner_stride;
            }
            return image;
        }

        private void RestoreDelta (byte[] pixels, int stride)
        {
            int src = 0;
            for (int i = m_pixel_size; i < stride; ++i)
                pixels[i] += pixels[src++];
            src = 0;
            for (int i = stride; i < pixels.Length; ++i)
                pixels[i] += pixels[src++];
        }

        private byte[] CreateBackground ()
        {
            var pixels = new byte[Stride * (int)m_info.Height];
            if (0 != LittleEndian.ToInt32 (m_info.Background, 0))
            {
                for (int i = 0; i < Stride; i += m_pixel_size)
                    Buffer.BlockCopy (m_info.Background, 0, pixels, i, m_pixel_size);
                Binary.CopyOverlapped (pixels, 0, Stride, pixels.Length-Stride);
            }
            return pixels;
        }
    }
}
