//! \file       ImageMCG.cs
//! \date       Mon Jul 13 17:58:33 2015
//! \brief      F&C Co. image format.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Formats.Strings;

namespace GameRes.Formats.FC01
{
    internal class McgMetaData : ImageMetaData
    {
        public int DataOffset;
        public int PackedSize;
        public int Version;
        public int ChannelsCount;
    }

    internal class McgOptions : ResourceOptions
    {
        public byte Key;
    }

    [Serializable]
    public class McgScheme : ResourceScheme
    {
        public Dictionary<string, byte> KnownKeys;
    }

    [Export(typeof(ImageFormat))]
    public class McgFormat : ImageFormat
    {
        public override string         Tag { get { return "MCG"; } }
        public override string Description { get { return "F&C Co. image format"; } }
        public override uint     Signature { get { return 0x2047434D; } } // 'MCG'

        public static Dictionary<string, byte> KnownKeys = new Dictionary<string, byte>();

        public override ResourceScheme Scheme
        {
            get { return new McgScheme { KnownKeys = KnownKeys }; }
            set { KnownKeys = ((McgScheme)value).KnownKeys; }
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x40);
            if (header[5] != '.')
                return null;
            int version = header[4] * 100 + header[6] * 10 + header[7] - 0x14D0;
            if (version != 200 && version != 101)
                throw new NotSupportedException ("Not supported MCG format version");
            int header_size = header.ToInt32 (0x10);
            if (header_size < 0x40)
                return null;
            int bpp = header.ToInt32 (0x24);
            if (24 != bpp && 8 != bpp && 16 != bpp)
                throw new NotSupportedException ("Not supported MCG image bitdepth");
            int packed_size = header.ToInt32 (0x38);
            return new McgMetaData
            {
                Width = header.ToUInt32 (0x1c),
                Height = header.ToUInt32 (0x20),
                OffsetX = header.ToInt32 (0x14),
                OffsetY = header.ToInt32 (0x18),
                BPP = bpp,
                DataOffset = header_size,
                PackedSize = packed_size,
                Version = version,
                ChannelsCount = header.ToInt32 (0x34),
            };
        }

        // cache key value so that dialog does not pop up on every file accessed.
        byte? LastKey = null;

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (McgMetaData)info;
            byte key = Properties.Settings.Default.MCGLastKey;
            if (101 == meta.Version)
            {
                if (null == LastKey)
                {
                    var options = Query<McgOptions> (arcStrings.ArcImageEncrypted);
                    key = options.Key;
                }
                else
                    key = LastKey.Value;
            }
            var reader = new McgDecoder (stream, meta, key);
            reader.Unpack();
            if (reader.Key != 0)
                LastKey = reader.Key;
            return ImageData.Create (info, reader.Format, reader.Palette, reader.Data, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("McgFormat.Write not implemented");
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new McgOptions { Key = Properties.Settings.Default.MCGLastKey };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            var w = widget as GUI.WidgetMCG;
            if (null != w)
                Properties.Settings.Default.MCGLastKey = w.GetKey ();
            return GetDefaultOptions();
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetMCG();
        }
    }

    // mcg decompression // graphic.unt @ 100047B0

    internal class McgDecoder
    {
        byte[]  m_input;
        byte[]  m_output;
        int     m_width;
        int     m_height;
        int     m_pixels;
        byte    m_key;
        IBinaryStream   m_file;
        McgMetaData     m_info;

        public byte              Key { get { return m_key; } }
        public byte[]           Data { get { return m_output; } }
        public int            Stride { get; private set; }
        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }

        public McgDecoder (IBinaryStream input, McgMetaData info, byte key)
        {
            m_file = input;
            m_info = info;
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            m_pixels = m_width*m_height;
            m_key = key;
            Stride = m_width * m_info.BPP / 8;
            if (101 == m_info.Version)
                Stride = (Stride + 3) & -4;
            if (24 == m_info.BPP)
                Format = PixelFormats.Bgr24;
            else if (16 == m_info.BPP)
                Format = PixelFormats.Bgr555;
            else if (8 == m_info.BPP)
                Format = PixelFormats.Indexed8;
            else
                throw new InvalidFormatException();
        }

        static readonly byte[] ChannelOrder = { 1, 0, 2 };

        public void Unpack ()
        {
            m_file.Position = m_info.DataOffset;
            int input_size = m_info.PackedSize;
            if (0 == input_size)
                input_size = (int)m_file.Length;
            input_size -= m_info.DataOffset;
            if (8 == m_info.BPP)
            {
                Palette = ImageFormat.ReadPalette (m_file.AsStream);
                input_size -= 0x400;
            }
            else if (m_info.ChannelsCount > 0)
            {
                var masks = new int[m_info.ChannelsCount];
                for (int i = 0; i < masks.Length; ++i)
                    masks[i] = m_file.ReadInt32();
                if (16 == m_info.BPP && 3 == m_info.ChannelsCount)
                {
                    if (0x7E0 == masks[1])
                        Format = PixelFormats.Bgr565;
                }
                input_size -= m_info.ChannelsCount * 4;
            }
            m_input = m_file.ReadBytes (input_size);
            if (m_input.Length != input_size)
                throw new InvalidFormatException ("Unexpected end of file");
            if (200 == m_info.Version)
                UnpackV200();
            else
                UnpackV101();
        }

        void UnpackV101 ()
        {
            if (m_key != 0)
            {
                MrgOpener.Decrypt (m_input, 0, m_input.Length-1, m_key);
            }
#if DEBUG
            else // bruteforce key *in debug build only*
            {
                var copy = new byte[m_input.Length];
                for (int key = 1; key < 256; ++key)
                {
                    Buffer.BlockCopy (m_input, 0, copy, 0, m_input.Length);
                    MrgOpener.Decrypt (copy, 0, copy.Length-1, (byte)key);
                    using (var input = new BinMemoryStream (copy))
                    using (var lzss = new MrgLzssReader (input, copy.Length, Stride * m_height))
                    {
                        lzss.Unpack();
                        if (input.Length - input.Position <= 1)
                        {
                            Trace.WriteLine (string.Format ("Found matching key {0:X2}", key), "[MCG]");
                            m_output = lzss.Data;
                            m_key = (byte)key;
                            return;
                        }
                    }
                }
            }
#endif
            using (var input = new BinMemoryStream (m_input))
            using (var lzss = new MrgLzssReader (input, m_input.Length, Stride * m_height))
            {
                lzss.Unpack();
                // data remaining within input stream indicates invalid encryption key
                if (input.Length - input.Position > 1)
                {
                    m_key = 0;
                }
                m_output = lzss.Data;
            }
        }

        void UnpackV200 ()
        {
            m_output = new byte[m_pixels*3];
            var reader = new MrgDecoder (m_input, 0, (uint)m_pixels);
            do
            {
                reader.ResetKey (m_key);
                try
                {
                    for (int i = 0; i < 3; ++i)
                    {
                        reader.Unpack();
                        var plane = reader.Data;
                        int src = 0;
                        for (int j = ChannelOrder[i]; j < m_output.Length; j += 3)
                        {
                            m_output[j] = plane[src++];
                        }
                    }
//                    Trace.WriteLine (string.Format ("Found matching key {0:X2}", key), "[MCG]");
                }
                catch (InvalidFormatException)
                {
                    m_key++;
                    continue;
                }
                Transform();
                Properties.Settings.Default.MCGLastKey = m_key;
                return;
            }
            while (m_key != Properties.Settings.Default.MCGLastKey);
            throw new UnknownEncryptionScheme();
        }

        void Transform ()
        {
            int dst = 0;
            for (int y = m_height-1; y > 0; --y) // @@1a
            {
                for (int x = Stride-3; x > 0; --x) // @@1b
                {
                    int p0 = m_output[dst];
                    int py = m_output[dst+Stride] - p0;
                    int px = m_output[dst+3] - p0;
                    p0 = Math.Abs (px + py);
                    py = Math.Abs (py);
                    px = Math.Abs (px);
                    byte pv;
                    if (p0 >= px && py >= px)
                        pv = m_output[dst+Stride];
                    else if (p0 < py)
                        pv = m_output[dst];
                    else
                        pv = m_output[dst+3];

                    m_output[dst+Stride+3] += (byte)(pv + 0x80);
                    ++dst;
                }
                dst += 3;
            }
            dst = 0;
            for (uint i = 0; i < m_pixels; ++i)
            {
                sbyte b = -128;
                sbyte r = -128;
                b += (sbyte)m_output[dst];
                r += (sbyte)m_output[dst+2];
                int g = m_output[dst+1] - ((b + r) >> 2);
                m_output[dst++] = (byte)(b + g);
                m_output[dst++] = (byte)g;
                m_output[dst++] = (byte)(r + g);
            }
        }
    }
}
