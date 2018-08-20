//! \file       ImageGYU.cs
//! \date       Mon Nov 02 00:38:41 2015
//! \brief      ExHIBIT engine image format.
//
// Copyright (C) 2015-2016 by morkt
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using GameRes.Utility;
using GameRes.Compression;
using GameRes.Cryptography;
using GameRes.Formats.Strings;
using System.Collections;

namespace GameRes.Formats.ExHibit
{
    internal class GyuMetaData : ImageMetaData
    {
        public int  Flags;
        public int  CompressionMode;
        public uint Key;
        public int  DataSize;
        public int  AlphaSize;
        public int  PaletteSize;
    }

    [Serializable]
    public class GyuMap : ResourceScheme
    {
        public Dictionary<string, Dictionary<int, uint>>    NumericKeys;
        public Dictionary<string, Dictionary<string, uint>> StringKeys;
    }

    [Export(typeof(ImageFormat))]
    public class GyuFormat : ImageFormat
    {
        public override string         Tag { get { return "GYU"; } }
        public override string Description { get { return "ExHIBIT engine image format"; } }
        public override uint     Signature { get { return 0x1A555947; } } // 'GYU'

        GyuMap DefaultScheme = new GyuMap {
            NumericKeys = new Dictionary<string, Dictionary<int, uint>>(),
            StringKeys  = new Dictionary<string, Dictionary<string, uint>>(),
        };

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (GyuMap)value; }
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Position = 4;
            return new GyuMetaData
            {
                Flags   = stream.ReadUInt16(),
                CompressionMode = stream.ReadUInt16(),
                Key     = stream.ReadUInt32(),
                BPP     = stream.ReadInt32(),
                Width   = stream.ReadUInt32(),
                Height  = stream.ReadUInt32(),
                DataSize    = stream.ReadInt32(),
                AlphaSize   = stream.ReadInt32(),
                PaletteSize = stream.ReadInt32(),
            };
        }

        IDictionary CurrentMap = null;

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (GyuMetaData)info;
            if (0 == meta.Key)
            {
                object token = null;
                if (null == CurrentMap)
                    CurrentMap = QueryScheme();
                if (CurrentMap != null)
                {
                    var name = Path.GetFileNameWithoutExtension (meta.FileName);
                    int num;
                    if (int.TryParse (name, out num) && CurrentMap.Contains (num))
                        token = num;
                    else if (CurrentMap.Contains (name))
                        token = name;
                }
                if (null == token)
                {
                    CurrentMap = null;
                    throw new UnknownEncryptionScheme ("Unknown image encryption key");
                }
                meta.Key = (uint)CurrentMap[token];
            }
            var reader = new GyuReader (stream.AsStream, meta);
            reader.Unpack();
            return ImageData.CreateFlipped (meta, reader.Format, reader.Palette, reader.Data, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GyuFormat.Write not implemented");
        }

        private IDictionary QueryScheme ()
        {
            var options = Query<GyuOptions> (arcStrings.GYUImageEncrypted);
            return options.Scheme;
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new GyuOptions { Scheme = GetScheme (Properties.Settings.Default.GYUTitle) };
        }

        public override object GetAccessWidget ()
        {
            var titles = DefaultScheme.NumericKeys.Keys.Concat (DefaultScheme.StringKeys.Keys).OrderBy (x => x);
            return new GUI.WidgetGYU (titles);
        }

        IDictionary GetScheme (string title)
        {
            Dictionary<int, uint> num_scheme = null;
            if (DefaultScheme.NumericKeys.TryGetValue (title, out num_scheme))
                return num_scheme;
            Dictionary<string, uint> str_scheme = null;
            DefaultScheme.StringKeys.TryGetValue (title, out str_scheme);
            return str_scheme;
        }
    }

    internal sealed class GyuReader
    {
        GyuMetaData     m_info;
        Stream          m_input;
        byte[]          m_output;
        int             m_width;
        int             m_height;

        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }
        public int            Stride { get; private set; }
        public byte[]           Data { get { return m_output; } }

        public GyuReader (Stream input, GyuMetaData info)
        {
            m_info = info;
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            Stride = (m_width * info.BPP / 8 + 3) & ~3;
            m_input = input;
            if (0 != m_info.AlphaSize)
                Format = PixelFormats.Bgra32;
            else if (32 == m_info.BPP)
                Format = PixelFormats.Bgr32;
            else if (24 == m_info.BPP)
                Format = PixelFormats.Bgr24;
            else if (8 == m_info.BPP)
                Format = PixelFormats.Indexed8;
            else
                throw new NotSupportedException ("Not supported GYU color depth");

            if (8 == m_info.BPP && 0 == m_info.PaletteSize)
                throw new InvalidFormatException();
        }

        public void Unpack ()
        {
            m_input.Position = 0x24;
            if (0 != m_info.PaletteSize)
                Palette = ImageFormat.ReadPalette (m_input, m_info.PaletteSize);

            var packed = new byte[m_info.DataSize];
            if (packed.Length != m_input.Read (packed, 0, packed.Length))
                throw new EndOfStreamException();

            if (m_info.Key != 0xFFFFFFFF)
                Deobfuscate (packed, m_info.Key);

            if (0x0100 == m_info.CompressionMode)
            {
                m_output = packed;
            }
            else
            {
                m_output = new byte[Stride * m_height];
                if (0x0800 == m_info.CompressionMode)
                    UnpackGyu (packed);
                else
                    UnpackLzss (packed);
            }
            if (0 != m_info.AlphaSize)
                ReadAlpha();
        }

        void UnpackLzss (byte[] packed)
        {
            using (var mem = new MemoryStream (packed))
            using (var lz = new LzssStream (mem))
                if (m_output.Length != lz.Read (m_output, 0, m_output.Length))
                    throw new EndOfStreamException ();
        }

        void UnpackGyu (byte[] packed)
        {
            using (var mem = new MemoryStream (packed, 4, packed.Length-4))
            using (var bits = new MsbBitStream (mem))
            {
                int dst = 0;
                m_output[dst++] = (byte)mem.ReadByte();
                while (dst < m_output.Length)
                {
                    int b = bits.GetNextBit();
                    if (-1 == b)
                        throw new EndOfStreamException();
                    if (1 == b)
                    {
                        m_output[dst++] = (byte)mem.ReadByte();
                        continue;
                    }
                    int count;
                    int offset;
                    if (1 == bits.GetNextBit())
                    {
                        count = mem.ReadByte() << 8;
                        count |= mem.ReadByte();
                        offset = -1 << 13 | count >> 3;
                        count &= 7;

                        if (0 != count)
                        {
                            ++count;
                        }
                        else
                        {
                            count = mem.ReadByte();
                            if (0 == count)
                                break;
                        }
                    }
                    else
                    {
                        count = 1 + bits.GetBits (2);
                        offset = -1 << 8 | mem.ReadByte();
                    }

                    Binary.CopyOverlapped (m_output, dst+offset, dst, ++count);
                    dst += count;
                }
            }
        }

        void ReadAlpha ()
        {
            int alpha_stride = (m_width + 3) & ~3;
            Stream alpha_stream;
            if (m_info.AlphaSize == alpha_stride * m_height)
                alpha_stream = new StreamRegion (m_input, m_input.Position, true);
            else
                alpha_stream = new LzssStream (m_input, LzssMode.Decompress, true);
            using (alpha_stream)
            {
                int src_stride = Stride;
                int new_stride = m_width * 4;
                bool extend_alpha = 3 != m_info.Flags;
                var alpha_line = new byte[alpha_stride];
                var pixels = new byte[new_stride * m_height];
                for (int y = 0; y < m_height; ++y)
                {
                    int src = y * src_stride;
                    int dst = y * new_stride;
                    if (alpha_line.Length != alpha_stream.Read (alpha_line, 0, alpha_line.Length))
                        throw new EndOfStreamException();
                    for (int x = 0; x < m_width; ++x)
                    {
                        if (8 == m_info.BPP)
                        {
                            var color = Palette.Colors[m_output[src++]];
                            pixels[dst++] = color.B;
                            pixels[dst++] = color.G;
                            pixels[dst++] = color.R;
                        }
                        else
                        {
                            pixels[dst++] = m_output[src++];
                            pixels[dst++] = m_output[src++];
                            pixels[dst++] = m_output[src++];
                        }
                        int alpha = alpha_line[x];
                        if (extend_alpha)
                            alpha = alpha >= 0x10 ? 0xFF : alpha * 0x10;
                        pixels[dst++] = (byte)alpha;
                    }
                }
                Stride = new_stride;
                Palette = null;
                m_output = pixels;
            }
        }

        static void Deobfuscate (byte[] data, uint key)
        {
            var mt = new MersenneTwister (key);
            for (int n = 0; n < 10; ++n)
            {
                uint i1 = mt.Rand() % (uint)data.Length;
                uint i2 = mt.Rand() % (uint)data.Length;
                var tmp = data[i1];
                data[i1] = data[i2];
                data[i2] = tmp;
            }
        }
    }

    public class GyuOptions : ResourceOptions
    {
        public IDictionary Scheme;
    }
}
