//! \file       ImageMG2.cs
//! \date       Sat Feb 25 03:08:01 2017
//! \brief      Valkyria image format.
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
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Valkyria
{
    internal class Mg2MetaData : ImageMetaData
    {
        public int          ImageLength;
        public int          AlphaLength;
        public IMg2Scheme   Scheme;
        public ImageFormat  Format;
    }

    internal interface IMg2Scheme
    {
        Mg2EncryptedStream CreateStream (Stream main, int offset, int length, int key);
        ImageData CreateImage (BitmapSource bitmap, ImageMetaData info);
    }

    [Export(typeof(ImageFormat))]
    public class Mg2Format : ImageFormat
    {
        public override string         Tag { get { return "MG2"; } }
        public override string Description { get { return "Valkyria image format"; } }
        public override uint     Signature { get { return 0; } } // 'MICO'

        static readonly IMg2Scheme[] KnownSchemes = { new Mg2SchemeV1(), new Mg2SchemeV2(), new Mg2SchemeV3(), new Mg2SchemeV4(), new Mg2SchemeV5() };

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            var header_array = header.ToArray();
            if (!header_array.AsciiEqual (0, "MICO"))
            {
                var enc = new EncryptionScheme
                {
                    OddKey1 = 0xd17aca4b,
                    OddKey2 = 2,
                    EvenKey1 = 0x6269b97c,
                    EvenKey2 = 3
                };
                do
                {
                    DatOpener.DecryptIndex (header_array, enc, (uint)file.Length);
                    if (header_array.AsciiEqual (0, "MICO"))
                        break;
                    DecryptHeader2 (header_array, file.Length); // chain
                    if (header_array.AsciiEqual (0, "MICO"))
                        break;
                    header_array = header.ToArray();
                    DecryptHeader2 (header_array, file.Length);
                    if (!header_array.AsciiEqual (0, "MICO"))
                        return null;
                }
                while (false);
            }
            if (!header_array.AsciiEqual (4, "CG0"))
                return null;
            int version = header_array[7] - '0';
            if (version > 3)
                return null;
            int length = header_array.ToInt32 (8);
            foreach (var scheme in KnownSchemes)
            {
                using (var input = scheme.CreateStream (file.AsStream, 0x10, length, length))
                using (var img = new BinaryStream (input, file.Name))
                {
                    ImageFormat format;
                    if (Png.Signature == img.Signature)
                        format = Png;
                    else if (0xE0FFD8FF == img.Signature)
                        format = Jpeg;
                    else
                        continue;
                    var info = format.ReadMetaData (img);
                    if (null == info)
                        continue;
                    return new Mg2MetaData
                    {
                        Width = info.Width,
                        Height = info.Height,
                        OffsetX = info.OffsetX,
                        OffsetY = info.OffsetY,
                        BPP = info.BPP,
                        ImageLength = length,
                        AlphaLength = header_array.ToInt32 (12),
                        Scheme = scheme,
                        Format = format,
                    };
                }
            }
            return null;
        }

        void DecryptHeader2 (byte[] header, long length)
        {
            var v0 = BitConverter.GetBytes (length);
            var v1 = (byte)(v0[1]+v0[3]);
            var v2 = (byte)(v0[0]+v0[2]);
            for (var i = 0; i < header.Length; i++)
            {
                header[i] ^= v1;
                v1 += v2;
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (Mg2MetaData)info;
            var frame = ReadBitmapSource (file.AsStream, meta);
            return meta.Scheme.CreateImage (frame, meta);
        }

        BitmapSource ReadBitmapSource (Stream file, Mg2MetaData meta)
        {
            BitmapSource frame;
            using (var input = meta.Scheme.CreateStream (file, 0x10, meta.ImageLength, meta.ImageLength))
            using (var img = new BinaryStream (input, meta.FileName))
            {
                var image = meta.Format.Read (img, meta);
                frame = image.Bitmap;
                if (0 == meta.AlphaLength)
                    return frame;
            }
            if (frame.Format.BitsPerPixel != 32)
                frame = new FormatConvertedBitmap (frame, PixelFormats.Bgr32, null, 0);
            int stride = frame.PixelWidth * 4;
            var pixels = new byte[stride * (int)meta.Height];
            frame.CopyPixels (pixels, stride, 0);

            using (var input = meta.Scheme.CreateStream (file, 0x10+meta.ImageLength, meta.AlphaLength, meta.ImageLength))
            {
                var decoder = BitmapDecoder.Create (input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                BitmapSource alpha_frame = decoder.Frames[0];
                if (alpha_frame.PixelWidth != frame.PixelWidth || alpha_frame.PixelHeight != frame.PixelHeight)
                    return BitmapSource.Create ((int)meta.Width, (int)meta.Height,
                                                ImageData.DefaultDpiX, ImageData.DefaultDpiY,
                                                PixelFormats.Bgr32, null, pixels, stride);

                alpha_frame = new FormatConvertedBitmap (alpha_frame, PixelFormats.Gray8, null, 0);
                var alpha = new byte[alpha_frame.PixelWidth * alpha_frame.PixelHeight];
                alpha_frame.CopyPixels (alpha, alpha_frame.PixelWidth, 0);

                int src = 0;
                for (int dst = 3; dst < pixels.Length; dst += 4)
                {
                    pixels[dst] = alpha[src++];
                }
                return BitmapSource.Create ((int)meta.Width, (int)meta.Height,
                                            ImageData.DefaultDpiX, ImageData.DefaultDpiY,
                                            PixelFormats.Bgra32, null, pixels, stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Mg2Format.Write not implemented");
        }
    }

    internal class Mg2EncryptedStream : StreamRegion
    {
        readonly byte   m_version;
        readonly int    m_threshold;
        readonly byte   m_key0;
        readonly byte   m_key1;
        readonly byte[] m_key2;
        readonly byte[] m_data;

        protected Mg2EncryptedStream (Stream main, int offset, int length, byte version, int threshold, byte key0)
            : base (main, offset, length, true)
        {
            // version == 1 || version == 2
            m_version = version;
            m_threshold = threshold;
            m_key0 = key0;
        }

        protected Mg2EncryptedStream (Stream main, int offset, int length, byte version, byte key0, byte key1)
            : base (main, offset, length, true)
        {
            // version == 3
            m_version = version;
            m_key0 = key0;
            m_key1 = key1;
        }

        protected Mg2EncryptedStream (Stream main, int offset, int length, byte version, int key)
            : base (main, offset, length, true)
        {
            // version == 4
            m_version = version;
            
            m_key2 = new byte[length];
            var v0 = BitConverter.GetBytes (key);
            var v1 = (byte)(v0[1]+v0[3]);
            var v2 = (byte)(v0[0]+v0[2]);

            for (var i = 0; i < length; i++)
            {
                m_key2[i] = v1;
                v1 += v2;
            }
        }

        protected Mg2EncryptedStream (Stream main, int offset, int length, byte version, int key, EncryptionScheme enc)
            : base (main, offset, length, true)
        {
            // version == 5
            m_version = version;

            // It'll be very hard to decrypt the data on-the-fly, so we just decrypt it during initialization.
            m_data = new byte[length];
            base.Read (m_data, 0, length);
            DatOpener.DecryptIndex (m_data, enc, (uint)key);
            Position = 0;
        }

        public static Mg2EncryptedStream CreateV1 (Stream main, int offset, int length)
        {
            return new Mg2EncryptedStream (main, offset, length, 1, length / 5, 0);
        }

        public static Mg2EncryptedStream CreateV2 (Stream main, int offset, int length)
        {
            return new Mg2EncryptedStream (main, offset, length, 2, Math.Min (25, length), (byte)length);
        }

        public static Mg2EncryptedStream CreateV3 (Stream main, int offset, int length, int key)
        {
            return new Mg2EncryptedStream (main, offset, length, 3, (byte)(key >> 1), (byte)((key & 1) + (key >> 3)));
        }

        public static Mg2EncryptedStream CreateV4 (Stream main, int offset, int length, int key)
        {
            return new Mg2EncryptedStream (main, offset, length, 4, key);
        }

        public static Mg2EncryptedStream CreateV5 (Stream main, int offset, int length, int key, EncryptionScheme enc)
        {
            return new Mg2EncryptedStream (main, offset, length, 5, key, enc);
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read;
            if (m_version == 5)
            {
                read = (int)Math.Min (m_data.Length - Position, count);
                Buffer.BlockCopy (m_data, (int)Position, buffer, offset, read);
                Position += read;
                return read;
            }

            long pos = Position;
            read = base.Read (buffer, offset, count);

            if (m_version == 3)
            {
                for (int i = 0; i < read; ++i)
                {
                    buffer[offset+i] ^= (byte)((pos >> 4) ^ (pos + m_key0) ^ m_key1);
                    pos++;
                }
            }
            else if (m_version == 4)
            {
                for (int i = 0; i < read; ++i)
                    buffer[offset+i] ^= m_key2[pos+i];
            }
            else
            {
                for (int i = 0; i < read && pos < m_threshold; ++i)
                    buffer[offset+i] ^= (byte)(m_key0 + pos++);
            }
            return read;
        }

        public override int ReadByte ()
        {
            if (m_version == 5)
            {
                return m_data[Position++];
            }

            long pos = Position;
            int b = base.ReadByte();

            if (m_version == 3)
            {
                b ^= (byte)((pos >> 4) ^ (pos + m_key0) ^ m_key1);
            }
            else if (m_version == 4)
            {
                b ^= m_key2[pos];
            }
            else
            {
                if (b != -1 && pos < m_threshold)
                    b ^= (byte)(m_key0 + pos);
            }

            return b;
        }
    }

    internal class Mg2SchemeV1 : IMg2Scheme
    {
        public Mg2EncryptedStream CreateStream (Stream main, int offset, int length, int key)
        {
            return Mg2EncryptedStream.CreateV1 (main, offset, length);
        }

        public ImageData CreateImage (BitmapSource frame, ImageMetaData info)
        {
            frame.Freeze();
            return new ImageData (frame, info);
        }
    }

    internal class Mg2SchemeV2 : IMg2Scheme
    {
        public virtual Mg2EncryptedStream CreateStream (Stream main, int offset, int length, int key)
        {
            return Mg2EncryptedStream.CreateV2 (main, offset, length);
        }

        public ImageData CreateImage (BitmapSource frame, ImageMetaData info)
        {
            frame = new TransformedBitmap (frame, new ScaleTransform { ScaleY = -1 });
            frame.Freeze();
            return new ImageData (frame, info);
        }
    }

    internal class Mg2SchemeV3 : Mg2SchemeV2
    {
        public override Mg2EncryptedStream CreateStream (Stream main, int offset, int length, int key)
        {
            return Mg2EncryptedStream.CreateV3 (main, offset, length, key);
        }
    }

    internal class Mg2SchemeV4 : Mg2SchemeV2
    {
        public override Mg2EncryptedStream CreateStream (Stream main, int offset, int length, int key)
        {
            return Mg2EncryptedStream.CreateV4 (main, offset, length, key);
        }
    }

    internal class Mg2SchemeV5 : Mg2SchemeV2
    {
        EncryptionScheme m_enc;

        public Mg2SchemeV5 ()
        {
            m_enc = new EncryptionScheme
            {
                OddKey1 = 0x915c1a4c,
                OddKey2 = 5,
                EvenKey1 = 0xbd3accdd,
                EvenKey2 = 3
            };
        }

        public override Mg2EncryptedStream CreateStream (Stream main, int offset, int length, int key)
        {
            return Mg2EncryptedStream.CreateV5 (main, offset, length, key, m_enc);
        }
    }
}
