//! \file       ImageDAT.cs
//! \date       2018 Jul 07
//! \brief      Studio Jikkenshitsu compressed image.
//
// Copyright (C) 2018 by morkt
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
using System.Windows.Media.Imaging;
using GameRes.Formats.Strings;

// [010719][Studio Jikkenshitsu] Shin Gekka Bijin ~Hitori Shizuka
// [030606][Studio Jikkenshitsu] Bias {biAs+}
// [031212][Studio Jikkenshitsu] Jam n' Limit

namespace GameRes.Formats.Jikkenshitsu
{
    internal class SpMetaData : ImageMetaData
    {
        public int  Flags;
        public int  Colors;
        public byte[] Key;

        public bool IsEncrypted { get { return (Flags & 8) != 0; } }
    }

    [Serializable]
    public class SjSchemeMap : ResourceScheme
    {
        public IDictionary<string, byte[]>  KnownSchemes;
    }

    internal class SjOptions : ResourceOptions
    {
        public byte[]   Key;
    }

    [Export(typeof(ImageFormat))]
    public class SpDatFormat : ImageFormat
    {
        public override string         Tag { get { return "DAT/SPEED"; } }
        public override string Description { get { return "Studio Jikkenshitsu image format"; } }
        public override uint     Signature { get { return 0; } }

        public SpDatFormat ()
        {
            Extensions = new string[] { /* "dat" */ };
            Signatures = new uint[] { 0x010003, 0x010007, 0x01000B, 0x010046, 0 };
        }

        byte[] DefaultKey = null;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x22);
            if (header.ToInt32 (4) != 0)
                return null;
            int flags = header.ToUInt16 (0);
            if ((flags & ~0xFF) != 0 || header[2] != 1)
                return null;
            var info = new SpMetaData {
                Width = header.ToUInt16 (0x16),
                Height = header.ToUInt16 (0x18),
                BPP = 8,
                Flags = flags,
                Colors = header.ToUInt16 (0x1E),
            };
            if (info.Width == 0 || info.Width > 0x2000 || info.Height == 0 || info.Height > 0x2000 ||
                info.Colors > 0x100)
                return null;
            if (info.IsEncrypted)
            {
                if (null == DefaultKey)
                {
                    DefaultKey = QueryKey (file.Name);
                    if (null == DefaultKey)
                        return null;
                }
                info.Key = DefaultKey;
            }
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new SpReader (file, (SpMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.CreateFlipped (info, reader.Format, reader.Palette, pixels, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("SpDatFormat.Write not implemented");
        }

        SjSchemeMap DefaultScheme = new SjSchemeMap { KnownSchemes = new Dictionary<string, byte[]>() };

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (SjSchemeMap)value; }
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new SjOptions { Key = GetKey (Properties.Settings.Default.SJDatTitle) };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetSJDAT (DefaultScheme.KnownSchemes.Keys);
        }

        byte[] QueryKey (string filename)
        {
            var options = Query<SjOptions> (arcStrings.ArcImageEncrypted);
            return options.Key;
        }

        byte[] GetKey (string title)
        {
            byte[] key = null;
            if (!string.IsNullOrEmpty (title))
                DefaultScheme.KnownSchemes.TryGetValue (title, out key);
            return key;
        }
    }

    internal class SpReader
    {
        IBinaryStream   m_input;
        SpMetaData      m_info;
        byte[]          m_output;
        int             m_stride;

        public PixelFormat    Format { get; private set; }
        public int            Stride { get { return m_stride; } }
        public BitmapPalette Palette { get; private set; }

        public SpReader (IBinaryStream input, SpMetaData info)
        {
            m_input = input;
            m_info = info;
            m_output = new byte[info.Width * info.Height];
            m_stride = info.iWidth;
        }

        public byte[] Unpack ()
        {
            m_input.Position = 0x22;
            int packed_size = m_input.ReadInt32();
            if (m_info.Colors > 0)
                Palette = ImageFormat.ReadPalette (m_input.AsStream, m_info.Colors);
            UnpackStream (m_output, packed_size);
            if ((m_info.Flags & 0xF4) == 4)
            {
                packed_size = m_input.ReadInt32();
                if (packed_size != 0)
                {
                    var alpha = new byte[m_output.Length >> 1];
                    UnpackStream (alpha, packed_size);
                    return ConvertToRgbA (alpha);
                }
            }
            Format = m_info.Colors > 0 ? PixelFormats.Indexed8 : PixelFormats.Gray4;
            return m_output;
        }

        void UnpackStream (byte[] output, int packed_size)
        {
            if (0 == packed_size)
            {
                m_input.Read (output, 0, output.Length);
                return;
            }
            var input = m_input.AsStream;
            var input_pos = m_input.Position;
            if (m_info.IsEncrypted)
            {
                input = new StreamRegion (input, input_pos, packed_size, true);
                input = new InputCryptoStream (input, new SjTransform (m_info.Key));
            }
            try
            {
                UnpackRle (input, output);
            }
            finally
            {
                if (input != m_input.AsStream)
                    input.Dispose();
                m_input.Position = input_pos + packed_size;
            }
        }

        void UnpackRle (Stream input, byte[] output)
        {
            int dst = 0;
            int state = 0;
            byte pixel = 0;
            while (dst < output.Length)
            {
                int rep = input.ReadByte();
                if (-1 == rep)
                    break;
                if (0 == state)
                {
                    state = 1;
                    output[dst++] = (byte)rep;
                }
                else if (1 == state)
                {
                    if (output[dst - 1] == rep)
                    {
                        pixel = (byte)rep;
                        state = 2;
                    }
                    output[dst++] = (byte)rep;
                }
                else
                {
                    int count = rep - 2;
                    for (int j = 0; j < count; ++j)
                        output[dst++] = pixel;
                    state = 0;
                }
            }
        }

        byte[] ConvertToRgbA (byte[] alpha)
        {
            m_stride = m_info.iWidth * 4;
            var pixels = new byte[m_stride * m_info.iHeight];
            var colors = Palette.Colors;
            int dst = 0;
            for (int src = 0; src < m_output.Length; ++src)
            {
                var color = colors[m_output[src]];
                int a = ((alpha[src >> 1] >> ((~src & 1) << 2)) & 0xF) * 0x11;
                pixels[dst++] = color.B;
                pixels[dst++] = color.G;
                pixels[dst++] = color.R;
                pixels[dst++] = (byte)a;
            }
            Format = PixelFormats.Bgra32;
            return pixels;
        }
    }
}
