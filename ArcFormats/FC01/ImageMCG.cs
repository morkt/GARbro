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
using System.IO;
using System.Windows.Media;
using GameRes.Formats.Properties;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.FC01
{
    internal class McgMetaData : ImageMetaData
    {
        public int DataOffset;
        public int PackedSize;
        public int Version;
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

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            byte[] header = new byte[0x40];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            if (header[5] != '.')
                return null;
            int version = header[4] * 100 + header[6] * 10 + header[7] - 0x14D0;
            if (version != 200 && version != 101)
                throw new NotSupportedException ("Not supported MCG format version");
            int header_size = LittleEndian.ToInt32 (header, 0x10);
            if (header_size < 0x40)
                return null;
            int bpp = LittleEndian.ToInt32 (header, 0x24);
            if (24 != bpp)
                throw new NotSupportedException ("Not supported MCG image bitdepth");
            return new McgMetaData
            {
                Width = LittleEndian.ToUInt32 (header, 0x1c),
                Height = LittleEndian.ToUInt32 (header, 0x20),
                OffsetX = LittleEndian.ToInt32 (header, 0x14),
                OffsetY = LittleEndian.ToInt32 (header, 0x18),
                BPP = bpp,
                DataOffset = header_size,
                PackedSize = LittleEndian.ToInt32 (header, 0x38) - header_size,
                Version = version,
            };
        }

        // cache key value so that dialog does not pop up on every file accessed.
        byte? LastKey = null;

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = (McgMetaData)info;
            byte key = Settings.Default.MCGLastKey;
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
            return ImageData.Create (info, PixelFormats.Bgr24, null, reader.Data, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("McgFormat.Write not implemented");
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new McgOptions { Key = Settings.Default.MCGLastKey };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            var w = widget as GUI.WidgetMCG;
            if (null != w)
                Settings.Default.MCGLastKey = w.GetKey();
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
        int     m_version;

        public byte[] Data { get { return m_output; } }
        public int  Stride { get; private set; }
        public byte    Key { get { return m_key; } }

        public McgDecoder (Stream input, McgMetaData info, byte key)
        {
            input.Position = info.DataOffset;
            m_input = new byte[info.PackedSize];
            if (m_input.Length != input.Read (m_input, 0, m_input.Length))
                throw new InvalidFormatException ("Unexpected end of file");
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            m_pixels = m_width*m_height;
            m_key = key;
            m_version = info.Version;
            Stride = 3 * m_width;
            if (101 == m_version)
                Stride = (Stride + 3) & -4;
        }

        static readonly byte[] ChannelOrder = { 1, 0, 2 };

        public void Unpack ()
        {
            if (200 == m_version)
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
                for (int key = 1; key < 256; ++key)
                {
                    var copy = m_input.Clone() as byte[];
                    MrgOpener.Decrypt (copy, 0, copy.Length-1, (byte)key);
                    using (var input = new MemoryStream (copy))
                    using (var lzss = new MrgLzssReader (input, m_input.Length, Stride * m_height))
                    {
                        lzss.Unpack();
                        if (input.Length - input.Position <= 1)
                        {
                            m_output = lzss.Data;
                            m_key = (byte)key;
                            return;
                        }
                    }
                }
            }
#endif
            using (var input = new MemoryStream (m_input))
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
                Settings.Default.MCGLastKey = m_key;
                return;
            }
            while (m_key != Settings.Default.MCGLastKey);
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
