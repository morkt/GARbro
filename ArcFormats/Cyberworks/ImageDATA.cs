//! \file       ImageDATA.cs
//! \date       2025 Apr 16
//! \brief      Tinker Bell image format when have Resources subdirectory
//
// Copyright (C) 2016-2022 by morkt
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
using System.Collections.Generic;
using System.Windows.Media;

namespace GameRes.Formats.Cyberworks
{
    internal sealed class DataImageDecoder : IImageDecoder
    {
        public Stream Source { get { m_input.Position = 0; return m_input.AsStream; } }

        public ImageFormat SourceFormat { get { return null; } }

        public ImageMetaData Info { get { return m_info; } }

        public ImageData Image
        {
            get
            {
                if (null == m_image)
                    CreateImageData();
                return m_image;
            }
        }

        public bool HasBaseImage { get; private set; }

        ImageMetaData m_info = new ImageMetaData();
        IBinaryStream m_input;
        byte[] m_output;
        ImageData m_image;
        int m_type;
        int m_flag;

        public DataImageDecoder(IBinaryStream input, int type)
        {
            m_input = input;
            m_type = type;

            m_input.Position = 0x9;

            m_info.Width = m_input.ReadUInt32();
            m_info.Height = m_input.ReadUInt32();
            m_info.BPP = 32;    //format : BGRA32
            m_info.OffsetX = 0;
            m_info.OffsetY = 0;

            m_input.Position = 0x11;
            m_flag = input.ReadByte();
            if (0x64 == type || (0x61 == type && 1 == m_flag)) 
                HasBaseImage = true;

        }

        void CreateImageData() 
        {
            switch (m_type)
            {
                case 0x61:
                    {
                        if (0 == m_flag)
                        {
                            FillPixelsType61Pure();
                        }
                        else if (1 == m_flag)
                        {
                            FillPixelsType61HasBase();
                        }
                        else if (0 != (m_flag & 2) && 0 != (m_flag & 4))
                        {
                            FillPixelsType61ExtendedAlpha();
                        }
                        break;
                    }
                case 0x64:
                    {
                        FillPixelsType64();
                        break;
                    }
            }

            var stride = (m_info.iWidth * m_info.BPP + 7) / 8;
            m_image = ImageData.Create(m_info, PixelFormats.Bgra32, null, m_output, stride);
        }

        internal void ReadBaseImage(ArcFile arc)
        {
            if (!HasBaseImage) 
                return;

            m_input.Position = 1;

            int base_img_index = (int)(m_input.ReadUInt64() % 10000000);
            List<Entry> dir = arc.Dir as List<Entry>;
            if (null == dir) 
                return;

            using (var base_img_decoder = arc.OpenImage(dir[base_img_index]))
            {
                var stride = (base_img_decoder.Info.iWidth * base_img_decoder.Info.BPP + 7) / 8;
                var base_pixel = new byte[stride * base_img_decoder.Info.Height];
                base_img_decoder.Image.Bitmap.CopyPixels(base_pixel, stride, 0);

                m_info.Width = base_img_decoder.Info.Width;
                m_info.Height = base_img_decoder.Info.Height;
                m_output = base_pixel;
            }

        }

        void FillPixelsType61Pure()
        {
            m_output = new byte[m_info.Height * m_info.Width * 4];

            m_input.Position = 0x1D;
            var pixel_buffer_size = m_input.ReadInt32();

            m_input.Position = 0x21;
            var pixel_buffer = m_input.ReadBytes(pixel_buffer_size);

            var pixel_offset = 0;
            var output_offset = 0;
            if (pixel_buffer_size == m_info.Width * m_info.Height)
            {

                for (uint y = 0; y < m_info.Height; y++)
                {
                    for (uint x = 0; x < m_info.Width; x++)
                    {
                        m_output[output_offset+0] = pixel_buffer[pixel_offset];     //write b channel
                        m_output[output_offset+1] = pixel_buffer[pixel_offset];     //write g channel
                        m_output[output_offset+2] = pixel_buffer[pixel_offset];     //write r channel
                        m_output[output_offset+3] = pixel_buffer[pixel_offset];     //write a channel
                        pixel_offset++;
                        output_offset += 4;
                    }
                }
            }
            else if (pixel_buffer_size >= m_info.Width * m_info.Height * 3)
            {
                int paddingByte = ((m_info.iWidth * 3 + 3) & ~3) - (m_info.iWidth * 3);

                for (uint y = 0; y < m_info.Height; y++)
                {
                    for (uint x = 0; x < m_info.Width; x++)
                    {
                        m_output[output_offset+0] = pixel_buffer[pixel_offset+0];       //write b channel
                        m_output[output_offset+1] = pixel_buffer[pixel_offset+1];       //write g channel
                        m_output[output_offset+2] = pixel_buffer[pixel_offset+2];       //write r channel
                        m_output[output_offset+3] = 0xFF;                               //write a channel
                        pixel_offset += 3;
                        output_offset += 4;
                    }
                    pixel_offset += paddingByte;
                }

            }
            else
            {
                throw new NotImplementedException();
            }
        }

        void FillPixelsType61HasBase()
        {
            m_input.Position = 0x19;

            var pixel_diff_mask_size = m_input.ReadInt32();
            var pixel_diff_mask_buffer = m_input.ReadBytes(pixel_diff_mask_size);
            m_input.Position = pixel_diff_mask_size + 0x1D;
            var pixel_delta_size = m_input.ReadInt32();
            var pixel_delta_buffer = m_input.ReadBytes(pixel_delta_size);

            var output_offset = 0;
            var delta_offset = 0;
            var diff_mask_offset = 0;
            var mask_bit_offset = 0;
            for (int y = 0; y < m_info.Height; y++)
            {
                for (int x = 0; x < m_info.Width; x++)
                {
                    if (1 == ((pixel_diff_mask_buffer[diff_mask_offset] >> mask_bit_offset) & 1))
                    {
                        m_output[output_offset+0] = pixel_delta_buffer[delta_offset+0];     //write b channel
                        m_output[output_offset+1] = pixel_delta_buffer[delta_offset+1];     //write g channel
                        m_output[output_offset+2] = pixel_delta_buffer[delta_offset+2];     //write r channel
                        delta_offset += 3;
                    }
                    m_output[output_offset+3] = 0xFF;                                       //write a channel
                    output_offset += 4;
                    if (7 == mask_bit_offset)
                    {
                        mask_bit_offset = 0;
                        diff_mask_offset++;
                    }
                    else
                    {
                        mask_bit_offset++;
                    }
                }
            }

        }

        void FillPixelsType61ExtendedAlpha()
        {
            m_output = new byte[m_info.Height * m_info.Width * 4];

            m_input.Position = 0x15;
            var alpha_buffer_size = m_input.ReadInt32();
            var alpha_buffer = m_input.ReadBytes(alpha_buffer_size);

            m_input.Position = alpha_buffer_size + 0x1D;
            var bgr_buffer_size = m_input.ReadInt32();
            var bgr_buffer = m_input.ReadBytes(bgr_buffer_size);

            var output_offset = 0;
            var pixel_offset = 0;
            int paddingByte = ((m_info.iWidth * 3 + 3) & ~3) - (m_info.iWidth * 3);

            for (int y = 0; y < m_info.Height; y++)
            {
                for (int x = 0; x < m_info.Width; x++)
                {
                    m_output[output_offset+0] = bgr_buffer[pixel_offset+0];         //write b channel
                    m_output[output_offset+1] = bgr_buffer[pixel_offset+1];         //write g channel
                    m_output[output_offset+2] = bgr_buffer[pixel_offset+2];         //write r channel
                    m_output[output_offset+3] = alpha_buffer[pixel_offset];         //write a channel
                    output_offset += 4;
                    pixel_offset += 3;
                }

                pixel_offset+=paddingByte;
            }

        }

        /// <remarks>
        /// if the image entry type is 0x64, the entry doesn't record the image's width and height.
        /// </remarks>
        void FillPixelsType64()
        {
            m_input.Position = 0x15;
            var alpha_delta_size = m_input.ReadInt32();
            var alpha_delta_buffer = m_input.ReadBytes(alpha_delta_size);
            var diff_mask_size = m_input.ReadInt32();
            var diff_mask_buffer = m_input.ReadBytes(diff_mask_size);

            m_input.Position = alpha_delta_size + diff_mask_size + 0x1D;
            var bgr_delta_size = m_input.ReadInt32();
            var bgr_delta_buffer = m_input.ReadBytes(bgr_delta_size);

            var output_offset = 0;
            var alpha_delta_offset = 0;
            var bgr_delta_offset = 0;
            var diff_mask_offset = 0;
            var mask_bit_offset = 0;
            for (int y = 0; y < m_info.Height; y++)
            {
                for (int x = 0; x < m_info.Width; x++)
                {
                    if (1 == ((diff_mask_buffer[diff_mask_offset] >> mask_bit_offset) & 1))
                    {
                        m_output[output_offset+0] = bgr_delta_buffer[bgr_delta_offset+0];       //write b channel
                        m_output[output_offset+1] = bgr_delta_buffer[bgr_delta_offset+1];       //write g channel
                        m_output[output_offset+2] = bgr_delta_buffer[bgr_delta_offset+2];       //write r channel
                        m_output[output_offset+3] = alpha_delta_buffer[alpha_delta_offset];     //write a channel
                        bgr_delta_offset += 3;
                        alpha_delta_offset++;
                    }
                    output_offset += 4;
                    if (7 == mask_bit_offset)
                    {
                        mask_bit_offset = 0;
                        diff_mask_offset++;
                    }
                    else
                    {
                        mask_bit_offset++;
                    }
                }
            }

        }

        #region IDisposable Members
        bool m_disposed = false;
        public void Dispose()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }
}
