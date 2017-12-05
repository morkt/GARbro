//! \file       ImageGCmp.cs
//! \date       2017 Dec 01
//! \brief      Nekotaro Game System compressed image format.
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
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Nekotaro
{
    [Export(typeof(ImageFormat))]
    public class GCmpFormat : ImageFormat
    {
        public override string         Tag { get { return "GCMP"; } }
        public override string Description { get { return "Nekotaro Game System image format"; } }
        public override uint     Signature { get { return 0x706D4347; } } // 'GCmp'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            int bpp = header[12];
            if (bpp != 24 && bpp != 8 && bpp != 1)
                return null;
            return new ImageMetaData {
                Width = header.ToUInt16 (8),
                Height = header.ToUInt16 (10),
                BPP = bpp,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var reader = new GCmpDecoder (file, info, this, true))
                return reader.Image;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GCmpFormat.Write not implemented");
        }
    }

    internal sealed class GCmpDecoder : IImageDecoder
    {
        IBinaryStream   m_input;
        ImageData       m_image;
        bool            m_should_dispose;

        public Stream            Source { get { return m_input.AsStream; } }
        public ImageFormat SourceFormat { get; private set; }
        public ImageMetaData       Info { get; private set; }
        public PixelFormat       Format { get; private set; }
        public BitmapPalette    Palette { get; private set; }
        public int               Stride { get; private set; }
        public ImageData          Image {
            get {
                if (null == m_image)
                {
                    var pixels = Unpack();
                    m_image = ImageData.CreateFlipped (Info, Format, Palette, pixels, Stride);
                }
                return m_image;
            }
        }

        public GCmpDecoder (IBinaryStream input, ImageMetaData info, ImageFormat source, bool leave_open = false)
        {
            m_input = input;
            Info = info;
            SourceFormat = source;
            m_should_dispose = !leave_open;
            if (info.BPP > 1)
                Stride = (int)info.Width * info.BPP / 8;
            else
                Stride = ((int)info.Width + 7) / 8;
        }

        public byte[] Unpack ()
        {
            m_input.Position = 0x10;
            if (24 == Info.BPP)
                return Unpack24bpp();
            else
                return Unpack8bpp();
        }

        byte[] Unpack24bpp ()
        {
            Format = PixelFormats.Bgr24;
            int pixel_count = (int)(Info.Width * Info.Height);
            var output = new byte[pixel_count * Info.BPP / 8 + 1];
            var frame = new byte[384];
            int dst = 0;
            int v19 = 0;
            while (pixel_count > 0)
            {
                int count, frame_pos, pixel;
                if (v19 != 0)
                {
                    pixel = m_input.ReadInt24();
                    count = 1;
                    frame_pos = 127;
                    --v19;
                }
                else
                {
                    count = m_input.ReadUInt8();
                    int lo = count & 0x1F;
                    if (0 != (count & 0x80))
                    {
                        count = ((byte)count >> 5) & 3;
                        if (count != 0)
                        {
                            frame_pos = lo;
                        }
                        else
                        {
                            count = lo << 1;
                            frame_pos = m_input.ReadUInt8();
                            if (0 != (frame_pos & 0x80))
                                ++count;
                            frame_pos &= 0x7F;
                        }
                        if (0 == count)
                        {
                            count = m_input.ReadInt32();
                        }
                        int fpos = 3 * frame_pos;
                        pixel = frame[fpos] | frame[fpos+1] << 8 | frame[fpos+2] << 16;
                    }
                    else
                    {
                        if (1 == count)
                        {
                            v19 = m_input.ReadUInt8() - 1;
                        }
                        else if (0 == count)
                        {
                            count = m_input.ReadInt32();
                        }
                        pixel = m_input.ReadInt24();
                        frame_pos = 127;
                    }
                }
                if (count > pixel_count)
                    count = pixel_count;
                pixel_count -= count;
                LittleEndian.Pack (pixel, output, dst);
                dst += 3;
                if (--count > 0)
                {
                    count *= 3;
                    Binary.CopyOverlapped (output, dst - 3, dst, count);
                    dst += count;
                }
                if (frame_pos != 0)
                    Buffer.BlockCopy (frame, 0, frame, 3, 3 * frame_pos);
                frame[0] = (byte)pixel;
                frame[1] = (byte)(pixel >> 8);
                frame[2] = (byte)(pixel >> 16);
            }
            return output;
        }

        byte[] Unpack8bpp ()
        {
            Format = 8 == Info.BPP ? PixelFormats.Gray8 : PixelFormats.BlackWhite;
            int pixel_count = (int)Info.Height * Stride;
            var output = new byte[pixel_count];
            int dst = 0;
            byte[] frame = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 0xFF };

            int count = pixel_count;
            int extra_count = pixel_count;
            while (pixel_count > 0)
            {
                byte pixel;
                int frame_pos;
                byte ctl = m_input.ReadUInt8();
                int hi = ctl >> 4;
                int lo = ctl & 0xF;
                if (hi != 0)
                {
                    frame_pos = hi - 1;
                    pixel = frame[frame_pos];
                    count = lo + 1;
                }
                else
                {
                    switch (lo)
                    {
                    default:
                        count = lo + 1;
                        break;
                    case 10:
                        count = m_input.ReadUInt8() + 11;
                        break;
                    case 11:
                        count = m_input.ReadUInt16() + 267;
                        break;
                    case 12:
                        count = m_input.ReadInt32() + 65803;
                        break;
                    case 13:
                        extra_count = 0x10;
                        count = m_input.ReadUInt8();
                        break;
                    case 14:
                        extra_count = 0x120;
                        count = m_input.ReadUInt16();
                        break;
                    case 15:
                        extra_count = 0x10130;
                        count = m_input.ReadInt32();
                        break;
                    }
                    pixel = m_input.ReadUInt8();
                    if (lo < 13)
                    {
                        frame_pos = 14;
                    }
                    else
                    {
                        lo = pixel & 0xF;
                        frame_pos = (pixel >> 4) - 1;
                        pixel = frame[frame_pos];
                        count = extra_count + 16 * count + lo + 1;
                    }
                }
                if (count > pixel_count)
                    count = pixel_count;
                pixel_count -= count;
                for (int i = 0; i < count; ++i)
                    output[dst++] = pixel;
                Buffer.BlockCopy (frame, 0, frame, 1, frame_pos);
                frame[0] = pixel;
            }
            return output;
        }

        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed && m_should_dispose)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
    }
}
