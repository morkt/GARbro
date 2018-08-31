//! \file       Texture2D.cs
//! \date       Fri Apr 14 08:20:08 2017
//! \brief      Unity engine texture deserialzer.
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
using System.IO;
using System.Windows.Media;
using GameRes.Formats.DirectDraw;
using GameRes.Utility;

namespace GameRes.Formats.Unity
{
    enum TextureFormat : int
    {
        Alpha8 = 1,
        ARGB4444 = 2,
        RGB24 = 3,
        RGBA32 = 4,
        ARGB32 = 5,
        R16 = 6, // A 16 bit color texture format that only has a red channel.
        RGB565 = 7,
        DXT1 = 10,
        DXT5 = 12,
        RGBA4444 = 13,
        BGRA32 = 14,
        BC7 = 25,
        DXT1Crunched = 28,
        DXT5Crunched = 29,
    }

    internal class Texture2D
    {
        public string   m_Name;
        public int      m_Width;
        public int      m_Height;
        public int      m_CompleteImageSize;
        public TextureFormat m_TextureFormat;
        public int      m_MipCount;
        public bool     m_IsReadable;
        public bool     m_ReadAllowed;
        public int      m_ImageCount;
        public int      m_TextureDimension;
        public int      m_FilterMode;
        public int      m_Aniso;
        public int      m_MipBias;
        public int      m_WrapMode;
        public int      m_LightFormat;
        public int      m_ColorSpace;
        public int      m_DataLength;
        public byte[]   m_Data;

        public void Load (AssetReader reader)
        {
            m_Name = reader.ReadString();
            reader.Align();
            m_Width = reader.ReadInt32();
            m_Height = reader.ReadInt32();
            m_CompleteImageSize = reader.ReadInt32();
            m_TextureFormat = (TextureFormat)reader.ReadInt32();
            m_MipCount = reader.ReadInt32();
            if (reader.Format > 9)
            {
                m_IsReadable = reader.ReadBool();
                m_ReadAllowed = reader.ReadBool();
                reader.Align();
            }
            m_ImageCount = reader.ReadInt32();
            m_TextureDimension = reader.ReadInt32();
            m_FilterMode = reader.ReadInt32();
            m_Aniso = reader.ReadInt32();
            m_MipBias = reader.ReadInt32();
            m_WrapMode = reader.ReadInt32();
            m_LightFormat = reader.ReadInt32();
            m_ColorSpace = reader.ReadInt32();
            m_DataLength = reader.ReadInt32();
        }

        public void LoadData (AssetReader reader)
        {
            m_Data = reader.ReadBytes (m_DataLength);
        }
    }

    internal class Texture2DDecoder : IImageDecoder
    {
        AssetReader     m_reader;
        Texture2D       m_texture;
        ImageData       m_image;

        public Stream            Source { get { m_reader.Position = 0; return m_reader.Source; } }
        public ImageFormat SourceFormat { get { return null; } }
        public PixelFormat       Format { get; private set; }
        public ImageMetaData       Info { get; private set; }
        public ImageData Image {
            get {
                if (null == m_image)
                {
                    m_image = Unpack();
                }
                return m_image;
            }
        }

        public Texture2DDecoder (Texture2D texture, AssetReader input)
        {
            m_reader = input;
            m_texture = texture;
            Info = new ImageMetaData {
                Width   = (uint)m_texture.m_Width,
                Height  = (uint)m_texture.m_Height,
            };
            SetFormat (m_texture.m_TextureFormat);
        }

        void SetFormat (TextureFormat format)
        {
            switch (format)
            {
            case TextureFormat.Alpha8:
                Format = PixelFormats.Gray8;
                Info.BPP = 8;
                break;

            case TextureFormat.R16:
                Format = PixelFormats.Gray16;
                Info.BPP = 16;
                break;

            case TextureFormat.RGB24:
                Format = PixelFormats.Rgb24;
                Info.BPP = 24;
                break;

            case TextureFormat.RGB565:
                Format = PixelFormats.Bgr565;
                Info.BPP = 16;
                break;

            default:
                Format = PixelFormats.Bgra32;
                Info.BPP = 32;
                break;
            }
        }

        ImageData Unpack ()
        {
            m_texture.LoadData (m_reader);
            byte[] pixels;
            switch (m_texture.m_TextureFormat)
            {
            case TextureFormat.DXT1:
                {
                    var decoder = new DxtDecoder (m_texture.m_Data, Info);
                    pixels = decoder.UnpackDXT1();
                    break;
                }
            case TextureFormat.DXT5:
                {
                    var decoder = new DxtDecoder (m_texture.m_Data, Info);
                    pixels = decoder.UnpackDXT5();
                    break;
                }
            case TextureFormat.Alpha8:
            case TextureFormat.R16:
            case TextureFormat.RGB24:
            case TextureFormat.BGRA32:
            case TextureFormat.RGB565:
                pixels = m_texture.m_Data;
                break;

            case TextureFormat.ARGB32:
                pixels = ConvertArgb (m_texture.m_Data);
                break;

            case TextureFormat.RGBA32:
                pixels = ConvertRgba (m_texture.m_Data);
                break;

            case TextureFormat.ARGB4444:
                pixels = ConvertArgb16 (m_texture.m_Data);
                break;

            default:
                throw new NotImplementedException (string.Format ("Not supported Unity Texture2D format '{0}'.", m_texture.m_TextureFormat));
            }
            return ImageData.CreateFlipped (Info, Format, null, pixels, (int)Info.Width*((Format.BitsPerPixel+7)/8));
        }

        byte[] ConvertArgb (byte[] data)
        {
            // XXX conversion performed in-place.
            for (int i = 0; i < data.Length; i += 4)
            {
                uint x = BigEndian.ToUInt32 (data, i);
                LittleEndian.Pack (x, data, i);
            }
            return data;
        }

        byte[] ConvertArgb16 (byte[] data)
        {
            var output = new byte[data.Length * 2];
            int dst = 0;
            for (int i = 0; i < data.Length; i += 2)
            {
                ushort p = LittleEndian.ToUInt16 (data, i);
                output[dst++] = (byte)( (p        & 0xF) * 0x11);
                output[dst++] = (byte)(((p >>  4) & 0xF) * 0x11);
                output[dst++] = (byte)(((p >>  8) & 0xF) * 0x11);
                output[dst++] = (byte)(((p >> 12) & 0xF) * 0x11);
            }
            return output;
        }

        byte[] ConvertRgba (byte[] data)
        {
            for (int i = 0; i < data.Length; i += 4)
            {
                byte r = data[i];
                data[i] = data[i+2];
                data[i+2] = r;
            }
            return data;
        }

        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_reader.Dispose();
                m_disposed = true;
            }
        }
    }
}
