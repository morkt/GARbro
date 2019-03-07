//! \file       ImageTLG.cs
//! \date       Thu Jul 17 21:31:39 2014
//! \brief      KiriKiri TLG image implementation.
//---------------------------------------------------------------------------
// TLG5/6 decoder
//	Copyright (C) 2000-2005  W.Dee <dee@kikyou.info> and contributors
//
// C# port by morkt
//

using System;
using System.IO;
using System.ComponentModel.Composition;
using System.Windows.Media;
using GameRes.Utility;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace GameRes.Formats.KiriKiri
{
    internal class TlgMetaData : ImageMetaData
    {
        public int Version;
        public int DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class TlgFormat : ImageFormat
    {
        public override string Tag { get { return "TLG"; } }
        public override string Description { get { return "KiriKiri game engine image format"; } }
        public override uint Signature { get { return 0x30474c54; } } // "TLG0"

        public TlgFormat ()
        {
            Extensions = new string[] { "tlg", "tlg5", "tlg6" };
            Signatures = new uint[] { 0x30474C54, 0x35474C54, 0x36474C54, 0x35474CAB, 0x584D4B4A };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x26);
            int offset = 0xf;
            if (!header.AsciiEqual ("TLG0.0\x00sds\x1a"))
                offset = 0;
            int version;
            if (!header.AsciiEqual (offset+6, "\x00raw\x1a"))
                return null;
            if (0xAB == header[offset])
                header[offset] = (byte)'T';
            if (header.AsciiEqual (offset, "TLG6.0"))
                version = 6;
            else if (header.AsciiEqual (offset, "TLG5.0"))
                version = 5;
            else if (header.AsciiEqual (offset, "XXXYYY"))
            {
                version = 5;
                header[offset+0x0C] ^= 0xAB;
                header[offset+0x10] ^= 0xAC;
            }
            else if (header.AsciiEqual (offset, "XXXZZZ"))
            {
                version = 6;
                header[offset+0x0F] ^= 0xAB;
                header[offset+0x13] ^= 0xAC;
            }
            else if (header.AsciiEqual (offset, "JKMXE8"))
            {
                version = 5;
                header[offset+0x0C] ^= 0x1A;
                header[offset+0x10] ^= 0x1C;
            }
            else
                return null;
            int colors = header[offset+11];
            if (6 == version)
            {
                if (1 != colors && 4 != colors && 3 != colors)
                    return null;
                if (header[offset+12] != 0 || header[offset+13] != 0 || header[offset+14] != 0)
                    return null;
                offset += 15;
            }
            else
            {
                if (4 != colors && 3 != colors)
                    return null;
                offset += 12;
            }
            return new TlgMetaData
            {
                Width   = header.ToUInt32 (offset),
                Height  = header.ToUInt32 (offset+4),
                BPP     = colors*8,
                Version     = version,
                DataOffset  = offset+8,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (TlgMetaData)info;

            var image = ReadTlg (file, meta);

            int tail_size = (int)Math.Min (file.Length - file.Position, 512);
            if (tail_size > 8)
            {
                var tail = file.ReadBytes (tail_size);
                try
                {
                    var blended_image = ApplyTags (image, meta, tail);
                    if (null != blended_image)
                        return blended_image;
                }
                catch (FileNotFoundException X)
                {
                    Trace.WriteLine (string.Format ("{0}: {1}", X.Message, X.FileName), "[TlgFormat.Read]");
                }
                catch (Exception X)
                {
                    Trace.WriteLine (X.Message, "[TlgFormat.Read]");
                }
            }
            PixelFormat format = 32 == meta.BPP ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
            return ImageData.Create (meta, format, null, image, (int)meta.Width * 4);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("TlgFormat.Write not implemented");
        }

        byte[] ReadTlg (IBinaryStream src, TlgMetaData info)
        {
            src.Position = info.DataOffset;
            if (6 == info.Version)
                return ReadV6 (src, info);
            else
                return ReadV5 (src, info);
        }

        ImageData ApplyTags (byte[] image, TlgMetaData meta, byte[] tail)
        {
            int i = tail.Length - 8;
            while (i >= 0)
            {
                if ('s' == tail[i+3] && 'g' == tail[i+2] && 'a' == tail[i+1] && 't' == tail[i])
                    break;
                --i;
            }
            if (i < 0)
                return null;
            var tags = new TagsParser (tail, i+4);
            if (!tags.Parse())
                return null;
            var base_name   = tags.GetString (1);
            meta.OffsetX    = tags.GetInt (2) & 0xFFFF;
            meta.OffsetY    = tags.GetInt (3) & 0xFFFF;
            if (string.IsNullOrEmpty (base_name))
                return null;
            int method = 1;
            if (tags.HasKey (4))
                method = tags.GetInt (4);

            base_name = VFS.CombinePath (VFS.GetDirectoryName (meta.FileName), base_name);
            if (base_name == meta.FileName)
                return null;

            TlgMetaData base_info;
            byte[] base_image;
            using (var base_file = VFS.OpenBinaryStream (base_name))
            {
                base_info = ReadMetaData (base_file) as TlgMetaData;
                if (null == base_info)
                    return null;
                base_info.FileName = base_name;
                base_image = ReadTlg (base_file, base_info);
            }
            var pixels = BlendImage (base_image, base_info, image, meta, method);
            PixelFormat format = 32 == base_info.BPP ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
            return ImageData.Create (base_info, format, null, pixels, (int)base_info.Width*4);
        }

        byte[] BlendImage (byte[] base_image, ImageMetaData base_info, byte[] overlay, ImageMetaData overlay_info, int method)
        {
            int dst_stride = (int)base_info.Width * 4;
            int src_stride = (int)overlay_info.Width * 4;
            int dst = overlay_info.OffsetY * dst_stride + overlay_info.OffsetX * 4;
            int src = 0;
            int gap = dst_stride - src_stride;
            for (uint y = 0; y < overlay_info.Height; ++y)
            {
                for (uint x = 0; x < overlay_info.Width; ++x)
                {
                    byte src_alpha = overlay[src+3];
                    if (2 == method)
                    {
                        base_image[dst]   ^= overlay[src];
                        base_image[dst+1] ^= overlay[src+1];
                        base_image[dst+2] ^= overlay[src+2];
                        base_image[dst+3] ^= src_alpha;
                    }
                    else if (src_alpha != 0)
                    {
                        if (0xFF == src_alpha || 0 == base_image[dst+3])
                        {
                            base_image[dst]   = overlay[src];
                            base_image[dst+1] = overlay[src+1];
                            base_image[dst+2] = overlay[src+2];
                            base_image[dst+3] = src_alpha;
                        }
                        else
                        {
                            // FIXME this blending algorithm is oversimplified.
                            base_image[dst+0] = (byte)((overlay[src+0] * src_alpha
                                              + base_image[dst+0] * (0xFF - src_alpha)) / 0xFF);
                            base_image[dst+1] = (byte)((overlay[src+1] * src_alpha
                                              + base_image[dst+1] * (0xFF - src_alpha)) / 0xFF);
                            base_image[dst+2] = (byte)((overlay[src+2] * src_alpha
                                              + base_image[dst+2] * (0xFF - src_alpha)) / 0xFF);
                            base_image[dst+3] = (byte)Math.Max (src_alpha, base_image[dst+3]);
                        }
                    }
                    dst += 4;
                    src += 4;
                }
                dst += gap;
            }
            return base_image;
        }

        const int TVP_TLG6_H_BLOCK_SIZE = 8;
        const int TVP_TLG6_W_BLOCK_SIZE = 8;

        const int TVP_TLG6_GOLOMB_N_COUNT = 4;
        const int TVP_TLG6_LeadingZeroTable_BITS = 12;
        const int TVP_TLG6_LeadingZeroTable_SIZE = (1<<TVP_TLG6_LeadingZeroTable_BITS);

        byte[] ReadV6 (IBinaryStream src, TlgMetaData info)
        {
            int width = (int)info.Width;
            int height = (int)info.Height;
            int colors = info.BPP / 8;
            int max_bit_length = src.ReadInt32();

            int x_block_count = ((width - 1)/ TVP_TLG6_W_BLOCK_SIZE) + 1;
            int y_block_count = ((height - 1)/ TVP_TLG6_H_BLOCK_SIZE) + 1;
            int main_count = width / TVP_TLG6_W_BLOCK_SIZE;
            int fraction = width -  main_count * TVP_TLG6_W_BLOCK_SIZE;

            var image_bits = new uint[height * width];
            var bit_pool = new byte[max_bit_length / 8 + 5];
            var pixelbuf = new uint[width * TVP_TLG6_H_BLOCK_SIZE + 1];
            var filter_types = new byte[x_block_count * y_block_count];
            var zeroline = new uint[width];
            var LZSS_text = new byte[4096];

            // initialize zero line (virtual y=-1 line)
            uint zerocolor = 3 == colors ? 0xff000000 : 0x00000000;
            for (var i = 0; i < width; ++i)
                zeroline[i] = zerocolor;

            uint[] prevline = zeroline;
            int prevline_index = 0;

            // initialize LZSS text (used by chroma filter type codes)
            int p = 0;
            for (uint i = 0; i < 32*0x01010101; i += 0x01010101)
            {
                for (uint j = 0; j < 16*0x01010101; j += 0x01010101)
                {
                    LZSS_text[p++] = (byte)(i       & 0xff);
                    LZSS_text[p++] = (byte)(i >> 8  & 0xff);
                    LZSS_text[p++] = (byte)(i >> 16 & 0xff);
                    LZSS_text[p++] = (byte)(i >> 24 & 0xff);
                    LZSS_text[p++] = (byte)(j       & 0xff);
                    LZSS_text[p++] = (byte)(j >> 8  & 0xff);
                    LZSS_text[p++] = (byte)(j >> 16 & 0xff);
                    LZSS_text[p++] = (byte)(j >> 24 & 0xff);
                }
            }
            // read chroma filter types.
            // chroma filter types are compressed via LZSS as used by TLG5.
            {
                int inbuf_size = src.ReadInt32();
                byte[] inbuf = src.ReadBytes (inbuf_size);
                if (inbuf_size != inbuf.Length)
                    return null;
                TVPTLG5DecompressSlide (filter_types, inbuf, inbuf_size, LZSS_text, 0);
            }

            // for each horizontal block group ...
            for (int y = 0; y < height; y += TVP_TLG6_H_BLOCK_SIZE)
            {
                int ylim = y + TVP_TLG6_H_BLOCK_SIZE;
                if (ylim >= height) ylim = height;

                int pixel_count = (ylim - y) * width;

                // decode values
                for (int c = 0; c < colors; c++)
                {
                    // read bit length
                    int bit_length = src.ReadInt32();

                    // get compress method
                    int method = (bit_length >> 30) & 3;
                    bit_length &= 0x3fffffff;

                    // compute byte length
                    int byte_length = bit_length / 8;
                    if (0 != (bit_length % 8)) byte_length++;

                    // read source from input
                    src.Read (bit_pool, 0, byte_length);

                    // decode values
                    // two most significant bits of bitlength are
                    // entropy coding method;
                    // 00 means Golomb method,
                    // 01 means Gamma method (not yet suppoted),
                    // 10 means modified LZSS method (not yet supported),
                    // 11 means raw (uncompressed) data (not yet supported).

                    switch (method)
                    {
                    case 0:
                        if (c == 0 && colors != 1)
                            TVPTLG6DecodeGolombValuesForFirst (pixelbuf, pixel_count, bit_pool);
                        else
                            TVPTLG6DecodeGolombValues (pixelbuf, c*8, pixel_count, bit_pool);
                        break;
                    default:
                        throw new InvalidFormatException ("Unsupported entropy coding method");
                    }
                }

                // for each line
                int ft = (y / TVP_TLG6_H_BLOCK_SIZE) * x_block_count; // within filter_types
                int skipbytes = (ylim - y) * TVP_TLG6_W_BLOCK_SIZE;

                for (int yy = y; yy < ylim; yy++)
                {
                    int curline = yy*width;

                    int dir = (yy&1)^1;
                    int oddskip = ((ylim - yy -1) - (yy-y));
                    if (0 != main_count)
                    {
                        int start =
                            ((width < TVP_TLG6_W_BLOCK_SIZE) ? width : TVP_TLG6_W_BLOCK_SIZE) *
                                (yy - y);
                        TVPTLG6DecodeLineGeneric (
                            prevline, prevline_index,
                            image_bits, curline,
                            width, 0, main_count,
                            filter_types, ft,
                            skipbytes,
                            pixelbuf, start,
                            zerocolor, oddskip, dir);
                    }

                    if (main_count != x_block_count)
                    {
                        int ww = fraction;
                        if (ww > TVP_TLG6_W_BLOCK_SIZE) ww = TVP_TLG6_W_BLOCK_SIZE;
                        int start = ww * (yy - y);
                        TVPTLG6DecodeLineGeneric (
                            prevline, prevline_index,
                            image_bits, curline,
                            width, main_count, x_block_count,
                            filter_types, ft,
                            skipbytes,
                            pixelbuf, start,
                            zerocolor, oddskip, dir);
                    }
                    prevline = image_bits;
                    prevline_index = curline;
                }
            }
            int stride = width * 4;
            var pixels = new byte[height * stride];
            Buffer.BlockCopy (image_bits, 0, pixels, 0, pixels.Length);
            return pixels;
        }

        byte[] ReadV5 (IBinaryStream src, TlgMetaData info)
        {
            int width = (int)info.Width;
            int height = (int)info.Height;
            int colors = info.BPP / 8;
            int blockheight = src.ReadInt32();
            int blockcount = (height - 1) / blockheight + 1;

            // skip block size section
            src.Seek (blockcount * 4, SeekOrigin.Current);

            int stride = width * 4;
            var image_bits = new byte[height * stride];
            var text = new byte[4096];
            for (int i = 0; i < 4096; ++i)
                text[i] = 0;

            var inbuf = new byte[blockheight * width + 10];
            byte [][] outbuf = new byte[4][];
            for (int i = 0; i < colors; i++)
                outbuf[i] = new byte[blockheight * width + 10];

            int z = 0;
            int prevline = -1;
            for (int y_blk = 0; y_blk < height; y_blk += blockheight)
            {
                // read file and decompress
                for (int c = 0; c < colors; c++)
                {
                    byte mark = src.ReadUInt8();
                    int size;
                    size = src.ReadInt32();
                    if (mark == 0)
                    {
                        // modified LZSS compressed data
                        if (size != src.Read (inbuf, 0, size))
                            return null;
                        z = TVPTLG5DecompressSlide (outbuf[c], inbuf, size, text, z);
                    }
                    else
                    {
                        // raw data
                        src.Read (outbuf[c], 0, size);
                    }
                }

                // compose colors and store
                int y_lim = y_blk + blockheight;
                if (y_lim > height) y_lim = height;
                int outbuf_pos = 0;
                for (int y = y_blk; y < y_lim; y++)
                {
                    int current = y * stride;
                    int current_org = current;
                    if (prevline >= 0)
                    {
                        // not first line
                        switch(colors)
                        {
                        case 3:
                            TVPTLG5ComposeColors3To4 (image_bits, current, prevline,
                                                        outbuf, outbuf_pos, width);
                            break;
                        case 4:
                            TVPTLG5ComposeColors4To4 (image_bits, current, prevline,
                                                        outbuf, outbuf_pos, width);
                            break;
                        }
                    }
                    else
                    {
                        // first line
                        switch(colors)
                        {
                        case 3:
                            for (int pr = 0, pg = 0, pb = 0, x = 0;
                                    x < width; x++)
                            {
                                int b = outbuf[0][outbuf_pos+x];
                                int g = outbuf[1][outbuf_pos+x];
                                int r = outbuf[2][outbuf_pos+x];
                                b += g; r += g;
                                image_bits[current++] = (byte)(pb += b);
                                image_bits[current++] = (byte)(pg += g);
                                image_bits[current++] = (byte)(pr += r);
                                image_bits[current++] = 0xff;
                            }
                            break;
                        case 4:
                            for (int pr = 0, pg = 0, pb = 0, pa = 0, x = 0;
                                    x < width; x++)
                            {
                                int b = outbuf[0][outbuf_pos+x];
                                int g = outbuf[1][outbuf_pos+x];
                                int r = outbuf[2][outbuf_pos+x];
                                int a = outbuf[3][outbuf_pos+x];
                                b += g; r += g;
                                image_bits[current++] = (byte)(pb += b);
                                image_bits[current++] = (byte)(pg += g);
                                image_bits[current++] = (byte)(pr += r);
                                image_bits[current++] = (byte)(pa += a);
                            }
                            break;
                        }
                    }
                    outbuf_pos += width;
                    prevline = current_org;
                }
            }
            return image_bits;
        }

        void TVPTLG5ComposeColors3To4 (byte[] outp, int outp_index, int upper,
                                       byte[][] buf, int bufpos, int width)
        {
            byte pc0 = 0, pc1 = 0, pc2 = 0;
            byte c0, c1, c2;
            for (int x = 0; x < width; x++)
            {
                c0 = buf[0][bufpos+x];
                c1 = buf[1][bufpos+x];
                c2 = buf[2][bufpos+x];
                c0 += c1; c2 += c1;
                outp[outp_index++] = (byte)(((pc0 += c0) + outp[upper+0]) & 0xff);
                outp[outp_index++] = (byte)(((pc1 += c1) + outp[upper+1]) & 0xff);
                outp[outp_index++] = (byte)(((pc2 += c2) + outp[upper+2]) & 0xff);
                outp[outp_index++] = 0xff;
                upper += 4;
            }
        }

        void TVPTLG5ComposeColors4To4 (byte[] outp, int outp_index, int upper,
                                       byte[][] buf, int bufpos, int width)
        {
            byte pc0 = 0, pc1 = 0, pc2 = 0, pc3 = 0;
            byte c0, c1, c2, c3;
            for (int x = 0; x < width; x++)
            {
                c0 = buf[0][bufpos+x];
                c1 = buf[1][bufpos+x];
                c2 = buf[2][bufpos+x];
                c3 = buf[3][bufpos+x];
                c0 += c1; c2 += c1;
                outp[outp_index++] = (byte)(((pc0 += c0) + outp[upper+0]) & 0xff);
                outp[outp_index++] = (byte)(((pc1 += c1) + outp[upper+1]) & 0xff);
                outp[outp_index++] = (byte)(((pc2 += c2) + outp[upper+2]) & 0xff);
                outp[outp_index++] = (byte)(((pc3 += c3) + outp[upper+3]) & 0xff);
                upper += 4;
            }
        }

        int TVPTLG5DecompressSlide (byte[] outbuf, byte[] inbuf, int inbuf_size, byte[] text, int initialr)
        {
            int r = initialr;
            uint flags = 0;
            int o = 0;
            for (int i = 0; i < inbuf_size; )
            {
                if (((flags >>= 1) & 256) == 0)
                {
                    flags = (uint)(inbuf[i++] | 0xff00);
                }
                if (0 != (flags & 1))
                {
                    int mpos = inbuf[i] | ((inbuf[i+1] & 0xf) << 8);
                    int mlen = (inbuf[i+1] & 0xf0) >> 4;
                    i += 2;
                    mlen += 3;
                    if (mlen == 18) mlen += inbuf[i++];

                    while (0 != mlen--)
                    {
                        outbuf[o++] = text[r++] = text[mpos++];
                        mpos &= (4096 - 1);
                        r &= (4096 - 1);
                    }
                }
                else
                {
                    byte c = inbuf[i++];
                    outbuf[o++] = c;
                    text[r++] = c;
                    r &= (4096 - 1);
                }
            }
            return r;
        }

        static uint tvp_make_gt_mask (uint a, uint b)
        {
            uint tmp2 = ~b;
            uint tmp = ((a & tmp2) + (((a ^ tmp2) >> 1) & 0x7f7f7f7f) ) & 0x80808080;
            tmp = ((tmp >> 7) + 0x7f7f7f7f) ^ 0x7f7f7f7f;
            return tmp;
        }

        static uint tvp_packed_bytes_add (uint a, uint b)
        {
            uint tmp = (uint)((((a & b)<<1) + ((a ^ b) & 0xfefefefe) ) & 0x01010100);
            return a+b-tmp;
        }

        static uint tvp_med2 (uint a, uint b, uint c)
        {
            /* do Median Edge Detector   thx, Mr. sugi  at    kirikiri.info */
            uint aa_gt_bb = tvp_make_gt_mask(a, b);
            uint a_xor_b_and_aa_gt_bb = ((a ^ b) & aa_gt_bb);
            uint aa = a_xor_b_and_aa_gt_bb ^ a;
            uint bb = a_xor_b_and_aa_gt_bb ^ b;
            uint n = tvp_make_gt_mask(c, bb);
            uint nn = tvp_make_gt_mask(aa, c);
            uint m = ~(n | nn);
            return (n & aa) | (nn & bb) | ((bb & m) - (c & m) + (aa & m));
        }

        static uint tvp_med (uint a, uint b, uint c, uint v)
        {
            return tvp_packed_bytes_add (tvp_med2 (a, b, c), v);
        }

        static uint tvp_avg (uint a, uint b, uint c, uint v)
        {
            return tvp_packed_bytes_add ((((a&b) + (((a^b) & 0xfefefefe) >> 1)) + ((a^b)&0x01010101)), v);
        }

        delegate uint tvp_decoder (uint a, uint b, uint c, uint v);

        void TVPTLG6DecodeLineGeneric (uint[] prevline, int prevline_index,
                                       uint[] curline, int curline_index,
                                       int width, int start_block, int block_limit,
                                       byte[] filtertypes, int filtertypes_index,
                                       int skipblockbytes,
                                       uint[] inbuf, int inbuf_index,
                                       uint initialp, int oddskip, int dir)
        {
            /*
                chroma/luminosity decoding
                (this does reordering, color correlation filter, MED/AVG  at a time)
            */
            uint p, up;

            if (0 != start_block)
            {
                prevline_index += start_block * TVP_TLG6_W_BLOCK_SIZE;
                curline_index  += start_block * TVP_TLG6_W_BLOCK_SIZE;
                p  = curline[curline_index-1];
                up = prevline[prevline_index-1];
            }
            else
            {
                p = up = initialp;
            }

            inbuf_index += skipblockbytes * start_block;
            int step = 0 != (dir & 1) ? 1 : -1;

            for (int i = start_block; i < block_limit; i++)
            {
                int w = width - i*TVP_TLG6_W_BLOCK_SIZE;
                if (w > TVP_TLG6_W_BLOCK_SIZE) w = TVP_TLG6_W_BLOCK_SIZE;
                int ww = w;
                if (step == -1) inbuf_index += ww-1;
                if (0 != (i & 1)) inbuf_index += oddskip * ww;

                tvp_decoder decoder;
                switch (filtertypes[filtertypes_index+i])
                {
                case 0:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, v);
                    break;
                case 1:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, v);
                    break;
                case 2:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+((v>>8)&0xff))<<16)) + (((v>>8)&0xff)<<8) + (0xff & ((v&0xff)+((v>>8)&0xff))) + ((v&0xff000000))));
                    break;
                case 3:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+((v>>8)&0xff))<<16)) + (((v>>8)&0xff)<<8) + (0xff & ((v&0xff)+((v>>8)&0xff))) + ((v&0xff000000))));
                    break;
                case 4:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff)+((v>>8)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 5:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff)+((v>>8)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 6:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>16)&0xff)+((v>>8)&0xff))) + ((v&0xff000000))));
                    break;
                case 7:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>16)&0xff)+((v>>8)&0xff))) + ((v&0xff000000))));
                    break;
                case 8:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff)+((v>>16)&0xff)+((v>>8)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff)+((v>>16)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>16)&0xff))) + ((v&0xff000000))));
                    break;
                case 9:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff)+((v>>16)&0xff)+((v>>8)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff)+((v>>16)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>16)&0xff))) + ((v&0xff000000))));
                    break;
                case 10:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff)+((v>>16)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>16)&0xff))) + ((v&0xff000000))));
                    break;
                case 11:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff)+((v>>16)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>16)&0xff))) + ((v&0xff000000))));
                    break;
                case 12:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>8)&0xff))) + ((v&0xff000000))));
                    break;
                case 13:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>8)&0xff))) + ((v&0xff000000))));
                    break;
                case 14:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 15:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 16:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+((v>>8)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 17:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+((v>>8)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 18:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff)+((v>>8)&0xff)+((v>>16)&0xff)+(v&0xff))) + ((v&0xff000000))));
                    break;
                case 19:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff)+((v>>8)&0xff)+((v>>16)&0xff)+(v&0xff))) + ((v&0xff000000))));
                    break;
                case 20:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>16)&0xff))) + ((v&0xff000000))));
                    break;
                case 21:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>16)&0xff))) + ((v&0xff000000))));
                    break;
                case 22:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 23:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 24:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 25:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 26:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff)+((v>>8)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff)+(v&0xff)+((v>>8)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>8)&0xff))) + ((v&0xff000000))));
                    break;
                case 27:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff)+((v>>8)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff)+(v&0xff)+((v>>8)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>8)&0xff))) + ((v&0xff000000))));
                    break;
                case 28:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff)+((v>>8)&0xff)+((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>8)&0xff)+((v>>16)&0xff))) + ((v&0xff000000))));
                    break;
                case 29:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff)+((v>>8)&0xff)+((v>>16)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v>>16)&0xff))<<8)) + (0xff & ((v&0xff)+((v>>8)&0xff)+((v>>16)&0xff))) + ((v&0xff000000))));
                    break;
                case 30:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+((v&0xff)<<1))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v&0xff)<<1))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 31:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+((v&0xff)<<1))<<16)) + (0xff00 & ((((v>>8)&0xff)+((v&0xff)<<1))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                default: return;
                }
                do {
                    uint u = prevline[prevline_index];
                    p = decoder (p, u, up, inbuf[inbuf_index]);
                    up = u;
                    curline[curline_index] = p;
                    curline_index++;
                    prevline_index++;
                    inbuf_index += step;
                } while (0 != --w);
                if (step == 1)
                    inbuf_index += skipblockbytes - ww;
                else
                    inbuf_index += skipblockbytes + 1;
                if (0 != (i&1)) inbuf_index -= oddskip * ww;
            }
        }

        static class TVP_Tables
        {
            public static byte[] TVPTLG6LeadingZeroTable = new byte[TVP_TLG6_LeadingZeroTable_SIZE];
            public static sbyte[,] TVPTLG6GolombBitLengthTable = new sbyte
                [TVP_TLG6_GOLOMB_N_COUNT*2*128, TVP_TLG6_GOLOMB_N_COUNT];
            static short[,] TVPTLG6GolombCompressed = new short[TVP_TLG6_GOLOMB_N_COUNT,9] {
                    {3,7,15,27,63,108,223,448,130,},
                    {3,5,13,24,51,95,192,384,257,},
                    {2,5,12,21,39,86,155,320,384,},
                    {2,3,9,18,33,61,129,258,511,},
                /* Tuned by W.Dee, 2004/03/25 */
            };

            static TVP_Tables ()
            {
                TVPTLG6InitLeadingZeroTable();
                TVPTLG6InitGolombTable();
            }

            static void TVPTLG6InitLeadingZeroTable ()
            {
                /* table which indicates first set bit position + 1. */
                /* this may be replaced by BSF (IA32 instrcution). */

                for (int i = 0; i < TVP_TLG6_LeadingZeroTable_SIZE; i++)
                {
                    int cnt = 0;
                    int j;
                    for(j = 1; j != TVP_TLG6_LeadingZeroTable_SIZE && 0 == (i & j);
                        j <<= 1, cnt++);
                    cnt++;
                    if (j == TVP_TLG6_LeadingZeroTable_SIZE) cnt = 0;
                    TVPTLG6LeadingZeroTable[i] = (byte)cnt;
                }
            }

            static void TVPTLG6InitGolombTable()
            {
                for (int n = 0; n < TVP_TLG6_GOLOMB_N_COUNT; n++)
                {
                    int a = 0;
                    for (int i = 0; i < 9; i++)
                    {
                        for (int j = 0; j < TVPTLG6GolombCompressed[n,i]; j++)
                            TVPTLG6GolombBitLengthTable[a++,n] = (sbyte)i;
                    }
                    if(a != TVP_TLG6_GOLOMB_N_COUNT*2*128)
                        throw new Exception ("Invalid data initialization");   /* THIS MUST NOT BE EXECUETED! */
                            /* (this is for compressed table data check) */
                }
            }
        }

        void TVPTLG6DecodeGolombValuesForFirst (uint[] pixelbuf, int pixel_count, byte[] bit_pool)
        {
            /*
                decode values packed in "bit_pool".
                values are coded using golomb code.

                "ForFirst" function do dword access to pixelbuf,
                clearing with zero except for blue (least siginificant byte).
            */
            int bit_pool_index = 0;

            int n = TVP_TLG6_GOLOMB_N_COUNT - 1; /* output counter */
            int a = 0; /* summary of absolute values of errors */

            int bit_pos = 1;
            bool zero = 0 == (bit_pool[bit_pool_index] & 1);

            for (int pixel = 0; pixel < pixel_count; )
            {
                /* get running count */
                int count;

                {
                    uint t = LittleEndian.ToUInt32 (bit_pool, bit_pool_index) >> bit_pos;
                    int b = TVP_Tables.TVPTLG6LeadingZeroTable[t & (TVP_TLG6_LeadingZeroTable_SIZE-1)];
                    int bit_count = b;
                    while (0 == b)
                    {
                        bit_count += TVP_TLG6_LeadingZeroTable_BITS;
                        bit_pos += TVP_TLG6_LeadingZeroTable_BITS;
                        bit_pool_index += bit_pos >> 3;
                        bit_pos &= 7;
                        t = LittleEndian.ToUInt32 (bit_pool, bit_pool_index) >> bit_pos;
                        b = TVP_Tables.TVPTLG6LeadingZeroTable[t&(TVP_TLG6_LeadingZeroTable_SIZE-1)];
                        bit_count += b;
                    }
                    bit_pos += b;
                    bit_pool_index += bit_pos >> 3;
                    bit_pos &= 7;

                    bit_count --;
                    count = 1 << bit_count;
                    count += ((LittleEndian.ToInt32 (bit_pool, bit_pool_index) >> (bit_pos)) & (count-1));

                    bit_pos += bit_count;
                    bit_pool_index += bit_pos >> 3;
                    bit_pos &= 7;
                }
                if (zero)
                {
                    /* zero values */

                    /* fill distination with zero */
                    do { pixelbuf[pixel++] = 0; } while (0 != --count);

                    zero = !zero;
                }
                else
                {
                    /* non-zero values */

                    /* fill distination with glomb code */

                    do
                    {
                        int k = TVP_Tables.TVPTLG6GolombBitLengthTable[a,n];
                        int v, sign;

                        uint t = LittleEndian.ToUInt32 (bit_pool, bit_pool_index) >> bit_pos;
                        int bit_count;
                        int b;
                        if (0 != t)
                        {
                            b = TVP_Tables.TVPTLG6LeadingZeroTable[t&(TVP_TLG6_LeadingZeroTable_SIZE-1)];
                            bit_count = b;
                            while (0 == b)
                            {
                                bit_count += TVP_TLG6_LeadingZeroTable_BITS;
                                bit_pos += TVP_TLG6_LeadingZeroTable_BITS;
                                bit_pool_index += bit_pos >> 3;
                                bit_pos &= 7;
                                t = LittleEndian.ToUInt32 (bit_pool, bit_pool_index) >> bit_pos;
                                b = TVP_Tables.TVPTLG6LeadingZeroTable[t&(TVP_TLG6_LeadingZeroTable_SIZE-1)];
                                bit_count += b;
                            }
                            bit_count --;
                        }
                        else
                        {
                            bit_pool_index += 5;
                            bit_count = bit_pool[bit_pool_index-1];
                            bit_pos = 0;
                            t = LittleEndian.ToUInt32 (bit_pool, bit_pool_index);
                            b = 0;
                        }

                        v = (int)((bit_count << k) + ((t >> b) & ((1<<k)-1)));
                        sign = (v & 1) - 1;
                        v >>= 1;
                        a += v;
                        pixelbuf[pixel++] = (byte)((v ^ sign) + sign + 1);

                        bit_pos += b;
                        bit_pos += k;
                        bit_pool_index += bit_pos >> 3;
                        bit_pos &= 7;

                        if (--n < 0)
                        {
                            a >>= 1;
                            n = TVP_TLG6_GOLOMB_N_COUNT - 1;
                        }
                    } while (0 != --count);
                    zero = !zero;
                }
            }
        }

        void TVPTLG6DecodeGolombValues (uint[] pixelbuf, int offset, int pixel_count, byte[] bit_pool)
        {
            /*
                decode values packed in "bit_pool".
                values are coded using golomb code.
            */
            uint mask = (uint)~(0xff << offset);
            int bit_pool_index = 0;

            int n = TVP_TLG6_GOLOMB_N_COUNT - 1; /* output counter */
            int a = 0; /* summary of absolute values of errors */

            int bit_pos = 1;
            bool zero = 0 == (bit_pool[bit_pool_index] & 1);

            for (int pixel = 0; pixel < pixel_count; )
            {
                /* get running count */
                int count;

                {
                    uint t = LittleEndian.ToUInt32 (bit_pool, bit_pool_index) >> bit_pos;
                    int b = TVP_Tables.TVPTLG6LeadingZeroTable[t&(TVP_TLG6_LeadingZeroTable_SIZE-1)];
                    int bit_count = b;
                    while (0 == b)
                    {
                        bit_count += TVP_TLG6_LeadingZeroTable_BITS;
                        bit_pos += TVP_TLG6_LeadingZeroTable_BITS;
                        bit_pool_index += bit_pos >> 3;
                        bit_pos &= 7;
                        t = LittleEndian.ToUInt32 (bit_pool, bit_pool_index) >> bit_pos;
                        b = TVP_Tables.TVPTLG6LeadingZeroTable[t&(TVP_TLG6_LeadingZeroTable_SIZE-1)];
                        bit_count += b;
                    }
                    bit_pos += b;
                    bit_pool_index += bit_pos >> 3;
                    bit_pos &= 7;

                    bit_count --;
                    count = 1 << bit_count;
                    count += (int)((LittleEndian.ToUInt32 (bit_pool, bit_pool_index) >> (bit_pos)) & (count-1));

                    bit_pos += bit_count;
                    bit_pool_index += bit_pos >> 3;
                    bit_pos &= 7;
                }
                if (zero)
                {
                    /* zero values */

                    /* fill distination with zero */
                    do { pixelbuf[pixel++] &= mask; } while (0 != --count);

                    zero = !zero;
                }
                else
                {
                    /* non-zero values */

                    /* fill distination with glomb code */

                    do
                    {
                        int k = TVP_Tables.TVPTLG6GolombBitLengthTable[a,n];
                        int v, sign;

                        uint t = LittleEndian.ToUInt32 (bit_pool, bit_pool_index) >> bit_pos;
                        int bit_count;
                        int b;
                        if (0 != t)
                        {
                            b = TVP_Tables.TVPTLG6LeadingZeroTable[t&(TVP_TLG6_LeadingZeroTable_SIZE-1)];
                            bit_count = b;
                            while (0 == b)
                            {
                                bit_count += TVP_TLG6_LeadingZeroTable_BITS;
                                bit_pos += TVP_TLG6_LeadingZeroTable_BITS;
                                bit_pool_index += bit_pos >> 3;
                                bit_pos &= 7;
                                t = LittleEndian.ToUInt32 (bit_pool, bit_pool_index) >> bit_pos;
                                b = TVP_Tables.TVPTLG6LeadingZeroTable[t&(TVP_TLG6_LeadingZeroTable_SIZE-1)];
                                bit_count += b;
                            }
                            bit_count --;
                        }
                        else
                        {
                            bit_pool_index += 5;
                            bit_count = bit_pool[bit_pool_index-1];
                            bit_pos = 0;
                            t = LittleEndian.ToUInt32 (bit_pool, bit_pool_index);
                            b = 0;
                        }

                        v = (int)((bit_count << k) + ((t >> b) & ((1<<k)-1)));
                        sign = (v & 1) - 1;
                        v >>= 1;
                        a += v;
                        uint c = (uint)((pixelbuf[pixel] & mask) | (uint)((byte)((v ^ sign) + sign + 1) << offset));
                        pixelbuf[pixel++] = c;

                        bit_pos += b;
                        bit_pos += k;
                        bit_pool_index += bit_pos >> 3;
                        bit_pos &= 7;

                        if (--n < 0)
                        {
                            a >>= 1;
                            n = TVP_TLG6_GOLOMB_N_COUNT - 1;
                        }
                    } while (0 != --count);
                    zero = !zero;
                }
            }
        }
    }

    internal class TagsParser
    {
        byte[]                              m_tags;
        Dictionary<int, Tuple<int, int>>    m_map = new Dictionary<int, Tuple<int, int>>();
        int                                 m_offset;

        public TagsParser (byte[] tags, int offset)
        {
            m_tags = tags;
            m_offset = offset;
        }

        public bool Parse ()
        {
            int length = LittleEndian.ToInt32 (m_tags, m_offset);
            m_offset += 4;
            if (length <= 0 || length > m_tags.Length - m_offset)
                return false;
            while (m_offset < m_tags.Length)
            {
                int key_len = ParseInt();
                if (key_len < 0)
                    return false;
                int key;
                switch (key_len)
                {
                case 1:
                    key = m_tags[m_offset];
                    break;
                case 2:
                    key = LittleEndian.ToUInt16 (m_tags, m_offset);
                    break;
                case 4:
                    key = LittleEndian.ToInt32 (m_tags, m_offset);
                    break;
                default:
                    return false;
                }
                m_offset += key_len + 1;
                int value_len = ParseInt();
                if (value_len < 0)
                    return false;
                m_map[key] = Tuple.Create (m_offset, value_len);
                m_offset += value_len + 1;
            }
            return m_map.Count > 0;
        }

        int ParseInt ()
        {
            int colon = Array.IndexOf (m_tags, (byte)':', m_offset);
            if (-1 == colon)
                return -1;
            var len_str = Encoding.ASCII.GetString (m_tags, m_offset, colon-m_offset);
            m_offset = colon + 1;
            return Int32.Parse (len_str);
        }

        public bool HasKey (int key)
        {
            return m_map.ContainsKey (key);
        }

        public int GetInt (int key)
        {
            var val = m_map[key];
            switch (val.Item2)
            {
            case 0: return 0;
            case 1: return m_tags[val.Item1];
            case 2: return LittleEndian.ToUInt16 (m_tags, val.Item1);
            case 4: return LittleEndian.ToInt32 (m_tags, val.Item1);
            default: throw new InvalidFormatException();
            }
        }

        public string GetString (int key)
        {
            var val = m_map[key];
            return Encodings.cp932.GetString (m_tags, val.Item1, val.Item2);
        }
    }
}
