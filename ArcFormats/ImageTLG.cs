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
using System.Windows.Media.Imaging;
using GameRes.Utility;

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
            Signatures = new uint[] { 0x30474c54, 0x35474c54, 0x36474c54 };
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x26];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            int offset = 0xf;
            if (!Binary.AsciiEqual (header, "TLG0.0\x00sds\x1a"))
                offset = 0;
            bool version6 = Binary.AsciiEqual (header, offset, "TLG6.0\x00raw\x1a");
            if (!version6 && !Binary.AsciiEqual (header, offset, "TLG5.0\x00raw\x1a"))
                return null;
            int colors = header[offset+11];
            if (version6)
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
            uint width  = LittleEndian.ToUInt32 (header, offset);
            uint height = LittleEndian.ToUInt32 (header, offset+4);
            return new TlgMetaData {
                Width   = width,
                Height  = height,
                BPP     = colors*8,
                Version     = version6 ? 6 : 5,
                DataOffset  = offset+8,
            };
        }

        public override ImageData Read (Stream file, ImageMetaData info)
        {
            var meta = info as TlgMetaData;
            if (null == meta)
                throw new System.ArgumentException ("TlgFormat.Read should be supplied with TlgMetaData", "info");
            file.Seek (meta.DataOffset, SeekOrigin.Begin);
            if (6 == meta.Version)
                return ReadV6 (file, meta);
            else
                return ReadV5 (file, meta);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("TlgFormat.Write not implemented");
        }
        
        const int TVP_TLG6_H_BLOCK_SIZE = 8;
        const int TVP_TLG6_W_BLOCK_SIZE = 8;

        const int TVP_TLG6_GOLOMB_N_COUNT = 4;
        const int TVP_TLG6_LeadingZeroTable_BITS = 12;
        const int TVP_TLG6_LeadingZeroTable_SIZE = (1<<TVP_TLG6_LeadingZeroTable_BITS);

        ImageData ReadV6 (Stream stream, TlgMetaData info)
        {
            using (var src = new ArcView.Reader (stream))
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
//                        Array.Copy (image_bits, curline, prevline, 0, width);
                    }
                }
                unsafe
                {
                    fixed (void* data = image_bits)
                    {
                        int stride = width * 4;
                        PixelFormat format = 32 == info.BPP ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
                        var bitmap = BitmapSource.Create(width, height, 96, 96,
                            format, null, (IntPtr) data, height * stride, stride);
                        bitmap.Freeze();
                        return new ImageData(bitmap, info);
                    }
                }
            }
        }

        ImageData ReadV5 (Stream stream, TlgMetaData info)
        {
            using (var src = new ArcView.Reader (stream))
            {
                int width = (int)info.Width;
                int height = (int)info.Height;
                int colors = info.BPP / 8;
                int blockheight = src.ReadInt32();
                int blockcount = (height - 1) / blockheight + 1;

                // skip block size section
                src.BaseStream.Seek (blockcount * 4, SeekOrigin.Current);

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
                        byte mark = src.ReadByte();
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
                PixelFormat format = 4 == colors ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
                var bitmap = BitmapSource.Create (width, height, 96, 96,
                    format, null, image_bits, stride);
                bitmap.Freeze();
                return new ImageData (bitmap, info);
            }
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

        /*
#define TVP_TLG6_DO_CHROMA_DECODE(N, R, G, B) case (N<<1): \
        TVP_TLG6_DO_CHROMA_DECODE_PROTO(R, G, B, IA, {inbuf_index+=step;}) break; \
        case (N<<1)+1: \
        TVP_TLG6_DO_CHROMA_DECODE_PROTO2(R, G, B, IA, {inbuf_index+=step;}) break;

#define TVP_TLG6_DO_CHROMA_DECODE_PROTO(B, G, R, A, POST_INCREMENT) do \
                { \
                    uint u = prevline[prevline_index]; \
                    p = tvp_med(p, u, up, \
                        (0xff0000 & ((R)<<16)) + (0xff00 & ((G)<<8)) + (0xff & (B)) + ((A) << 24) ); \
                    up = u; \
                    curline[curline_index] = p; \
                    curline_index++; \
                    prevline_index++; \
                    POST_INCREMENT \
                } while(--w);
#define TVP_TLG6_DO_CHROMA_DECODE_PROTO2(B, G, R, A, POST_INCREMENT) do \
                { \
                    uint u = *prevline; \
                    p = avg(p, u, up, \
                        (0xff0000 & ((R)<<16)) + (0xff00 & ((G)<<8)) + (0xff & (B)) + ((A) << 24) ); \
                    up = u; \
                    *curline = p; \
                    curline ++; \
                    prevline ++; \
                    POST_INCREMENT \
                } while(--w);
*/
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

//                byte IA = (byte)(inbuf[inbuf_index]>>24);
//                byte IR = (byte)(inbuf[inbuf_index]>>16);
//                byte IG = (byte)(inbuf[inbuf_index]>>8 );
//                byte IB = (byte)(inbuf[inbuf_index]    );
                tvp_decoder decoder;
                switch (filtertypes[filtertypes_index+i])
                {
//		TVP_TLG6_DO_CHROMA_DECODE( 0, IB, IG, IR); 
                case 0:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, v);
                    break;
                case 1:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, v);
                    break;
//		TVP_TLG6_DO_CHROMA_DECODE( 1, IB+IG, IG, IR+IG); 
                case 2:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+((v>>8)&0xff))<<16)) + (((v>>8)&0xff)<<8) + (0xff & ((v&0xff)+((v>>8)&0xff))) + ((v&0xff000000))));
                    break;
                case 3:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+((v>>8)&0xff))<<16)) + (((v>>8)&0xff)<<8) + (0xff & ((v&0xff)+((v>>8)&0xff))) + ((v&0xff000000))));
                    break;
//		TVP_TLG6_DO_CHROMA_DECODE( 2, IB, IG+IB, IR+IB+IG); 
                case 4:
                    decoder = (a, b, c, v) => tvp_med (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff)+((v>>8)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
                case 5:
                    decoder = (a, b, c, v) => tvp_avg (a, b, c, (uint)
                        ((0xff0000 & ((((v>>16)&0xff)+(v&0xff)+((v>>8)&0xff))<<16)) + (0xff00 & ((((v>>8)&0xff)+(v&0xff))<<8)) + (0xff & ((v&0xff))) + ((v&0xff000000))));
                    break;
//		TVP_TLG6_DO_CHROMA_DECODE( 3, IB+IR+IG, IG+IR, IR); 
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
//		TVP_TLG6_DO_CHROMA_DECODE(15, IB, IG+(IB<<1), IR+(IB<<1));
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
//                TVPInitDitherTable();
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
}
