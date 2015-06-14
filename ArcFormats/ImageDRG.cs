//! \file       ImageDRG.cs
//! \date       Fri Aug 22 05:58:40 2014
//! \brief      Digital Romance System image format implementation.
//
// Copyright (C) 2014-2015 by morkt
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
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.DRS
{
    [Export(typeof(ImageFormat))]
    public class DrgFormat : ImageFormat
    {
        public override string         Tag { get { return "DRG"; } }
        public override string Description { get { return "Digital Romance System image format"; } }
        public override uint     Signature { get { return ~0x4c4c5546u; } } // 'FULL'

        public DrgFormat ()
        {
            Extensions = new string[] { "drg", "ggd" };
            Signatures = new uint[] { ~0x4c4c5546u, ~0x45555254u, ~0x48474948u };
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var writer = new Writer (file))
            {
                writer.Pack (image.Bitmap);
            }
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            uint signature = ~FormatCatalog.ReadSignature (stream);
            int bpp;
            switch (signature)
            {
            case 0x4c4c5546: /* fall through */
            case 0x45555254: bpp = 24; break;
            case 0x48474948: bpp = 16; break;
            default: return null;
            }
            using (var input = new BinaryReader (stream, Encoding.ASCII, true))
            {
                uint width = input.ReadUInt16();
                uint height = input.ReadUInt16();
                return new ImageMetaData {
                    Width  = width,
                    Height = height,
                    BPP    = bpp,
                };
            }
        }

        public override ImageData Read (Stream file, ImageMetaData info)
        {
            file.Position = 8;
            PixelFormat format;
            if (24 == info.BPP)
                format = PixelFormats.Bgr24;
            else
                format = PixelFormats.Bgr565;

            int stride = (int)info.Width*((info.BPP+7)/8);
            var pixel_data = DecodeStream (file, stride*(int)info.Height);
            if (null == pixel_data)
                throw new InvalidFormatException();
            var bitmap = BitmapSource.Create ((int)info.Width, (int)info.Height, 96, 96,
                                              format, null, pixel_data, stride);
            bitmap.Freeze();
            return new ImageData (bitmap, info);
        }

        byte[] DecodeStream (Stream input, int pixel_count)
        {
            byte[] output = new byte[pixel_count];
            for (int out_pos = 0; pixel_count > 0; )
            {
                int opcode = input.ReadByte ();
                if (-1 == opcode)
                    break;
                int count, src_offset;
                switch (opcode)
                {
                case 0:
                    count = input.ReadByte ();
                    src_offset = out_pos - 3;
                    if (count < 0 || count * 3 > pixel_count || src_offset < 0)
                        return null;
                    for (int i = 0; i < count; ++i)
                    {
                        Buffer.BlockCopy (output, src_offset, output, out_pos, 3);
                        out_pos += 3;
                    }
                    pixel_count -= count * 3;
                    break;
                case 1:
                    count = 3 * input.ReadByte ();
                    src_offset = out_pos - 3 * input.ReadByte ();
                    if (count < 0 || count > pixel_count || src_offset < 0 || src_offset == out_pos)
                        return null;
                    Binary.CopyOverlapped (output, src_offset, out_pos, count);
                    out_pos += count;
                    pixel_count -= count;
                    break;
                case 2:
                    {
                        count = 3 * input.ReadByte ();
                        int off_lo = input.ReadByte ();
                        int off_hi = input.ReadByte ();
                        src_offset = out_pos - 3 * (off_hi << 8 | off_lo);
                        if (count < 0 || count > pixel_count || src_offset < 0 || src_offset == out_pos)
                            return null;
                        Binary.CopyOverlapped (output, src_offset, out_pos, count);
                        out_pos += count;
                        pixel_count -= count;
                        break;
                    }
                case 3:
                    count = 3;
                    src_offset = out_pos - 3 * input.ReadByte ();
                    if (count > pixel_count || src_offset < 0 || src_offset == out_pos)
                        return null;
                    Buffer.BlockCopy (output, src_offset, output, out_pos, count);
                    out_pos += count;
                    pixel_count -= count;
                    break;
                case 4:
                    {
                        count = 3;
                        int off_lo = input.ReadByte ();
                        int off_hi = input.ReadByte ();
                        src_offset = out_pos - 3 * (off_hi << 8 | off_lo);
                        if (count > pixel_count || src_offset < 0 || src_offset == out_pos)
                            return null;
                        Buffer.BlockCopy (output, src_offset, output, out_pos, count);
                        out_pos += count;
                        pixel_count -= count;
                        break;
                    }
                default:
                    count = 3*(opcode - 4);
                    if (count > pixel_count)
                        return null;
                    for (int i = 0; i < count; ++i)
                    {
                        output[out_pos++] = (byte)input.ReadByte ();
                    }
                    pixel_count -= count;
                    break;
                }
            }
            return output;
        }

        internal class Writer : IDisposable
        {
            BinaryWriter    m_out;
            uint[]          m_input;

            const int MaxWindowSize = 0xfffe;
            const int MaxMatchSize = 0xff;

            struct WindowPosition
            {
                public ushort Offset;
                public ushort Length;
            }

            public Writer (Stream output)
            {
                m_out = new BinaryWriter (output, Encoding.ASCII, true);
            }

            void WriteHeader (int width, int height)
            {
                m_out.Write (~0x4c4c5546u);
                m_out.Write ((ushort)width);
                m_out.Write ((ushort)height);
            }

            void PrepareInput (BitmapSource bitmap)
            {
                int width  = bitmap.PixelWidth;
                int height = bitmap.PixelHeight;
                int pixels = width*height;
                m_input = new uint[pixels];
                if (bitmap.Format != PixelFormats.Bgr32)
                {
                    var converted_bitmap = new FormatConvertedBitmap();
                    converted_bitmap.BeginInit();
                    converted_bitmap.Source = bitmap;
                    converted_bitmap.DestinationFormat = PixelFormats.Bgr32;
                    converted_bitmap.EndInit();
                    bitmap = converted_bitmap;
                }
                unsafe
                {
                    fixed (uint* buffer = m_input)
                    {
                        bitmap.CopyPixels (Int32Rect.Empty, (IntPtr)buffer, pixels*4, width*4);
                    }
                }
                WriteHeader (width, height);
            }

            List<byte>  m_buffer = new List<byte>();
            int         m_buffer_size;

            Dictionary<uint, SortedSet<int>> m_dict = new Dictionary<uint, SortedSet<int>> (MaxWindowSize);

            public void Pack (BitmapSource bitmap)
            {
                PrepareInput (bitmap);
                m_dict.Clear();
                m_buffer.Clear();
                m_buffer_size = 0;
                int last    = m_input.Length;
                int current = 0;
                int win_begin = current;
                int win_end   = current;
                while (current != last)
                {
                    int new_win_end = current;
                    int window_size = Math.Min (new_win_end - win_begin, MaxWindowSize);
                    int new_win_begin = new_win_end - window_size;
                    AdjustWindow (ref win_begin, ref win_end, new_win_begin, new_win_end);
                    var win_pos = FindLongest (win_begin, win_end, current, last);
                    if (win_pos.Length > 0)
                    {
                        Flush();
                        WritePos (win_pos, current - win_pos.Offset);
                        current += win_pos.Length;
                    }
                    else
                    {
                        WritePixel (m_input[current++]);
                    }
                }
                Flush();
            }

            void AdjustWindow (ref int win_begin, ref int win_end, int new_begin, int new_end)
            {
                while (win_begin != new_begin)
                {
                    var pixel = m_input[win_begin];
                    SortedSet<int> pos = m_dict[pixel];
                    pos.Remove (win_begin);
                    if (0 == pos.Count)
                        m_dict.Remove (pixel);
                    ++win_begin;
                }
                while (win_end != new_end)
                {
                    var pixel = m_input[win_end];
                    SortedSet<int> pos;
                    if (!m_dict.TryGetValue (pixel, out pos))
                    {
                        pos = new SortedSet<int>();
                        m_dict[pixel] = pos;
                    }
                    pos.Add (win_end);
                    ++win_end;
                }
            }

            void WritePixel (uint pixel)
            {
                if (0xff-4 == m_buffer_size)
                    Flush();
                m_buffer.Add ((byte)pixel);
                m_buffer.Add ((byte)(pixel >> 8));
                m_buffer.Add ((byte)(pixel >> 16));
                ++m_buffer_size;
            }

            void Flush ()
            {
                if (0 != m_buffer.Count)
                {
                    m_out.Write ((byte)(m_buffer_size+4));
                    foreach (var b in m_buffer)
                        m_out.Write (b);
                    m_buffer.Clear();
                    m_buffer_size = 0;
                }
            }

            int Mismatch (int first1, int last1, int first2)
            {
                while (first1 != last1 && m_input[first1] == m_input[first2])
                {
                    ++first1;
                    ++first2;
                }
                return first2;
            }

            WindowPosition FindLongest (int win_begin, int win_end, int buf_begin, int buf_end)
            {
                buf_end = Math.Min (buf_begin + MaxMatchSize, buf_end);
                WindowPosition pos = new WindowPosition { Offset = 0, Length = 0 };
                if (win_begin == win_end)
                    return pos;
                SortedSet<int> found;
                if (m_dict.TryGetValue (m_input[buf_begin], out found))
                {
                    foreach (var win_pos in found.Reverse())
                    {
                        var match_end = Mismatch (buf_begin+1, buf_end, win_pos+1);
                        int weight = match_end - win_pos;
                        int distance = buf_begin - win_pos;
                        if (weight > pos.Length)
                        {
                            pos.Offset = (ushort)distance;
                            pos.Length = (ushort)weight;
                            if (MaxMatchSize == weight)
                                break;
                        }
                    }
                }
                return pos;
            }

            void WritePos (WindowPosition pos, int buf)
            {
                if (1 == pos.Offset)
                {
                    uint pixel = m_input[buf];
                    if (1 == pos.Length || -1 == Array.FindIndex (m_input, buf+1, pos.Length-1, x => x != pixel))
                    {
                        m_out.Write ((byte)0);
                        m_out.Write ((byte)pos.Length);
                    }
                    else
                    {
                        m_out.Write ((byte)1);
                        m_out.Write ((byte)pos.Length);
                        m_out.Write ((byte)1);
                    }
                }
                else if (1 == pos.Length)
                {
                    if (pos.Offset < 0x100)
                    {
                        m_out.Write ((byte)3);
                        m_out.Write ((byte)pos.Offset);
                    }
                    else
                    {
                        m_out.Write ((byte)4);
                        m_out.Write (pos.Offset);
                    }
                }
                else if (pos.Offset < 0x100)
                {
                    m_out.Write ((byte)1);
                    m_out.Write ((byte)pos.Length);
                    m_out.Write ((byte)pos.Offset);
                }
                else
                {
                    m_out.Write ((byte)2);
                    m_out.Write ((byte)pos.Length);
                    m_out.Write (pos.Offset);
                }
            }

            #region IDisposable Members
            bool disposed = false;

            public void Dispose ()
            {
                Dispose (true);
                GC.SuppressFinalize (this);
            }

            protected virtual void Dispose (bool disposing)
            {
                if (!disposed)
                {
                    if (disposing)
                    {
                        m_out.Dispose();
                    }
                    disposed = true;
                }
            }
            #endregion
        }
    }

    internal class GgdMetaData : ImageMetaData
    {
        public uint HeaderSize;
        public uint BitmapSize;
        public bool Flipped;
    }

    [Export(typeof(ImageFormat))]
    public class DrgIndexedFormat : ImageFormat
    {
        public override string         Tag { get { return "GGD"; } }
        public override string Description { get { return "Digital Romance System indexed image format"; } }
        public override uint     Signature { get { return ~0x47363532u; } } // '256G'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            using (var input = new ArcView.Reader (stream))
            {
                input.ReadUInt32();
                var info = new GgdMetaData { BPP = 8 };
                info.HeaderSize = input.ReadUInt32();
                info.Width = input.ReadUInt32();
                int height = input.ReadInt32();
                if (height < 0)
                {
                    height = -height;
                    info.Flipped = true;
                }
                info.Height = (uint)height;
                input.ReadInt64();
                info.BitmapSize = input.ReadUInt32();
                return info;
            }
        }

        public override ImageData Read (Stream file, ImageMetaData info)
        {
            var meta = info as GgdMetaData;
            if (null == meta)
                throw new ArgumentException ("DrgIndexedFormat.Read should be supplied with GgdMetaData", "info");
            file.Position = meta.HeaderSize + 4;
            var palette_data = new byte[0x400];
            if (palette_data.Length != file.Read (palette_data, 0, palette_data.Length))
                throw new InvalidFormatException();
            var colors = new Color[256];
            for (int i = 0; i < 256; ++i)
            {
                colors[i] = Color.FromRgb (palette_data[i*4+2], palette_data[i*4+1], palette_data[i*4]);
            }
            file.Seek (4, SeekOrigin.Current);
            int input_size = (int)(file.Length - file.Position);
            using (var reader = new LzssReader (file, input_size, (int)meta.BitmapSize))
            {
                reader.Unpack();
                var palette = new BitmapPalette (colors);
                return ImageData.Create (info, PixelFormats.Indexed8, palette, reader.Data);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("DrgIndexedFormat.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class GgaFormat : ImageFormat
    {
        public override string         Tag { get { return "GG2"; } }
        public override string Description { get { return "IKURA GDL image format"; } }
        public override uint     Signature { get { return 0x30414747u; } } // 'GGA0'

        public GgaFormat ()
        {
            Extensions = new string[] { "gg1", "gg2", "gg3", "gg0" };
        }

        internal class GgaMetaData : ImageMetaData
        {
            public uint Flags;
            public uint HeaderSize;
            public uint CompSize;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("GgaFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[24];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            if (!Binary.AsciiEqual (header, "GGA00000"))
                return null;
            return new GgaMetaData {
                Width  = LittleEndian.ToUInt16 (header, 8),
                Height = LittleEndian.ToUInt16 (header, 10),
                BPP    = header[14],
                Flags  = header[15],
                HeaderSize = LittleEndian.ToUInt32 (header, 16),
                CompSize = LittleEndian.ToUInt32 (header, 20)
            };
        }

        public override ImageData Read (Stream file, ImageMetaData info)
        {
            var meta = info as GgaMetaData;
            if (null == meta)
                throw new ArgumentException ("GgaFormat.Read should be supplied with GgaMetaData", "info");
            file.Position = meta.HeaderSize;
            var pixel_data = DecodeStream (file, meta);
            PixelFormat format = 0 == (meta.Flags & 1) ? PixelFormats.Bgr32 : PixelFormats.Bgra32;
            return ImageData.Create (info, format, null, pixel_data);
        }

        byte[] DecodeStream (Stream file, GgaMetaData meta)
        {
            int dst = 0;
            var output = new byte[meta.Width*meta.Height*4];
            using (var input = new BinaryReader (file, Encoding.ASCII, true))
            {
                var end_pos = input.BaseStream.Position + meta.CompSize;
                while (input.BaseStream.Position < end_pos)
                {
                    int code = input.ReadByte();
                    switch (code)
                    {
                    case 0:
                        {
                            int src = dst - 4;
                            int count = input.ReadByte() * 4;
                            Binary.CopyOverlapped (output, src, dst, count);
                            dst += count;
                            break;
                        }
                    case 1:
                        {
                            int src = dst - 4;
                            int count = input.ReadUInt16() * 4;
                            Binary.CopyOverlapped (output, src, dst, count);
                            dst += count;
                            break;
                        }
                    case 2:
                        {
                            int l = input.ReadByte();
                            int src = dst - (l << 2);
                            System.Buffer.BlockCopy (output, src, output, dst, 4);
                            dst += 4;
                            break;
                        }
                    case 3:
                        {
                            int l = input.ReadUInt16();
                            int src = dst - (l << 2);
                            System.Buffer.BlockCopy (output, src, output, dst, 4);
                            dst += 4;
                            break;
                        }
                    case 4:
                        {
                            int l = input.ReadByte();
                            int src = dst - (l << 2);
                            int count = input.ReadByte() * 4;
                            Binary.CopyOverlapped (output, src, dst, count);
                            dst += count;
                            break;
                        }
                    case 5:
                        {
                            int l = input.ReadByte();
                            int src = dst - (l << 2);
                            int count = input.ReadUInt16() * 4;
                            Binary.CopyOverlapped (output, src, dst, count);
                            dst += count;
                            break;
                        }
                    case 6:
                        {
                            int l = input.ReadUInt16();
                            int src = dst - (l << 2);
                            int count = input.ReadByte() * 4;
                            Binary.CopyOverlapped (output, src, dst, count);
                            dst += count;
                            break;
                        }
                    case 7:
                        {
                            int l = input.ReadUInt16();
                            int src = dst - (l << 2);
                            int count = input.ReadUInt16() * 4;
                            Binary.CopyOverlapped (output, src, dst, count);
                            dst += count;
                            break;
                        }
                    case 8:
                        {
                            System.Buffer.BlockCopy (output, dst-4, output, dst, 4);
                            dst += 4;
                            break;
                        }
                    case 9:
                        {
                            int src = dst - (int)meta.Width * 4;
                            System.Buffer.BlockCopy (output, src, output, dst, 4);
                            dst += 4;
                            break;
                        }
                    case 0x0a:
                        {
                            int src = dst - ((int)meta.Width * 4 + 4);
                            System.Buffer.BlockCopy (output, src, output, dst, 4);
                            dst += 4;
                            break;
                        }
                    case 0x0b:
                        {
                            int src = dst - ((int)meta.Width * 4 - 4);
                            System.Buffer.BlockCopy (output, src, output, dst, 4);
                            dst += 4;
                            break;
                        }
                    default:
                        {
                            int count = (code-11)*4;
                            if (count != input.Read (output, dst, count))
                                throw new InvalidFormatException ("Unexpected end of input");
                            dst += count;
                            break;
                        }
                    }
                }
                return output;
            }
        }
    }
}
