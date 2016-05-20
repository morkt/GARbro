//! \file       Lossless.cs
//! \date       Wed May 18 20:10:59 2016
//! \brief      Google WEBP lossless compression decoder.
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
using GameRes.Utility;

namespace GameRes.Formats.Google
{
    enum VP8StatusCode
    {
        Ok = 0,
        OutOfMemory,
        InvalidParam,
        BitstreamError,
        UnsupportedFeature,
        Suspended,
        UserAbort,
        NotEnoughData
    }

    enum VP8DecodeState
    {
        ReadData = 0,
        ReadHdr = 1,
        ReadDim = 2
    }

    enum VP8LImageTransformType
    {
        Predictor,
        CrossColor,
        SubtractGreen,
        ColorIndexing
    }

    internal struct VP8LMultipliers
    {
        public byte green_to_red_;
        public byte green_to_blue_;
        public byte red_to_blue_;

        public void Reset ()
        {
            green_to_red_ = 0;
            green_to_blue_ = 0;
            red_to_blue_ = 0;
        }

        public void ColorCodeToMultipliers (uint color_code)
        {
            green_to_red_  = (byte)color_code;
            green_to_blue_ = (byte)(color_code >>  8);
            red_to_blue_   = (byte)(color_code >> 16);
        }

        static uint ColorTransformDelta (sbyte color_pred, sbyte color)
        {
            return (uint)((int)color_pred * color) >> 5;
        }

        public void TransformColorInverse (uint[] data, int offset, int num_pixels)
        {
            for (int i = 0; i < num_pixels; ++i)
            {
                uint argb = data[offset+i];
                uint green = argb >> 8;
                uint red = argb >> 16;
                uint new_red = red;
                uint new_blue = argb;
                new_red += ColorTransformDelta ((sbyte)green_to_red_, (sbyte)green);
                new_red &= 0xFF;
                new_blue += ColorTransformDelta ((sbyte)green_to_blue_, (sbyte)green);
                new_blue += ColorTransformDelta ((sbyte)red_to_blue_, (sbyte)new_red);
                new_blue &= 0xFF;
                data[offset+i] = (argb & 0xFF00FF00u) | (new_red << 16) | (new_blue);
            }
        }
    }

    internal class VP8LTransform
    {
        public VP8LImageTransformType type_;   // transform type.
        public int                    bits_;   // subsampling bits defining transform window.
        public int                    xsize_;  // transform window X index.
        public int                    ysize_;  // transform window Y index.
        public uint[]                 data_;   // transform data.

        public void InverseTransform (int row_start, int row_end,
                                      uint[] input, int src, uint[] output, int dst)
        {
            int width = xsize_;
            switch (type_)
            {
            case VP8LImageTransformType.SubtractGreen:
                AddGreenToBlueAndRed (output, dst, (row_end - row_start) * width);
                break;

            case VP8LImageTransformType.Predictor:
                PredictorInverseTransform (row_start, row_end, output, dst);
                if (row_end != ysize_)
                {
                    // The last predicted row in this iteration will be the top-pred row
                    // for the first row in next iteration.
                    Buffer.BlockCopy (output, dst + (row_end - row_start - 1) * width,
                                      output, dst - width, width * sizeof(uint));
                }
                break;

            case VP8LImageTransformType.CrossColor:
                ColorSpaceInverseTransform (row_start, row_end, output, dst);
                break;

            case VP8LImageTransformType.ColorIndexing:
                if (input == output && src == dst && bits_ > 0)
                {
                    // Move packed pixels to the end of unpacked region, so that unpacking
                    // can occur seamlessly.
                    // Also, note that this is the only transform that applies on
                    // the effective width of VP8LSubSampleSize(xsize_, bits_). All other
                    // transforms work on effective width of xsize_.
                    int out_stride = (row_end - row_start) * width;
                    int in_stride = (row_end - row_start) * LosslessDecoder.SubSampleSize (xsize_, bits_);
                    src += out_stride - in_stride;
                    Buffer.BlockCopy (output, dst, input, src, in_stride * sizeof(uint));
//                    memmove(src, out, in_stride * sizeof(*src));
                    ColorIndexInverseTransform (row_start, row_end, input, src, output, dst);
                }
                else
                {
                    ColorIndexInverseTransform (row_start, row_end, input, src, output, dst);
                }
                break;
            }
        }

        static byte GetAlphaValue (uint val)
        {
            return (byte)(val >> 8);
        }

        static void MapARGB (uint[] input, int src, uint[] color_map,
                             uint[] output, int dst, int y_start, int y_end, int width)
        {
            for (int y = y_start; y < y_end; ++y)
            for (int x = 0; x < width; ++x)
                output[dst++] = color_map[(input[src++] >> 8) & 0xFF];
        }

        static void MapAlpha (byte[] input, int src, uint[] color_map,
                              byte[] output, int dst, int y_start, int y_end, int width)
        {
            for (int y = y_start; y < y_end; ++y)
            for (int x = 0; x < width; ++x)
                output[dst++] = GetAlphaValue (color_map[input[src++]]);
        }

        void ColorIndexInverseTransform (int y_start, int y_end, uint[] input, int src,
                                         uint[] output, int dst)
        {
            int bits_per_pixel = 8 >> bits_;
            int width = xsize_;
            if (bits_per_pixel < 8)
            {
                int pixels_per_byte = 1 << bits_;
                int count_mask = pixels_per_byte - 1;
                uint bit_mask = (1u << bits_per_pixel) - 1u;
                for (int y = y_start; y < y_end; ++y)
                {
                    uint packed_pixels = 0;
                    for (int x = 0; x < width; ++x)
                    {
                        // We need to load fresh 'packed_pixels' once every
                        // 'pixels_per_byte' increments of x. Fortunately, pixels_per_byte
                        // is a power of 2, so can just use a mask for that, instead of
                        // decrementing a counter.
                        if ((x & count_mask) == 0)
                            packed_pixels = (input[src++] >> 8) & 0xFF; // GetARGBIndex
                        output[dst++] = data_[packed_pixels & bit_mask]; // GetARGBValue
                        packed_pixels >>= bits_per_pixel;
                    }
                }
            }
            else
            {
                MapARGB (input, src, data_, output, dst, y_start, y_end, width);
            }
        }

        public void ColorIndexInverseTransformAlpha (int y_start, int y_end, byte[] input, int src, byte[] output, int dst)
        {
            int bits_per_pixel = 8 >> bits_;
            int width = xsize_;
            var color_map = data_;
            if (bits_per_pixel < 8)
            {
                int pixels_per_byte = 1 << bits_;
                int count_mask = pixels_per_byte - 1;
                uint bit_mask = (1u << bits_per_pixel) - 1u;
                for (int y = y_start; y < y_end; ++y)
                {
                    uint packed_pixels = 0;
                    for (int x = 0; x < width; ++x)
                    {
                        if ((x & count_mask) == 0)
                            packed_pixels = input[src++];
                        output[dst++] = GetAlphaValue (color_map[packed_pixels & bit_mask]);
                        packed_pixels >>= bits_per_pixel;
                    }
                }
            }
            else
            {
                MapAlpha (input, src, color_map, output, dst, y_start, y_end, width);
            }
        }

        void AddGreenToBlueAndRed (uint[] data, int offset, int num_pixels)
        {
            for (int i = 0; i < num_pixels; ++i)
            {
                uint argb = data[i];
                uint green = (argb >> 8) & 0xFFu;
                uint red_blue = argb & 0x00FF00FFu;
                red_blue += (green << 16) | green;
                red_blue &= 0x00FF00FFu;
                data[i] = (argb & 0xFF00FF00u) | red_blue;
            }
        }

        void PredictorInverseTransform (int y_start, int y_end, uint[] data, int offset)
        {
            int width = xsize_;
            if (y_start == 0)    // First Row follows the L (mode=1) mode.
            {
                uint pred0 = Predictor0 (data[offset-1], data, offset-1);
                AddPixelsEq (data, offset, pred0);
                for (int x = 1; x < width; ++x)
                {
                    uint pred1 = Predictor1 (data[offset + x - 1], data, offset-1);
                    AddPixelsEq (data, offset + x, pred1);
                }
                offset += width;
                ++y_start;
            }

            int y = y_start;
            int tile_width = 1 << bits_;
            int mask = tile_width - 1;
            int safe_width = width & ~mask;
            int tiles_per_row = LosslessDecoder.SubSampleSize (width, bits_);
            int pred_mode_base = (y >> bits_) * tiles_per_row; // within data_

            while (y < y_end)
            {
                uint pred2 = Predictor2 (data[offset-1], data, offset - width);
                int pred_mode_src = pred_mode_base;
                int x = 1;
                int t = 1;
                // First pixel follows the T (mode=2) mode.
                AddPixelsEq (data, offset, pred2);
                // .. the rest:
                while (x < safe_width)
                {
                    var pred_func = kPredictors[(data_[pred_mode_src++] >> 8) & 0xF];
                    for (; t < tile_width; ++t, ++x)
                    {
                        uint pred = pred_func (data[offset + x - 1], data, offset + x - width);
                        AddPixelsEq (data, offset + x, pred);
                    }
                    t = 0;
                }
                if (x < width)
                {
                    var pred_func = kPredictors[(data_[pred_mode_src++] >> 8) & 0xF];
                    for (; x < width; ++x)
                    {
                        uint pred = pred_func (data[offset + x - 1], data, offset + x - width);
                        AddPixelsEq (data, offset + x, pred);
                    }
                }
                offset += width;
                ++y;
                if ((y & mask) == 0)     // Use the same mask, since tiles are squares.
                    pred_mode_base += tiles_per_row;
            }
        }

        void ColorSpaceInverseTransform (int y_start, int y_end, uint[] data, int offset)
        {
            int width = xsize_;
            int tile_width = 1 << bits_;
            int mask = tile_width - 1;
            int safe_width = width & ~mask;
            int remaining_width = width - safe_width;
            int tiles_per_row = LosslessDecoder.SubSampleSize (width, bits_);
            int y = y_start;
            int pred_row = (y >> bits_) * tiles_per_row; // within data_

            var m = new VP8LMultipliers();
            while (y < y_end)
            {
                m.Reset();
                int pred = pred_row;
                int data_safe_end = offset + safe_width;
                int data_end = offset + width;
                while (offset < data_safe_end)
                {
                    m.ColorCodeToMultipliers (data_[pred++]);
                    m.TransformColorInverse (data, offset, tile_width);
                    offset += tile_width;
                }
                if (offset < data_end)
                {
                    m.ColorCodeToMultipliers (data_[pred++]);
                    m.TransformColorInverse (data, offset, remaining_width);
                    offset += remaining_width;
                }
                ++y;
                if ((y & mask) == 0)
                    pred_row += tiles_per_row;
            }
        }

        //------------------------------------------------------------------------------
        // Predictors

        static uint Predictor0 (uint left, uint[] data, int top)
        {
            return 0xFF000000u; // ARGB_BLACK
        }
        static uint Predictor1 (uint left, uint[] data, int top)
        {
            return left;
        }
        static uint Predictor2 (uint left, uint[] data, int top)
        {
            return data[top];
        }
        static uint Predictor3 (uint left, uint[] data, int top)
        {
            return data[top+1];
        }
        static uint Predictor4 (uint left, uint[] data, int top)
        {
            return data[top-1];
        }
        static uint Predictor5 (uint left, uint[] data, int top)
        {
            return Average3 (left, data[top], data[top+1]);
        }
        static uint Predictor6 (uint left, uint[] data, int top)
        {
            return Average2 (left, data[top-1]);
        }
        static uint Predictor7 (uint left, uint[] data, int top)
        {
            return Average2 (left, data[top]);
        }
        static uint Predictor8 (uint left, uint[] data, int top)
        {
            return Average2 (data[top-1], data[top]);
        }
        static uint Predictor9 (uint left, uint[] data, int top)
        {
            return Average2 (data[top], data[top+1]);
        }
        static uint Predictor10 (uint left, uint[] data, int top)
        {
            return Average4 (left, data[top-1], data[top], data[top+1]);
        }
        static uint Predictor11(uint left, uint[] data, int top)
        {
            return Select (data[top], data[left], data[top-1]);
        }
        static uint Predictor12 (uint left, uint[] data, int top)
        {
            return ClampedAddSubtractFull (left, data[top], data[top-1]);
        }
        static uint Predictor13 (uint left, uint[] data, int top)
        {
            return ClampedAddSubtractHalf (left, data[top], data[top-1]);
        }

        delegate uint PredictorFunc (uint left, uint[] data, int top);
        static readonly PredictorFunc[] kPredictors = new PredictorFunc[16]
        {
            Predictor0,  Predictor1,  Predictor2,  Predictor3,
            Predictor4,  Predictor5,  Predictor6,  Predictor7,
            Predictor8,  Predictor9,  Predictor10, Predictor11,
            Predictor12, Predictor13, Predictor0,  Predictor0,
        };

        //------------------------------------------------------------------------------
        // Image transforms.

        // In-place sum of each component with mod 256.
        static void AddPixelsEq (uint[] data, int a, uint b)
        {
            data[a] = MMX.PAddB (data[a], b);
        }

        static uint Average2 (uint a0, uint a1)
        {
            return (((a0 ^ a1) & 0xFEFEFEFEu) >> 1) + (a0 & a1);
        }

        static uint Average3 (uint a0, uint a1, uint a2)
        {
            return Average2 (Average2 (a0, a2), a1);
        }

        static uint Average4 (uint a0, uint a1, uint a2, uint a3)
        {
            return Average2 (Average2 (a0, a1), Average2 (a2, a3));
        }

        static int Sub3 (int a, int b, int c)
        {
            return Math.Abs (b - c) - Math.Abs(a - c);
        }

        static uint Select (uint a, uint b, uint c)
        {
            int pa_minus_pb =
                Sub3 ((int)(a >> 24)       , (int)(b >> 24)       , (int)(c >> 24)       ) +
                Sub3 ((int)(a >> 16) & 0xFF, (int)(b >> 16) & 0xFF, (int)(c >> 16) & 0xFF) +
                Sub3 ((int)(a >>  8) & 0xFF, (int)(b >>  8) & 0xFF, (int)(c >>  8) & 0xFF) +
                Sub3 ((int)(a      ) & 0xFF, (int)(b      ) & 0xFF, (int)(c      ) & 0xFF);
            return (pa_minus_pb <= 0) ? a : b;
        }

        static uint Clip255 (uint a)
        {
            if (a < 256)
                return a;
            // return 0, when a is a negative integer.
            // return 255, when a is positive.
            return ~a >> 24;
        }

        static uint AddSubtractComponentFull (uint a, uint b, uint c)
        {
            return Clip255 ((uint)((int)a + (int)b - (int)c));
        }

        static uint ClampedAddSubtractFull (uint c0, uint c1, uint c2)
        {
            uint a = AddSubtractComponentFull (c0 >> 24, c1 >> 24, c2 >> 24);
            uint r = AddSubtractComponentFull ((c0 >> 16) & 0xFF,
                                               (c1 >> 16) & 0xFF,
                                               (c2 >> 16) & 0xFF);
            uint g = AddSubtractComponentFull ((c0 >> 8) & 0xFF,
                                               (c1 >> 8) & 0xFF,
                                               (c2 >> 8) & 0xFF);
            uint b = AddSubtractComponentFull (c0 & 0xFF, c1 & 0xFF, c2 & 0xFF);
            return (a << 24) | (r << 16) | (g << 8) | b;
        }

        static uint AddSubtractComponentHalf (uint a, uint b)
        {
            return Clip255 ((uint)((int)a + ((int)a - (int)b) / 2));
        }

        static uint ClampedAddSubtractHalf (uint c0, uint c1, uint c2)
        {
            uint ave = Average2 (c0, c1);
            uint a = AddSubtractComponentHalf (ave >> 24, c2 >> 24);
            uint r = AddSubtractComponentHalf ((ave >> 16) & 0xFF, (c2 >> 16) & 0xFF);
            uint g = AddSubtractComponentHalf ((ave >> 8) & 0xFF, (c2 >> 8) & 0xFF);
            uint b = AddSubtractComponentHalf ((ave >> 0) & 0xFF, (c2 >> 0) & 0xFF);
            return (a << 24) | (r << 16) | (g << 8) | b;
        }
    }

    internal static class HuffIndex
    {
        public const int Green  = 0;
        public const int Red    = 1;
        public const int Blue   = 2;
        public const int Alpha  = 3;
        public const int Dist   = 4;
    }

    internal class LosslessDecoder
    {
        const uint  kHashMul = 0x1E35A7BDu;
        const int   NumARGBCacheRows = 16;
        const int   MaxCacheBits = 11;
        const int   NumTransforms = 4;
        const int   SyncEveryNRows = 8;  // minimum number of rows between check-points
        const int   BitsSpecialMarker = 0x100;  // something large enough (and a bit-mask)
        const int   PackedNonLiteralCode = 0;  // must be < NUM_LITERAL_CODES

        const int   kCodeLengthLiterals = 16;
        const int   kCodeLengthRepeatCode = 16;
        static readonly int[] kCodeLengthExtraBits = { 2, 3, 7 };
        static readonly int[] kCodeLengthRepeatOffsets = { 3, 3, 11 };

        public delegate void ProcessRowsFunc (LosslessDecoder dec, int row);

        VP8StatusCode   status_;
        VP8DecodeState  state_;
        public VP8Io    io_;

        byte[]          pixels8_;
        uint[]          pixels32_;      // Internal data: either uint8_t* for alpha
                                        // or uint32_t* for BGRA.
        int             argb_cache_;    // Scratch buffer for temporary BGRA storage.

        LBitReader      br_ = new LBitReader();
        bool            incremental_ = false;   // if true, incremental decoding is expected
        LBitReader      saved_br_ = new LBitReader(); // note: could be local variables too
        int             saved_last_pixel_;

        int             width_;
        int             height_;
        public int      last_row_;      // last input row decoded so far.
        public int      last_pixel_;    // last pixel decoded so far. However, it may
                                        // not be transformed, scaled and
                                        // color-converted yet.
        int             last_out_row_;  // last row output so far.

        public uint[] Pixels { get { return pixels32_; } }
        public int     Width
        {
            get { return width_; }
            set { width_ = value; }
        }
        public int    Height
        {
            get { return height_; }
            set { height_ = value; }
        }

        LMetadata       hdr_ = new LMetadata();

        public int      next_transform_;
        public VP8LTransform[] transforms_ = new VP8LTransform[NumTransforms];
        // or'd bitset storing the transforms types.
        uint            transforms_seen_;

        public LosslessDecoder ()
        {
            status_ = VP8StatusCode.Ok;
            state_  = VP8DecodeState.ReadDim;

            for (int i = 0; i < transforms_.Length; ++i)
                transforms_[i] = new VP8LTransform();
        }

        public void Init (int width, int height, VP8Io io,
                          byte[] data, int data_i, int data_size, byte[] output)
        {
            width_ = width;
            height_ = height;
            status_ = VP8StatusCode.Ok;
            io_ = io;

            io_.opaque = output;
            io_.width = width_;
            io_.height = height_;

            br_.Init (data, data_i, (uint)data_size);
        }

        public bool Is8bOptimizable ()
        {
            if (hdr_.color_cache_size_ > 0)
                return false;
            // When the Huffman tree contains only one symbol, we can skip the
            // call to ReadSymbol() for red/blue/alpha channels.
            for (int i = 0; i < hdr_.num_htree_groups_; ++i)
            {
                var htree_group = hdr_.htree_groups_[i];
                if (htree_group.GetCode (HuffIndex.Red, 0).bits > 0) return false;
                if (htree_group.GetCode (HuffIndex.Blue, 0).bits > 0) return false;
                if (htree_group.GetCode (HuffIndex.Alpha, 0).bits > 0) return false;
            }
            return true;
        }

        //------------------------------------------------------------------------------

        public bool DecodeImage ()
        {
            // Initialization.
            if (state_ != VP8DecodeState.ReadData)
            {
                AllocateInternalBuffers32b (io_.width);
                if (incremental_)
                {
                    if (hdr_.color_cache_size_ > 0
                        && hdr_.saved_color_cache_.colors_ == null)
                    {
                        hdr_.saved_color_cache_.Init (hdr_.color_cache_.hash_bits_);
                    }
                }
                state_ = VP8DecodeState.ReadData;
            }

            // Decode.
            return DecodeImageData (pixels32_, width_, height_, height_, ProcessRows);
        }

        // Processes (transforms, scales & color-converts) the rows decoded after the
        // last call.
        static void ProcessRows (LosslessDecoder dec, int row)
        {
            throw new NotImplementedException ("Lossless RGB decoder not implemented");
        }

        //------------------------------------------------------------------------------
        // Allocate internal buffers dec->pixels_ and dec->argb_cache_.

        public void AllocateInternalBuffers32b (int final_width)
        {
            int num_pixels = width_ * height_;
            // Scratch buffer corresponding to top-prediction row for transforming the
            // first row in the row-blocks. Not needed for paletted alpha.
            int cache_top_pixels = (ushort)final_width;
            // Scratch buffer for temporary BGRA storage. Not needed for paletted alpha.
            int cache_pixels = final_width * NumARGBCacheRows;
            int total_num_pixels = num_pixels + cache_top_pixels + cache_pixels;

            pixels32_ = new uint[total_num_pixels];
            argb_cache_ = num_pixels + cache_top_pixels;
        }

        public void AllocateInternalBuffers8b ()
        {
            int total_num_pixels = width_ * height_;
            pixels8_ = new byte[total_num_pixels];
            argb_cache_ = 0;
        }

        public bool DecodeImageStream (int xsize, int ysize, bool is_level0, ref uint[] decoded_data, bool set_data)
        {
            bool ok = true;
            int transform_xsize = xsize;
            int transform_ysize = ysize;
            int color_cache_bits = 0;
            uint[] data = null;

            // Read the transforms (may recurse).
            if (is_level0)
            {
                while (ok && 0 != br_.ReadBits (1))
                    ok = ReadTransform (ref transform_xsize, ref transform_ysize);
            }

            // Color cache
            if (ok && 0 != br_.ReadBits (1))
            {
                color_cache_bits = (int)br_.ReadBits (4);
                ok = (color_cache_bits >= 1 && color_cache_bits <= MaxCacheBits);
                if (!ok)
                {
                    status_ = VP8StatusCode.BitstreamError;
                    return false;
                }
            }

            // Read the Huffman codes (may recurse).
            ok = ok && ReadHuffmanCodes (transform_xsize, transform_ysize, color_cache_bits, is_level0);
            if (!ok)
            {
                status_ = VP8StatusCode.BitstreamError;
                return false;
            }

            // Finish setting up the color-cache
            if (color_cache_bits > 0)
            {
                hdr_.color_cache_size_ = 1 << color_cache_bits;
                hdr_.color_cache_.Init (color_cache_bits);
            }
            else
            {
                hdr_.color_cache_size_ = 0;
            }
            UpdateDecoder (transform_xsize, transform_ysize);

            if (is_level0)     // level 0 complete
            {
                state_ = VP8DecodeState.ReadHdr;
            }
            else
            {
                var total_size = transform_xsize * transform_ysize;
                data = new uint[total_size];

                // Use the Huffman trees to decode the LZ77 encoded data.
                ok = DecodeImageData (data, transform_xsize, transform_ysize, transform_ysize, null);
                ok = ok && !br_.EoS;
            }
            if (ok)
            {
                if (set_data)
                {
                    decoded_data = data;
                }
                last_pixel_ = 0;  // Reset for future DECODE_DATA_FUNC() calls.
                if (!is_level0)
                    hdr_.ClearMetadata();
            }
            return ok;
        }

        bool ReadTransform (ref int xsize, ref int ysize)
        {
            bool ok = true;
            var transform = transforms_[next_transform_];
            var type = (VP8LImageTransformType)br_.ReadBits (2);

            // Each transform type can only be present once in the stream.
            if (0 != (transforms_seen_ & (1U << (int)type)))
                return false;  // Already there, let's not accept the second same transform.

            transforms_seen_ |= (1U << (int)type);

            transform.type_ = type;
            transform.xsize_ = xsize;
            transform.ysize_ = ysize;
            transform.data_ = null;
            ++next_transform_;

            switch (type)
            {
            case VP8LImageTransformType.Predictor:
            case VP8LImageTransformType.CrossColor:
                transform.bits_ = (int)br_.ReadBits (3) + 2;
                ok = DecodeImageStream (SubSampleSize (transform.xsize_, transform.bits_),
                                        SubSampleSize (transform.ysize_, transform.bits_),
                                        false, ref transform.data_, true);
                break;
            case VP8LImageTransformType.ColorIndexing:
                int num_colors = (int)br_.ReadBits (8) + 1;
                int bits = (num_colors > 16) ? 0
                         : (num_colors > 4) ? 1
                         : (num_colors > 2) ? 2
                         : 3;
                xsize = SubSampleSize (transform.xsize_, bits);
                transform.bits_ = bits;
                ok = DecodeImageStream (num_colors, 1, false, ref transform.data_, true);
                ok = ok && ExpandColorMap (num_colors, transform);
                break;
            case VP8LImageTransformType.SubtractGreen:
                break;
            default:
                throw new InvalidFormatException();
            }
            return ok;
        }

        bool ExpandColorMap (int num_colors, VP8LTransform transform)
        {
            int final_num_colors = 1 << (8 >> transform.bits_);
            uint[] new_color_map = new uint[final_num_colors];
            new_color_map[0] = transform.data_[0];
            for (int i = 1; i < num_colors; ++i)
                new_color_map[i] = MMX.PAddB (transform.data_[i], new_color_map[i-1]);
            transform.data_ = new_color_map;
            return true;
        }

        bool ReadHuffmanCodes (int xsize, int ysize, int color_cache_bits, bool allow_recursion)
        {
            uint[] huffman_image = null;
            int num_htree_groups = 1;
            int max_alphabet_size = 0;
            int table_size = kTableSize[color_cache_bits];

            if (allow_recursion && 0 != br_.ReadBits (1))
            {
                // use meta Huffman codes.
                int huffman_precision = (int)br_.ReadBits (3) + 2;
                int huffman_xsize = SubSampleSize (xsize, huffman_precision);
                int huffman_ysize = SubSampleSize (ysize, huffman_precision);
                int huffman_pixs = huffman_xsize * huffman_ysize;
                if (!DecodeImageStream (huffman_xsize, huffman_ysize, false, ref huffman_image, true))
                    return false;
                hdr_.huffman_subsample_bits_ = huffman_precision;
                for (int i = 0; i < huffman_pixs; ++i)
                {
                    // The huffman data is stored in red and green bytes.
                    int group = (int)(huffman_image[i] >> 8) & 0xffff;
                    huffman_image[i] = (uint)group;
                    if (group >= num_htree_groups)
                        num_htree_groups = group + 1;
                }
            }

            if (br_.EoS) return false;

            // Find maximum alphabet size for the htree group.
            for (int j = 0; j < Huffman.CodesPerMetaCode; ++j)
            {
                int alphabet_size = kAlphabetSize[j];
                if (j == 0 && color_cache_bits > 0)
                    alphabet_size += 1 << color_cache_bits;
                if (max_alphabet_size < alphabet_size)
                    max_alphabet_size = alphabet_size;
            }

            var htree_groups = HTreeGroup.New (num_htree_groups, table_size);
            var huffman_tables = htree_groups[0].Tables;
            var code_lengths = new int[max_alphabet_size];

            int next = 0;
            for (int i = 0; i < num_htree_groups; ++i)
            {
                var htree_group = htree_groups[i];
                int size;
                int total_size = 0;
                bool is_trivial_literal = true;
                int max_bits = 0;
                for (int j = 0; j < Huffman.CodesPerMetaCode; ++j)
                {
                    int alphabet_size = kAlphabetSize[j];
                    htree_group.SetMeta (j, next);
                    if (j == 0 && color_cache_bits > 0)
                        alphabet_size += 1 << color_cache_bits;

                    size = ReadHuffmanCode (alphabet_size, code_lengths, huffman_tables, next);
                    if (0 == size)
                        return false;

                    if (is_trivial_literal && kLiteralMap[j] == 1)
                        is_trivial_literal = (huffman_tables[next].bits == 0);

                    total_size += huffman_tables[next].bits;
                    next += size;
                    if (j <= HuffIndex.Alpha)
                    {
                        int local_max_bits = code_lengths[0];
                        for (int k = 1; k < alphabet_size; ++k)
                        {
                            if (code_lengths[k] > local_max_bits)
                                local_max_bits = code_lengths[k];
                        }
                        max_bits += local_max_bits;
                    }
                }
                htree_group.is_trivial_literal = is_trivial_literal;
                htree_group.is_trivial_code = false;
                if (is_trivial_literal)
                {
                    uint red = htree_group.GetCode (HuffIndex.Red, 0).value;
                    uint blue = htree_group.GetCode (HuffIndex.Blue, 0).value;
                    uint alpha = htree_group.GetCode (HuffIndex.Alpha, 0).value;
                    htree_group.literal_arb = (alpha << 24) | (red << 16) | blue;
                    if (total_size == 0 && htree_group.GetCode (HuffIndex.Green, 0).value < Huffman.NumLiteralCodes)
                    {
                        htree_group.is_trivial_code = true;
                        htree_group.literal_arb |= (uint)htree_group.GetCode (HuffIndex.Green, 0).value << 8;
                    }
                }
                htree_group.use_packed_table = !htree_group.is_trivial_code && (max_bits < Huffman.PackedBits);
                if (htree_group.use_packed_table)
                    BuildPackedTable (htree_group);
            }

            // All OK. Finalize pointers and return.
            hdr_.huffman_image_ = huffman_image;
            hdr_.num_htree_groups_ = num_htree_groups;
            hdr_.htree_groups_ = htree_groups;
            return true;
        }

        int ReadHuffmanCode (int alphabet_size, int[] code_lengths, HuffmanCode[] table, int index)
        {
            bool ok = false;
            int size = 0;
            bool simple_code = br_.ReadBits (1) != 0;

            for (int i = 0; i < alphabet_size; ++i)
                code_lengths[i] = 0;

            if (simple_code)    // Read symbols, codes & code lengths directly.
            {
                int num_symbols = (int)br_.ReadBits (1) + 1;
                int first_symbol_len_code = (int)br_.ReadBits (1);
                // The first code is either 1 bit or 8 bit code.
                int symbol = (int)br_.ReadBits ((first_symbol_len_code == 0) ? 1 : 8);
                code_lengths[symbol] = 1;
                // The second code (if present), is always 8 bit long.
                if (2 == num_symbols)
                {
                    symbol = (int)br_.ReadBits (8);
                    code_lengths[symbol] = 1;
                }
                ok = true;
            }
            else    // Decode Huffman-coded code lengths.
            {
                var code_length_code_lengths = new int[NumCodeLengthCodes];
                int num_codes = (int)br_.ReadBits (4) + 4;
                if (num_codes > NumCodeLengthCodes)
                {
                    status_ = VP8StatusCode.BitstreamError;
                    return 0;
                }
                for (int i = 0; i < num_codes; ++i)
                {
                    code_length_code_lengths[kCodeLengthCodeOrder[i]] = (int)br_.ReadBits (3);
                }
                ok = ReadHuffmanCodeLengths (code_length_code_lengths, alphabet_size, code_lengths);
            }

            ok = ok && !br_.EoS;
            if (ok)
                size = Huffman.BuildTable (table, index, Huffman.TableBits, code_lengths, alphabet_size);
            if (!ok || size == 0)
            {
                status_ = VP8StatusCode.BitstreamError;
                return 0;
            }
            return size;
        }

        bool ReadHuffmanCodeLengths (int[] code_length_code_lengths, int num_symbols, int[] code_lengths)
        {
            int prev_code_len = Huffman.DefaultCodeLength;
            var table = new HuffmanCode[1 << Huffman.LengthsTableBits];

            if (0 == Huffman.BuildTable (table, 0, Huffman.LengthsTableBits, code_length_code_lengths, NumCodeLengthCodes))
            {
                status_ = VP8StatusCode.BitstreamError;
                return false;
            }

            int max_symbol;
            if (0 != br_.ReadBits (1))      // use length
            {
                int length_nbits = 2 + 2 * (int)br_.ReadBits (3);
                max_symbol = 2 + (int)br_.ReadBits (length_nbits);
                if (max_symbol > num_symbols)
                {
                    status_ = VP8StatusCode.BitstreamError;
                    return false;
                }
            }
            else
            {
                max_symbol = num_symbols;
            }

            int symbol = 0;
            while (symbol < num_symbols)
            {
                if (max_symbol-- == 0) break;
                br_.FillBitWindow();
                int p = (int)br_.PrefetchBits() & Huffman.LengthsTableMask;
                br_.SkipBits (table[p].bits);
                int code_len = table[p].value;
                if (code_len < kCodeLengthLiterals)
                {
                    code_lengths[symbol++] = code_len;
                    if (code_len != 0) prev_code_len = code_len;
                }
                else
                {
                    bool use_prev = (code_len == kCodeLengthRepeatCode);
                    int slot = code_len - kCodeLengthLiterals;
                    int extra_bits = kCodeLengthExtraBits[slot];
                    int repeat_offset = kCodeLengthRepeatOffsets[slot];
                    int repeat = (int)br_.ReadBits(extra_bits) + repeat_offset;
                    if (symbol + repeat > num_symbols)
                    {
                        status_ = VP8StatusCode.BitstreamError;
                        return false;
                    }
                    else
                    {
                        int length = use_prev ? prev_code_len : 0;
                        while (repeat-- > 0)
                            code_lengths[symbol++] = length;
                    }
                }
            }
            return true;
        }

        void BuildPackedTable (HTreeGroup htree_group)
        {
            var huff = htree_group.packed_table;
            for (int code = 0; code < Huffman.PackedTableSize; ++code)
            {
                uint bits = (uint)code;
                var hcode = htree_group.GetCode (HuffIndex.Green, code);
                if (hcode.value >= Huffman.NumLiteralCodes)
                {
                    huff[code].bits = hcode.bits + BitsSpecialMarker;
                    huff[code].value = hcode.value;
                }
                else
                {
                    huff[code].bits = 0;
                    huff[code].value = 0;
                    bits >>= AccumulateHCode (hcode, 8, ref huff[code]);
                    bits >>= AccumulateHCode (htree_group.GetCode (HuffIndex.Red, (int)bits), 16, ref huff[code]);
                    bits >>= AccumulateHCode (htree_group.GetCode (HuffIndex.Blue, (int)bits), 0, ref huff[code]);
                    bits >>= AccumulateHCode (htree_group.GetCode (HuffIndex.Alpha, (int)bits), 24, ref huff[code]);
                }
            }
        }

        int AccumulateHCode (HuffmanCode hcode, int shift, ref HuffmanCode32 huff)
        {
            huff.bits += hcode.bits;
            huff.value |= (uint)hcode.value << shift;
            return hcode.bits;
        }

        void UpdateDecoder (int width, int height)
        {
            int num_bits = hdr_.huffman_subsample_bits_;
            width_ = width;
            height_ = height;

            hdr_.huffman_xsize_ = SubSampleSize (width, num_bits);
            hdr_.huffman_mask_ = (num_bits == 0) ? ~0 : (1 << num_bits) - 1;
        }

        public static int SubSampleSize (int size, int sampling_bits)
        {
            return (int)(((uint)size + (1u << sampling_bits) - 1u) >> sampling_bits);
        }

        public bool DecodeImageData (uint[] data, int width, int height, int last_row, ProcessRowsFunc process_func)
        {
            int row = last_pixel_ / width;
            int col = last_pixel_ % width;
            var htree_group = GetHtreeGroupForPos (col, row);
            int src = last_pixel_;
            int last_cached = src;
            int src_end = width * height;     // End of data
            int src_last = width * last_row;  // Last pixel to decode
            int len_code_limit = Huffman.NumLiteralCodes + Huffman.NumLengthCodes;
            int color_cache_limit = len_code_limit + hdr_.color_cache_size_;
            int next_sync_row = incremental_ ? row : 1 << 24;
            var color_cache = (hdr_.color_cache_size_ > 0) ? hdr_.color_cache_ : null;
            int mask = hdr_.huffman_mask_;

            while (src < src_last)
            {
                int code;
                if (row >= next_sync_row)
                {
                    SaveState (src);
                    next_sync_row = row + SyncEveryNRows;
                }
                // Only update when changing tile. Note we could use this test:
                // if "((((prev_col ^ col) | prev_row ^ row)) > mask)" -> tile changed
                // but that's actually slower and needs storing the previous col/row.
                if ((col & mask) == 0)
                    htree_group = GetHtreeGroupForPos (col, row);
                if (htree_group.is_trivial_code)
                {
                    data[src] = htree_group.literal_arb;
                    goto AdvanceByOne;
                }
                br_.FillBitWindow();
                if (htree_group.use_packed_table)
                {
                    code = ReadPackedSymbols (htree_group, data, src);
                    if (code == PackedNonLiteralCode)
                        goto AdvanceByOne;
                }
                else
                {
                    code = ReadSymbol (htree_group.Tables, htree_group.GetMeta (HuffIndex.Green));
                }
                if (br_.EoS) break;  // early out
                if (code < Huffman.NumLiteralCodes)    // Literal
                {
                    if (htree_group.is_trivial_literal)
                    {
                        data[src] = htree_group.literal_arb | (uint)(code << 8);
                    }
                    else
                    {
                        int red, blue, alpha;
                        red = ReadSymbol (htree_group.Tables, htree_group.GetMeta (HuffIndex.Red));
                        br_.FillBitWindow();
                        blue = ReadSymbol (htree_group.Tables, htree_group.GetMeta (HuffIndex.Blue));
                        alpha = ReadSymbol (htree_group.Tables, htree_group.GetMeta (HuffIndex.Alpha));
                        if (br_.EoS) break;
                        data[src] = ((uint)alpha << 24) | ((uint)red << 16) | ((uint)code << 8) | (uint)blue;
                    }
                }
                else if (code < len_code_limit)    // Backward reference
                {
                    int length_sym = code - Huffman.NumLiteralCodes;
                    int length = GetCopyLength (length_sym);
                    int dist_symbol = ReadSymbol (htree_group.Tables, htree_group.GetMeta (HuffIndex.Dist));
                    br_.FillBitWindow();
                    int dist_code = GetCopyDistance (dist_symbol);
                    int dist = PlaneCodeToDistance (width, dist_code);
                    if (br_.EoS) break;
                    if (src < dist || src_end - src < length)
                    {
                        status_ = VP8StatusCode.BitstreamError;
                        return false;
                    }
                    else
                    {
                        int dst = src;
                        int s = dst - dist;
                        for (int i = 0; i < length; ++i)
                            data[dst+i] = data[s+i];
                    }
                    src += length;
                    col += length;
                    while (col >= width)
                    {
                        col -= width;
                        ++row;
                        if ((row % NumARGBCacheRows == 0) && (process_func != null))
                            process_func (this, row);
                    }
                    if (0 != (col & mask)) htree_group = GetHtreeGroupForPos (col, row);
                    if (color_cache != null)
                    {
                        while (last_cached < src)
                            color_cache.Insert (data[last_cached++]);
                    }
                    continue;
                }
                else if (code < color_cache_limit)    // Color cache
                {
                    int key = code - len_code_limit;
                    while (last_cached < src)
                        color_cache.Insert (data[last_cached++]);
                    data[src] = color_cache.Lookup ((uint)key);
                }
                else    // Not reached
                {
                    status_ = VP8StatusCode.BitstreamError;
                    return false;
                }

            AdvanceByOne:
                ++src;
                ++col;
                if (col >= width)
                {
                    col = 0;
                    ++row;
                    if ((row % NumARGBCacheRows == 0) && (process_func != null))
                    {
                        process_func (this, row);
                    }
                    if (color_cache != null)
                    {
                        while (last_cached < src)
                            color_cache.Insert (data[last_cached++]);
                    }
                }
            }

            if (incremental_ && br_.EoS && src < src_end)
            {
                RestoreState();
            }
            else if (!br_.EoS)
            {
                // Process the remaining rows corresponding to last row-block.
                if (process_func != null)
                    process_func (this, row);
                status_ = VP8StatusCode.Ok;
                last_pixel_ = src;  // end-of-scan marker
            }
            else
            {
                // if not incremental, and we are past the end of buffer (eos_=1), then this
                // is a real bitstream error.
                status_ = VP8StatusCode.BitstreamError;
                return false;
            }
            return true;
        }

        HTreeGroup GetHtreeGroupForPos (int x, int y)
        {
            int meta_index = GetMetaIndex (hdr_.huffman_image_, hdr_.huffman_xsize_, hdr_.huffman_subsample_bits_, x, y);
            return hdr_.htree_groups_[meta_index];
        }

        int GetMetaIndex (uint[] image, int xsize, int bits, int x, int y)
        {
            if (0 == bits) return 0;
            return (int)image[xsize * (y >> bits) + (x >> bits)];
        }

        //------------------------------------------------------------------------------
        // Decodes the next Huffman code from bit-stream.
        // FillBitWindow(br) needs to be called at minimum every second call
        // to ReadSymbol, in order to pre-fetch enough bits.
        int ReadSymbol (HuffmanCode[] table, int index)
        {
            int val = (int)br_.PrefetchBits();
            index += val & Huffman.TableMask;
            int nbits = table[index].bits - Huffman.TableBits;
            if (nbits > 0)
            {
                br_.SkipBits (Huffman.TableBits);
                val = (int)br_.PrefetchBits();
                index += table[index].value;
                index += val & ((1 << nbits) - 1);
            }
            br_.SkipBits (table[index].bits);
            return table[index].value;
        }

        int ReadPackedSymbols (HTreeGroup group, uint[] data, int dst)
        {
            uint val = br_.PrefetchBits() & (Huffman.PackedTableSize - 1);
            var code = group.packed_table[val];
            if (code.bits < BitsSpecialMarker)
            {
                br_.SkipBits (code.bits);
                data[dst] = code.value;
                return PackedNonLiteralCode;
            }
            else
            {
                br_.SkipBits (code.bits - BitsSpecialMarker);
                return (int)code.value;
            }
        }

        int GetCopyLength (int length_symbol)
        {
            // Length and distance prefixes are encoded the same way.
            return GetCopyDistance (length_symbol);
        }

        int GetCopyDistance (int distance_symbol)
        {
            if (distance_symbol < 4)
                return distance_symbol + 1;
            int extra_bits = (distance_symbol - 2) >> 1;
            int offset = (2 + (distance_symbol & 1)) << extra_bits;
            return offset + (int)br_.ReadBits (extra_bits) + 1;
        }

        int PlaneCodeToDistance (int xsize, int plane_code)
        {
            if (plane_code > CodeToPlaneCodes)
                return plane_code - CodeToPlaneCodes;
            int dist_code = kCodeToPlane[plane_code - 1];
            int yoffset = dist_code >> 4;
            int xoffset = 8 - (dist_code & 0xf);
            int dist = yoffset * xsize + xoffset;
            return (dist >= 1) ? dist : 1;  // dist<1 can happen if xsize is very small
        }

        void SaveState (int last_pixel)
        {
            br_.CopyStateTo (saved_br_);
            saved_last_pixel_ = last_pixel;
            if (hdr_.color_cache_size_ > 0)
                hdr_.color_cache_.Copy (hdr_.saved_color_cache_);
        }

        void RestoreState ()
        {
            status_ = VP8StatusCode.Suspended;
            saved_br_.CopyStateTo (br_);
            last_pixel_ = saved_last_pixel_;
            if (hdr_.color_cache_size_ > 0)
                hdr_.saved_color_cache_.Copy (hdr_.color_cache_);
        }

        public bool DecodeAlphaData (int width, int height, int last_row)
        {
            var data = pixels8_;
            bool ok = true;
            int row = last_pixel_ / width;
            int col = last_pixel_ % width;
            var htree_group = GetHtreeGroupForPos (col, row);
            int pos = last_pixel_;        // current position
            int end = width * height;     // End of data
            int last = width * last_row;  // Last pixel to decode
            int len_code_limit = Huffman.NumLiteralCodes + Huffman.NumLengthCodes;
            int mask = hdr_.huffman_mask_;

            while (!br_.EoS && pos < last)
            {
                // Only update when changing tile.
                if ((col & mask) == 0)
                    htree_group = GetHtreeGroupForPos (col, row);

                br_.FillBitWindow();
                int code = ReadSymbol (htree_group.Tables, htree_group.GetMeta (HuffIndex.Green));
                if (code < Huffman.NumLiteralCodes)    // Literal
                {
                    data[pos] = (byte)code;
                    ++pos;
                    ++col;
                    if (col >= width)
                    {
                        col = 0;
                        ++row;
                        if (row % NumARGBCacheRows == 0)
                            ExtractPalettedAlphaRows (row);
                    }
                }
                else if (code < len_code_limit)    // Backward reference
                {
                    int length_sym = code - Huffman.NumLiteralCodes;
                    int length = GetCopyLength (length_sym);
                    int dist_symbol = ReadSymbol (htree_group.Tables, htree_group.GetMeta (HuffIndex.Dist));
                    br_.FillBitWindow();
                    int dist_code = GetCopyDistance (dist_symbol);
                    int dist = PlaneCodeToDistance (width, dist_code);
                    if (pos >= dist && end - pos >= length)
                    {
                        Binary.CopyOverlapped (data, pos - dist, pos, length);
                    }
                    else
                    {
                        ok = false;
                        goto End;
                    }
                    pos += length;
                    col += length;
                    while (col >= width)
                    {
                        col -= width;
                        ++row;
                        if (row % NumARGBCacheRows == 0)
                            ExtractPalettedAlphaRows (row);
                    }
                    if (pos < last && 0 != (col & mask))
                        htree_group = GetHtreeGroupForPos (col, row);
                }
                else    // Not reached
                {
                    ok = false;
                    goto End;
                }
            }
            // Process the remaining rows corresponding to last row-block.
            ExtractPalettedAlphaRows (row);

        End:
            if (!ok || (br_.EoS && pos < end))
            {
                ok = false;
                status_ = br_.EoS ? VP8StatusCode.Suspended : VP8StatusCode.BitstreamError;
            }
            else
            {
                last_pixel_ = pos;
            }
            return ok;
        }

        void ExtractPalettedAlphaRows (int row)
        {
            int num_rows = row - last_row_;
            int input = width_ * last_row_;
            if (num_rows > 0)
                ApplyInverseTransformsAlpha (num_rows, pixels8_, input);

            last_row_ = last_out_row_ = row;
        }

        void ApplyInverseTransforms (int num_rows, uint[] rows, int rows_in)
        {
            int n = next_transform_;
            int cache_pixs = width_ * num_rows;
            int start_row = last_row_;
            int end_row = start_row + num_rows;
            int rows_out = argb_cache_;

            // Inverse transforms.
            // TODO: most transforms only need to operate on the cropped region only.
            Buffer.BlockCopy (rows, rows_in, pixels32_, rows_out, cache_pixs * sizeof(uint));
            while (n --> 0)
            {
                transforms_[n].InverseTransform (start_row, end_row, rows, rows_in, pixels32_, rows_out);
                rows = pixels32_;
                rows_in = rows_out;
            }
        }

        void ApplyInverseTransformsAlpha (int num_rows, byte[] rows, int rows_in)
        {
            int start_row = last_row_;
            int end_row = start_row + num_rows;
            byte[] output = io_.opaque;
            int rows_out = io_.width * start_row;
            transforms_[0].ColorIndexInverseTransformAlpha (start_row, end_row, rows, rows_in, output, rows_out);
        }

        /// <summary>
        /// Special row-processing that only stores the alpha data.
        /// </summary>
        public static void ExtractAlphaRows (LosslessDecoder dec, int row)
        {
            int num_rows = row - dec.last_row_;
            if (num_rows <= 0) return;  // Nothing to be done.

            dec.ApplyInverseTransforms (num_rows, dec.pixels32_, dec.width_ * dec.last_row_);

            // Extract alpha (which is stored in the green plane).

            int width = dec.io_.width;      // the final width (!= dec->width_)
            int cache_pixs = width * num_rows;
//            uint8_t* const dst = (uint8_t*)dec->io_->opaque + width * dec->last_row_;
            int dst = width * dec.last_row_;
            int src = dec.argb_cache_;
            for (int i = 0; i < cache_pixs; ++i)
                dec.io_.opaque[dst+i] = (byte)(dec.pixels32_[src+i] >> 8);
            dec.last_row_ = dec.last_out_row_ = row;
        }

        //------------------------------------------------------------------------------

        internal class LMetadata
        {
            public int          color_cache_size_;
            public LColorCache  color_cache_ = new LColorCache();
            public LColorCache  saved_color_cache_ = new LColorCache();  // for incremental

            public int          huffman_mask_;
            public int          huffman_subsample_bits_;
            public int          huffman_xsize_;
            public uint[]       huffman_image_;
            public int          num_htree_groups_;
            public HTreeGroup[]  htree_groups_;

            public void ClearMetadata ()
            {
                color_cache_size_ = 0;
                huffman_mask_ = 0;
                huffman_subsample_bits_ = 0;
                huffman_xsize_ = 0;
                huffman_image_ = null;
                num_htree_groups_ = 0;
                htree_groups_ = null;
            }
        }

        internal class LColorCache
        {
            public uint[] colors_;  // color entries
            public int hash_shift_; // Hash shift: 32 - hash_bits_.
            public int hash_bits_;

            public void Init (int hash_bits)
            {
                int hash_size = 1 << hash_bits;
                colors_ = new uint[hash_size];
                hash_shift_ = 32 - hash_bits;
                hash_bits_ = hash_bits;
            }

            public void Copy (LColorCache dst)
            {
                Array.Copy (colors_, dst.colors_, 1u << dst.hash_bits_);
            }

            public uint Lookup (uint key)
            {
                return colors_[key];
            }

            public void Set (uint key, uint argb)
            {
                colors_[key] = argb;
            }

            public void Insert (uint argb)
            {
                uint key = (kHashMul * argb) >> hash_shift_;
                colors_[key] = argb;
            }

            public int GetIndex (uint argb)
            {
                return (int)((kHashMul * argb) >> hash_shift_);
            }
        }

        // -----------------------------------------------------------------------------
        /// <summary>
        /// Bitreader for lossless format
        /// </summary>
        internal class LBitReader
        {
            ulong          val_;        // pre-fetched bits
            byte[]         buf_;        // input byte buffer
            uint           len_;        // buffer length
            uint           pos_;        // byte position in buf_
            int            bit_pos_;    // current bit-reading position in val_
            bool           eos_;        // true if a bit was read past the end of buffer

            public const int MaxNumBitRead = 24;

            public const int LBITS = 64; // Number of bits prefetched.
            public const int WBITS = 32; // Minimum number of bytes ready after VP8LFillBitWindow.

            const int LOG8_WBITS = 4;    // Number of bytes needed to store WBITS bits.

            public bool EoS { get { return eos_; } }

            /// <summary>
            /// Returns true if there was an attempt at reading bit past the end of
            /// the buffer. Doesn't set br->eos_ flag.
            /// </summary>
            private bool IsEndOfStream { get { return eos_ || ((pos_ == len_) && (bit_pos_ > LBITS)); } }

            public void Init (byte[] input, int start, uint length)
            {
                len_ = length;
                val_ = 0;
                bit_pos_ = 0;
                eos_ = false;

                if (length > sizeof(ulong))
                    length = sizeof(ulong);

                ulong v = 0;
                for (uint i = 0; i < length; ++i)
                    v |= (ulong)input[start+i] << (8 * (int)i);

                val_ = v;
                pos_ = (uint)start+length;
                buf_ = input;
            }

            public void CopyStateTo (LBitReader other)
            {
                other.val_ = val_;
                other.buf_ = buf_;
                other.len_ = len_;
                other.pos_ = pos_;
                other.bit_pos_ = bit_pos_;
                other.eos_ = eos_;
            }

            /// <summary>
            ///  Sets a new data buffer.
            /// </summary>
            public void SetBuffer (byte[] buffer, uint length)
            {
                buf_ = buffer;
                len_ = length;
                // pos_ > len_ should be considered a param error.
                eos_ = (pos_ > len_) || IsEndOfStream;
            }

            /// <summary>
            /// Reads the specified number of bits from read buffer.
            /// Flags an error in case end_of_stream or n_bits is more than the allowed limit
            /// of VP8L_MAX_NUM_BIT_READ (inclusive).
            /// Flags eos_ if this read attempt is going to cross the read buffer.
            /// </summary>
            public uint ReadBits (int n_bits)
            {
                // Flag an error if end_of_stream or n_bits is more than allowed limit.
                if (!eos_ && n_bits <= MaxNumBitRead)
                {
                    uint val = PrefetchBits() & kBitMask[n_bits];
                    int new_bits = bit_pos_ + n_bits;
                    bit_pos_ = new_bits;
                    ShiftBytes();
                    return val;
                }
                else
                {
                    SetEndOfStream();
                    return 0;
                }
            }

            /// <summary>
            /// Return the prefetched bits, so they can be looked up.
            /// </summary>
            public uint PrefetchBits ()
            {
                return (uint)(val_ >> (bit_pos_ & (LBITS - 1)));
            }

            /// <summary>
            /// For jumping over a number of bits in the bit stream when accessed with
            /// VP8LPrefetchBits and VP8LFillBitWindow.
            /// </summary>
            public void SetBitPos (int val)
            {
                bit_pos_ = val;
                eos_ = IsEndOfStream;
            }

            public void SkipBits (int bits)
            {
                SetBitPos (bit_pos_ + bits);
            }

            /// <summary>
            /// Advances the read buffer by 4 bytes to make room for reading next 32 bits.
            /// Speed critical, but infrequent part of the code can be non-inlined.
            /// </summary>

            public void FillBitWindow ()
            {
                if (bit_pos_ >= WBITS)
                    DoFillBitWindow();
            }

            void DoFillBitWindow ()
            {
                if (pos_ + sizeof(ulong) < len_)
                {
                    val_ >>= WBITS;
                    bit_pos_ -= WBITS;
                    val_ |= (ulong)LittleEndian.ToUInt32 (buf_, (int)pos_) << (LBITS - WBITS);
                    pos_ += LOG8_WBITS;
                    return;
                }
                ShiftBytes();       // Slow path.
            }

            void ShiftBytes ()
            {
                while (bit_pos_ >= 8 && pos_ < len_)
                {
                    val_ >>= 8;
                    val_ |= ((ulong)buf_[pos_]) << (LBITS - 8);
                    ++pos_;
                    bit_pos_ -= 8;
                }
                if (IsEndOfStream)
                    SetEndOfStream();
            }

            void SetEndOfStream ()
            {
                eos_ = true;
                bit_pos_ = 0;  // To avoid undefined behaviour with shifts.
            }

            static readonly uint[] kBitMask = new uint[MaxNumBitRead + 1]
            {
                0,
                0x000001, 0x000003, 0x000007, 0x00000f,
                0x00001f, 0x00003f, 0x00007f, 0x0000ff,
                0x0001ff, 0x0003ff, 0x0007ff, 0x000fff,
                0x001fff, 0x003fff, 0x007fff, 0x00ffff,
                0x01ffff, 0x03ffff, 0x07ffff, 0x0fffff,
                0x1fffff, 0x3fffff, 0x7fffff, 0xffffff
            };
        }

        // Memory needed for lookup tables of one Huffman tree group. Red, blue, alpha
        // and distance alphabets are constant (256 for red, blue and alpha, 40 for
        // distance) and lookup table sizes for them in worst case are 630 and 410
        // respectively. Size of green alphabet depends on color cache size and is equal
        // to 256 (green component values) + 24 (length prefix values)
        // + color_cache_size (between 0 and 2048).
        // All values computed for 8-bit first level lookup with Mark Adler's tool:
        // http://www.hdfgroup.org/ftp/lib-external/zlib/zlib-1.2.5/examples/enough.c

        const int FIXED_TABLE_SIZE = 630 * 3 + 410;
        static readonly int[] kTableSize = {
            FIXED_TABLE_SIZE + 654,
            FIXED_TABLE_SIZE + 656,
            FIXED_TABLE_SIZE + 658,
            FIXED_TABLE_SIZE + 662,
            FIXED_TABLE_SIZE + 670,
            FIXED_TABLE_SIZE + 686,
            FIXED_TABLE_SIZE + 718,
            FIXED_TABLE_SIZE + 782,
            FIXED_TABLE_SIZE + 912,
            FIXED_TABLE_SIZE + 1168,
            FIXED_TABLE_SIZE + 1680,
            FIXED_TABLE_SIZE + 2704
        };

        static readonly ushort[] kAlphabetSize = new ushort[Huffman.CodesPerMetaCode]
        {
            Huffman.NumLiteralCodes + Huffman.NumLengthCodes,
            Huffman.NumLiteralCodes,
            Huffman.NumLiteralCodes,
            Huffman.NumLiteralCodes,
            Huffman.NumDistanceCodes
        };

        const int NumCodeLengthCodes = 19;
        static readonly byte[] kCodeLengthCodeOrder = new byte[NumCodeLengthCodes] {
            17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15
        };

        static readonly byte[] kLiteralMap = new byte[Huffman.CodesPerMetaCode] {
            0, 1, 1, 1, 0
        };

        const int CodeToPlaneCodes = 120;
        static readonly byte[] kCodeToPlane = new byte[CodeToPlaneCodes]
        {
            0x18, 0x07, 0x17, 0x19, 0x28, 0x06, 0x27, 0x29, 0x16, 0x1a,
            0x26, 0x2a, 0x38, 0x05, 0x37, 0x39, 0x15, 0x1b, 0x36, 0x3a,
            0x25, 0x2b, 0x48, 0x04, 0x47, 0x49, 0x14, 0x1c, 0x35, 0x3b,
            0x46, 0x4a, 0x24, 0x2c, 0x58, 0x45, 0x4b, 0x34, 0x3c, 0x03,
            0x57, 0x59, 0x13, 0x1d, 0x56, 0x5a, 0x23, 0x2d, 0x44, 0x4c,
            0x55, 0x5b, 0x33, 0x3d, 0x68, 0x02, 0x67, 0x69, 0x12, 0x1e,
            0x66, 0x6a, 0x22, 0x2e, 0x54, 0x5c, 0x43, 0x4d, 0x65, 0x6b,
            0x32, 0x3e, 0x78, 0x01, 0x77, 0x79, 0x53, 0x5d, 0x11, 0x1f,
            0x64, 0x6c, 0x42, 0x4e, 0x76, 0x7a, 0x21, 0x2f, 0x75, 0x7b,
            0x31, 0x3f, 0x63, 0x6d, 0x52, 0x5e, 0x00, 0x74, 0x7c, 0x41,
            0x4f, 0x10, 0x20, 0x62, 0x6e, 0x30, 0x73, 0x7d, 0x51, 0x5f,
            0x40, 0x72, 0x7e, 0x61, 0x6f, 0x50, 0x71, 0x7f, 0x60, 0x70
        };
    }
}
