//! \file       Alpha.cs
//! \date       Wed May 18 20:06:15 2016
//! \brief      Google WEBP alpha channel processing functions.
/*
Copyright (c) 2010, Google Inc. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

  * Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.

  * Redistributions in binary form must reproduce the above copyright
    notice, this list of conditions and the following disclaimer in
    the documentation and/or other materials provided with the
    distribution.

  * Neither the name of Google nor the names of its contributors may
    be used to endorse or promote products derived from this software
    without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/
//
// C# port by morkt (C) 2016
//

using System;

namespace GameRes.Formats.Google
{
    internal class AlphaDecoder
    {
        public int width_;
        public int height_;
        public int method_;
        public int filter_;
        public int pre_processing_;
        public LosslessDecoder vp8l_dec_;
        public VP8Io m_io;
        public bool use_8b_decode_;

        public const int HeaderLen = 1;
        public const int NoCompression = 0;
        public const int LosslessCompression = 1;
        public const int PreprocessedLevels = 1;

        public bool DecodeComplete { get; private set; }

        delegate void FilterFunc (byte[] input, int src, int width, int height,
                                  int stride, byte[] output, int dst);
        delegate void UnfilterFunc (int width, int height, int stride, int row,
                                    int num_rows, byte[] data, int dst);

        FilterFunc[] Filters = new FilterFunc[WebpFilter.Last];
        UnfilterFunc[] Unfilters = new UnfilterFunc[WebpFilter.Last];

        public bool Init (byte[] data, VP8Io src_io, byte[] output)
        {
            m_io = new VP8Io();
            int alpha_data = HeaderLen;
            int alpha_data_size = data.Length - HeaderLen;

            width_ = src_io.width;
            height_ = src_io.height;

            if (data.Length <= HeaderLen)
                return false;

            method_ = (data[0] >> 0) & 3;
            filter_ = (data[0] >> 2) & 3;
            pre_processing_ = (data[0] >> 4) & 3;
            int rsrv = (data[0] >> 6) & 3;
            if (method_ < NoCompression
                || method_ > LosslessCompression
                || filter_ >= WebpFilter.Last
                || pre_processing_ > PreprocessedLevels
                || rsrv != 0)
            {
                return false;
            }

            bool ok = false;
            if (NoCompression == method_)
            {
                int alpha_decoded_size = width_ * height_;
                ok = (alpha_data_size >= alpha_decoded_size);
            }
            else
            {
                ok = DecodeAlphaHeader (data, alpha_data, alpha_data_size, output);
            }
            FiltersInit();

            // Copy the necessary parameters from src_io to io
//            m_io.Init();
            m_io.opaque = output;      // output plane
            m_io.width = src_io.width;
            m_io.height = src_io.height;

            return ok;
        }

        void FiltersInit ()
        {
            Unfilters[WebpFilter.None] = null;
            Unfilters[WebpFilter.Horizontal] = WebpFilter.HorizontalUnfilter;
            Unfilters[WebpFilter.Vertical] = WebpFilter.VerticalUnfilter;
            Unfilters[WebpFilter.Gradient] = WebpFilter.GradientUnfilter;

            Filters[WebpFilter.None] = null;
            Filters[WebpFilter.Horizontal] = WebpFilter.HorizontalFilter;
            Filters[WebpFilter.Vertical] = WebpFilter.VerticalFilter;
            Filters[WebpFilter.Gradient] = WebpFilter.GradientFilter;
        }

        /// <summary>
        // Decodes, unfilters and dequantizes *at least* 'num_rows' rows of alpha starting from row number
        // 'row'. It assumes that rows up to (row - 1) have already been decoded.
        // Returns false in case of bitstream error.
        /// </summary>
        public bool Decode (byte[] alpha_data, byte[] alpha_plane, int row, int num_rows)
        {
            var unfilter_func = Unfilters[filter_];
            if (AlphaDecoder.NoCompression == method_)
            {
                int offset = row * width_;
                int num_pixels = num_rows * width_;
                Buffer.BlockCopy (alpha_data, AlphaDecoder.HeaderLen + offset, alpha_plane, offset, num_pixels);
            }
            else // alph_dec_->method_ == ALPHA_LOSSLESS_COMPRESSION
            {
                if (!DecodeAlphaImageStream (row + num_rows))
                    return false;
            }

            if (unfilter_func != null)
                unfilter_func (width_, height_, width_, row, num_rows, alpha_plane, 0);

//            if (row + num_rows >= alph_dec_.m_io.crop_bottom)
            if (row + num_rows >= height_)
                DecodeComplete = true;

            return true;
        }

        bool DecodeAlphaHeader (byte[] data, int data_i, int data_size, byte[] output)
        {
            vp8l_dec_ = new LosslessDecoder();
            vp8l_dec_.Init (width_, height_, m_io, data, data_i, data_size, output);

            uint[] decoded = null;
            if (!vp8l_dec_.DecodeImageStream (width_, height_, true, ref decoded, false))
                return false;

            // Special case: if alpha data uses only the color indexing transform and
            // doesn't use color cache (a frequent case), we will use DecodeAlphaData()
            // method that only needs allocation of 1 byte per pixel (alpha channel).
            if (vp8l_dec_.next_transform_ == 1
                && vp8l_dec_.transforms_[0].type_ == VP8LImageTransformType.ColorIndexing
                && vp8l_dec_.Is8bOptimizable())
            {
                use_8b_decode_ = true;
                vp8l_dec_.AllocateInternalBuffers8b();
            }
            else
            {
                // Allocate internal buffers (note that dec->width_ may have changed here).
                use_8b_decode_ = false;
                vp8l_dec_.AllocateInternalBuffers32b (width_);
            }
            return true;
        }

        public bool DecodeAlphaImageStream (int last_row)
        {
            if (vp8l_dec_.last_pixel_ == vp8l_dec_.Width * vp8l_dec_.Height)
                return true;  // done

            // Decode (with special row processing).
            return use_8b_decode_ ?
                    vp8l_dec_.DecodeAlphaData (vp8l_dec_.Width, vp8l_dec_.Height, last_row) :
                    vp8l_dec_.DecodeImageData (vp8l_dec_.Pixels, vp8l_dec_.Width, vp8l_dec_.Height, last_row, LosslessDecoder.ExtractAlphaRows);
        }
    }
}
