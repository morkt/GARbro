//! \file       ImageRCT.cs
//! \date       Fri Aug 01 11:36:31 2014
//! \brief      RCT/RC8 image format implementation.
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
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Formats.Properties;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.Majiro
{
    internal class RctMetaData : ImageMetaData
    {
        public int  Version;
        public bool IsEncrypted;
        public uint DataOffset;
        public uint DataSize;
        public uint AddSize;
    }

    internal class RctOptions : ResourceOptions
    {
        public string Password;
    }

    [Serializable]
    public class RctScheme : ResourceScheme
    {
        public Dictionary<string, string> KnownKeys;
    }

    [Export(typeof(ImageFormat))]
    public class RctFormat : ImageFormat
    {
        public override string         Tag { get { return "RCT"; } }
        public override string Description { get { return "Majiro game engine RGB image format"; } }
        public override uint     Signature { get { return 0x9a925a98; } }

        public bool OverlayFrames = true;

        public static Dictionary<string, string> KnownKeys = new Dictionary<string, string>();

        public override ResourceScheme Scheme
        {
            get { return new RctScheme { KnownKeys = KnownKeys }; }
            set { KnownKeys = ((RctScheme)value).KnownKeys; }
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            stream.Position = 4;
            int id = stream.ReadByte();
            if (0x54 != id)
                return null;
            int encryption = stream.ReadByte();
            if (encryption != 0x43 && encryption != 0x53)
                return null;
            bool is_encrypted = 0x53 == encryption;
            int version = stream.ReadByte();
            if (0x30 != version)
                return null;
            version = stream.ReadByte() - 0x30;
            if (version != 0 && version != 1)
                return null;

            using (var reader = new BinaryReader (stream, Encoding.ASCII, true))
            {
                uint width = reader.ReadUInt32();
                uint height = reader.ReadUInt32();
                uint data_size = reader.ReadUInt32();
                uint additional_size = 0;
                if (1 == version)
                {
                    additional_size = reader.ReadUInt16();
                }
                if (width > 0x8000 || height > 0x8000)
                    return null;

                return new RctMetaData
                {
                    Width   = width,
                    Height  = height,
                    OffsetX = 0,
                    OffsetY = 0,
                    BPP     = 24,
                    Version = version,
                    IsEncrypted = is_encrypted,
                    DataOffset = (uint)stream.Position,
                    DataSize = data_size,
                    AddSize = additional_size,
                };
            }
        }

        byte[] Key = null;

        public override ImageData Read (Stream file, ImageMetaData info)
        {
            var meta = info as RctMetaData;
            if (null == meta)
                throw new ArgumentException ("RctFormat.Read should be supplied with RctMetaData", "info");

            byte[] base_image = null;
            if (meta.FileName != null && meta.AddSize > 0 && OverlayFrames)
                base_image = ReadBaseImage (file, meta);

            var pixels = ReadPixelsData (file, meta);
            if (base_image != null)
                pixels = CombineImage (base_image, pixels);
            return ImageData.Create (meta, PixelFormats.Bgr24, null, pixels, (int)meta.Width*3);
        }

        byte[] CombineImage (byte[] base_image, byte[] overlay)
        {
            for (int i = 2; i < base_image.Length; i += 3)
            {
                if (0 == overlay[i-2] && 0 == overlay[i-1] && 0xff == overlay[i])
                {
                    overlay[i-2] = base_image[i-2];
                    overlay[i-1] = base_image[i-1];
                    overlay[i]   = base_image[i];
                }
            }
            return overlay;
        }

        byte[] ReadPixelsData (Stream file, RctMetaData meta)
        {
            file.Position = meta.DataOffset + meta.AddSize;
            if (meta.IsEncrypted)
                file = OpenEncryptedStream (file, meta.DataSize);
            try
            {
                using (var reader = new Reader (file, meta))
                {
                    reader.Unpack();
                    return reader.Data;
                }
            }
            catch
            {
                if (meta.IsEncrypted)
                    Key = null; // probably incorrect encryption scheme caused exception, reset key
                throw;
            }
        }

        byte[] ReadBaseImage (Stream file, RctMetaData meta)
        {
            file.Position = meta.DataOffset;
            byte[] name_bin = new byte[meta.AddSize];
            if (name_bin.Length != file.Read (name_bin, 0, name_bin.Length))
                throw new EndOfStreamException();
            try
            {
                string name = Encodings.cp932.GetString (name_bin, 0, name_bin.Length-1);
                string dir_name = Path.GetDirectoryName (meta.FileName);
                name = VFS.CombinePath (dir_name, name);
                if (VFS.FileExists (name))
                {
                    using (var base_file = VFS.OpenSeekableStream (name))
                    {
                        var base_info = ReadMetaData (base_file) as RctMetaData;
                        if (null != base_info && 0 == base_info.AddSize
                            && meta.Width == base_info.Width && meta.Height == base_info.Height)
                        {
                            base_info.FileName = name;
                            return ReadPixelsData (base_file, base_info);
                        }
                    }
                }
            }
            catch { /* ignore baseline image read errors */ }
            return null;
        }

        Stream OpenEncryptedStream (Stream file, uint data_size)
        {
            if (null == Key)
            {
                var password = QueryPassword();
                if (string.IsNullOrEmpty (password))
                    throw new UnknownEncryptionScheme();
                Key = InitDecryptionKey (password);
            }
            byte[] data = new byte[data_size];
            if (data.Length != file.Read (data, 0, data.Length))
                throw new EndOfStreamException();

            for (int i = 0; i < data.Length; ++i)
            {
                data[i] ^= Key[i & 0x3FF];
            }
            return new MemoryStream (data);
        }

        private byte[] InitDecryptionKey (string password)
        {
            byte[] bin_pass = Encodings.cp932.GetBytes (password);
            uint crc32 = Crc32.Compute (bin_pass, 0, bin_pass.Length);
            byte[] key_table = new byte[0x400];
            unsafe
            {
                fixed (byte* key_ptr = key_table)
                {
                    uint* key32 = (uint*)key_ptr;
                    for (int i = 0; i < 0x100; ++i)
                        *key32++ = crc32 ^ Crc32.Table[(i + crc32) & 0xFF];
                }
            }
            return key_table;
        }

        private string QueryPassword ()
        {
            var options = Query<RctOptions> (arcStrings.RCTNotice);
            return options.Password;
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new RctOptions { Password = Settings.Default.RCTPassword };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            var w = widget as GUI.WidgetRCT;
            if (null != w)
                Settings.Default.RCTPassword = w.Password.Text;
            return GetDefaultOptions();
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetRCT();
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var writer = new Writer (file))
                writer.Pack (image.Bitmap);
        }

        internal class Writer : IDisposable
        {
            BinaryWriter    m_out;
            uint[]          m_input;
            int             m_width;
            int             m_height;

            int[]           m_shift_table = new int[32];

            const int MaxThunkSize = 0xffff + 0x7f;
            const int MaxMatchSize = 0xffff;

            struct ChunkPosition
            {
                public ushort Offset;
                public ushort Length;
            }

            public Writer (Stream output)
            {
                m_out = new BinaryWriter (output, Encoding.ASCII, true);
            }

            void PrepareInput (BitmapSource bitmap)
            {
                m_width  = bitmap.PixelWidth;
                m_height = bitmap.PixelHeight;
                int pixels = m_width*m_height;
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
                        bitmap.CopyPixels (Int32Rect.Empty, (IntPtr)buffer, pixels*4, m_width*4);
                    }
                }
                InitShiftTable (m_width);
            }

            void InitShiftTable (int width)
            {
                for (int i = 0; i < 32; ++i)
                {
                    int shift = Reader.ShiftTable[i];
                    int shift_row = shift & 0x0f;
                    shift >>= 4;
                    shift_row *= width;
                    shift -= shift_row;
                    m_shift_table[i] = shift;
                }
            }

            List<byte>  m_buffer = new List<byte>();
            int         m_buffer_size;

            public void Pack (BitmapSource bitmap)
            {
                PrepareInput (bitmap);
                long data_offset = 0x14;
                m_out.BaseStream.Position = data_offset;
                uint pixel = m_input[0];
                m_out.Write ((byte)pixel);
                m_out.Write ((byte)(pixel >> 8));
                m_out.Write ((byte)(pixel >> 16));

                m_buffer.Clear();
                m_buffer_size = 0;
                int last    = m_input.Length;
                int current = 1;
                while (current != last)
                {
                    var chunk_pos = FindLongest (current, last);
                    if (chunk_pos.Length > 0)
                    {
                        Flush();
                        WritePos (chunk_pos);
                        current += chunk_pos.Length;
                    }
                    else
                    {
                        WritePixel (m_input[current++]);
                    }
                }
                Flush();
                var data_size = m_out.BaseStream.Position - data_offset;
                m_out.BaseStream.Position = 0;
                WriteHeader ((uint)data_size);
            }

            void WriteHeader (uint data_size)
            {
                m_out.Write (0x9a925a98u);
                m_out.Write (0x30304354u);
                m_out.Write (m_width);
                m_out.Write (m_height);
                m_out.Write (data_size);
            }

            void WritePixel (uint pixel)
            {
                if (MaxThunkSize == m_buffer_size)
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
                    if (m_buffer_size > 0x7f)
                    {
                        m_out.Write ((byte)0x7f);
                        m_out.Write ((ushort)(m_buffer_size-0x80));
                    }
                    else
                        m_out.Write ((byte)(m_buffer_size-1));
                    foreach (var b in m_buffer)
                        m_out.Write (b);
                    m_buffer.Clear();
                    m_buffer_size = 0;
                }
            }

            ChunkPosition FindLongest (int buf_begin, int buf_end)
            {
                buf_end = Math.Min (buf_begin + MaxMatchSize, buf_end);
                ChunkPosition pos = new ChunkPosition { Offset = 0, Length = 0 };
                for (int i = 0; i < 32; ++i)
                {
                    int offset = buf_begin + m_shift_table[i];
                    if (offset < 0)
                        continue;
                    if (m_input[offset] != m_input[buf_begin])
                        continue;
                    var last = Mismatch (buf_begin+1, buf_end, offset+1);
                    int weight = last - offset;
                    if (weight > pos.Length)
                    {
                        pos.Offset = (ushort)i;
                        pos.Length = (ushort)weight;
                    }
                }
                return pos;
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

            void WritePos (ChunkPosition pos)
            {
                int code = (pos.Offset << 2) | 0x80;
                if (pos.Length > 3)
                    code |= 3;
                else
                    code |= pos.Length - 1;
                m_out.Write ((byte)code);
                if (pos.Length > 3)
                    m_out.Write ((ushort)(pos.Length - 4));
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

        internal class Reader : IDisposable
        {
            private BinaryReader    m_input;
            private uint            m_width;
            private byte[]          m_data;

            public byte[] Data { get { return m_data; } }

            public Reader (Stream file, RctMetaData info)
            {
                m_width = info.Width;
                m_data = new byte[m_width * info.Height * 3];
                m_input = new BinaryReader (file, Encoding.ASCII, true);
            }

            internal static readonly sbyte[] ShiftTable = new sbyte[] {
                -16, -32, -48, -64, -80, -96,
                49, 33, 17, 1, -15, -31, -47,
                50, 34, 18, 2, -14, -30, -46,
                51, 35, 19, 3, -13, -29, -45,
                36, 20, 4, -12, -28,
            };

            public void Unpack ()
            {
                int pixels_remaining = m_data.Length;
                int data_pos = 0;
                int eax = 0;
                while (pixels_remaining > 0)
                {
                    int count = eax*3 + 3;
                    if (count > pixels_remaining)
                        throw new InvalidFormatException();
                    pixels_remaining -= count;
                    if (count != m_input.Read (m_data, data_pos, count))
                        throw new InvalidFormatException();
                    data_pos += count;

                    while (pixels_remaining > 0)
                    {
                        eax = m_input.ReadByte();
                        if (0 == (eax & 0x80))
                        {
                            if (0x7f == eax)
                                eax += m_input.ReadUInt16();
                            break;
                        }
                        int shift_index = eax >> 2;
                        eax &= 3;
                        if (3 == eax)
                            eax += m_input.ReadUInt16();

                        count = eax*3 + 3;
                        if (pixels_remaining < count)
                            throw new InvalidFormatException();
                        pixels_remaining -= count;
                        int shift = ShiftTable[shift_index & 0x1f];
                        int shift_row = shift & 0x0f;
                        shift >>= 4;
                        shift_row *= (int)m_width;
                        shift -= shift_row;
                        shift *= 3;
                        if (shift >= 0 || data_pos+shift < 0)
                            throw new InvalidFormatException();
                        Binary.CopyOverlapped (m_data, data_pos+shift, data_pos, count);
                        data_pos += count;
                    }
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
                        m_input.Dispose();
                    m_input = null;
                    m_data = null;
                    disposed = true;
                }
            }
            #endregion
        }
    }

    [Export(typeof(ImageFormat))]
    public class Rc8Format : ImageFormat
    {
        public override string Tag { get { return "RC8"; } }
        public override string Description { get { return "Majiro game engine indexed image format"; } }
        public override uint Signature { get { return 0x9a925a98; } }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            stream.Position = 4;
            int id = stream.ReadByte();
            if (0x38 != id)
                return null;
            id = stream.ReadByte();
            if (0x5f != id)
                return null;
            int version = stream.ReadByte();
            if (0x30 != version)
                return null;
            version = stream.ReadByte() - 0x30;
            if (version != 0)
                return null;

            using (var reader = new BinaryReader (stream, Encoding.ASCII, true))
            {
                uint width = reader.ReadUInt32();
                uint height = reader.ReadUInt32();
                if (width > 0x8000 || height > 0x8000)
                    return null;
                return new ImageMetaData
                {
                    Width   = width,
                    Height  = height,
                    OffsetX = 0,
                    OffsetY = 0,
                    BPP     = 8,
                };
            }
        }

        public override ImageData Read (Stream file, ImageMetaData info)
        {
            file.Position = 0x14;
            using (var reader = new Reader (file, info))
            {
                reader.Unpack();
                var palette = new BitmapPalette (reader.Palette);
                return ImageData.Create (info, PixelFormats.Indexed8, palette, reader.Data, (int)info.Width);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("Rc8Format.Write is not implemented.");
        }

        internal class Reader : IDisposable
        {
            private BinaryReader    m_input;
            private uint            m_width;
            private Color[]         m_palette;
            private byte[]          m_data;

            public Color[] Palette { get { return m_palette; } }
            public byte[]     Data { get { return m_data; } }

            public Reader (Stream file, ImageMetaData info)
            {
                m_width = info.Width;
                var palette_data = new byte[0x300];
                if (palette_data.Length != file.Read (palette_data, 0, palette_data.Length))
                    throw new InvalidFormatException();
                m_palette = new Color[0x100];
                for (int i = 0; i < 0x100; ++i)
                {
                    m_palette[i] = Color.FromRgb (palette_data[i*3], palette_data[i*3+1], palette_data[i*3+2]);
                }
                m_data = new byte[m_width * info.Height];
                m_input = new BinaryReader (file, Encoding.ASCII, true);
            }

            private static readonly sbyte[] ShiftTable = new sbyte[] {
                -16, -32, -48, -64,
                49, 33, 17, 1, -15, -31, -47,
                34, 18, 2, -14, -30,
            };

            public void Unpack ()
            {
                int data_pos = 0;
                int eax = 0;
                int pixels_remaining = m_data.Length;
                while (pixels_remaining > 0)
                {
                    int count = eax + 1;
                    if (count > pixels_remaining)
                        throw new InvalidFormatException();
                    pixels_remaining -= count;

                    if (count != m_input.Read (m_data, data_pos, count))
                        throw new InvalidFormatException();
                    data_pos += count;

                    while (pixels_remaining > 0)
                    {
                        eax = m_input.ReadByte();
                        if (0 == (eax & 0x80))
                        {
                            if (0x7f == eax)
                                eax += m_input.ReadUInt16();
                            break;
                        }
                        int shift_index = eax >> 3;
                        eax &= 7;
                        if (7 == eax)
                            eax += m_input.ReadUInt16();

                        count = eax + 3;
                        if (pixels_remaining < count)
                            throw new InvalidFormatException();
                        pixels_remaining -= count;
                        int shift = ShiftTable[shift_index & 0x0f];
                        int shift_row = shift & 0x0f;
                        shift >>= 4;
                        shift_row *= (int)m_width;
                        shift -= shift_row;
                        if (shift >= 0 || data_pos+shift < 0)
                            throw new InvalidFormatException();
                        Binary.CopyOverlapped (m_data, data_pos+shift, data_pos, count);
                        data_pos += count;
                    }
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
                        m_input.Dispose();
                    m_input = null;
                    m_data = null;
                    disposed = true;
                }
            }
            #endregion
        }
    }
}
