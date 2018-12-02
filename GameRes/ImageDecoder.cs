//! \file       ImageDecoder.cs
//! \date       Wed Oct 26 00:33:00 2016
//! \brief      Image decoder interface.
//
// Copyright (C) 2016 by morkt
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
using System.Windows.Media.Imaging;

namespace GameRes
{
    public interface IImageDecoder : IDisposable
    {
        Stream Source { get; }

        /// <summary>
        /// Underlying image format or null if image is not represented by any format.
        /// </summary>
        ImageFormat SourceFormat { get; }

        /// <summary>
        /// Image parameters.
        /// </summary>
        ImageMetaData Info { get; }

        /// <summary>
        /// Decoded image data.
        /// </summary>
        ImageData    Image { get; }
    }

    public sealed class ImageFormatDecoder : IImageDecoder
    {
        IBinaryStream       m_file;
        ImageData           m_image;

        public Stream            Source { get { m_file.Position = 0; return m_file.AsStream; } }
        public ImageFormat SourceFormat { get; private set; }
        public ImageMetaData       Info { get; private set; }

        public ImageData Image
        {
            get
            {
                if (null == m_image)
                {
                    m_file.Position = 0;
                    m_image = SourceFormat.Read (m_file, Info);
                }
                return m_image;
            }
        }

        public ImageFormatDecoder (IBinaryStream file)
        {
            m_file = file;
            var format = ImageFormat.FindFormat (file);
            if (null == format)
                throw new InvalidFormatException();
            SourceFormat = format.Item1;
            Info = format.Item2;
        }

        public ImageFormatDecoder (IBinaryStream file, ImageFormat format, ImageMetaData info)
        {
            m_file = file;
            SourceFormat = format;
            Info = info;
        }

        /// <summary>
        /// Create instance of ImageFormatDecoder from input binary stream.
        /// In case of error input stream is disposed.
        /// </summary>
        public static ImageFormatDecoder Create (IBinaryStream input)
        {
            try
            {
                return new ImageFormatDecoder (input);
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_file.Dispose();
                m_disposed = true;
            }
        }
    }

    public abstract class BinaryImageDecoder : IImageDecoder
    {
        protected IBinaryStream   m_input;
        protected ImageData       m_image;

        public Stream            Source { get { m_input.Position = 0; return m_input.AsStream; } }
        public ImageFormat SourceFormat { get { return null; } }
        public ImageMetaData       Info { get; protected set; }
        public ImageData          Image { get { return m_image ?? (m_image = GetImageData()); } }

        protected BinaryImageDecoder (IBinaryStream input)
        {
            m_input = input;
        }

        protected BinaryImageDecoder (IBinaryStream input, ImageMetaData info)
        {
            m_input = input;
            Info = info;
        }

        protected abstract ImageData GetImageData ();

        #region IDisposable members
        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        bool m_disposed = false;
        protected virtual void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                    m_input.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }

    public class BitmapSourceDecoder : IImageDecoder
    {
        public Stream            Source { get; set; }
        public ImageFormat SourceFormat { get; set; }
        public ImageMetaData       Info { get; private set; }
        public ImageData          Image { get; private set; }

        public BitmapSourceDecoder (BitmapSource bitmap)
        {
            Info = new ImageMetaData {
                Width = (uint)bitmap.PixelWidth,
                Height = (uint)bitmap.PixelHeight,
                BPP = bitmap.Format.BitsPerPixel,
            };
            Image = new ImageData (bitmap);
        }

        public void Dispose ()
        {
        }
    }
}
