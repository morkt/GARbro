//! \file       ImageRCT.cs
//! \date       Fri Aug 01 11:36:31 2014
//! \brief      RCT image format implementation.
//
// Copyright (C) 2014-2017 by morkt
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
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.Majiro
{
    internal class RctMetaData : ImageMetaData
    {
        public int  Version;
        public bool IsEncrypted;
        public uint DataOffset;
        public int  DataSize;
        public int  BaseNameLength;
        public int  BaseRecursionDepth;
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
    public sealed class RctFormat : ImageFormat
    {
        public override string         Tag { get { return "RCT"; } }
        public override string Description { get { return "Majiro game engine RGB image format"; } }
        public override uint     Signature { get { return 0x9a925a98; } }
        public override bool      CanWrite { get { return true; } }

        public RctFormat ()
        {
            Settings = new[] { OverlayFrames, ApplyMask };
        }

        LocalResourceSetting OverlayFrames = new LocalResourceSetting ("RCTOverlayFrames");
        LocalResourceSetting ApplyMask = new LocalResourceSetting ("RCTApplyMask");

        public const int BaseRecursionLimit = 8;

        public static Dictionary<string, string> KnownKeys = new Dictionary<string, string>();

        public override ResourceScheme Scheme
        {
            get { return new RctScheme { KnownKeys = KnownKeys }; }
            set { KnownKeys = ((RctScheme)value).KnownKeys; }
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x14);
            if (header[4] != 'T')
                return null;
            int encryption = header[5];
            if (encryption != 'C' && encryption != 'S')
                return null;
            bool is_encrypted = 'S' == encryption;
            if (header[6] != '0')
                return null;
            int version = header[7] - '0';
            if (version != 0 && version != 1)
                return null;

            uint width = header.ToUInt32 (8);
            uint height = header.ToUInt32 (12);
            int data_size = header.ToInt32 (16);
            int additional_size = 0;
            if (1 == version)
            {
                additional_size = stream.ReadUInt16();
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
                BaseNameLength = additional_size,
            };
        }

        byte[] Key = null;

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (RctMetaData)info;
            var pixels = ReadPixelsData (file, meta);
            if (ApplyMask.Get<bool>())
            {
                var mask_name = Path.GetFileNameWithoutExtension (meta.FileName) + "_.rc8";
                mask_name = VFS.ChangeFileName (meta.FileName, mask_name);
                if (VFS.FileExists (mask_name))
                {
                    try
                    {
                        return ApplyMaskToImage (meta, pixels, mask_name);
                    }
                    catch { /* ignore mask read errors */ }
                }
            }
            return ImageData.Create (meta, PixelFormats.Bgr24, null, pixels, (int)meta.Width*3);
        }

        static readonly ResourceInstance<ImageFormat> s_rc8_format = new ResourceInstance<ImageFormat> ("RC8");

        ImageData ApplyMaskToImage (RctMetaData info, byte[] image, string mask_name)
        {
            using (var mask_file = VFS.OpenBinaryStream (mask_name))
            {
                var mask_info = s_rc8_format.Value.ReadMetaData (mask_file);
                if (null == mask_info
                    || info.Width != mask_info.Width || info.Height != mask_info.Height)
                    throw new InvalidFormatException();

                using (var reader = new Rc8Format.Reader (mask_file, mask_info))
                {
                    reader.Unpack();
                    var palette = reader.Palette;
                    int dst_stride = (int)info.Width * 4;
                    var pixels = new byte[dst_stride * (int)info.Height];
                    var alpha = reader.Data;
                    int a_src = 0;
                    int src = 0;
                    for (int dst = 0; dst < pixels.Length; dst += 4)
                    {
                        pixels[dst  ] = image[src++];
                        pixels[dst+1] = image[src++];
                        pixels[dst+2] = image[src++];
                        var color = palette[alpha[a_src++]];
                        pixels[dst+3] = (byte)~((color.B + color.G + color.R) / 3);
                    }
                    return ImageData.Create (info, PixelFormats.Bgra32, null, pixels, dst_stride);
                }
            }
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

        byte[] ReadPixelsData (IBinaryStream file, RctMetaData meta)
        {
            byte[] base_image = null;
            if (meta.FileName != null && meta.BaseNameLength > 0 && OverlayFrames.Get<bool>()
                && meta.BaseRecursionDepth < BaseRecursionLimit)
                base_image = ReadBaseImage (file, meta);

            file.Position = meta.DataOffset + meta.BaseNameLength;
            if (meta.IsEncrypted)
                file = OpenEncryptedStream (file, meta.DataSize);
            try
            {
                using (var reader = new Reader (file, meta))
                {
                    reader.Unpack();
                    if (base_image != null)
                        return CombineImage (base_image, reader.Data);
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

        byte[] ReadBaseImage (IBinaryStream file, RctMetaData meta)
        {
            try
            {
                file.Position = meta.DataOffset;
                var name = file.ReadCString (meta.BaseNameLength);
                string dir_name = VFS.GetDirectoryName (meta.FileName);
                name = VFS.CombinePath (dir_name, name);
                if (VFS.FileExists (name))
                {
                    using (var base_file = VFS.OpenBinaryStream (name))
                    {
                        var base_info = ReadMetaData (base_file) as RctMetaData;
                        if (null != base_info
                            && meta.Width == base_info.Width && meta.Height == base_info.Height)
                        {
                            base_info.BaseRecursionDepth = meta.BaseRecursionDepth + 1;
                            base_info.FileName = name;
                            return ReadPixelsData (base_file, base_info);
                        }
                    }
                }
            }
            catch { /* ignore baseline image read errors */ }
            return null;
        }

        IBinaryStream OpenEncryptedStream (IBinaryStream file, int data_size)
        {
            if (null == Key)
            {
                var password = QueryPassword();
                if (string.IsNullOrEmpty (password))
                    throw new UnknownEncryptionScheme();
                Key = InitDecryptionKey (password);
            }
            byte[] data = file.ReadBytes (data_size);
            if (data.Length != data_size)
                throw new EndOfStreamException();

            for (int i = 0; i < data.Length; ++i)
            {
                data[i] ^= Key[i & 0x3FF];
            }
            return new BinMemoryStream (data, file.Name);
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
            if (VFS.IsVirtual)
            {
                var password = FindImageKey();
                if (!string.IsNullOrEmpty (password))
                    return password;
            }
            var options = Query<RctOptions> (arcStrings.ArcImageEncrypted);
            return options.Password;
        }

        static readonly ResourceInstance<ArchiveFormat> s_Majiro = new ResourceInstance<ArchiveFormat> ("MAJIRO");

        private string FindImageKey ()
        {
            var arc_fs = VFS.Top as ArchiveFileSystem;
            if (null == arc_fs)
                return null;
            try
            {
                // look for "start.mjo" within "scenario.arc" archive
                var src_name = arc_fs.Source.File.Name;
                var scenario_arc_name = Path.Combine (Path.GetDirectoryName (src_name), "scenario.arc");
                if (!File.Exists (scenario_arc_name))
                    return null;
                byte[] script;
                using (var file = new ArcView (scenario_arc_name))
                using (var arc = s_Majiro.Value.TryOpen (file))
                {
                    if (null == arc)
                        return null;
                    var start_mjo = arc.Dir.First (e => e.Name == "start.mjo");
                    using (var mjo = arc.OpenEntry (start_mjo))
                    {
                        script = new byte[mjo.Length];
                        mjo.Read (script, 0, script.Length);
                    }
                }
                if (Binary.AsciiEqual (script, "MajiroObjX1.000"))
                    DecryptMjo (script);
                else if (!Binary.AsciiEqual (script, "MjPlainBytecode"))
                    return null;

                // locate key within start.mjo script
                int n = script.ToInt32 (0x18);
                for (int offset = 0x20 + n * 8; offset < script.Length - 4; ++offset)
                {
                    offset = Array.IndexOf<byte> (script, 1, offset);
                    if (-1 == offset)
                        break;
                    if (8 != script[offset+1])
                        continue;
                    int str_length = script.ToUInt16 (offset+2);
                    if (0 == str_length || str_length + 12 > script.Length - offset
                        || 0x0835 != script.ToUInt16 (offset+str_length+4)
                        || 0x7A7B6ED4 != script.ToUInt32 (offset+str_length+6))
                        continue;
                    offset += 4;
                    int end = Array.IndexOf<byte> (script, 0, offset, str_length);
                    if (-1 != end)
                        str_length = end - offset;
                    var password = Encodings.cp932.GetString (script, offset, str_length);
                    Trace.WriteLine (string.Format ("Found key in start.mjo [{0}]", password), "[RCT]");
                    return password;
                }
            }
            catch { /* ignore errors */ }
            return null;
        }

        private void DecryptMjo (byte[] data)
        {
            int offset = 0x1C + 8 * data.ToInt32 (0x18);
            if (offset < 0x1C || offset >= data.Length - 4)
                return;
            int count = data.ToInt32 (offset);
            offset += 4;
            if (count <= 0 || count > data.Length - offset)
                return;
            Debug.Assert (Crc32.Table.Length == 0x100);
            unsafe
            {
                fixed (uint* table = Crc32.Table)
                {
                    byte* key = (byte*)table;
                    for (int i = 0; i < count; ++i)
                        data[offset+i] ^= key[i & 0x3FF];
                }
            }
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new RctOptions { Password = Properties.Settings.Default.RCTPassword };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            var w = widget as GUI.WidgetRCT;
            if (null != w)
                Properties.Settings.Default.RCTPassword = w.Password.Text;
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

        internal sealed class Reader : IDisposable
        {
            private IBinaryStream   m_input;
            private uint            m_width;
            private byte[]          m_data;

            public byte[] Data { get { return m_data; } }

            public Reader (IBinaryStream file, RctMetaData info)
            {
                m_width = info.Width;
                m_data = new byte[m_width * info.Height * 3];
                m_input = file;
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
            public void Dispose ()
            {
                GC.SuppressFinalize (this);
            }
            #endregion
        }
    }
}
