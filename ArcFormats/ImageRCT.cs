//! \file       ImageRCT.cs
//! \date       Fri Aug 01 11:36:31 2014
//! \brief      RCT/RC8 image format implementation.
//
// Copyright (C) 2014 by morkt
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
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Formats.Strings;

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

    [Export(typeof(ImageFormat))]
    public class RctFormat : ImageFormat
    {
        public override string Tag { get { return "RCT"; } }
        public override string Description { get { return "Majiro game engine RGB image format"; } }
        public override uint Signature { get { return 0x9a925a98; } }

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

        public override ImageData Read (Stream file, ImageMetaData info)
        {
            var meta = info as RctMetaData;
            if (null == meta)
                throw new ArgumentException ("RctFormat.Read should be supplied with RctMetaData", "info");
            if (meta.AddSize > 0)
            {
                Trace.WriteLine (string.Format ("{0} bytes of additional data", meta.AddSize), "RctFormat.Read");
                file.Position = meta.DataOffset;
                byte[] data = new byte[meta.AddSize];
                if (data.Length != file.Read (data, 0, data.Length))
                    return null;
            }
            file.Position = meta.DataOffset + meta.AddSize;
            if (meta.IsEncrypted)
            {
                throw new NotImplementedException ("RCT image decryption is not implemented");
            }
            using (var reader = new Reader (file, meta))
            {
                reader.Unpack();
                byte[] pixels = reader.Data;
                var bitmap = BitmapSource.Create ((int)meta.Width, (int)meta.Height, 96, 96,
                    PixelFormats.Bgr24, null, pixels, (int)meta.Width*3);
                bitmap.Freeze();
                return new ImageData (bitmap, meta);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("RctFormat.Write is not implemented.");
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

            private static readonly sbyte[] ShiftTable = new sbyte[] {
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
                        CopyOverlapped (m_data, data_pos+shift, data_pos, count);
                        data_pos += count;
                    }
                }
            }

            static internal void CopyOverlapped (byte[] data, int src, int dst, int count)
            {
                int preceding = dst-src;
                while (count > 0)
                {
                    if (preceding > count)
                        preceding = count;
                    Array.Copy (data, src, data, dst, preceding);
                    src = dst;
                    dst += preceding;
                    count -= preceding;
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
                byte[] pixels = reader.Data;
                var palette = new BitmapPalette (reader.Palette);
                var bitmap = BitmapSource.Create ((int)info.Width, (int)info.Height, 96, 96,
                    PixelFormats.Indexed8, palette, pixels, (int)info.Width);
                bitmap.Freeze();
                return new ImageData (bitmap, info);
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
                        RctFormat.Reader.CopyOverlapped (m_data, data_pos+shift, data_pos, count);
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
