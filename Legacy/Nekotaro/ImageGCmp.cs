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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Nekotaro
{
    internal class GCmpMetaData : ImageMetaData
    {
        public bool IsCompressed;
    }

    [Export(typeof(ImageFormat))]
    public class GCmpFormat : ImageFormat
    {
        public override string         Tag { get { return "GCMP"; } }
        public override string Description { get { return "Nekotaro Game System image format"; } }
        public override uint     Signature { get { return 0x706D4347; } } // 'GCmp'

        public GCmpFormat ()
        {
            Extensions = new[] { "GCMP", "AIG" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            int format = (sbyte)header[12];
            int bpp = Math.Abs (format);
            if (bpp != 24 && bpp != 8 && bpp != 1)
                return null;
            return new GCmpMetaData {
                Width = header.ToUInt16 (8),
                Height = header.ToUInt16 (10),
                BPP = bpp,
                IsCompressed = format > 0,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var reader = new GCmpDecoder (file, (GCmpMetaData)info, this, true))
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
        GCmpMetaData    m_info;
        bool            m_should_dispose;

        public Stream            Source { get { return m_input.AsStream; } }
        public ImageFormat SourceFormat { get; private set; }
        public ImageMetaData       Info { get { return m_info; } }
        public PixelFormat       Format { get; private set; }
        public BitmapPalette    Palette { get; private set; }
        public int               Stride { get; private set; }
        public ImageData          Image { get { return m_image ?? (m_image = Unpack()); } }

        public GCmpDecoder (IBinaryStream input, GCmpMetaData info, ImageFormat source, bool leave_open = false)
        {
            m_input = input;
            m_info = info;
            SourceFormat = source;
            m_should_dispose = !leave_open;
            if (info.BPP > 1)
                Stride = info.iWidth * info.BPP / 8;
            else
                Stride = (info.iWidth + 7) / 8;
        }

        static BitmapPalette LastUsedPalette = null;

        public ImageData Unpack ()
        {
            m_input.Position = 0x10;
            byte[] pixels;
            if (24 == Info.BPP)
                pixels = Unpack24bpp();
            else
                pixels = Unpack8bpp();
            if (8 == Info.BPP)
                Palette = LastUsedPalette ?? (LastUsedPalette = RetrievePalette() ?? DefaultPalette);
            return ImageData.CreateFlipped (Info, Format, Palette, pixels, Stride);
        }

        byte[] Unpack24bpp ()
        {
            Format = PixelFormats.Bgr24;
            int pixel_count = Info.iWidth * Info.iHeight;
            var output = new byte[pixel_count * Info.BPP / 8 + 1];
            var frame = new byte[384];
            int dst = 0;
            int chunk_length = 0;
            while (pixel_count > 0)
            {
                int count, frame_pos, pixel;
                if (chunk_length != 0)
                {
                    pixel = m_input.ReadInt24();
                    count = 1;
                    frame_pos = 127;
                    --chunk_length;
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
                            chunk_length = m_input.ReadUInt8() - 1;
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
            if (8 == Info.BPP)
                Format = PixelFormats.Indexed8;
            else
                Format = PixelFormats.BlackWhite;
            int pixel_count = Info.iHeight * Stride;
            if (!m_info.IsCompressed)
                return m_input.ReadBytes (pixel_count);

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

        static void LzssUnpack (IBinaryStream input, byte[] output)
        {
            int dst = 0;
            int mask = 0;
            int ctl = 0;
            while (dst < output.Length)
            {
                mask >>= 1;
                if (0 == mask)
                {
                    ctl = input.ReadUInt8();
                    mask = 0x80;
                }
                if ((ctl & mask) != 0)
                {
                    int off = input.ReadUInt16();
                    int count = (off & 0xF) + 3;
                    off >>= 4;
                    int src = dst - off - 1;
                    Binary.CopyOverlapped (output, src, dst, count);
                    dst += count;
                }
                else
                {
                    output[dst++] = input.ReadUInt8();
                }
            }
        }

        BitmapPalette RetrievePalette ()
        {
            // find SYSTEM.LZS file, decompress and read it as text file
            // find 'P' line that denotes archive name and entry number
            // if entry number is zero, then it's just a file (possibly compressed)
            // open referenced file and retrieve palette
            try
            {
                string system_name = "SYSTEM.LZS";
                if (!File.Exists (system_name))
                {
                    system_name = @"..\SYSTEM.LZS";
                    if (!File.Exists (system_name))
                        return null;
                }
                byte[] system_bin;
                using (var input = BinaryStream.FromFile (system_name))
                {
                    int unpacked_size = input.ReadUInt16();
                    input.ReadUInt16();
                    system_bin = new byte[unpacked_size];
                    LzssUnpack (input, system_bin);
                }
                string line;
                using (var mem = new MemoryStream (system_bin))
                using (var text = new StreamReader (mem, Encodings.cp932))
                {
                    while ((line = text.ReadLine()) != null)
                    {
                        if (line.Length > 3 && line.StartsWith ("P:"))
                            break;
                    }
                    if (null == line)
                        return null;
                }
                var match = PLineRe.Match (line);
                if (!match.Success)
                    return null;
                int id;
                if (!Int32.TryParse (match.Groups[2].Value, out id))
                    return null;
                var arc_name = Path.Combine (Path.GetDirectoryName (system_name), match.Groups[1].Value);
                if (0 == id)
                {
                    using (var file = BinaryStream.FromFile (arc_name))
                    {
                        Stream pal_stream;
                        int unpacked_size = file.ReadUInt16();
                        int packed_size = file.ReadUInt16();
                        if (packed_size + 4 == file.Length)
                        {
                            var pal_data = new byte[unpacked_size];
                            LzssUnpack (file, pal_data);
                            pal_stream = new MemoryStream (pal_data);
                        }
                        else
                        {
                            file.Position = 0;
                            pal_stream = file.AsStream;
                        }
                        int colors = (int)pal_stream.Length / 3;
                        using (pal_stream)
                            return ImageFormat.ReadPalette (pal_stream, colors, PaletteFormat.Rgb);
                    }
                }
                else
                {
                    using (var file = new ArcView (arc_name))
                    {
                        var arc = Nsc.Value.TryOpen (file);
                        if (null == arc)
                            return null;
                        var entry = ((List<Entry>)arc.Dir)[id-1];
                        using (var input = arc.OpenEntry (entry))
                            return ImageFormat.ReadPalette (input, 0x100, PaletteFormat.Rgb);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        static readonly Regex PLineRe = new Regex (@"^P:([^,]+),(\d+),(\d+)", RegexOptions.Compiled);
        static readonly ResourceInstance<ArchiveFormat> Nsc = new ResourceInstance<ArchiveFormat> ("NSC");

        static readonly BitmapPalette DefaultPalette = new BitmapPalette (
            // [000317][PIL] Seek -remasters-
#region colors
            new Color[] {
                Color.FromRgb (0x00, 0x00, 0x00),
                Color.FromRgb (0xFF, 0xFF, 0xFF),
                Color.FromRgb (0x22, 0x22, 0x22),
                Color.FromRgb (0x44, 0x44, 0x44),
                Color.FromRgb (0x55, 0x55, 0x55),
                Color.FromRgb (0x66, 0x66, 0x66),
                Color.FromRgb (0x77, 0x77, 0x77),
                Color.FromRgb (0x88, 0x88, 0x88),
                Color.FromRgb (0x99, 0x99, 0x99),
                Color.FromRgb (0xAA, 0xAA, 0xAA),
                Color.FromRgb (0xBB, 0xBB, 0xBB),
                Color.FromRgb (0xCC, 0xCC, 0xCC),
                Color.FromRgb (0xDD, 0xDD, 0xDD),
                Color.FromRgb (0xEE, 0xEE, 0xEE),
                Color.FromRgb (0x00, 0xFF, 0x00),
                Color.FromRgb (0xFF, 0x00, 0x00),
                Color.FromRgb (0xFF, 0xFF, 0x00),
                Color.FromRgb (0x00, 0x00, 0xFF),
                Color.FromRgb (0x01, 0x01, 0x01),
                Color.FromRgb (0x04, 0x07, 0x17),
                Color.FromRgb (0x0D, 0x07, 0x05),
                Color.FromRgb (0x18, 0x03, 0x01),
                Color.FromRgb (0x1A, 0x0D, 0x09),
                Color.FromRgb (0x0F, 0x11, 0x15),
                Color.FromRgb (0x07, 0x14, 0x25),
                Color.FromRgb (0x2A, 0x09, 0x04),
                Color.FromRgb (0x37, 0x03, 0x02),
                Color.FromRgb (0x23, 0x14, 0x0D),
                Color.FromRgb (0x4C, 0x03, 0x01),
                Color.FromRgb (0x18, 0x19, 0x20),
                Color.FromRgb (0x3F, 0x0C, 0x07),
                Color.FromRgb (0x06, 0x13, 0x67),
                Color.FromRgb (0x0B, 0x20, 0x2E),
                Color.FromRgb (0x2E, 0x19, 0x0F),
                Color.FromRgb (0x5E, 0x03, 0x02),
                Color.FromRgb (0x02, 0x2D, 0x2D),
                Color.FromRgb (0x22, 0x21, 0x2C),
                Color.FromRgb (0x4E, 0x12, 0x08),
                Color.FromRgb (0x40, 0x0E, 0x4D),
                Color.FromRgb (0x3A, 0x1E, 0x10),
                Color.FromRgb (0x71, 0x04, 0x02),
                Color.FromRgb (0x16, 0x32, 0x1C),
                Color.FromRgb (0x11, 0x2B, 0x4F),
                Color.FromRgb (0x28, 0x29, 0x38),
                Color.FromRgb (0x86, 0x03, 0x03),
                Color.FromRgb (0x64, 0x15, 0x0B),
                Color.FromRgb (0x41, 0x26, 0x17),
                Color.FromRgb (0x50, 0x20, 0x0E),
                Color.FromRgb (0x03, 0x40, 0x44),
                Color.FromRgb (0x9C, 0x02, 0x00),
                Color.FromRgb (0x04, 0x34, 0x80),
                Color.FromRgb (0x2C, 0x30, 0x43),
                Color.FromRgb (0x00, 0x00, 0xFB),
                Color.FromRgb (0x56, 0x1C, 0x43),
                Color.FromRgb (0x7E, 0x13, 0x0B),
                Color.FromRgb (0x3A, 0x30, 0x2F),
                Color.FromRgb (0x70, 0x1E, 0x02),
                Color.FromRgb (0x5B, 0x27, 0x10),
                Color.FromRgb (0x4F, 0x2E, 0x1C),
                Color.FromRgb (0x1C, 0x48, 0x1D),
                Color.FromRgb (0x31, 0x35, 0x53),
                Color.FromRgb (0x24, 0x24, 0x9E),
                Color.FromRgb (0xB7, 0x02, 0x01),
                Color.FromRgb (0x78, 0x22, 0x05),
                Color.FromRgb (0x3A, 0x37, 0x3D),
                Color.FromRgb (0x0A, 0x4B, 0x68),
                Color.FromRgb (0x9A, 0x0E, 0x34),
                Color.FromRgb (0x92, 0x1A, 0x0B),
                Color.FromRgb (0x5A, 0x33, 0x25),
                Color.FromRgb (0x81, 0x04, 0xBA),
                Color.FromRgb (0x7B, 0x28, 0x18),
                Color.FromRgb (0x68, 0x33, 0x18),
                Color.FromRgb (0x3B, 0x3E, 0x58),
                Color.FromRgb (0x45, 0x3F, 0x42),
                Color.FromRgb (0xD0, 0x04, 0x04),
                Color.FromRgb (0x67, 0x27, 0x77),
                Color.FromRgb (0x8C, 0x2B, 0x12),
                Color.FromRgb (0x2E, 0x5D, 0x11),
                Color.FromRgb (0x66, 0x3D, 0x29),
                Color.FromRgb (0x2F, 0x53, 0x49),
                Color.FromRgb (0xED, 0x01, 0x01),
                Color.FromRgb (0x75, 0x39, 0x1C),
                Color.FromRgb (0x45, 0x45, 0x61),
                Color.FromRgb (0x29, 0x43, 0xA2),
                Color.FromRgb (0xA3, 0x27, 0x1A),
                Color.FromRgb (0x5D, 0x46, 0x42),
                Color.FromRgb (0x15, 0x64, 0x6A),
                Color.FromRgb (0x78, 0x35, 0x62),
                Color.FromRgb (0x76, 0x44, 0x1C),
                Color.FromRgb (0x92, 0x37, 0x20),
                Color.FromRgb (0xB0, 0x0C, 0xB3),
                Color.FromRgb (0x51, 0x50, 0x5B),
                Color.FromRgb (0x79, 0x47, 0x33),
                Color.FromRgb (0xE6, 0x11, 0x29),
                Color.FromRgb (0x0F, 0x62, 0xB7),
                Color.FromRgb (0x4F, 0x51, 0x79),
                Color.FromRgb (0xA6, 0x37, 0x25),
                Color.FromRgb (0x86, 0x49, 0x21),
                Color.FromRgb (0x44, 0x6C, 0x20),
                Color.FromRgb (0x3C, 0x56, 0xA5),
                Color.FromRgb (0x20, 0x72, 0x7B),
                Color.FromRgb (0x71, 0x53, 0x49),
                Color.FromRgb (0xA1, 0x3F, 0x3A),
                Color.FromRgb (0x7F, 0x44, 0x7D),
                Color.FromRgb (0x5C, 0x5C, 0x61),
                Color.FromRgb (0xDC, 0x02, 0xE7),
                Color.FromRgb (0x59, 0x57, 0x82),
                Color.FromRgb (0xB7, 0x3B, 0x27),
                Color.FromRgb (0x91, 0x51, 0x26),
                Color.FromRgb (0xED, 0x24, 0x44),
                Color.FromRgb (0x69, 0x61, 0x67),
                Color.FromRgb (0xB9, 0x44, 0x31),
                Color.FromRgb (0x5F, 0x61, 0x88),
                Color.FromRgb (0x92, 0x53, 0x58),
                Color.FromRgb (0xA3, 0x53, 0x3B),
                Color.FromRgb (0x56, 0x7D, 0x2B),
                Color.FromRgb (0x45, 0x74, 0x89),
                Color.FromRgb (0x8F, 0x66, 0x13),
                Color.FromRgb (0x25, 0x87, 0x84),
                Color.FromRgb (0x69, 0x68, 0x70),
                Color.FromRgb (0xBA, 0x4F, 0x3A),
                Color.FromRgb (0x69, 0x69, 0x94),
                Color.FromRgb (0xE9, 0x3B, 0x35),
                Color.FromRgb (0x90, 0x53, 0xA6),
                Color.FromRgb (0x9E, 0x60, 0x43),
                Color.FromRgb (0xC1, 0x3A, 0xAD),
                Color.FromRgb (0x5B, 0x67, 0xBA),
                Color.FromRgb (0x06, 0x95, 0xC7),
                Color.FromRgb (0x6D, 0x71, 0x7E),
                Color.FromRgb (0xC8, 0x51, 0x46),
                Color.FromRgb (0x84, 0x6E, 0x6C),
                Color.FromRgb (0x29, 0xAA, 0x28),
                Color.FromRgb (0xA2, 0x6D, 0x2F),
                Color.FromRgb (0x74, 0x74, 0x96),
                Color.FromRgb (0xC1, 0x5E, 0x44),
                Color.FromRgb (0x43, 0x92, 0x8B),
                Color.FromRgb (0xA8, 0x6D, 0x57),
                Color.FromRgb (0xF3, 0x45, 0x60),
                Color.FromRgb (0xE8, 0x5A, 0x28),
                Color.FromRgb (0x71, 0x94, 0x34),
                Color.FromRgb (0x7D, 0x7E, 0x8E),
                Color.FromRgb (0xD1, 0x61, 0x51),
                Color.FromRgb (0x75, 0x7E, 0xBC),
                Color.FromRgb (0xA9, 0x74, 0x72),
                Color.FromRgb (0xC5, 0x6C, 0x51),
                Color.FromRgb (0xA1, 0x87, 0x38),
                Color.FromRgb (0x9E, 0x70, 0xB7),
                Color.FromRgb (0x84, 0x85, 0x96),
                Color.FromRgb (0xEC, 0x5C, 0x5D),
                Color.FromRgb (0xD1, 0x70, 0x5E),
                Color.FromRgb (0x40, 0xAD, 0xAC),
                Color.FromRgb (0x8A, 0x8B, 0x99),
                Color.FromRgb (0xB0, 0x83, 0x69),
                Color.FromRgb (0xE5, 0x54, 0xD2),
                Color.FromRgb (0x7D, 0xA3, 0x51),
                Color.FromRgb (0xA3, 0x7E, 0xB2),
                Color.FromRgb (0xF3, 0x6A, 0x5D),
                Color.FromRgb (0x86, 0x8B, 0xC9),
                Color.FromRgb (0x91, 0x92, 0x9D),
                Color.FromRgb (0xD7, 0x7A, 0x67),
                Color.FromRgb (0x00, 0xFD, 0x01),
                Color.FromRgb (0xC9, 0x85, 0x73),
                Color.FromRgb (0xAD, 0x93, 0x80),
                Color.FromRgb (0xB9, 0x96, 0x58),
                Color.FromRgb (0x98, 0x99, 0xA1),
                Color.FromRgb (0xF5, 0x74, 0x69),
                Color.FromRgb (0xA2, 0xB1, 0x1B),
                Color.FromRgb (0xE4, 0x82, 0x6B),
                Color.FromRgb (0x90, 0x98, 0xCD),
                Color.FromRgb (0xB7, 0x8A, 0xBE),
                Color.FromRgb (0xE1, 0x89, 0x76),
                Color.FromRgb (0xA0, 0xA2, 0xAD),
                Color.FromRgb (0xD3, 0x92, 0x85),
                Color.FromRgb (0x7C, 0xA5, 0xF5),
                Color.FromRgb (0xF9, 0x84, 0x77),
                Color.FromRgb (0xC0, 0xA2, 0x77),
                Color.FromRgb (0xF8, 0x99, 0x1F),
                Color.FromRgb (0xE9, 0x8F, 0x84),
                Color.FromRgb (0x9D, 0xA3, 0xDC),
                Color.FromRgb (0xAB, 0xAB, 0xAA),
                Color.FromRgb (0xF9, 0x90, 0x7D),
                Color.FromRgb (0xB0, 0xC6, 0x24),
                Color.FromRgb (0xCD, 0xA1, 0xB0),
                Color.FromRgb (0xDE, 0x9E, 0x90),
                Color.FromRgb (0xAE, 0xAF, 0xB9),
                Color.FromRgb (0xEE, 0x99, 0x86),
                Color.FromRgb (0xA9, 0xAB, 0xE4),
                Color.FromRgb (0xF8, 0x9B, 0x89),
                Color.FromRgb (0xB4, 0xB6, 0xB9),
                Color.FromRgb (0xAD, 0xB5, 0xE3),
                Color.FromRgb (0xF9, 0xA3, 0x88),
                Color.FromRgb (0xE4, 0xAA, 0x9F),
                Color.FromRgb (0xF9, 0xA6, 0x95),
                Color.FromRgb (0xD5, 0xCE, 0x29),
                Color.FromRgb (0xD3, 0xB5, 0xBC),
                Color.FromRgb (0xBC, 0xBF, 0xC4),
                Color.FromRgb (0xBA, 0xB9, 0xE9),
                Color.FromRgb (0xFB, 0xAC, 0x9A),
                Color.FromRgb (0xEB, 0xB4, 0xA9),
                Color.FromRgb (0xFC, 0xB0, 0xA0),
                Color.FromRgb (0xBD, 0xC3, 0xEA),
                Color.FromRgb (0xFB, 0xBB, 0x74),
                Color.FromRgb (0xC6, 0xC6, 0xCA),
                Color.FromRgb (0xEE, 0xBA, 0xAF),
                Color.FromRgb (0xFD, 0xB5, 0xA4),
                Color.FromRgb (0xFC, 0xB8, 0xA9),
                Color.FromRgb (0xFC, 0xBC, 0xAC),
                Color.FromRgb (0xC7, 0xCA, 0xEF),
                Color.FromRgb (0xCE, 0xCD, 0xD2),
                Color.FromRgb (0xEF, 0xC3, 0xC0),
                Color.FromRgb (0xFC, 0xC0, 0xB3),
                Color.FromRgb (0xD4, 0xED, 0x38),
                Color.FromRgb (0xFC, 0xC5, 0xB9),
                Color.FromRgb (0xD0, 0xD3, 0xE8),
                Color.FromRgb (0xFC, 0xD5, 0x78),
                Color.FromRgb (0xFC, 0xC9, 0xBF),
                Color.FromRgb (0xD8, 0xD9, 0xE2),
                Color.FromRgb (0xFC, 0xCD, 0xC4),
                Color.FromRgb (0xFC, 0xD1, 0xC9),
                Color.FromRgb (0xDB, 0xDE, 0xE9),
                Color.FromRgb (0xFD, 0xD5, 0xD0),
                Color.FromRgb (0xFE, 0xFD, 0x04),
                Color.FromRgb (0xFD, 0xD8, 0xD4),
                Color.FromRgb (0xFB, 0xE0, 0xB9),
                Color.FromRgb (0xE2, 0xE5, 0xEE),
                Color.FromRgb (0xFD, 0xDD, 0xD9),
                Color.FromRgb (0xFD, 0xE3, 0xE0),
                Color.FromRgb (0xFC, 0xF4, 0x91),
                Color.FromRgb (0xEB, 0xED, 0xF1),
                Color.FromRgb (0xFD, 0xE8, 0xE4),
                Color.FromRgb (0xFD, 0xEC, 0xE8),
                Color.FromRgb (0xF1, 0xF3, 0xF6),
                Color.FromRgb (0xFD, 0xF0, 0xED),
                Color.FromRgb (0xFD, 0xF6, 0xF4),
                Color.FromRgb (0xFD, 0xFB, 0xF9),
                Color.FromRgb (0xFF, 0xFF, 0xFF),
                Color.FromRgb (0x00, 0x00, 0x00),
                Color.FromRgb (0x00, 0x00, 0x00),
                Color.FromRgb (0x00, 0x00, 0x00),
                Color.FromRgb (0x00, 0x00, 0x00),
                Color.FromRgb (0x00, 0x00, 0x00),
                Color.FromRgb (0x00, 0x00, 0x00),
                Color.FromRgb (0x00, 0x00, 0x00),
                Color.FromRgb (0x00, 0x00, 0x00),
                Color.FromRgb (0x00, 0x00, 0x00),
                Color.FromRgb (0x00, 0x00, 0x00),
                Color.FromRgb (0x00, 0x00, 0x00),
                Color.FromRgb (0x00, 0x00, 0x00),
                Color.FromRgb (0x00, 0x00, 0x00),
                Color.FromRgb (0x00, 0x00, 0x00),
                Color.FromRgb (0x00, 0x00, 0x00),
                Color.FromRgb (0x00, 0x00, 0x00),
                Color.FromRgb (0x00, 0x00, 0x00),
                Color.FromRgb (0x00, 0x00, 0x00),
                Color.FromRgb (0x00, 0x00, 0x00),
                Color.FromRgb (0x00, 0x00, 0x00),
            }
#endregion
        );

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
