//! \file       Decoder.cs
//! \date       Mon Apr 11 02:53:27 2016
//! \brief      Google WEBP decoder implementation.
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
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Google
{
    internal sealed class WebPDecoder : IDisposable
    {
        BinaryReader    m_input;
        byte[]          m_output;
        byte[]          m_alpha_data;   // compressed alpha data (if present)
        byte[]          m_alpha_plane;  // output. Persistent, contains the whole data.
        WebPMetaData    m_info;
        int             m_stride;
        VP8Io           m_io;

        public PixelFormat Format { get; private set; }
        public byte[]      Output { get { return m_output; } }
        public int         Stride { get { return m_stride; } }
        public byte[]       Cache { get { return m_cache; } }
        public byte[]  AlphaPlane { get { return m_alpha_plane; } }

        public WebPDecoder (Stream input, WebPMetaData info)
        {
            m_input = new ArcView.Reader (input);
            m_info = info;
            m_stride = (int)info.Width * 4;
            m_output = new byte[m_stride * (int)info.Height];
            m_io = new VP8Io();
            if (0 != m_info.AlphaOffset)
            {
                m_input.BaseStream.Position = m_info.AlphaOffset;
                m_alpha_data = m_input.ReadBytes (m_info.AlphaSize);
                m_alpha_plane = new byte[info.Width * info.Height];
                Format = PixelFormats.Bgra32;
            }
            else if (m_info.HasAlpha)
            {
                Format = PixelFormats.Bgra32;
            }
            else
            {
                Format = PixelFormats.Bgr32;
            }
        }

        // Macroblock to process/filter, depending on cropping and filter_type.
        int tl_mb_x, tl_mb_y;  // top-left MB that must be in-loop filtered
        int br_mb_x, br_mb_y;  // last bottom-right MB that must be decoded
        int mb_x_, mb_y_;      // current position, in macroblock units

        int     m_num_parts = 1;
        BitReader   m_br;

        public void Decode ()
        {
            m_input.BaseStream.Position = m_info.DataOffset;
            if (m_info.IsLossless)
            {
                m_io.opaque = m_output;
                var ld = new LosslessDecoder();
                ld.Init (m_input, m_info.DataSize, m_io);
                if (!ld.DecodeImage())
                    throw new InvalidFormatException();
            }
            else
            {
                GetHeaders();
                EnterCritical();
                InitFrame();
                ParseFrame();
            }
        }

        int ReadInt24 ()
        {
            int v = m_input.ReadByte();
            v |= m_input.ReadByte() << 8;
            v |= m_input.ReadByte() << 16;
            return v;
        }

        internal class FrameHeader
        {
            public bool KeyFrame;
            public int  Profile;
            public bool Show;
            public int  PartitionLength;
        }

        int mb_w_, mb_h_;

        byte[] m_cache;
        internal int cache_y_;
        internal int cache_u_;
        internal int cache_v_;
        internal int cache_y_stride_;
        internal int cache_uv_stride_;

        int     m_filter_type;
        bool    m_use_skip_proba;
        int     m_skip_p;

        PictureHeader   m_pic_hdr = new PictureHeader();
        FrameHeader     m_frame_header = new FrameHeader();

        SegmentHeader   m_segment_hdr = new SegmentHeader();
        FilterHeader    m_filter_hdr = new FilterHeader();
        Proba           m_proba = new Proba();

        BitReader[]     m_parts = new BitReader[MaxNumPartitions];
        QuantMatrix[]   m_dqm = new QuantMatrix[NumMbSegments];

        void EnterCritical ()
        {
            // Define the area where we can skip in-loop filtering, in case of cropping.
            //
            // 'Simple' filter reads two luma samples outside of the macroblock
            // and filters one. It doesn't filter the chroma samples. Hence, we can
            // avoid doing the in-loop filtering before crop_top/crop_left position.
            // For the 'Complex' filter, 3 samples are read and up to 3 are filtered.
            // Means: there's a dependency chain that goes all the way up to the
            // top-left corner of the picture (MB #0). We must filter all the previous
            // macroblocks.
            int extra_pixels = kFilterExtraRows[m_filter_type];
            tl_mb_x = 0;
            tl_mb_y = 0;

            // We need some 'extra' pixels on the right/bottom.
            br_mb_y = Math.Min (mb_h_, (m_io.height + 15 + extra_pixels) >> 4);
            br_mb_x = Math.Min (mb_w_, (m_io.width  + 15 + extra_pixels) >> 4);
            PrecomputeFilterStrengths();
        }

        void PrecomputeFilterStrengths ()
        {
            if (m_filter_type <= 0)
                return;
            for (int s = 0; s < NumMbSegments; ++s)
            {
                // First, compute the initial level
                int base_level;
                if (m_segment_hdr.UseSegment)
                {
                    base_level = m_segment_hdr.FilterStrength[s];
                    if (!m_segment_hdr.AbsoluteDelta)
                        base_level += m_filter_hdr.Level;
                }
                else
                {
                    base_level = m_filter_hdr.Level;
                }
                for (int i4x4 = 0; i4x4 <= 1; ++i4x4)
                {
                    int level = base_level;
                    if (m_filter_hdr.UseLfDelta)
                    {
                        level += m_filter_hdr.RefLfDelta[0];
                        if (i4x4 > 0)
                            level += m_filter_hdr.ModeLfDelta[0];
                    }
                    level = (level < 0) ? 0 : (level > 63) ? 63 : level;
                    if (level > 0)
                    {
                        int ilevel = level;
                        if (m_filter_hdr.Sharpness > 0)
                        {
                            if (m_filter_hdr.Sharpness > 4)
                                ilevel >>= 2;
                            else
                                ilevel >>= 1;
                            if (ilevel > 9 - m_filter_hdr.Sharpness)
                                ilevel = 9 - m_filter_hdr.Sharpness;
                        }
                        if (ilevel < 1) ilevel = 1;
                        m_fstrengths[s,i4x4].f_ilevel_ = (byte)ilevel;
                        m_fstrengths[s,i4x4].f_limit_ = (byte)(2 * level + ilevel);
                        m_fstrengths[s,i4x4].hev_thresh_ = (byte)((level >= 40) ? 2 : (level >= 15) ? 1 : 0);
                    }
                    else
                    {
                        m_fstrengths[s,i4x4].f_limit_ = 0;  // no filtering
                    }
                    m_fstrengths[s,i4x4].f_inner_ = (byte)i4x4;
                }
            }
        }

        void GetHeaders ()
        {
            int chunk_size = m_info.DataSize;

            int bits = ReadInt24();
            chunk_size -= 3;
            m_frame_header.KeyFrame     = 0 == (bits & 1);
            m_frame_header.Profile      = (bits >> 1) & 7;
            m_frame_header.Show         = 0 != (bits & (1u << 4));
            m_frame_header.PartitionLength = bits >> 5;
            if (m_frame_header.Profile > 3)
                throw new InvalidFormatException ("Incorrect keyframe parameters.");
            if (!m_frame_header.Show)
                throw new InvalidFormatException ("Frame not displayable.");

            if (m_frame_header.KeyFrame)
            {
                // Paragraph 9.2
                if (!CheckSignature())
                    throw new InvalidFormatException ("Bad code word");
                ushort w = m_input.ReadUInt16();
                ushort h = m_input.ReadUInt16();
                m_pic_hdr.Width = (ushort)(w & 0x3fff);
                m_pic_hdr.XScale = (byte)(w >> 14);   // ratio: 1, 5/4 5/3 or 2
                m_pic_hdr.Height = (ushort)(h & 0x3fff);
                m_pic_hdr.YScale = (byte)(h >> 14);
                chunk_size -= 7;

                mb_w_ = (m_pic_hdr.Width + 15) >> 4;
                mb_h_ = (m_pic_hdr.Height + 15) >> 4;

                // Setup default output area (can be later modified during m_io.setup())
                m_io.width = m_pic_hdr.Width;
                m_io.height = m_pic_hdr.Height;

                m_io.mb_w = m_io.width;
                m_io.mb_h = m_io.height;

                m_proba.Reset();
                m_segment_hdr.Reset();
            }

            if (m_frame_header.PartitionLength > chunk_size)
                throw new InvalidFormatException ("bad partition length");

            m_br = new BitReader (m_input, m_frame_header.PartitionLength);
            chunk_size -= m_frame_header.PartitionLength;

            if (m_frame_header.KeyFrame)
            {
                m_pic_hdr.Colorspace = (byte)m_br.GetNextBit();
                m_pic_hdr.ClampType  = (byte)m_br.GetNextBit();
            }
            if (!ParseSegmentHeader (m_br))
                throw new InvalidFormatException ("Cannot parse segment header");
            // Filter specs
            if (!ParseFilterHeader (m_br))
                throw new InvalidFormatException ("Cannot parse filter header");

            if (!ParsePartitions (m_br, chunk_size))
                throw new InvalidFormatException ("Cannot parse partitions");

            for (int i = 0; i < m_dqm.Length; ++i)
                m_dqm[i] = new QuantMatrix();

            // quantizer change
            ParseQuant (m_br);

            // Frame buffer marking
            if (!m_frame_header.KeyFrame)
                throw new InvalidFormatException ("Not a key frame");

            m_br.GetNextBit();   // ignore the value of update_proba_
            ParseProba (m_br);
        }

        bool CheckSignature ()
        {
            if (m_input.ReadByte() != 0x9D)
                return false;
            if (m_input.ReadByte() != 1)
                return false;
            return m_input.ReadByte() == 0x2A;
        }

        bool ParseSegmentHeader (BitReader br)
        {
            m_segment_hdr.UseSegment = br.GetNextBit() != 0;
            if (m_segment_hdr.UseSegment)
            {
                m_segment_hdr.UpdateMap = br.GetNextBit() != 0;
                if (0 != br.GetNextBit())
                {   // update data
                    m_segment_hdr.AbsoluteDelta = br.GetNextBit() != 0;
                    for (int s = 0; s < NumMbSegments; ++s)
                    {
                        m_segment_hdr.Quantizer[s] = (byte)(br.GetNextBit() != 0 ? br.GetSignedValue (7) : 0);
                    }
                    for (int s = 0; s < NumMbSegments; ++s)
                    {
                        m_segment_hdr.FilterStrength[s] = (byte)(br.GetNextBit() != 0 ? br.GetSignedValue (6) : 0);
                    }
                }
                if (m_segment_hdr.UpdateMap)
                {
                    for (int s = 0; s < TreeProbs; ++s)
                    {
                        m_proba.Segments[s] = (byte)(br.GetNextBit() != 0 ? br.GetBits (8) : 255);
                    }
                }
            }
            else
            {
                m_segment_hdr.UpdateMap = false;
            }
            return !br.Eof;
        }

        bool ParseFilterHeader (BitReader br)
        {
            m_filter_hdr.Simple     = br.GetNextBit() != 0;
            m_filter_hdr.Level      = br.GetBits (6);
            m_filter_hdr.Sharpness  = br.GetBits (3);
            m_filter_hdr.UseLfDelta = br.GetNextBit() != 0;
            if (m_filter_hdr.UseLfDelta)
            {
                if (0 != br.GetNextBit())
                {   // update lf-delta?
                    for (int i = 0; i < NumRefLfDeltas; ++i)
                    {
                        if (0 != br.GetNextBit())
                            m_filter_hdr.RefLfDelta[i] = br.GetSignedValue (6);
                    }
                    for (int i = 0; i < NumModeLfDeltas; ++i)
                    {
                        if (0 != br.GetNextBit())
                            m_filter_hdr.ModeLfDelta[i] = br.GetSignedValue (6);
                    }
                }
            }
            m_filter_type = (0 == m_filter_hdr.Level) ? 0 : m_filter_hdr.Simple ? 1 : 2;
            return !br.Eof;
        }

        bool ParsePartitions (BitReader br, int size)
        {
            long part_end = m_input.BaseStream.Position + size;
            int size_left = size;
            m_num_parts = 1 << br.GetBits (2);
            int last_part = m_num_parts - 1;
            if (size < 3 * last_part)
                return false;
            long part_start = m_input.BaseStream.Position + last_part * 3;
            size_left -= last_part * 3;
            for (int p = 0; p < last_part; ++p)
            {
                int psize = ReadInt24();
                var sz_pos = m_input.BaseStream.Position;
                if (psize > size_left) psize = size_left;
                m_input.BaseStream.Position = part_start;
                m_parts[p] = new BitReader (m_input, psize);
                part_start += psize;
                size_left -= psize;
                m_input.BaseStream.Position = sz_pos;
            }
            m_input.BaseStream.Position = part_start;
            m_parts[last_part] = new BitReader (m_input, size_left);
            return part_start < part_end;
        }

        static int Clip (int v, int M)
        {
            return v < 0 ? 0 : v > M ? M : v;
        }

        void ParseQuant (BitReader br)
        {
            int base_q0 = br.GetBits (7);
            int dqy1_dc = br.GetNextBit() != 0 ? br.GetSignedValue (4) : 0;
            int dqy2_dc = br.GetNextBit() != 0 ? br.GetSignedValue (4) : 0;
            int dqy2_ac = br.GetNextBit() != 0 ? br.GetSignedValue (4) : 0;
            int dquv_dc = br.GetNextBit() != 0 ? br.GetSignedValue (4) : 0;
            int dquv_ac = br.GetNextBit() != 0 ? br.GetSignedValue (4) : 0;

            for (int i = 0; i < NumMbSegments; ++i)
            {
                int q;
                if (m_segment_hdr.UseSegment)
                {
                    q = m_segment_hdr.Quantizer[i];
                    if (!m_segment_hdr.AbsoluteDelta)
                        q += base_q0;
                }
                else if (i > 0)
                {
                    m_dqm[i] = m_dqm[0];
                    continue;
                }
                else
                {
                    q = base_q0;
                }
                var m = m_dqm[i];
                m.y1_mat[0] = kDcTable[Clip (q + dqy1_dc, 127)];
                m.y1_mat[1] = kAcTable[Clip (q + 0,       127)];

                m.y2_mat[0] = kDcTable[Clip (q + dqy2_dc, 127)] * 2;
                // For all x in [0..284], x*155/100 is bitwise equal to (x*101581) >> 16.
                // The smallest precision for that is '(x*6349) >> 12' but 16 is a good
                // word size.
                m.y2_mat[1] = (kAcTable[Clip (q + dqy2_ac, 127)] * 101581) >> 16;
                if (m.y2_mat[1] < 8) m.y2_mat[1] = 8;

                m.uv_mat[0] = kDcTable[Clip (q + dquv_dc, 117)];
                m.uv_mat[1] = kAcTable[Clip (q + dquv_ac, 127)];

                m.uv_quant = q + dquv_ac;   // for dithering strength evaluation
            }
        }

        void ParseProba (BitReader br)
        {
            for (int t = 0; t < NumTypes; ++t)
            {
                for (int b = 0; b < NumBands; ++b)
                for (int c = 0; c < NumCtx; ++c)
                for (int p = 0; p < NumProbas; ++p)
                {
                    int v = br.GetBit (CoeffsUpdateProba[t,b,c,p]) != 0 ?
                        br.GetBits (8) : CoeffsProba0[t,b,c,p];
                    m_proba.Bands[t,b].Probas[c][p] = (byte)v;
                }
                for (int b = 0; b < 16 + 1; ++b)
                {
                    m_proba.BandsPtr[t][b] = m_proba.Bands[t,kBands[b]];
                }
            }
            m_use_skip_proba = br.GetNextBit() != 0;
            if (m_use_skip_proba)
                m_skip_p = br.GetBits (8);
        }

        void InitFrame ()
        {
            AllocateMemory();
            m_io.Init (this);
            DspInit();
        }

        void ParseFrame ()
        {
            for (mb_y_ = 0; mb_y_ < br_mb_y; ++mb_y_)
            {
                // Parse bitstream for this row.
                var token_br = m_parts[mb_y_ & (m_num_parts - 1)];
                if (!ParseIntraModeRow (m_br))
                    throw new InvalidFormatException ("Premature end-of-partition0 encountered");
                for (; mb_x_ < mb_w_; ++mb_x_)
                {
                    if (!DecodeMB (token_br))
                        throw new InvalidFormatException ("Premature end-of-file encountered");
                }
                InitScanline();   // Prepare for next scanline

                // Reconstruct, filter and emit the row.
                if (!ProcessRow())
                    throw new InvalidFormatException ("Output aborted");
            }
        }

        bool ParseIntraModeRow (BitReader br)
        {
            for (int mb_x = 0; mb_x < mb_w_; ++mb_x)
                ParseIntraMode (br, mb_x);
            return !m_br.Eof;
        }

        internal struct FilterInfo
        {
            public byte f_limit_;      // filter limit in [3..189], or 0 if no filtering
            public byte f_ilevel_;     // inner limit in [1..63]
            public byte f_inner_;      // do inner filtering?
            public byte hev_thresh_;   // high edge variance threshold in [0..2]
        }

        FilterInfo[] m_filter_info;     // filter strength info
        FilterInfo[,] m_fstrengths = new FilterInfo[NumMbSegments,2];

        void ParseIntraMode (BitReader br, int mb_x)
        {
            int top = 4 * mb_x; // within intra_t
            int left = 0; // within intra_l
            var block = m_mb_data[mb_x];

            // Note: we don't save segment map (yet), as we don't expect
            // to decode more than 1 keyframe.
            if (m_segment_hdr.UpdateMap)
            {
                // Hardcoded tree parsing
                block.segment_ = 0 == br.GetBit (m_proba.Segments[0])
                                ? (byte)br.GetBit (m_proba.Segments[1])
                                : (byte)(2 + br.GetBit (m_proba.Segments[2]));
            }
            else
            {
                block.segment_ = 0;  // default for intra
            }
            if (m_use_skip_proba)
                block.skip_ = br.GetBit (m_skip_p) != 0;

            block.is_i4x4_ = 0 == br.GetBit (145);   // decide for B_PRED first
            if (!block.is_i4x4_)
            {
                // Hardcoded 16x16 intra-mode decision tree.
                int ymode =
                        0 != br.GetBit (156) ? (0 != br.GetBit (128) ? TM_PRED : H_PRED)
                                             : (0 != br.GetBit (163) ? V_PRED : DC_PRED);
                block.imodes_[0] = (byte)ymode;
                for (int i = 0; i < 4; ++i)
                {
                    m_intra_t[top+i] = (byte)ymode;
                    m_intra_l[left+i] = (byte)ymode;
                }
            }
            else
            {
                int modes = 0; // within block.imodes_;
                for (int y = 0; y < 4; ++y)
                {
                    int ymode = m_intra_l[left + y];
                    for (int x = 0; x < 4; ++x)
                    {
                        int prob = m_intra_t[top + x];
                        int i = kYModesIntra4[br.GetBit (kBModesProba[prob,ymode,0])];
                        while (i > 0)
                            i = kYModesIntra4[2 * i + br.GetBit (kBModesProba[prob,ymode,i])];
                        ymode = -i;
                        m_intra_t[top + x] = (byte)ymode;
                    }
                    Buffer.BlockCopy (m_intra_t, top, block.imodes_, modes, 4);
                    modes += 4;
                    m_intra_l[left + y] = (byte)ymode;
                }
            }
            // Hardcoded UVMode decision tree
            block.uvmode_ = 0 == br.GetBit (142) ? DC_PRED
                          : 0 == br.GetBit (114) ? V_PRED
                          : 0 != br.GetBit (183) ? TM_PRED : H_PRED;
        }

        bool DecodeMB (BitReader token_br)
        {
            int left = m_mb_info - 1;
            int mb = m_mb_info + mb_x_;
            var block = m_mb_data[mb_x_];
            bool skip = m_use_skip_proba && block.skip_;

            if (!skip)
            {
                skip = ParseResiduals (mb, token_br);
            }
            else
            {
                m_mb[left].nz_ = m_mb[mb].nz_ = 0;
                if (!block.is_i4x4_)
                    m_mb[left].nz_dc_ = m_mb[mb].nz_dc_ = 0;
                block.non_zero_y_ = 0;
                block.non_zero_uv_ = 0;
            }
            if (m_filter_type > 0)    // store filter info
            {
                int finfo = mb_x_;
                m_filter_info[finfo] = m_fstrengths[block.segment_, block.is_i4x4_ ? 1 : 0];
                m_filter_info[finfo].f_inner_ |= (byte)(skip ? 0 : 1);
            }
            return !token_br.Eof;
        }

        bool ParseResiduals (int mb, BitReader token_br)
        {
            var bands = m_proba.BandsPtr;
            BandProbas[] ac_proba;
            var block = m_mb_data[mb_x_];
            var q = m_dqm[block.segment_];
            int dst = 0; // block->coeffs_
            int left_mb = m_mb_info - 1;
            uint non_zero_y = 0;
            uint non_zero_uv = 0;
            int x, y;
            uint out_t_nz, out_l_nz;
            int first;

            for (int i = 0; i < 384; ++i)
                block.coeffs_[i] = 0;
            if (!block.is_i4x4_)      // parse DC
            {
                var dc = new short[16];
                int ctx = m_mb[mb].nz_dc_ + m_mb[left_mb].nz_dc_;
                int nz = GetCoeffs (token_br, bands[1], ctx, q.y2_mat, 0, dc, 0);
                m_mb[mb].nz_dc_ = m_mb[left_mb].nz_dc_ = (byte)(nz > 0 ? 1 : 0);
                if (nz > 1)
                {   // more than just the DC -> perform the full transform
                    TransformWHT (dc, block.coeffs_, dst);
                }
                else
                {   // only DC is non-zero -> inlined simplified transform
                    int dc0 = (dc[0] + 3) >> 3;
                    for (int i = 0; i < 16 * 16; i += 16)
                        block.coeffs_[dst+i] = (short)dc0;
                }
                first = 1;
                ac_proba = bands[0];
            }
            else
            {
                first = 0;
                ac_proba = bands[3];
            }

            byte tnz = (byte)(m_mb[mb].nz_      & 0xF);
            byte lnz = (byte)(m_mb[left_mb].nz_ & 0xF);
            for (y = 0; y < 4; ++y)
            {
                int l = lnz & 1;
                uint nz_coeffs = 0;
                for (x = 0; x < 4; ++x)
                {
                    int ctx = l + (tnz & 1);
                    int nz = GetCoeffs (token_br, ac_proba, ctx, q.y1_mat, first, block.coeffs_, dst);
                    l = nz > first ? 1 : 0;
                    tnz = (byte)((tnz >> 1) | (l << 7));
                    nz_coeffs = NzCodeBits (nz_coeffs, nz, block.coeffs_[dst] != 0 ? 1 : 0);
                    dst += 16;
                }
                tnz >>= 4;
                lnz = (byte)((lnz >> 1) | (l << 7));
                non_zero_y = (non_zero_y << 8) | nz_coeffs;
            }
            out_t_nz = tnz;
            out_l_nz = (uint)(lnz >> 4);

            for (int ch = 0; ch < 4; ch += 2)
            {
                uint nz_coeffs = 0;
                tnz = (byte)(m_mb[mb].nz_      >> (4 + ch));
                lnz = (byte)(m_mb[left_mb].nz_ >> (4 + ch));
                for (y = 0; y < 2; ++y)
                {
                    int l = lnz & 1;
                    for (x = 0; x < 2; ++x)
                    {
                        int ctx = l + (tnz & 1);
                        int nz = GetCoeffs (token_br, bands[2], ctx, q.uv_mat, 0, block.coeffs_, dst);
                        l = nz > 0 ? 1 : 0;
                        tnz = (byte)((tnz >> 1) | (l << 3));
                        nz_coeffs = NzCodeBits (nz_coeffs, nz, block.coeffs_[dst] != 0 ? 1 : 0);
                        dst += 16;
                    }
                    tnz >>= 2;
                    lnz = (byte)((lnz >> 1) | (l << 5));
                }
                // Note: we don't really need the per-4x4 details for U/V blocks.
                non_zero_uv |= nz_coeffs << (4 * ch);
                out_t_nz |= (uint)(tnz << 4) << ch;
                out_l_nz |= (uint)(lnz & 0xf0) << ch;
            }
            m_mb[mb].nz_      = (byte)out_t_nz;
            m_mb[left_mb].nz_ = (byte)out_l_nz;

            block.non_zero_y_ = non_zero_y;
            block.non_zero_uv_ = non_zero_uv;

            return 0 == (non_zero_y | non_zero_uv);  // will be used for further optimization
        }

        static uint NzCodeBits (uint nz_coeffs, int nz, int dc_nz)
        {
            nz_coeffs <<= 2;
            nz_coeffs |= (nz > 3) ? 3u : (nz > 1) ? 2u : (uint)dc_nz;
            return nz_coeffs;
        }

        int GetCoeffs (BitReader br, BandProbas[] prob, int ctx, int[] dq, int n, short[] out_ptr, int dst)
        {
            var p = prob[n].Probas[ctx];
            for (; n < 16; ++n)
            {
                if (0 == br.GetBit (p[0]))
                    return n;  // previous coeff was last non-zero coeff

                while (0 == br.GetBit (p[1]))         // sequence of zero coeffs
                {
                    p = prob[++n].Probas[0];
                    if (16 == n) return 16;
                }
                // non zero coeff
                var p_ctx = prob[n + 1].Probas;
                int v;
                if (0 == br.GetBit (p[2]))
                {
                    v = 1;
                    p = p_ctx[1];
                }
                else
                {
                    v = br.GetLargeValue (p);
                    p = p_ctx[2];
                }
                out_ptr[dst+kZigzag[n]] = (short)(br.GetSigned (v) * dq[n > 0 ? 1 : 0]);
            }
            return 16;
        }

        bool m_filter_row;

        bool ProcessRow ()
        {
            m_filter_row = (m_filter_type > 0) && (mb_y_ >= tl_mb_y) && (mb_y_ <= br_mb_y);
            ReconstructRow();
            return FinishRow();
        }

        void ReconstructRow ()
        {
            int j;
            int cache_id = 0;
            int y_dst = Y_OFF; // within m_yuv_b
            int u_dst = U_OFF;
            int v_dst = V_OFF;

            // Initialize left-most block.
            for (j = 0; j < 16; ++j)
            {
                m_yuv_b[y_dst + j * BPS - 1] = 129;
            }
            for (j = 0; j < 8; ++j)
            {
                m_yuv_b[u_dst + j * BPS - 1] = 129;
                m_yuv_b[v_dst + j * BPS - 1] = 129;
            }

            int mb_y = mb_y_;
            // Init top-left sample on left column too.
            if (mb_y > 0)
            {
                m_yuv_b[y_dst - 1 - BPS] = m_yuv_b[u_dst - 1 - BPS] = m_yuv_b[v_dst - 1 - BPS] = 129;
            }
            else
            {
                // we only need to do this init once at block (0,0).
                // Afterward, it remains valid for the whole topmost row.
                for (int i = 0; i < 16+4+1; ++i)
                    m_yuv_b[y_dst - BPS - 1 + i] = 127;
                for (int i = 0; i < 8+1; ++i)
                {
                    m_yuv_b[u_dst - BPS - 1 + i] = 127;
                    m_yuv_b[v_dst - BPS - 1 + i] = 127;
                }
            }

            // Reconstruct one row.
            for (int mb_x = 0; mb_x < mb_w_; ++mb_x)
            {
                var block = m_mb_data[mb_x];

                // Rotate in the left samples from previously decoded block. We move four
                // pixels at a time for alignment reason, and because of in-loop filter.
                if (mb_x > 0)
                {
                    for (j = -1; j < 16; ++j)
                    {
                        Buffer.BlockCopy (m_yuv_b, y_dst + j * BPS + 12, m_yuv_b, y_dst + j * BPS - 4, 4);
                    }
                    for (j = -1; j < 8; ++j)
                    {
                        Buffer.BlockCopy (m_yuv_b, u_dst + j * BPS + 4, m_yuv_b, u_dst + j * BPS - 4, 4);
                        Buffer.BlockCopy (m_yuv_b, v_dst + j * BPS + 4, m_yuv_b, v_dst + j * BPS - 4, 4);
                    }
                }
                // bring top samples into the cache
                var top_yuv = mb_x; // within yuv_t_
                var coeffs = block.coeffs_;
                uint bits = block.non_zero_y_;

                if (mb_y > 0)
                {
                    Buffer.BlockCopy (m_yuv_t[top_yuv].y, 0, m_yuv_b, y_dst - BPS, 16);
                    Buffer.BlockCopy (m_yuv_t[top_yuv].u, 0, m_yuv_b, u_dst - BPS, 8);
                    Buffer.BlockCopy (m_yuv_t[top_yuv].v, 0, m_yuv_b, v_dst - BPS, 8);
                }

                int pred_func;
                // predict and add residuals
                if (block.is_i4x4_)     // 4x4
                {
                    int top_right = y_dst - BPS + 16;
                    if (mb_y > 0)
                    {
                        if (mb_x >= mb_w_ - 1)      // on rightmost border
                        {
                            byte v = m_yuv_t[top_yuv].y[15];
                            m_yuv_b[top_right] = v;
                            m_yuv_b[top_right+1] = v;
                            m_yuv_b[top_right+2] = v;
                            m_yuv_b[top_right+3] = v;
                        }
                        else
                        {
                            Buffer.BlockCopy (m_yuv_t[top_yuv+1].y, 0, m_yuv_b, top_right, 4);
                        }
                    }
                    // replicate the top-right pixels below
                    Buffer.BlockCopy (m_yuv_b, top_right, m_yuv_b, top_right+BPS*4,   4);
                    Buffer.BlockCopy (m_yuv_b, top_right, m_yuv_b, top_right+BPS*8, 4);
                    Buffer.BlockCopy (m_yuv_b, top_right, m_yuv_b, top_right+BPS*12, 4);

                    // predict and add residuals for all 4x4 blocks in turn.
                    for (int n = 0; n < 16; ++n, bits <<= 2)
                    {
                        int dst = y_dst + kScan[n];
                        PredLuma4[block.imodes_[n]] (m_yuv_b, dst);
                        DoTransform (bits, coeffs, n * 16, m_yuv_b, dst);
                    }
                }
                else // 16x16
                {
                    pred_func = CheckMode (mb_x, mb_y, block.imodes_[0]);
                    PredLuma16[pred_func] (m_yuv_b, y_dst);
                    if (bits != 0)
                    {
                        for (int n = 0; n < 16; ++n, bits <<= 2)
                            DoTransform (bits, coeffs, n * 16, m_yuv_b, y_dst + kScan[n]);
                    }
                }
                // Chroma
                uint bits_uv = block.non_zero_uv_;
                pred_func = CheckMode (mb_x, mb_y, block.uvmode_);
                PredChroma8[pred_func] (m_yuv_b, u_dst);
                PredChroma8[pred_func] (m_yuv_b, v_dst);
                DoUVTransform(bits_uv >> 0, coeffs, 16 * 16, m_yuv_b, u_dst);
                DoUVTransform(bits_uv >> 8, coeffs, 20 * 16, m_yuv_b, v_dst);

                // stash away top samples for next block
                if (mb_y < mb_h_ - 1)
                {
                    Buffer.BlockCopy (m_yuv_b, y_dst + 15 * BPS, m_yuv_t[top_yuv].y, 0, 16);
                    Buffer.BlockCopy (m_yuv_b, u_dst + 7 * BPS, m_yuv_t[top_yuv].u, 0, 8);
                    Buffer.BlockCopy (m_yuv_b, v_dst + 7 * BPS, m_yuv_t[top_yuv].v, 0, 8);
                }
                // Transfer reconstructed samples from yuv_b_ cache to final destination.
                int y_offset = cache_id * 16 * cache_y_stride_;
                int uv_offset = cache_id * 8 * cache_uv_stride_;
                int y_out = cache_y_ + mb_x * 16 + y_offset;
                int u_out = cache_u_ + mb_x * 8 + uv_offset;
                int v_out = cache_v_ + mb_x * 8 + uv_offset;
                for (j = 0; j < 16; ++j)
                {
                    Buffer.BlockCopy (m_yuv_b, y_dst + j * BPS, m_cache, y_out + j * cache_y_stride_, 16);
                }
                for (j = 0; j < 8; ++j)
                {
                    Buffer.BlockCopy (m_yuv_b, u_dst + j * BPS, m_cache, u_out + j * cache_uv_stride_, 8);
                    Buffer.BlockCopy (m_yuv_b, v_dst + j * BPS, m_cache, v_out + j * cache_uv_stride_, 8);
                }
            }
        }

        static int CheckMode (int mb_x, int mb_y, int mode)
        {
            if (B_DC_PRED == mode)
            {
                if (0 == mb_x)
                    return (0 == mb_y) ? B_DC_PRED_NOTOPLEFT : B_DC_PRED_NOLEFT;
                else
                    return (0 == mb_y) ? B_DC_PRED_NOTOP : B_DC_PRED;
            }
            return mode;
        }

        void DoTransform (uint bits, short[] src, int src_i, byte[] dst, int dst_i)
        {
            switch (bits >> 30)
            {
            case 3:
                TransformTwo (src, src_i, dst, dst_i, false);
                break;
            case 2:
                TransformAC3 (src, src_i, dst, dst_i);
                break;
            case 1:
                TransformDC (src, src_i, dst, dst_i);
                break;
            default:
                break;
            }
        }

        void DoUVTransform (uint bits, short[] src, int src_i, byte[] dst, int dst_i)
        {
            if (0 != (bits & 0xFF))      // any non-zero coeff at all?
            {
                if (0 != (bits & 0xAA))    // any non-zero AC coefficient?
                    TransformUV (src, src_i, dst, dst_i);   // note we don't use the AC3 variant for U/V
                else
                    TransformDCUV (src, src_i, dst, dst_i);
            }
        }

        static void TransformOne(short[] src, int src_i, byte[] dst, int dst_i)
        {
            var C = new int[4*4];
            var tmp = 0;
            for (int i = 0; i < 4; ++i)      // vertical pass
            {
                int a = src[src_i] + src[src_i+8];    // [-4096, 4094]
                int b = src[src_i] - src[src_i+8];    // [-4095, 4095]
                int c = MUL2(src[src_i+4]) - MUL1(src[src_i+12]);   // [-3783, 3783]
                int d = MUL1(src[src_i+4]) + MUL2(src[src_i+12]);   // [-3785, 3781]
                C[tmp+0] = a + d;   // [-7881, 7875]
                C[tmp+1] = b + c;   // [-7878, 7878]
                C[tmp+2] = b - c;   // [-7878, 7878]
                C[tmp+3] = a - d;   // [-7877, 7879]
                tmp += 4;
                src_i++;
            }
            // Each pass is expanding the dynamic range by ~3.85 (upper bound).
            // The exact value is (2. + (20091 + 35468) / 65536).
            // After the second pass, maximum interval is [-3794, 3794], assuming
            // an input in [-2048, 2047] interval. We then need to add a dst value
            // in the [0, 255] range.
            // In the worst case scenario, the input to clip_8b() can be as large as
            // [-60713, 60968].
            tmp = 0;
            for (int i = 0; i < 4; ++i)      // horizontal pass
            {
                int dc = C[tmp] + 4;
                int a =  dc +  C[tmp+8];
                int b =  dc -  C[tmp+8];
                int c = MUL2(C[tmp+4]) - MUL1(C[tmp+12]);
                int d = MUL1(C[tmp+4]) + MUL2(C[tmp+12]);
                dst[dst_i  ] = clip_8b (dst[dst_i  ] + ((a + d) >> 3));
                dst[dst_i+1] = clip_8b (dst[dst_i+1] + ((b + c) >> 3));
                dst[dst_i+2] = clip_8b (dst[dst_i+2] + ((b - c) >> 3));
                dst[dst_i+3] = clip_8b (dst[dst_i+3] + ((a - d) >> 3));
                tmp++;
                dst_i += BPS;
            }
        }

        static byte clip_8b (int v)
        {
            return (byte)((0 == (v & ~0xFF)) ? v : (v < 0) ? 0 : 255);
        }

        static int MUL1 (int a)
        {
            return ((a * 20091) >> 16) + a;
        }

        static int MUL2 (int a)
        {
            return (a * 35468) >> 16;
        }

        static void TransformTwo (short[] src, int src_i, byte[] dst, int dst_i, bool do_two)
        {
            TransformOne (src, src_i, dst, dst_i);
            if (do_two)
                TransformOne (src, src_i+16, dst, dst_i+4);
        }

        static void TransformAC3 (short[] src, int src_i, byte[] dst, int dst_i)
        {
            int a = src[src_i] + 4;
            int c4 = MUL2(src[src_i+4]);
            int d4 = MUL1(src[src_i+4]);
            int c1 = MUL2(src[src_i+1]);
            int d1 = MUL1(src[src_i+1]);

            // STORE2(0, a + d4, d1, c1);
            int DC = a + d4;
            dst[dst_i  ] = clip_8b (dst[dst_i  ] + ((DC + d1) >> 3));
            dst[dst_i+1] = clip_8b (dst[dst_i+1] + ((DC + c1) >> 3));
            dst[dst_i+2] = clip_8b (dst[dst_i+2] + ((DC - c1) >> 3));
            dst[dst_i+3] = clip_8b (dst[dst_i+3] + ((DC - d1) >> 3));

            // STORE2(1, a + c4, d1, c1);
            DC = a + c4;
            dst[dst_i+BPS  ] = clip_8b (dst[dst_i+BPS  ] + ((DC + d1) >> 3));
            dst[dst_i+BPS+1] = clip_8b (dst[dst_i+BPS+1] + ((DC + c1) >> 3));
            dst[dst_i+BPS+2] = clip_8b (dst[dst_i+BPS+2] + ((DC - c1) >> 3));
            dst[dst_i+BPS+3] = clip_8b (dst[dst_i+BPS+3] + ((DC - d1) >> 3));

            // STORE2(2, a - c4, d1, c1);
            DC = a - c4;
            dst[dst_i+BPS*2  ] = clip_8b (dst[dst_i+BPS*2  ] + ((DC + d1) >> 3));
            dst[dst_i+BPS*2+1] = clip_8b (dst[dst_i+BPS*2+1] + ((DC + c1) >> 3));
            dst[dst_i+BPS*2+2] = clip_8b (dst[dst_i+BPS*2+2] + ((DC - c1) >> 3));
            dst[dst_i+BPS*2+3] = clip_8b (dst[dst_i+BPS*2+3] + ((DC - d1) >> 3));

            // STORE2(3, a - d4, d1, c1);
            DC = a - d4;
            dst[dst_i+BPS*3  ] = clip_8b (dst[dst_i+BPS*3  ] + ((DC + d1) >> 3));
            dst[dst_i+BPS*3+1] = clip_8b (dst[dst_i+BPS*3+1] + ((DC + c1) >> 3));
            dst[dst_i+BPS*3+2] = clip_8b (dst[dst_i+BPS*3+2] + ((DC - c1) >> 3));
            dst[dst_i+BPS*3+3] = clip_8b (dst[dst_i+BPS*3+3] + ((DC - d1) >> 3));
        }

        static void TransformUV (short[] src, int src_i, byte[] dst, int dst_i)
        {
            TransformTwo (src, src_i, dst, dst_i, true);
            TransformTwo (src, src_i + 32, dst, dst_i + 4 * BPS, true);
        }

        static void TransformDC (short[] src, int src_i, byte[] dst, int dst_i)
        {
            int DC = src[src_i] + 4;
            for (int j = 0; j < 4; ++j)
            for (int i = 0; i < 4; ++i)
            {
                int pos = dst_i + i + BPS * j;
                dst[pos] = clip_8b (dst[pos] + (DC >> 3));
            }
        }

        static void TransformDCUV (short[] src, int src_i, byte[] dst, int dst_i)
        {
            if (0 != src[src_i])      TransformDC (src, src_i, dst, dst_i);
            if (0 != src[src_i+16])   TransformDC (src, src_i+16, dst, dst_i+4);
            if (0 != src[src_i+2*16]) TransformDC (src, src_i+2*16, dst, dst_i+4*BPS);
            if (0 != src[src_i+3*16]) TransformDC (src, src_i+3*16, dst, dst_i+4*BPS+4);
        }

        bool FinishRow ()
        {
            bool ok = true;
            int cache_id = 0;
            int extra_y_rows = kFilterExtraRows[m_filter_type];
            int ysize = extra_y_rows * cache_y_stride_;
            int uvsize = (extra_y_rows / 2) * cache_uv_stride_;
            int y_offset = cache_id * 16 * cache_y_stride_;
            int uv_offset = cache_id * 8 * cache_uv_stride_;
            int ydst = cache_y_ - ysize + y_offset;
            int udst = cache_u_ - uvsize + uv_offset;
            int vdst = cache_v_ - uvsize + uv_offset;
            int mb_y = mb_y_;
            bool is_first_row = (mb_y == 0);
            bool is_last_row = mb_y >= br_mb_y - 1;

            if (m_filter_row)
                FilterRow();

            {
                int y_start = mb_y * 16;
                int y_end = (mb_y + 1) * 16;
                if (!is_first_row)
                {
                    y_start -= extra_y_rows;
                    m_io.y = ydst;
                    m_io.u = udst;
                    m_io.v = vdst;
                }
                else
                {
                    m_io.y = cache_y_ + y_offset;
                    m_io.u = cache_u_ + uv_offset;
                    m_io.v = cache_v_ + uv_offset;
                }

                if (!is_last_row)
                    y_end -= extra_y_rows;
                if (y_end > m_io.height)
                    y_end = m_io.height;    // make sure we don't overflow on last row.
                if (m_alpha_data != null && y_start < y_end)
                {
                    m_io.alpha_plane = m_alpha_plane;
                    m_io.a = DecompressAlphaRows (y_start, y_end - y_start);
                    if (-1 == m_io.a)
                        throw new InvalidFormatException ("Could not decode alpha data.");
                }
                if (y_start < 0)
                {
                    int delta_y = -y_start;
                    y_start = 0;
                    m_io.y += cache_y_stride_ * delta_y;
                    m_io.u += cache_uv_stride_ * (delta_y >> 1);
                    m_io.v += cache_uv_stride_ * (delta_y >> 1);
                    if (m_io.alpha_plane != null)
                        m_io.a += m_io.width * delta_y;
                }
                if (y_start < y_end)
                {
                    m_io.mb_y = y_start;
                    m_io.mb_w = m_io.width;
                    m_io.mb_h = y_end - y_start;
                    ok = m_io.Put (this);
                }
            }
            // rotate top samples if needed
            if (cache_id + 1 == num_caches_ && !is_last_row)
            {
                Buffer.BlockCopy (m_cache, ydst + 16 * cache_y_stride_, m_cache, cache_y_ - ysize,  ysize);
                Buffer.BlockCopy (m_cache, udst + 8 * cache_uv_stride_, m_cache, cache_u_ - uvsize, uvsize);
                Buffer.BlockCopy (m_cache, vdst + 8 * cache_uv_stride_, m_cache, cache_v_ - uvsize, uvsize);
            }
            return ok;
        }

        void FilterRow ()
        {
            int mb_y = mb_y_;
            for (int mb_x = tl_mb_x; mb_x < br_mb_x; ++mb_x)
                DoFilter (mb_x, mb_y);
        }

        void DoFilter (int mb_x, int mb_y)
        {
            const int cache_id = 0;
            int y_bps = cache_y_stride_;
            var f_info = m_filter_info[mb_x];
            int y_dst = cache_y_ + cache_id * 16 * y_bps + mb_x * 16; // within m_cache
            int ilevel = f_info.f_ilevel_;
            int limit = f_info.f_limit_;
            if (0 == limit)
                return;
            if (1 == m_filter_type) // simple
            {
                if (mb_x > 0)
                    SimpleHFilter16 (m_cache, y_dst, y_bps, limit + 4);
                if (f_info.f_inner_ != 0)
                    SimpleHFilter16i (m_cache, y_dst, y_bps, limit);
                if (mb_y > 0)
                    SimpleVFilter16 (m_cache, y_dst, y_bps, limit + 4);
                if (f_info.f_inner_ != 0)
                    SimpleVFilter16i (m_cache, y_dst, y_bps, limit);
            }
            else // complex
            {
                int uv_bps = cache_uv_stride_;
                int u_dst = cache_u_ + cache_id * 8 * uv_bps + mb_x * 8;
                int v_dst = cache_v_ + cache_id * 8 * uv_bps + mb_x * 8;
                int hev_thresh = f_info.hev_thresh_;
                if (mb_x > 0)
                {
                    HFilter16 (m_cache, y_dst, y_bps, limit + 4, ilevel, hev_thresh);
                    HFilter8 (m_cache, u_dst, v_dst, uv_bps, limit + 4, ilevel, hev_thresh);
                }
                if (f_info.f_inner_ != 0)
                {
                    HFilter16i (m_cache, y_dst, y_bps, limit, ilevel, hev_thresh);
                    HFilter8i (m_cache, u_dst, v_dst, uv_bps, limit, ilevel, hev_thresh);
                }
                if (mb_y > 0)
                {
                    VFilter16 (m_cache, y_dst, y_bps, limit + 4, ilevel, hev_thresh);
                    VFilter8 (m_cache, u_dst, v_dst, uv_bps, limit + 4, ilevel, hev_thresh);
                }
                if (f_info.f_inner_ != 0)
                {
                    VFilter16i (m_cache, y_dst, y_bps, limit, ilevel, hev_thresh);
                    VFilter8i (m_cache, u_dst, v_dst, uv_bps, limit, ilevel, hev_thresh);
                }
            }
        }

        //------------------------------------------------------------------------------
        // Edge filtering functions

        // 4 pixels in, 2 pixels out
        static void do_filter2 (byte[] dst, int p, int step)
        {
            int p1 = dst[p-2*step], p0 = dst[p-step], q0 = dst[p], q1 = dst[p+step];
            int a = 3 * (q0 - p0) + kSClip1[1020 + p1 - q1];  // in [-893,892]
            int a1 = kSClip2[112 + ((a + 4) >> 3)];            // in [-16,15]
            int a2 = kSClip2[112 + ((a + 3) >> 3)];
            dst[p-step] = kClip1[255 + p0 + a2];
            dst[p     ] = kClip1[255 + q0 - a1];
        }

        // 4 pixels in, 4 pixels out
        static void do_filter4 (byte[] dst, int p, int step)
        {
            int p1 = dst[p-2*step], p0 = dst[p-step], q0 = dst[p], q1 = dst[p+step];
            int a = 3 * (q0 - p0);
            int a1 = kSClip2[112 + ((a + 4) >> 3)];
            int a2 = kSClip2[112 + ((a + 3) >> 3)];
            int a3 = (a1 + 1) >> 1;
            dst[p-2*step] = kClip1[255 + p1 + a3];
            dst[p-  step] = kClip1[255 + p0 + a2];
            dst[p       ] = kClip1[255 + q0 - a1];
            dst[p+  step] = kClip1[255 + q1 - a3];
        }

        // 6 pixels in, 6 pixels out
        static void do_filter6 (byte[] dst, int p, int step)
        {
            int p2 = dst[p-3*step], p1 = dst[p-2*step], p0 = dst[p-step];
            int q0 = dst[p], q1 = dst[p+step], q2 = dst[p+2*step];
            int a = kSClip1[1020 + 3 * (q0 - p0) + kSClip1[1020 + p1 - q1]];
            // a is in [-128,127], a1 in [-27,27], a2 in [-18,18] and a3 in [-9,9]
            int a1 = (27 * a + 63) >> 7;  // eq. to ((3 * a + 7) * 9) >> 7
            int a2 = (18 * a + 63) >> 7;  // eq. to ((2 * a + 7) * 9) >> 7
            int a3 = (9  * a + 63) >> 7;  // eq. to ((1 * a + 7) * 9) >> 7
            dst[p-3*step] = kClip1[255 + p2 + a3];
            dst[p-2*step] = kClip1[255 + p1 + a2];
            dst[p-  step] = kClip1[255 + p0 + a1];
            dst[p       ] = kClip1[255 + q0 - a1];
            dst[p+  step] = kClip1[255 + q1 - a2];
            dst[p+2*step] = kClip1[255 + q2 - a3];
        }

        static bool hev (byte[] dst, int p, int step, int thresh)
        {
            int p1 = dst[p-2*step], p0 = dst[p-step], q0 = dst[p], q1 = dst[p+step];
            return (kAbs0[255 + p1 - p0] > thresh) || (kAbs0[255 + q1 - q0] > thresh);
        }

        static bool needs_filter (byte[] dst, int p, int step, int t)
        {
            int p1 = dst[p-2 * step], p0 = dst[p-step], q0 = dst[p], q1 = dst[p+step];
            return ((4 * kAbs0[255 + p0 - q0] + kAbs0[255 + p1 - q1]) <= t);
        }

        static bool needs_filter2 (byte[] dst, int p, int step, int t, int it)
        {
            int p3 = dst[p-4 * step], p2 = dst[p-3 * step], p1 = dst[p-2 * step];
            int p0 = dst[p-step], q0 = dst[p];
            int q1 = dst[p+step], q2 = dst[p+2 * step], q3 = dst[p+3 * step];
            if ((4 * kAbs0[255 + p0 - q0] + kAbs0[255 + p1 - q1]) > t) return false;
            return kAbs0[255 + p3 - p2] <= it && kAbs0[255 + p2 - p1] <= it &&
                   kAbs0[255 + p1 - p0] <= it && kAbs0[255 + q3 - q2] <= it &&
                   kAbs0[255 + q2 - q1] <= it && kAbs0[255 + q1 - q0] <= it;
        }

        //------------------------------------------------------------------------------
        // Simple In-loop filtering (Paragraph 15.2)

        static void SimpleVFilter16 (byte[] dst, int p, int stride, int thresh)
        {
            int thresh2 = 2 * thresh + 1;
            for (int i = 0; i < 16; ++i)
            {
                if (needs_filter (dst, p + i, stride, thresh2))
                    do_filter2 (dst, p + i, stride);
            }
        }

        static void SimpleHFilter16 (byte[] dst, int p, int stride, int thresh)
        {
            int thresh2 = 2 * thresh + 1;
            for (int i = 0; i < 16; ++i)
            {
                if (needs_filter (dst, p + i * stride, 1, thresh2))
                    do_filter2 (dst, p + i * stride, 1);
            }
        }

        static void SimpleVFilter16i (byte[] dst, int p, int stride, int thresh)
        {
            for (int k = 3; k > 0; --k)
            {
                p += 4 * stride;
                SimpleVFilter16 (dst, p, stride, thresh);
            }
        }

        static void SimpleHFilter16i (byte[] dst, int p, int stride, int thresh)
        {
            for (int k = 3; k > 0; --k)
            {
                p += 4;
                SimpleHFilter16 (dst, p, stride, thresh);
            }
        }

        //------------------------------------------------------------------------------
        // Complex In-loop filtering (Paragraph 15.3)

        static void FilterLoop26(byte[] dst, int p, int hstride, int vstride, int size, int thresh, int ithresh, int hev_thresh)
        {
            int thresh2 = 2 * thresh + 1;
            while (size-- > 0)
            {
                if (needs_filter2 (dst, p, hstride, thresh2, ithresh))
                {
                    if (hev (dst, p, hstride, hev_thresh))
                        do_filter2 (dst, p, hstride);
                    else
                        do_filter6 (dst, p, hstride);
                }
                p += vstride;
            }
        }

        static void FilterLoop24 (byte[] dst, int p, int hstride, int vstride, int size, int thresh, int ithresh, int hev_thresh)
        {
            int thresh2 = 2 * thresh + 1;
            while (size-- > 0)
            {
                if (needs_filter2 (dst, p, hstride, thresh2, ithresh))
                {
                    if (hev (dst, p, hstride, hev_thresh))
                        do_filter2 (dst, p, hstride);
                    else
                        do_filter4 (dst, p, hstride);
                }
                p += vstride;
            }
        }

        // on macroblock edges
        static void VFilter16 (byte[] dst, int p, int stride, int thresh, int ithresh, int hev_thresh)
        {
            FilterLoop26 (dst, p, stride, 1, 16, thresh, ithresh, hev_thresh);
        }

        static void HFilter16 (byte[] dst, int p, int stride, int thresh, int ithresh, int hev_thresh)
        {
            FilterLoop26 (dst, p, 1, stride, 16, thresh, ithresh, hev_thresh);
        }

        // on three inner edges
        static void VFilter16i (byte[] dst, int p, int stride, int thresh, int ithresh, int hev_thresh)
        {
            for (int k = 3; k > 0; --k)
            {
                p += 4 * stride;
                FilterLoop24 (dst, p, stride, 1, 16, thresh, ithresh, hev_thresh);
            }
        }

        static void HFilter16i (byte[] dst, int p, int stride, int thresh, int ithresh, int hev_thresh)
        {
            for (int k = 3; k > 0; --k)
            {
                p += 4;
                FilterLoop24 (dst, p, 1, stride, 16, thresh, ithresh, hev_thresh);
            }
        }

        // 8-pixels wide variant, for chroma filtering
        static void VFilter8 (byte[] dst, int u, int v, int stride, int thresh, int ithresh, int hev_thresh)
        {
            FilterLoop26(dst, u, stride, 1, 8, thresh, ithresh, hev_thresh);
            FilterLoop26(dst, v, stride, 1, 8, thresh, ithresh, hev_thresh);
        }

        static void HFilter8 (byte[] dst, int u, int v, int stride, int thresh, int ithresh, int hev_thresh)
        {
            FilterLoop26(dst, u, 1, stride, 8, thresh, ithresh, hev_thresh);
            FilterLoop26(dst, v, 1, stride, 8, thresh, ithresh, hev_thresh);
        }

        static void VFilter8i (byte[] dst, int u, int v, int stride, int thresh, int ithresh, int hev_thresh)
        {
            FilterLoop24(dst, u + 4 * stride, stride, 1, 8, thresh, ithresh, hev_thresh);
            FilterLoop24(dst, v + 4 * stride, stride, 1, 8, thresh, ithresh, hev_thresh);
        }

        static void HFilter8i (byte[] dst, int u, int v, int stride, int thresh, int ithresh, int hev_thresh)
        {
            FilterLoop24(dst, u + 4, 1, stride, 8, thresh, ithresh, hev_thresh);
            FilterLoop24(dst, v + 4, 1, stride, 8, thresh, ithresh, hev_thresh);
        }

        //------------------------------------------------------------------------------

        TopSamples[]    m_yuv_t;
        byte[]          m_yuv_b;
        byte[]          m_intra_t;
        byte[]          m_intra_l = new byte[4];
        MacroBlock[]    m_mb;
        int             m_mb_info;
        MacroBlockData[] m_mb_data;     // parsed reconstruction data
        const int       num_caches_ = 1;

        void AllocateMemory ()
        {
            int top_size = 32 * mb_w_;
            int intra_pred_mode_size = 4 * mb_w_;
            m_intra_t = new byte[intra_pred_mode_size];

            m_yuv_t = new TopSamples[mb_w_];
            for (int i = 0; i < m_yuv_t.Length; ++i)
                m_yuv_t[i] = new TopSamples();

            int mb_info_size = mb_w_ + 1;
            if (null == m_mb || m_mb.Length < mb_info_size)
                m_mb = new MacroBlock[mb_info_size];
            m_mb_info = 1;

            if (m_filter_type > 0)
                m_filter_info = new FilterInfo[mb_w_];

            m_yuv_b = new byte[YUV_SIZE];

            m_mb_data = new MacroBlockData[mb_w_];
            for (int i = 0; i < m_mb_data.Length; ++i)
                m_mb_data[i] = new MacroBlockData();

            int cache_height = (16 * num_caches_ + kFilterExtraRows[m_filter_type]) * 3 / 2;
            int cache_size = top_size * cache_height;
            m_cache = new byte[cache_size];

            cache_y_stride_ = 16 * mb_w_;
            cache_uv_stride_ = 8 * mb_w_;
            int extra_rows = kFilterExtraRows[m_filter_type];
            int extra_y = extra_rows * cache_y_stride_;
            int extra_uv = (extra_rows / 2) * cache_uv_stride_;
            cache_y_ = extra_y;
            cache_u_ = cache_y_ + 16 * num_caches_ * cache_y_stride_ + extra_uv;
            cache_v_ = cache_u_ + 8 * num_caches_ * cache_uv_stride_ + extra_uv;

            InitScanline();
        }

        void InitScanline ()
        {
            int left = m_mb_info - 1;
            m_mb[left].nz_ = 0;
            m_mb[left].nz_dc_ = 0;
            for (int i = 0; i < 4; ++i)
                m_intra_l[i] = 0;
            mb_x_ = 0;
        }

        void DspInit ()
        {
            InitClipTables();

            PredLuma4[0] = DC4;
            PredLuma4[1] = TM4;
            PredLuma4[2] = VE4;
            PredLuma4[3] = HE4;
            PredLuma4[4] = RD4;
            PredLuma4[5] = VR4;
            PredLuma4[6] = LD4;
            PredLuma4[7] = VL4;
            PredLuma4[8] = HD4;
            PredLuma4[9] = HU4;

            PredLuma16[0] = DC16;
            PredLuma16[1] = TM16;
            PredLuma16[2] = VE16;
            PredLuma16[3] = HE16;
            PredLuma16[4] = DC16NoTop;
            PredLuma16[5] = DC16NoLeft;
            PredLuma16[6] = DC16NoTopLeft;

            PredChroma8[0] = DC8uv;
            PredChroma8[1] = TM8uv;
            PredChroma8[2] = VE8uv;
            PredChroma8[3] = HE8uv;
            PredChroma8[4] = DC8uvNoTop;
            PredChroma8[5] = DC8uvNoLeft;
            PredChroma8[6] = DC8uvNoTopLeft;
        }

        static byte[] kAbs0;
        static sbyte[] kSClip1;
        static sbyte[] kSClip2;
        static byte[] kClip1;

        static bool s_tables_initialized = false;

        void InitClipTables ()
        {
            if (!s_tables_initialized)
            {
                var abs0 = new byte[255 + 255 + 1];
                var sclip1 = new sbyte[1020 + 1020 + 1];
                var sclip2 = new sbyte[112 + 112 + 1];
                var clip1 = new byte[255 + 511 + 1];
                int i;
                for (i = -255; i <= 255; ++i)
                    abs0[255 + i] = (byte)((i < 0) ? -i : i);
                for (i = -1020; i <= 1020; ++i)
                    sclip1[1020 + i] = (sbyte)((i < -128) ? -128 : (i > 127) ? 127 : i);
                for (i = -112; i <= 112; ++i)
                    sclip2[112 + i] = (sbyte)((i < -16) ? -16 : (i > 15) ? 15 : i);
                for (i = -255; i <= 255 + 255; ++i)
                    clip1[255 + i] = (byte)((i < 0) ? 0 : (i > 255) ? 255 : i);
                kAbs0 = abs0;
                kSClip1 = sclip1;
                kSClip2 = sclip2;
                kClip1 = clip1;
                s_tables_initialized = true;
            }
        }

        static void TransformWHT(short[] src, short[] output, int dst)
        {
            int[] tmp = new int[16];
            int i;
            for (i = 0; i < 4; ++i)
            {
                int a0 = src[0 + i] + src[12 + i];
                int a1 = src[4 + i] + src[ 8 + i];
                int a2 = src[4 + i] - src[ 8 + i];
                int a3 = src[0 + i] - src[12 + i];
                tmp[0  + i] = a0 + a1;
                tmp[8  + i] = a0 - a1;
                tmp[4  + i] = a3 + a2;
                tmp[12 + i] = a3 - a2;
            }
            for (i = 0; i < 4; ++i)
            {
                int dc = tmp[0 + i * 4] + 3;    // w/ rounder
                int a0 = dc             + tmp[3 + i * 4];
                int a1 = tmp[1 + i * 4] + tmp[2 + i * 4];
                int a2 = tmp[1 + i * 4] - tmp[2 + i * 4];
                int a3 = dc             - tmp[3 + i * 4];
                output[dst+ 0] = (short)((a0 + a1) >> 3);
                output[dst+16] = (short)((a3 + a2) >> 3);
                output[dst+32] = (short)((a0 - a1) >> 3);
                output[dst+48] = (short)((a3 - a2) >> 3);
                dst += 64;
            }
        }

        //------------------------------------------------------------------------------
        // Intra predictions

        static void TrueMotion (byte[] buf, int dst, int size)
        {
            int top = dst - BPS;
            int clip0 = 255 - buf[top-1];
            for (int y = 0; y < size; ++y)
            {
                int clip = clip0 + buf[dst-1];
                for (int x = 0; x < size; ++x)
                {
                    buf[dst+x] = kClip1[clip + buf[top+x]];
                }
                dst += BPS;
            }
        }

        static void TM4 (byte[] buf, int dst)   { TrueMotion (buf, dst, 4); }
        static void TM8uv (byte[] buf, int dst) { TrueMotion (buf, dst, 8); }
        static void TM16 (byte[] buf, int dst)  { TrueMotion (buf, dst, 16); }

        //------------------------------------------------------------------------------
        // 16x16

        static void VE16 (byte[] buf, int dst)       // vertical
        {
            for (int j = 0; j < 16; ++j)
                Buffer.BlockCopy (buf, dst - BPS, buf, dst + j * BPS, 16);
        }

        static void HE16 (byte[] buf, int dst)       // horizontal
        {
            for (int j = 16; j > 0; --j)
            {
                byte v = buf[dst-1];
                for (int i = 0; i < 16; ++i)
                    buf[dst+i] = v;
                dst += BPS;
            }
        }

        static void Put16 (int v, byte[] buf, int dst)
        {
            for (int j = 0; j < 16; ++j)
            {
                for (int i = 0; i < 16; ++i)
                    buf[dst+i] = (byte)v;
                dst += BPS;
            }
        }

        static void DC16 (byte[] buf, int dst)      // DC
        {
            int DC = 16;
            for (int j = 0; j < 16; ++j)
            {
                DC += buf[dst - 1 + j * BPS] + buf[dst + j - BPS];
            }
            Put16 (DC >> 5, buf, dst);
        }

        static void DC16NoTop (byte[] buf, int dst)     // DC with top samples not available
        {
            int DC = 8;
            for (int j = 0; j < 16; ++j)
            {
                DC += buf[dst - 1 + j * BPS];
            }
            Put16 (DC >> 4, buf, dst);
        }

        static void DC16NoLeft (byte[] buf, int dst)    // DC with left samples not available
        {
            int DC = 8;
            for (int i = 0; i < 16; ++i)
            {
                DC += buf[dst + i - BPS];
            }
            Put16 (DC >> 4, buf, dst);
        }

        static void DC16NoTopLeft (byte[] buf, int dst)    // DC with no top and left samples
        {
            Put16 (0x80, buf, dst);
        }

        //------------------------------------------------------------------------------
        // 4x4

        static byte AVG3 (byte a, byte b, byte c)
        {
            return (byte)((a + 2 * b + c + 2) >> 2);
        }

        static byte AVG2 (byte a, byte b)
        {
            return (byte)((a + b + 1) >> 1);
        }

        static void VE4 (byte[] buf, int dst)      // vertical
        {
            int top = dst - BPS;
            byte vals0 = AVG3 (buf[top-1], buf[top],   buf[top+1]);
            byte vals1 = AVG3 (buf[top],   buf[top+1], buf[top+2]);
            byte vals2 = AVG3 (buf[top+1], buf[top+2], buf[top+3]);
            byte vals3 = AVG3 (buf[top+2], buf[top+3], buf[top+4]);
            for (int i = 0; i < 4; ++i) {
                buf[dst]   = vals0;
                buf[dst+1] = vals1;
                buf[dst+2] = vals2;
                buf[dst+3] = vals3;
                dst += BPS;
            }
        }

        static void HE4 (byte[] buf, int dst)      // horizontal
        {
            byte A = buf[dst -1 - BPS];
            byte B = buf[dst -1];
            byte C = buf[dst -1 + BPS];
            byte D = buf[dst -1 + 2 * BPS];
            byte E = buf[dst -1 + 3 * BPS];
            LittleEndian.Pack (0x01010101U * AVG3(A, B, C), buf, dst);
            LittleEndian.Pack (0x01010101U * AVG3(B, C, D), buf, dst + BPS);
            LittleEndian.Pack (0x01010101U * AVG3(C, D, E), buf, dst + BPS * 2);
            LittleEndian.Pack (0x01010101U * AVG3(D, E, E), buf, dst + BPS * 3);
        }

        static void DC4 (byte[] buf, int dst)     // DC
        {
            uint dc = 4;
            for (int i = 0; i < 4; ++i)
                dc += (uint)buf[dst + i - BPS] + buf[dst - 1 + i * BPS];
            dc >>= 3;
            for (int i = 0; i < 4; ++i)
            {
                buf[dst]   = (byte)dc;
                buf[dst+1] = (byte)dc;
                buf[dst+2] = (byte)dc;
                buf[dst+3] = (byte)dc;
                dst += BPS;
            }
        }

        static void RD4 (byte[] buf, int dst)     // Down-right
        {
            byte I = buf[dst - 1 + 0 * BPS];
            byte J = buf[dst - 1 + 1 * BPS];
            byte K = buf[dst - 1 + 2 * BPS];
            byte L = buf[dst - 1 + 3 * BPS];
            byte X = buf[dst - 1 - BPS];
            byte A = buf[dst + 0 - BPS];
            byte B = buf[dst + 1 - BPS];
            byte C = buf[dst + 2 - BPS];
            byte D = buf[dst + 3 - BPS];
            buf[dst + 0 + 3 * BPS] = AVG3(J, K, L);
            buf[dst + 1 + 3 * BPS] = buf[dst + 0 + 2 * BPS] = AVG3(I, J, K);
            buf[dst + 2 + 3 * BPS] = buf[dst + 1 + 2 * BPS] = buf[dst + BPS] = AVG3(X, I, J);
            buf[dst + 3 + 3 * BPS] = buf[dst + 2 + 2 * BPS] = buf[dst + 1 + BPS] = buf[dst] = AVG3(A, X, I);
            buf[dst + 3 + 2 * BPS] = buf[dst + 2 + BPS] = buf[dst + 1] = AVG3(B, A, X);
            buf[dst + 3 + BPS] = buf[dst + 2] = AVG3(C, B, A);
            buf[dst + 3] = AVG3(D, C, B);
        }

        static void LD4 (byte[] buf, int dst)     // Down-Left
        {
            byte A = buf[dst + 0 - BPS];
            byte B = buf[dst + 1 - BPS];
            byte C = buf[dst + 2 - BPS];
            byte D = buf[dst + 3 - BPS];
            byte E = buf[dst + 4 - BPS];
            byte F = buf[dst + 5 - BPS];
            byte G = buf[dst + 6 - BPS];
            byte H = buf[dst + 7 - BPS];
            buf[dst] = AVG3(A, B, C);
            buf[dst + 1] = buf[dst + 0 + BPS] = AVG3(B, C, D);
            buf[dst + 2] = buf[dst + 1 + BPS] = buf[dst + 0 + 2 * BPS] = AVG3(C, D, E);
            buf[dst + 3] = buf[dst + 2 + 1 * BPS] = buf[dst + 1 + 2 * BPS] = buf[dst + 0 + 3 * BPS] = AVG3(D, E, F);
            buf[dst + 3 + 1 * BPS] = buf[dst + 2 + 2 * BPS] = buf[dst + 1 + 3 * BPS] = AVG3(E, F, G);
            buf[dst + 3 + 2 * BPS] = buf[dst + 2 + 3 * BPS] = AVG3(F, G, H);
            buf[dst + 3 + 3 * BPS] = AVG3(G, H, H);
        }

        static void VR4 (byte[] buf, int dst)     // Vertical-Right
        {
            byte I = buf[dst - 1 + 0 * BPS];
            byte J = buf[dst - 1 + 1 * BPS];
            byte K = buf[dst - 1 + 2 * BPS];
            byte X = buf[dst - 1 - BPS];
            byte A = buf[dst + 0 - BPS];
            byte B = buf[dst + 1 - BPS];
            byte C = buf[dst + 2 - BPS];
            byte D = buf[dst + 3 - BPS];
            buf[dst + 0] = buf[dst + 1 + 2 * BPS] = AVG2(X, A);
            buf[dst + 1] = buf[dst + 2 + 2 * BPS] = AVG2(A, B);
            buf[dst + 2] = buf[dst + 3 + 2 * BPS] = AVG2(B, C);
            buf[dst + 3] = AVG2(C, D);

            buf[dst + 0 + 3 * BPS] =             AVG3(K, J, I);
            buf[dst + 0 + 2 * BPS] =             AVG3(J, I, X);
            buf[dst + 0 + 1 * BPS] = buf[dst + 1 + 3 * BPS] = AVG3(I, X, A);
            buf[dst + 1 + 1 * BPS] = buf[dst + 2 + 3 * BPS] = AVG3(X, A, B);
            buf[dst + 2 + 1 * BPS] = buf[dst + 3 + 3 * BPS] = AVG3(A, B, C);
            buf[dst + 3 + 1 * BPS] =             AVG3(B, C, D);
        }

        static void VL4 (byte[] buf, int dst)     // Vertical-Left
        {
            byte A = buf[dst + 0 - BPS];
            byte B = buf[dst + 1 - BPS];
            byte C = buf[dst + 2 - BPS];
            byte D = buf[dst + 3 - BPS];
            byte E = buf[dst + 4 - BPS];
            byte F = buf[dst + 5 - BPS];
            byte G = buf[dst + 6 - BPS];
            byte H = buf[dst + 7 - BPS];
            buf[dst] = AVG2(A, B);
            buf[dst + 1] = buf[dst + 0 + 2 * BPS] = AVG2(B, C);
            buf[dst + 2] = buf[dst + 1 + 2 * BPS] = AVG2(C, D);
            buf[dst + 3] = buf[dst + 2 + 2 * BPS] = AVG2(D, E);

            buf[dst + 0 + 1 * BPS] = AVG3(A, B, C);
            buf[dst + 1 + 1 * BPS] = buf[dst + 0 + 3 * BPS] = AVG3(B, C, D);
            buf[dst + 2 + 1 * BPS] = buf[dst + 1 + 3 * BPS] = AVG3(C, D, E);
            buf[dst + 3 + 1 * BPS] = buf[dst + 2 + 3 * BPS] = AVG3(D, E, F);
            buf[dst + 3 + 2 * BPS] = AVG3(E, F, G);
            buf[dst + 3 + 3 * BPS] = AVG3(F, G, H);
        }

        static void HU4 (byte[] buf, int dst)     // Horizontal-Up
        {
            byte I = buf[dst - 1 + 0 * BPS];
            byte J = buf[dst - 1 + 1 * BPS];
            byte K = buf[dst - 1 + 2 * BPS];
            byte L = buf[dst - 1 + 3 * BPS];
            buf[dst] = AVG2(I, J);
            buf[dst + 2 + 0 * BPS] = buf[dst + 0 + 1 * BPS] = AVG2(J, K);
            buf[dst + 2 + 1 * BPS] = buf[dst + 0 + 2 * BPS] = AVG2(K, L);
            buf[dst + 1 + 0 * BPS] = AVG3(I, J, K);
            buf[dst + 3 + 0 * BPS] = buf[dst + 1 + 1 * BPS] = AVG3(J, K, L);
            buf[dst + 3 + 1 * BPS] = buf[dst + 1 + 2 * BPS] = AVG3(K, L, L);
            buf[dst + 3 + 2 * BPS] = buf[dst + 2 + 2 * BPS] =
            buf[dst + 0 + 3 * BPS] = buf[dst + 1 + 3 * BPS] = buf[dst + 2 + 3 * BPS] = buf[dst + 3 + 3 * BPS] = L;
        }

        static void HD4 (byte[] buf, int dst)    // Horizontal-Down
        {
            byte I = buf[dst - 1 + 0 * BPS];
            byte J = buf[dst - 1 + 1 * BPS];
            byte K = buf[dst - 1 + 2 * BPS];
            byte L = buf[dst - 1 + 3 * BPS];
            byte X = buf[dst - 1 - BPS];
            byte A = buf[dst + 0 - BPS];
            byte B = buf[dst + 1 - BPS];
            byte C = buf[dst + 2 - BPS];

            buf[dst + 0 + 0 * BPS] = buf[dst + 2 + 1 * BPS] = AVG2(I, X);
            buf[dst + 0 + 1 * BPS] = buf[dst + 2 + 2 * BPS] = AVG2(J, I);
            buf[dst + 0 + 2 * BPS] = buf[dst + 2 + 3 * BPS] = AVG2(K, J);
            buf[dst + 0 + 3 * BPS] = AVG2(L, K);

            buf[dst + 3 + 0 * BPS] = AVG3(A, B, C);
            buf[dst + 2 + 0 * BPS] = AVG3(X, A, B);
            buf[dst + 1 + 0 * BPS] = buf[dst + 3 + 1 * BPS] = AVG3(I, X, A);
            buf[dst + 1 + 1 * BPS] = buf[dst + 3 + 2 * BPS] = AVG3(J, I, X);
            buf[dst + 1 + 2 * BPS] = buf[dst + 3 + 3 * BPS] = AVG3(K, J, I);
            buf[dst + 1 + 3 * BPS] = AVG3(L, K, J);
        }

        //------------------------------------------------------------------------------
        // Chroma

        static void VE8uv (byte[] buf, int dst)      // vertical
        {
            for (int j = 0; j < 8; ++j)
            {
                Buffer.BlockCopy (buf, dst - BPS, buf, dst + j * BPS, 8);
            }
        }

        static void HE8uv (byte[] buf, int dst)      // horizontal
        {
            for (int j = 0; j < 8; ++j)
            {
                byte v = buf[dst-1];
                for (int i = 0; i < 8; ++i)
                    buf[dst+i] = v;
                dst += BPS;
            }
        }

        static void Put8x8uv (int val, byte[] buf, int dst)
        {
            for (int j = 0; j < 8; ++j)
            {
                for (int i = 0; i < 8; ++i)
                    buf[dst+i] = (byte)val;
                dst += BPS;
            }
        }

        static void DC8uv (byte[] buf, int dst)       // DC
        {
            int dc0 = 8;
            for (int i = 0; i < 8; ++i)
            {
                dc0 += buf[dst + i - BPS] + buf[dst - 1 + i * BPS];
            }
            Put8x8uv (dc0 >> 4, buf, dst);
        }

        static void DC8uvNoLeft (byte[] buf, int dst)     // DC with no left samples
        {
            int dc0 = 4;
            for (int i = 0; i < 8; ++i)
            {
                dc0 += buf[dst + i - BPS];
            }
            Put8x8uv (dc0 >> 3, buf, dst);
        }

        static void DC8uvNoTop (byte[] buf, int dst)    // DC with no top samples
        {
            int dc0 = 4;
            for (int i = 0; i < 8; ++i) {
                dc0 += buf[dst - 1 + i * BPS];
            }
            Put8x8uv(dc0 >> 3, buf, dst);
        }

        static void DC8uvNoTopLeft (byte[] buf, int dst)      // DC with nothing
        {
            Put8x8uv(0x80, buf, dst);
        }

        //------------------------------------------------------------------------------

        const int TreeProbs = 3;
        const int NumMbSegments = 4;
        const int NumRefLfDeltas = 4;
        const int NumModeLfDeltas = 4;
        const int MaxNumPartitions = 8;

        const int NumTypes = 4;
        const int NumBands = 8;
        const int NumCtx = 3;
        const int NumProbas = 11;

        const sbyte B_DC_PRED = 0;   // 4x4 modes
        const sbyte B_TM_PRED = 1;
        const sbyte B_VE_PRED = 2;
        const sbyte B_HE_PRED = 3;
        const sbyte B_RD_PRED = 4;
        const sbyte B_VR_PRED = 5;
        const sbyte B_LD_PRED = 6;
        const sbyte B_VL_PRED = 7;
        const sbyte B_HD_PRED = 8;
        const sbyte B_HU_PRED = 9;

        const byte H_PRED   = (byte)B_HE_PRED;
        const byte TM_PRED  = (byte)B_TM_PRED;
        const byte DC_PRED  = (byte)B_DC_PRED;
        const byte V_PRED   = (byte)B_VE_PRED;

        const int B_DC_PRED_NOTOP = 4;
        const int B_DC_PRED_NOLEFT = 5;
        const int B_DC_PRED_NOTOPLEFT = 6;

        const int NumBModes = 10;
        const int NumBDCModes = 7;

        const int BPS = 32;
        const int YUV_SIZE = BPS * 17 + BPS * 9;
        const int Y_SIZE   = BPS * 17;
        const int Y_OFF    = BPS * 1 + 8;
        const int U_OFF    = Y_OFF + BPS * 16 + BPS;
        const int V_OFF    = U_OFF + 16;

        delegate void PredFunc (byte[] buf, int dst);

        PredFunc[] PredLuma4 = new PredFunc[NumBModes];
        PredFunc[] PredLuma16 = new PredFunc[NumBDCModes];
        PredFunc[] PredChroma8 = new PredFunc[NumBDCModes];

        internal class PictureHeader
        {
            public ushort   Width;
            public ushort   Height;
            public byte     XScale;
            public byte     YScale;
            public byte     Colorspace;   // 0 = YCbCr
            public byte     ClampType;
        }

        internal class SegmentHeader
        {
            public bool UseSegment;
            public bool UpdateMap;
            public bool AbsoluteDelta;
            public byte[]   Quantizer = new byte[NumMbSegments];
            public byte[]   FilterStrength = new byte[NumMbSegments];

            public void Reset ()
            {
                UseSegment = false;
                UpdateMap = false;
                AbsoluteDelta = true;
                for (int i = 0; i < NumMbSegments; ++i)
                {
                    Quantizer[i] = 0;
                    FilterStrength[i] = 0;
                }
            }
        }

        internal class FilterHeader
        {
            public bool Simple;
            public int  Level;
            public int  Sharpness;
            public bool UseLfDelta;
            public int[] RefLfDelta = new int[NumRefLfDeltas];
            public int[] ModeLfDelta = new int[NumModeLfDeltas];
        }

        internal class BandProbas
        {
            public byte[][] Probas = new byte[NumCtx][];

            public BandProbas ()
            {
                for (int i = 0; i < NumCtx; ++i)
                {
                    Probas[i] = new byte[NumProbas];
                }
            }
        }

        internal class Proba
        {
            public byte[]           Segments = new byte[TreeProbs];
            public BandProbas[,]    Bands = new BandProbas[NumTypes,NumBands];
            public BandProbas[][]   BandsPtr;

            public Proba ()
            {
                BandsPtr = new BandProbas[NumTypes][];
                for (int i = 0; i < NumTypes; ++i)
                    BandsPtr[i] = new BandProbas[17];
            }

            public void Reset ()
            {
                for (int i = 0; i < Segments.Length; ++i)
                    Segments[i] = 0xFF;
                for (int t = 0; t < NumTypes; ++t)
                for (int b = 0; b < NumBands; ++b)
                {
                    Bands[t,b] = new BandProbas();
                }
            }
        }

        internal class QuantMatrix
        {
            public int[]    y1_mat = new int[2];
            public int[]    y2_mat = new int[2];
            public int[]    uv_mat = new int[2];
            public int      uv_quant;
        }

        internal class MacroBlockData
        {
            public short[] coeffs_ = new short[384];   // 384 coeffs = (16+4+4) * 4*4
            public bool is_i4x4_;       // true if intra4x4
            public byte[] imodes_ = new byte[16];    // one 16x16 mode (#0) or sixteen 4x4 modes
            public byte uvmode_;        // chroma prediction mode
            // bit-wise info about the content of each sub-4x4 blocks (in decoding order).
            // Each of the 4x4 blocks for y/u/v is associated with a 2b code according to:
            //   code=0 -> no coefficient
            //   code=1 -> only DC
            //   code=2 -> first three coefficients are non-zero
            //   code=3 -> more than three coefficients are non-zero
            // This allows to call specialized transform functions.
            public uint non_zero_y_;
            public uint non_zero_uv_;
            public bool skip_;
            public byte segment_;
        }

        struct MacroBlock
        {
            public byte nz_;
            public byte nz_dc_;
        }

        class TopSamples
        {
            public byte[] y = new byte[16];
            public byte[] u = new byte[8]; 
            public byte[] v = new byte[8]; 
        }

        internal class BitReader : IBitStream
        {
            uint    m_value;
            int     m_range;
            int     m_bits;
            byte[]  m_buf;
            int     m_buf_pos;
            int     m_buf_max;
            int     m_buf_end;
            bool    m_eof;

            public bool Eof { get { return m_eof; } }

            public BitReader (BinaryReader input, int length)
            {
                Init (input, length);
            }

            public void Init (BinaryReader input, int length)
            {
                if (null == m_buf || m_buf.Length < length)
                    m_buf = new byte[length];
                if (length != input.Read (m_buf, 0, length))
                    throw new EndOfStreamException();
                m_buf_end = length;
                Reset();
            }

            public void Reset ()
            {
                m_range = 255 - 1;
                m_value = 0;
                m_bits  = -8;
                m_eof   = false;
                m_buf_pos = 0;
                m_buf_max = m_buf_end >= sizeof(uint) ? m_buf_end - sizeof(uint) + 1 : 0;
                LoadNewBytes();
            }

            public int GetNextBit ()
            {
                return GetBits (1);
            }

            public int GetBits (int bits)
            {
                int v = 0;
                while (bits --> 0)
                    v |= GetBit (0x80) << bits;
                return v;
            }

            public int GetSignedValue (int bits)
            {
                int val = GetBits (bits);
                return GetNextBit() != 0 ? -val : val;
            }

            public int GetBit (int prob)
            {
                if (m_bits < 0)
                    LoadNewBytes();

                int split = (m_range * prob) >> 8;
                uint value = m_value >> m_bits;

                int bit;
                if (value > split)
                {
                    m_range -= split + 1;
                    m_value -= (uint)(split + 1) << m_bits;
                    bit = 1;
                }
                else
                {
                    m_range = split;
                    bit = 0;
                }
                if (m_range <= 0x7Eu)
                {
                    int shift = kLog2Range[m_range];
                    m_range = kNewRange[m_range];
                    m_bits -= shift;
                }
                return bit;
            }

            const int BITS = 24;

            void LoadNewBytes ()
            {
                if (m_buf_pos < m_buf_max)
                {
                    uint bits = BigEndian.ToUInt32 (m_buf, m_buf_pos);
                    m_buf_pos += BITS >> 3;
                    bits >>= (32 - BITS);

                    m_value = bits | (m_value << BITS);
                    m_bits += BITS;
                } else {
                    LoadFinalBytes();
                }
            }

            void LoadFinalBytes ()
            {
                // Only read 8bits at a time
                if (m_buf_pos < m_buf_end)
                {
                    m_bits += 8;
                    m_value = (uint)(m_buf[m_buf_pos++]) | (m_value << 8);
                }
                else if (!m_eof)
                {
                    m_value <<= 8;
                    m_bits += 8;
                    m_eof = true;
                }
                else
                {
                    m_bits = 0;  // This is to avoid undefined behaviour with shifts.
                }
            }

            public int GetSigned (int v)
            {
                if (m_bits < 0)
                    LoadNewBytes();

                int pos = m_bits;
                uint split = (uint)m_range >> 1;
                uint val = m_value >> pos;
                int mask = (int)(split - val) >> 31;  // -1 or 0
                m_bits -= 1;
                m_range += mask;
                m_range |= 1;
                m_value -= ((split + 1) & (uint)mask) << pos;
                return (v ^ mask) - mask;
            }

            public int GetLargeValue (byte[] p)
            {
                int v;
                if (0 == GetBit (p[3]))
                {
                    if (0 == GetBit (p[4]))
                        v = 2;
                    else
                        v = 3 + GetBit (p[5]);
                }
                else
                {
                    if (0 == GetBit (p[6]))
                    {
                        if (0 == GetBit (p[7]))
                        {
                            v = 5 + GetBit (159);
                        }
                        else
                        {
                            v = 7 + 2 * GetBit (165);
                            v += GetBit (145);
                        }
                    }
                    else
                    {
                        int bit1 = GetBit (p[8]);
                        int bit0 = GetBit (p[9 + bit1]);
                        int cat = bit1 << 1 | bit0;
                        v = 0;
                        var tab = kCat3456[cat];
                        for (int i = 0; tab[i] != 0; ++i)
                        {
                            v += v + GetBit (tab[i]);
                        }
                        v += 3 + (8 << cat);
                    }
                }
                return v;
            }
        }

        bool is_alpha_decoded_ = false;

        AlphaDecoder alph_dec_;
        int alpha_dithering_;       // derived from decoding options (0=off, 100=full)

        int DecompressAlphaRows (int row, int num_rows)
        {
            int width = m_io.width;
            int height = m_io.height;

            if (row < 0 || num_rows <= 0 || row + num_rows > height)
                throw new InvalidFormatException ("Could not decode alpha data.");

            if (!is_alpha_decoded_)
            {
                if (null == alph_dec_)      // Initialize decoder.
                {
                    alph_dec_ = new AlphaDecoder();
                    if (!alph_dec_.Init (m_alpha_data, m_io, m_alpha_plane))
                        throw new InvalidFormatException ("Could not decode alpha data.");
                    // if we allowed use of alpha dithering, check whether it's needed at all
                    if (alph_dec_.pre_processing_ != AlphaDecoder.PreprocessedLevels)
                        alpha_dithering_ = 0;   // disable dithering
                    else
                        num_rows = height - row;     // decode everything in one pass
                }

                if (!ALPHDecode (row, num_rows))
                    return -1;

                if (is_alpha_decoded_ && alpha_dithering_ > 0)
                {
                    throw new NotImplementedException ("Alpha dithering not implemented.");
                    /*
                    var alpha = m_io.height * width;
                    if (!WebPDequantizeLevels (alpha, m_io.width, m_io.height, width, dec->alpha_dithering_))
                        throw new InvalidFormatException ("Could not decode alpha data.");
                    */
                }
            }

            // Return a pointer to the current decoded row.
            return row * width;
        }

        bool ALPHDecode (int row, int num_rows)
        {
            bool ok = alph_dec_.Decode (m_alpha_data, m_alpha_plane, row, num_rows);
            if (ok)
                is_alpha_decoded_ = alph_dec_.DecodeComplete;
            return ok;
        }

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize (this);
        }
        #endregion

        static readonly byte[,,,] CoeffsProba0 = new byte[,,,] {
            { { { 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128 },
                { 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128 },
                { 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128 }
                },
                { { 253, 136, 254, 255, 228, 219, 128, 128, 128, 128, 128 },
                { 189, 129, 242, 255, 227, 213, 255, 219, 128, 128, 128 },
                { 106, 126, 227, 252, 214, 209, 255, 255, 128, 128, 128 }
                },
                { { 1, 98, 248, 255, 236, 226, 255, 255, 128, 128, 128 },
                { 181, 133, 238, 254, 221, 234, 255, 154, 128, 128, 128 },
                { 78, 134, 202, 247, 198, 180, 255, 219, 128, 128, 128 },
                },
                { { 1, 185, 249, 255, 243, 255, 128, 128, 128, 128, 128 },
                { 184, 150, 247, 255, 236, 224, 128, 128, 128, 128, 128 },
                { 77, 110, 216, 255, 236, 230, 128, 128, 128, 128, 128 },
                },
                { { 1, 101, 251, 255, 241, 255, 128, 128, 128, 128, 128 },
                { 170, 139, 241, 252, 236, 209, 255, 255, 128, 128, 128 },
                { 37, 116, 196, 243, 228, 255, 255, 255, 128, 128, 128 }
                },
                { { 1, 204, 254, 255, 245, 255, 128, 128, 128, 128, 128 },
                { 207, 160, 250, 255, 238, 128, 128, 128, 128, 128, 128 },
                { 102, 103, 231, 255, 211, 171, 128, 128, 128, 128, 128 }
                },
                { { 1, 152, 252, 255, 240, 255, 128, 128, 128, 128, 128 },
                { 177, 135, 243, 255, 234, 225, 128, 128, 128, 128, 128 },
                { 80, 129, 211, 255, 194, 224, 128, 128, 128, 128, 128 }
                },
                { { 1, 1, 255, 128, 128, 128, 128, 128, 128, 128, 128 },
                { 246, 1, 255, 128, 128, 128, 128, 128, 128, 128, 128 },
                { 255, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128 }
                }
            },
            { { { 198, 35, 237, 223, 193, 187, 162, 160, 145, 155, 62 },
                { 131, 45, 198, 221, 172, 176, 220, 157, 252, 221, 1 },
                { 68, 47, 146, 208, 149, 167, 221, 162, 255, 223, 128 }
                },
                { { 1, 149, 241, 255, 221, 224, 255, 255, 128, 128, 128 },
                { 184, 141, 234, 253, 222, 220, 255, 199, 128, 128, 128 },
                { 81, 99, 181, 242, 176, 190, 249, 202, 255, 255, 128 }
                },
                { { 1, 129, 232, 253, 214, 197, 242, 196, 255, 255, 128 },
                { 99, 121, 210, 250, 201, 198, 255, 202, 128, 128, 128 },
                { 23, 91, 163, 242, 170, 187, 247, 210, 255, 255, 128 }
                },
                { { 1, 200, 246, 255, 234, 255, 128, 128, 128, 128, 128 },
                { 109, 178, 241, 255, 231, 245, 255, 255, 128, 128, 128 },
                { 44, 130, 201, 253, 205, 192, 255, 255, 128, 128, 128 }
                },
                { { 1, 132, 239, 251, 219, 209, 255, 165, 128, 128, 128 },
                { 94, 136, 225, 251, 218, 190, 255, 255, 128, 128, 128 },
                { 22, 100, 174, 245, 186, 161, 255, 199, 128, 128, 128 }
                },
                { { 1, 182, 249, 255, 232, 235, 128, 128, 128, 128, 128 },
                { 124, 143, 241, 255, 227, 234, 128, 128, 128, 128, 128 },
                { 35, 77, 181, 251, 193, 211, 255, 205, 128, 128, 128 }
                },
                { { 1, 157, 247, 255, 236, 231, 255, 255, 128, 128, 128 },
                { 121, 141, 235, 255, 225, 227, 255, 255, 128, 128, 128 },
                { 45, 99, 188, 251, 195, 217, 255, 224, 128, 128, 128 }
                },
                { { 1, 1, 251, 255, 213, 255, 128, 128, 128, 128, 128 },
                { 203, 1, 248, 255, 255, 128, 128, 128, 128, 128, 128 },
                { 137, 1, 177, 255, 224, 255, 128, 128, 128, 128, 128 }
                }
            },
            { { { 253, 9, 248, 251, 207, 208, 255, 192, 128, 128, 128 },
                { 175, 13, 224, 243, 193, 185, 249, 198, 255, 255, 128 },
                { 73, 17, 171, 221, 161, 179, 236, 167, 255, 234, 128 }
                },
                { { 1, 95, 247, 253, 212, 183, 255, 255, 128, 128, 128 },
                { 239, 90, 244, 250, 211, 209, 255, 255, 128, 128, 128 },
                { 155, 77, 195, 248, 188, 195, 255, 255, 128, 128, 128 }
                },
                { { 1, 24, 239, 251, 218, 219, 255, 205, 128, 128, 128 },
                { 201, 51, 219, 255, 196, 186, 128, 128, 128, 128, 128 },
                { 69, 46, 190, 239, 201, 218, 255, 228, 128, 128, 128 }
                },
                { { 1, 191, 251, 255, 255, 128, 128, 128, 128, 128, 128 },
                { 223, 165, 249, 255, 213, 255, 128, 128, 128, 128, 128 },
                { 141, 124, 248, 255, 255, 128, 128, 128, 128, 128, 128 }
                },
                { { 1, 16, 248, 255, 255, 128, 128, 128, 128, 128, 128 },
                { 190, 36, 230, 255, 236, 255, 128, 128, 128, 128, 128 },
                { 149, 1, 255, 128, 128, 128, 128, 128, 128, 128, 128 }
                },
                { { 1, 226, 255, 128, 128, 128, 128, 128, 128, 128, 128 },
                { 247, 192, 255, 128, 128, 128, 128, 128, 128, 128, 128 },
                { 240, 128, 255, 128, 128, 128, 128, 128, 128, 128, 128 }
                },
                { { 1, 134, 252, 255, 255, 128, 128, 128, 128, 128, 128 },
                { 213, 62, 250, 255, 255, 128, 128, 128, 128, 128, 128 },
                { 55, 93, 255, 128, 128, 128, 128, 128, 128, 128, 128 }
                },
                { { 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128 },
                { 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128 },
                { 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128 }
                }
            },
            { { { 202, 24, 213, 235, 186, 191, 220, 160, 240, 175, 255 },
                { 126, 38, 182, 232, 169, 184, 228, 174, 255, 187, 128 },
                { 61, 46, 138, 219, 151, 178, 240, 170, 255, 216, 128 }
                },
                { { 1, 112, 230, 250, 199, 191, 247, 159, 255, 255, 128 },
                { 166, 109, 228, 252, 211, 215, 255, 174, 128, 128, 128 },
                { 39, 77, 162, 232, 172, 180, 245, 178, 255, 255, 128 }
                },
                { { 1, 52, 220, 246, 198, 199, 249, 220, 255, 255, 128 },
                { 124, 74, 191, 243, 183, 193, 250, 221, 255, 255, 128 },
                { 24, 71, 130, 219, 154, 170, 243, 182, 255, 255, 128 }
                },
                { { 1, 182, 225, 249, 219, 240, 255, 224, 128, 128, 128 },
                { 149, 150, 226, 252, 216, 205, 255, 171, 128, 128, 128 },
                { 28, 108, 170, 242, 183, 194, 254, 223, 255, 255, 128 }
                },
                { { 1, 81, 230, 252, 204, 203, 255, 192, 128, 128, 128 },
                { 123, 102, 209, 247, 188, 196, 255, 233, 128, 128, 128 },
                { 20, 95, 153, 243, 164, 173, 255, 203, 128, 128, 128 }
                },
                { { 1, 222, 248, 255, 216, 213, 128, 128, 128, 128, 128 },
                { 168, 175, 246, 252, 235, 205, 255, 255, 128, 128, 128 },
                { 47, 116, 215, 255, 211, 212, 255, 255, 128, 128, 128 }
                },
                { { 1, 121, 236, 253, 212, 214, 255, 255, 128, 128, 128 },
                { 141, 84, 213, 252, 201, 202, 255, 219, 128, 128, 128 },
                { 42, 80, 160, 240, 162, 185, 255, 205, 128, 128, 128 }
                },
                { { 1, 1, 255, 128, 128, 128, 128, 128, 128, 128, 128 },
                { 244, 1, 255, 128, 128, 128, 128, 128, 128, 128, 128 },
                { 238, 1, 255, 128, 128, 128, 128, 128, 128, 128, 128 }
                }
            }
        };

        static readonly byte[,,,] CoeffsUpdateProba = new byte[NumTypes,NumBands,NumCtx,NumProbas] {
            { { { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 176, 246, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 223, 241, 252, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 249, 253, 253, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 244, 252, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 234, 254, 254, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 253, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 246, 254, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 239, 253, 254, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 254, 255, 254, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 248, 254, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 251, 255, 254, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 253, 254, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 251, 254, 254, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 254, 255, 254, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 254, 253, 255, 254, 255, 255, 255, 255, 255, 255 },
                { 250, 255, 254, 255, 254, 255, 255, 255, 255, 255, 255 },
                { 254, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }
                }
            },
            { { { 217, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 225, 252, 241, 253, 255, 255, 254, 255, 255, 255, 255 },
                { 234, 250, 241, 250, 253, 255, 253, 254, 255, 255, 255 }
                },
                { { 255, 254, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 223, 254, 254, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 238, 253, 254, 254, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 248, 254, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 249, 254, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 253, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 247, 254, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 253, 254, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 252, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 254, 254, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 253, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 254, 253, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 250, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 254, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }
                }
            },
            { { { 186, 251, 250, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 234, 251, 244, 254, 255, 255, 255, 255, 255, 255, 255 },
                { 251, 251, 243, 253, 254, 255, 254, 255, 255, 255, 255 }
                },
                { { 255, 253, 254, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 236, 253, 254, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 251, 253, 253, 254, 254, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 254, 254, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 254, 254, 254, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 254, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 254, 254, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 254, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 254, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }
                }
            },
            { { { 248, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 250, 254, 252, 254, 255, 255, 255, 255, 255, 255, 255 },
                { 248, 254, 249, 253, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 253, 253, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 246, 253, 253, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 252, 254, 251, 254, 254, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 254, 252, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 248, 254, 253, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 253, 255, 254, 254, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 251, 254, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 245, 251, 254, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 253, 253, 254, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 251, 253, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 252, 253, 254, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 254, 255, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 252, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 249, 255, 254, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 254, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 255, 253, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 250, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }
                },
                { { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 254, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
                { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }
                }
            }
        };

        static readonly int[] kBands = {
            0, 1, 2, 3, 6, 4, 5, 6, 6, 6, 6, 6, 6, 6, 6, 7, 0
        };

        static readonly byte[] kLog2Range = {
               7, 6, 6, 5, 5, 5, 5, 4, 4, 4, 4, 4, 4, 4, 4,
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0
        };

        static readonly byte[] kNewRange = {
            127, 127, 191, 127, 159, 191, 223, 127,
            143, 159, 175, 191, 207, 223, 239, 127,
            135, 143, 151, 159, 167, 175, 183, 191,
            199, 207, 215, 223, 231, 239, 247, 127,
            131, 135, 139, 143, 147, 151, 155, 159,
            163, 167, 171, 175, 179, 183, 187, 191,
            195, 199, 203, 207, 211, 215, 219, 223,
            227, 231, 235, 239, 243, 247, 251, 127,
            129, 131, 133, 135, 137, 139, 141, 143,
            145, 147, 149, 151, 153, 155, 157, 159,
            161, 163, 165, 167, 169, 171, 173, 175,
            177, 179, 181, 183, 185, 187, 189, 191,
            193, 195, 197, 199, 201, 203, 205, 207,
            209, 211, 213, 215, 217, 219, 221, 223,
            225, 227, 229, 231, 233, 235, 237, 239,
            241, 243, 245, 247, 249, 251, 253, 127
        };

        static readonly byte[] kDcTable = {
            4,     5,   6,   7,   8,   9,  10,  10,
            11,   12,  13,  14,  15,  16,  17,  17,
            18,   19,  20,  20,  21,  21,  22,  22,
            23,   23,  24,  25,  25,  26,  27,  28,
            29,   30,  31,  32,  33,  34,  35,  36,
            37,   37,  38,  39,  40,  41,  42,  43,
            44,   45,  46,  46,  47,  48,  49,  50,
            51,   52,  53,  54,  55,  56,  57,  58,
            59,   60,  61,  62,  63,  64,  65,  66,
            67,   68,  69,  70,  71,  72,  73,  74,
            75,   76,  76,  77,  78,  79,  80,  81,
            82,   83,  84,  85,  86,  87,  88,  89,
            91,   93,  95,  96,  98, 100, 101, 102,
            104, 106, 108, 110, 112, 114, 116, 118,
            122, 124, 126, 128, 130, 132, 134, 136,
            138, 140, 143, 145, 148, 151, 154, 157
        };

        static readonly ushort[] kAcTable = {
            4,     5,   6,   7,   8,   9,  10,  11,
            12,   13,  14,  15,  16,  17,  18,  19,
            20,   21,  22,  23,  24,  25,  26,  27,
            28,   29,  30,  31,  32,  33,  34,  35,
            36,   37,  38,  39,  40,  41,  42,  43,
            44,   45,  46,  47,  48,  49,  50,  51,
            52,   53,  54,  55,  56,  57,  58,  60,
            62,   64,  66,  68,  70,  72,  74,  76,
            78,   80,  82,  84,  86,  88,  90,  92,
            94,   96,  98, 100, 102, 104, 106, 108,
            110, 112, 114, 116, 119, 122, 125, 128,
            131, 134, 137, 140, 143, 146, 149, 152,
            155, 158, 161, 164, 167, 170, 173, 177,
            181, 185, 189, 193, 197, 201, 205, 209,
            213, 217, 221, 225, 229, 234, 239, 245,
            249, 254, 259, 264, 269, 274, 279, 284
        };

        static readonly byte[,,] kBModesProba = new byte[NumBModes,NumBModes,NumBModes - 1] {
        { { 231, 120, 48, 89, 115, 113, 120, 152, 112 },
            { 152, 179, 64, 126, 170, 118, 46, 70, 95 },
            { 175, 69, 143, 80, 85, 82, 72, 155, 103 },
            { 56, 58, 10, 171, 218, 189, 17, 13, 152 },
            { 114, 26, 17, 163, 44, 195, 21, 10, 173 },
            { 121, 24, 80, 195, 26, 62, 44, 64, 85 },
            { 144, 71, 10, 38, 171, 213, 144, 34, 26 },
            { 170, 46, 55, 19, 136, 160, 33, 206, 71 },
            { 63, 20, 8, 114, 114, 208, 12, 9, 226 },
            { 81, 40, 11, 96, 182, 84, 29, 16, 36 } },
        { { 134, 183, 89, 137, 98, 101, 106, 165, 148 },
            { 72, 187, 100, 130, 157, 111, 32, 75, 80 },
            { 66, 102, 167, 99, 74, 62, 40, 234, 128 },
            { 41, 53, 9, 178, 241, 141, 26, 8, 107 },
            { 74, 43, 26, 146, 73, 166, 49, 23, 157 },
            { 65, 38, 105, 160, 51, 52, 31, 115, 128 },
            { 104, 79, 12, 27, 217, 255, 87, 17, 7 },
            { 87, 68, 71, 44, 114, 51, 15, 186, 23 },
            { 47, 41, 14, 110, 182, 183, 21, 17, 194 },
            { 66, 45, 25, 102, 197, 189, 23, 18, 22 } },
        { { 88, 88, 147, 150, 42, 46, 45, 196, 205 },
            { 43, 97, 183, 117, 85, 38, 35, 179, 61 },
            { 39, 53, 200, 87, 26, 21, 43, 232, 171 },
            { 56, 34, 51, 104, 114, 102, 29, 93, 77 },
            { 39, 28, 85, 171, 58, 165, 90, 98, 64 },
            { 34, 22, 116, 206, 23, 34, 43, 166, 73 },
            { 107, 54, 32, 26, 51, 1, 81, 43, 31 },
            { 68, 25, 106, 22, 64, 171, 36, 225, 114 },
            { 34, 19, 21, 102, 132, 188, 16, 76, 124 },
            { 62, 18, 78, 95, 85, 57, 50, 48, 51 } },
        { { 193, 101, 35, 159, 215, 111, 89, 46, 111 },
            { 60, 148, 31, 172, 219, 228, 21, 18, 111 },
            { 112, 113, 77, 85, 179, 255, 38, 120, 114 },
            { 40, 42, 1, 196, 245, 209, 10, 25, 109 },
            { 88, 43, 29, 140, 166, 213, 37, 43, 154 },
            { 61, 63, 30, 155, 67, 45, 68, 1, 209 },
            { 100, 80, 8, 43, 154, 1, 51, 26, 71 },
            { 142, 78, 78, 16, 255, 128, 34, 197, 171 },
            { 41, 40, 5, 102, 211, 183, 4, 1, 221 },
            { 51, 50, 17, 168, 209, 192, 23, 25, 82 } },
        { { 138, 31, 36, 171, 27, 166, 38, 44, 229 },
            { 67, 87, 58, 169, 82, 115, 26, 59, 179 },
            { 63, 59, 90, 180, 59, 166, 93, 73, 154 },
            { 40, 40, 21, 116, 143, 209, 34, 39, 175 },
            { 47, 15, 16, 183, 34, 223, 49, 45, 183 },
            { 46, 17, 33, 183, 6, 98, 15, 32, 183 },
            { 57, 46, 22, 24, 128, 1, 54, 17, 37 },
            { 65, 32, 73, 115, 28, 128, 23, 128, 205 },
            { 40, 3, 9, 115, 51, 192, 18, 6, 223 },
            { 87, 37, 9, 115, 59, 77, 64, 21, 47 } },
        { { 104, 55, 44, 218, 9, 54, 53, 130, 226 },
            { 64, 90, 70, 205, 40, 41, 23, 26, 57 },
            { 54, 57, 112, 184, 5, 41, 38, 166, 213 },
            { 30, 34, 26, 133, 152, 116, 10, 32, 134 },
            { 39, 19, 53, 221, 26, 114, 32, 73, 255 },
            { 31, 9, 65, 234, 2, 15, 1, 118, 73 },
            { 75, 32, 12, 51, 192, 255, 160, 43, 51 },
            { 88, 31, 35, 67, 102, 85, 55, 186, 85 },
            { 56, 21, 23, 111, 59, 205, 45, 37, 192 },
            { 55, 38, 70, 124, 73, 102, 1, 34, 98 } },
        { { 125, 98, 42, 88, 104, 85, 117, 175, 82 },
            { 95, 84, 53, 89, 128, 100, 113, 101, 45 },
            { 75, 79, 123, 47, 51, 128, 81, 171, 1 },
            { 57, 17, 5, 71, 102, 57, 53, 41, 49 },
            { 38, 33, 13, 121, 57, 73, 26, 1, 85 },
            { 41, 10, 67, 138, 77, 110, 90, 47, 114 },
            { 115, 21, 2, 10, 102, 255, 166, 23, 6 },
            { 101, 29, 16, 10, 85, 128, 101, 196, 26 },
            { 57, 18, 10, 102, 102, 213, 34, 20, 43 },
            { 117, 20, 15, 36, 163, 128, 68, 1, 26 } },
        { { 102, 61, 71, 37, 34, 53, 31, 243, 192 },
            { 69, 60, 71, 38, 73, 119, 28, 222, 37 },
            { 68, 45, 128, 34, 1, 47, 11, 245, 171 },
            { 62, 17, 19, 70, 146, 85, 55, 62, 70 },
            { 37, 43, 37, 154, 100, 163, 85, 160, 1 },
            { 63, 9, 92, 136, 28, 64, 32, 201, 85 },
            { 75, 15, 9, 9, 64, 255, 184, 119, 16 },
            { 86, 6, 28, 5, 64, 255, 25, 248, 1 },
            { 56, 8, 17, 132, 137, 255, 55, 116, 128 },
            { 58, 15, 20, 82, 135, 57, 26, 121, 40 } },
        { { 164, 50, 31, 137, 154, 133, 25, 35, 218 },
            { 51, 103, 44, 131, 131, 123, 31, 6, 158 },
            { 86, 40, 64, 135, 148, 224, 45, 183, 128 },
            { 22, 26, 17, 131, 240, 154, 14, 1, 209 },
            { 45, 16, 21, 91, 64, 222, 7, 1, 197 },
            { 56, 21, 39, 155, 60, 138, 23, 102, 213 },
            { 83, 12, 13, 54, 192, 255, 68, 47, 28 },
            { 85, 26, 85, 85, 128, 128, 32, 146, 171 },
            { 18, 11, 7, 63, 144, 171, 4, 4, 246 },
            { 35, 27, 10, 146, 174, 171, 12, 26, 128 } },
        { { 190, 80, 35, 99, 180, 80, 126, 54, 45 },
            { 85, 126, 47, 87, 176, 51, 41, 20, 32 },
            { 101, 75, 128, 139, 118, 146, 116, 128, 85 },
            { 56, 41, 15, 176, 236, 85, 37, 9, 62 },
            { 71, 30, 17, 119, 118, 255, 17, 18, 138 },
            { 101, 38, 60, 138, 55, 70, 43, 26, 142 },
            { 146, 36, 19, 30, 171, 255, 97, 27, 20 },
            { 138, 45, 61, 62, 219, 1, 81, 188, 64 },
            { 32, 41, 20, 117, 151, 142, 20, 21, 163 },
            { 112, 19, 12, 61, 195, 128, 48, 4, 24 } }
        };

        static readonly sbyte[] kYModesIntra4 = new sbyte[18] {
            -B_DC_PRED, 1,
                -B_TM_PRED, 2,
                    -B_VE_PRED, 3,
                        4, 6,
                            -B_HE_PRED, 5,
                                -B_RD_PRED, -B_VR_PRED,
                        -B_LD_PRED, 7,
                            -B_VL_PRED, 8,
                                -B_HD_PRED, -B_HU_PRED
        };

        static readonly byte[] kFilterExtraRows = { 0, 2, 8 };

        static readonly byte[] kCat3 = { 173, 148, 140, 0 };
        static readonly byte[] kCat4 = { 176, 155, 140, 135, 0 };
        static readonly byte[] kCat5 = { 180, 157, 141, 134, 130, 0 };
        static readonly byte[] kCat6 = { 254, 254, 243, 230, 196, 177, 153, 140, 133, 130, 129, 0 };
        static readonly byte[][] kCat3456 = { kCat3, kCat4, kCat5, kCat6 };
        static readonly byte[] kZigzag = {
            0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15
        };

        static readonly int[] kScan = {
            0 +  0 * BPS,  4 +  0 * BPS, 8 +  0 * BPS, 12 +  0 * BPS,
            0 +  4 * BPS,  4 +  4 * BPS, 8 +  4 * BPS, 12 +  4 * BPS,
            0 +  8 * BPS,  4 +  8 * BPS, 8 +  8 * BPS, 12 +  8 * BPS,
            0 + 12 * BPS,  4 + 12 * BPS, 8 + 12 * BPS, 12 + 12 * BPS
        };

        const int WEBP_FILTER_NONE = 0;
        const int WEBP_FILTER_HORIZONTAL = 1;
        const int WEBP_FILTER_VERTICAL = 2;
        const int WEBP_FILTER_GRADIENT = 3;
        const int WEBP_FILTER_LAST = WEBP_FILTER_GRADIENT + 1;
    }

    internal class VP8Io
    {
        public int width, height;

        public int mb_y;                  // position of the current rows (in pixels)
        public int mb_w;                  // number of columns in the sample
        public int mb_h;                  // number of rows in the sample
        public int y, u, v;               // rows to copy (in yuv420 format)
        public int y_stride;              // row stride for luma
        public int uv_stride;             // row stride for chroma

        public byte[] opaque;

        int last_y;                       // coordinate of the line that was last output

        // If non NULL, pointer to the alpha data (if present) corresponding to the
        // start of the current row (That is: it is pre-offset by mb_y and takes
        // cropping into account).
        public byte[] alpha_plane;
        public int a;

        public void Init (WebPDecoder dec)
        {
            mb_y = 0;
            y = dec.cache_y_;
            u = dec.cache_u_;
            v = dec.cache_v_;
            y_stride = dec.cache_y_stride_;
            uv_stride = dec.cache_uv_stride_;
            alpha_plane = dec.AlphaPlane;
            a = 0;
            last_y = 0;
        }

        public bool Put (WebPDecoder dec)
        {
            if (mb_w <= 0 || mb_h <= 0)
                return false;
            int num_lines_out = EmitSampledRGB (dec);
            if (alpha_plane != null)
                EmitAlphaRGB (dec, num_lines_out);
            last_y += num_lines_out;
            return true;
        }

        public int EmitSampledRGB (WebPDecoder dec)
        {
            var input = dec.Cache;
            var output = dec.Output;
            int dst_stride = dec.Stride;
            int dst = mb_y * dst_stride;
            for (int j = 0; j < mb_h; ++j)
            {
                YuvToBgraRow (input, y, u, v, output, dst, mb_w);
                y += y_stride;
                if (0 != (j & 1))
                {
                    u += uv_stride;
                    v += uv_stride;
                }
                dst += dst_stride;
            }
            return mb_h;
        }

        void EmitAlphaRGB (WebPDecoder dec, int expected_num_lines_out)
        {
            int base_rgba = mb_y * dec.Stride;
            int dst = base_rgba + 3;
            DispatchAlpha (alpha_plane, a, width, mb_w, mb_h, dec.Output, dst, dec.Stride);
        }

        static bool DispatchAlpha (byte[] alpha, int src, int alpha_stride, int width, int height, byte[] output, int dst, int dst_stride)
        {
            int alpha_src = src;
            uint alpha_mask = 0xFF;
            for (int j = 0; j < height; ++j)
            {
                for (int i = 0; i < width; ++i)
                {
                    byte alpha_value = alpha[alpha_src+i];
                    output[dst + 4 * i] = alpha_value;
                    alpha_mask &= alpha_value;
                }
                alpha_src += alpha_stride;
                dst += dst_stride;
            }
            return alpha_mask != 0xFF;
        }

        static void YuvToBgraRow (byte[] input, int y, int u, int v, byte[] output, int dst, int len)
        {
            const int x_step = 4;
            int end = dst + (len & ~1) * x_step;
            while (dst != end)
            {
                YuvToBgra (input[y], input[u], input[v], output, dst);
                YuvToBgra (input[y+1], input[u], input[v], output, dst + x_step);
                y += 2;
                ++u;
                ++v;
                dst += 2 * x_step;
            }
            if (0 != (len & 1))
                YuvToBgra (input[y], input[u], input[v], output, dst);
        }

        static void YuvToBgra (byte y, byte u, byte v, byte[] bgra, int dst)
        {
            bgra[dst]   = YUVToB (y, u);
            bgra[dst+1] = YUVToG (y, u, v);
            bgra[dst+2] = YUVToR (y, v);
            bgra[dst+3] = 0xFF;
        }

        static byte YUVToR(int y, int v)
        {
            return Clip8 (MultHi (y, 19077) + MultHi (v, 26149) - 14234);
        }

        static byte YUVToG (int y, int u, int v)
        {
            return Clip8 (MultHi (y, 19077) - MultHi (u, 6419) - MultHi (v, 13320) + 8708);
        }

        static byte YUVToB(int y, int u)
        {
            return Clip8 (MultHi (y, 19077) + MultHi (u, 33050) - 17685);
        }

        static byte Clip8 (int v)
        {
            return (byte)(((v & ~YUV_MASK2) == 0) ? (v >> YUV_FIX2) : (v < 0) ? 0 : 255);
        }

        static int MultHi (int v, int coeff) // _mm_mulhi_epu16 emulation
        {
            return (v * coeff) >> 8;
        }

        const int YUV_FIX = 16;                     // fixed-point precision for RGB->YUV
        const int YUV_HALF = 1 << (YUV_FIX - 1);
        const int YUV_MASK = (256 << YUV_FIX) - 1;
        const int YUV_RANGE_MIN = -227;             // min value of r/g/b output
        const int YUV_RANGE_MAX = 256 + 226;        // max value of r/g/b output

        const int YUV_FIX2 = 6;                     // fixed-point precision for YUV->RGB
        const int YUV_HALF2 = 1 << YUV_FIX2 >> 1;
        const int YUV_MASK2 = (256 << YUV_FIX2) - 1;
    }
}
