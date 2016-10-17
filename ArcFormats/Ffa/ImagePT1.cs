//! \file       ImagePT1.cs
//! \date       Wed Apr 15 15:17:24 2015
//! \brief      FFA System image format implementation.
//
// Copyright (C) 2015 by morkt
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Ffa
{
    internal class Pt1MetaData : ImageMetaData
    {
        public int Type;
        public int PackedSize;
        public int UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class Pt1Format : ImageFormat
    {
        public override string         Tag { get { return "PT1"; } }
        public override string Description { get { return "FFA System RGB image format"; } }
        public override uint     Signature { get { return 2u; } }

        public Pt1Format ()
        {
            Signatures = new uint[] { 3, 2, 1, 0 };
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("Pt1Format.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            int type = file.ReadInt32();
            if (type < 0 || type > 3)
                return null;
            if (-1 != file.ReadInt32())
                return null;
            int x = file.ReadInt32();
            int y = file.ReadInt32();
            uint width = file.ReadUInt32();
            uint height = file.ReadUInt32();
            int comp_size = file.ReadInt32();
            int uncomp_size = file.ReadInt32();
            if (uncomp_size != width*height*3u)
                return null;
            return new Pt1MetaData {
                Width = width,
                Height = height,
                OffsetX = x,
                OffsetY = y,
                BPP = 3 == type ? 32 : 24,
                Type = type,
                PackedSize = comp_size,
                UnpackedSize = uncomp_size
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var reader = new Reader (stream, (Pt1MetaData)info);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, null, reader.Data);
        }

        internal class Reader
        {
            byte[]  m_input;
            byte[]  m_output;
            byte[]  m_alpha_packed;
            int     m_type;
            int     m_width;
            int     m_height;
            int     m_stride;

            public PixelFormat Format { get; private set; }
            public byte[]        Data { get { return m_output; } }

            public Reader (IBinaryStream input, Pt1MetaData info)
            {
                m_type = info.Type;
                m_input = new byte[info.PackedSize+8];
                input.Position = 0x20;
                if (info.PackedSize != input.Read (m_input, 0, info.PackedSize))
                    throw new InvalidFormatException ("Unexpected end of file");
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_output = new byte[info.UnpackedSize];
                m_stride = m_width*3;
                if (3 == m_type)
                {
                    Format = PixelFormats.Bgra32;
                    int packed_size = input.ReadInt32();
                    m_alpha_packed = input.ReadBytes (packed_size);
                    if (m_alpha_packed.Length != packed_size)
                        throw new EndOfStreamException();
                }
                else
                {
                    Format = PixelFormats.Bgr24;
                }
            }

            public byte[] Unpack ()
            {
                switch (m_type)
                {
                case 3: UnpackV3(); break;
                case 2: UnpackV2(); break;
                case 1: UnpackV1(); break;
                case 0: UnpackV0 (m_input, m_output); break;
                }
                return m_output;
            }

            void UnpackV3 ()
            {
                UnpackV2();
                int total = m_width * m_height;
                var alpha = new byte[total];
                UnpackV0 (m_alpha_packed, alpha);
                var pixels = new byte[total * 4];
                int src = 0;
                int dst = 0;
                for (int i = 0; i < total; ++i)
                {
                    pixels[dst++] = m_output[src++];
                    pixels[dst++] = m_output[src++];
                    pixels[dst++] = m_output[src++];
                    pixels[dst++] = alpha[i];
                }
                m_output = pixels;
            }

            uint edx;
            byte ch;
            int src;

            void ReadNext ()
            {
                byte cl = (byte)(32 - ch);
                edx &= 0xFFFFFFFFu >> cl;
                edx += LittleEndian.ToUInt32 (m_input, src) << ch;
                src += cl >> 3;
                ch += (byte)(cl & 0xf8);
            }

            void UnpackV2 ()
            {
                src = 0;
                int dst = 0;
                Buffer.BlockCopy (m_input, src, m_output, dst, 3);
                src += 3;
                dst += 3;
                edx = LittleEndian.ToUInt32 (m_input, src);
                src += 3;
                ch = 0x18;
                uint _CF;
                uint ebx;
                sbyte ah;
                byte al;

                // [ebp+var_8] = i
                for (int i = 1; i < m_width; ++i)
                {
                    ReadNext();

                    _CF = edx & 1;
                    edx >>= 1;

                    if (0 != _CF)
                    {
                        --ch;
                        Buffer.BlockCopy (m_output, dst-3, m_output, dst, 3);
                        dst += 3;
                    }
                    else
                    {
                        ch -= 2;
                        _CF = edx & 1;
                        edx >>= 1;
                        if (0 != _CF)
                        {
                            ah = sub_4225EA();
                            al = (byte)(ah + m_output[dst-3]);
                            m_output[dst++] = al;

                            ReadNext();
                            ah = sub_4225EA();
                            al = (byte)(ah + m_output[dst-3]);
                            m_output[dst++] = al;

                            ReadNext();
                            ah = sub_4225EA();
                            al = (byte)(ah + m_output[dst-3]);
                            m_output[dst++] = al;
                        }
                        else
                        {
                            ReadNext();
                            LittleEndian.Pack ((ushort)edx, m_output, dst);
                            edx >>= 16;
                            m_output[dst+2] = (byte)edx;
                            dst += 3;
                            edx >>= 8;
                            ch -= 24;
                        }
                    }
                }
                for (int i = 1; i < m_height; ++i)
                {
                    ReadNext();
                    _CF = edx & 1;
                    edx >>= 1;
                    if (0 != _CF)
                    {
                        --ch;
                        Buffer.BlockCopy (m_output, dst-m_stride, m_output, dst, 3);
                        dst += 3;
                    }
                    else // loc_42207F
                    {
                        ch -= 2;
                        _CF = edx & 1;
                        edx >>= 1;
                        if (0 != _CF)
                        {
                            ah = sub_4225EA();
                            al = (byte)(ah + m_output[dst-m_stride]);
                            m_output[dst++] = al;

                            ReadNext();
                            ah = sub_4225EA();
                            al = (byte)(ah + m_output[dst-m_stride]);
                            m_output[dst++] = al;

                            ReadNext();
                            ah = sub_4225EA();
                            al = (byte)(ah + m_output[dst-m_stride]);
                            m_output[dst++] = al;
                        }
                        else // loc_4220FC
                        {
                            ReadNext();
                            LittleEndian.Pack ((ushort)edx, m_output, dst);
                            edx >>= 16;
                            m_output[dst+2] = (byte)edx;
                            dst += 3;
                            edx >>= 8;
                            ch -= 24;
                        }
                    }
                    for (int j = 1; j < m_width; ++j)
                    {
                        ReadNext();
                        _CF = edx & 1;
                        edx >>= 1;
                        if (0 != _CF)
                        {
                            --ch;
                            ebx = (uint)(dst - m_stride);
                            ah = sub_4225EA();
                            al = (byte)(m_output[dst-3] - m_output[ebx-3] + m_output[ebx] + ah);
                            m_output[dst++] = al;

                            ReadNext();
                            ah = sub_4225EA();
                            al = (byte)(m_output[dst-3] - m_output[ebx-2] + m_output[ebx+1] + ah);
                            m_output[dst++] = al;

                            ReadNext();
                            ah = sub_4225EA();
                            al = (byte)(m_output[dst-3] - m_output[ebx-1] + m_output[ebx+2] + ah);
                            m_output[dst++] = al;
                        }
                        else
                        {
                            _CF = edx & 1;
                            edx >>= 1;
                            if (0 != _CF)
                            {
                                ch -= 2;
                                ebx = (uint)(dst - m_stride);
                                al = (byte)(m_output[dst-3] - m_output[ebx-3] + m_output[ebx]);
                                m_output[dst++] = al;
                                al = (byte)(m_output[dst-3] - m_output[ebx-2] + m_output[ebx+1]);
                                m_output[dst++] = al;
                                al = (byte)(m_output[dst-3] - m_output[ebx-1] + m_output[ebx+2]);
                                m_output[dst++] = al;
                            }
                            else
                            {
                                ebx = edx & 3;
                                if (3 == ebx)
                                {
                                    edx >>= 2;
                                    ch -= 4;
                                    Buffer.BlockCopy (m_output, dst-3, m_output, dst, 3);
                                    dst += 3;
                                }
                                else if (2 == ebx)
                                {
                                    edx >>= 2;
                                    ch -= 4;

                                    ReadNext();
                                    LittleEndian.Pack ((ushort)edx, m_output, dst);
                                    edx >>= 16;
                                    m_output[dst+2] = (byte)edx;
                                    dst += 3;
                                    edx >>= 8;
                                    ch -= 24;
                                }
                                else if (1 == ebx)
                                {
                                    edx >>= 2;
                                    ch -= 4;
                                    ah = sub_4225EA();
                                    al = (byte)(ah + m_output[dst-3]);
                                    m_output[dst++] = al;

                                    ReadNext();
                                    ah = sub_4225EA();
                                    al = (byte)(ah + m_output[dst-3]);
                                    m_output[dst++] = al;

                                    ReadNext();
                                    ah = sub_4225EA();
                                    al = (byte)(ah + m_output[dst-3]);
                                    m_output[dst++] = al;
                                }
                                else
                                {
                                    ebx = edx & 0xf;
                                    edx >>= 4;
                                    ch -= 6;
                                    if (0 == ebx)
                                    {
                                        Buffer.BlockCopy (m_output, dst - m_stride - 3, m_output, dst, 3);
                                        dst += 3;
                                    }
                                    else if (8 == ebx)
                                    {
                                        Buffer.BlockCopy (m_output, dst - m_stride, m_output, dst, 3);
                                        dst += 3;
                                    }
                                    else
                                    {
                                        int off = dst - m_stride;
                                        if (4 == ebx) off -= 3;
                                        ah = sub_4225EA();
                                        m_output[dst++] = (byte)(ah + m_output[off++]);

                                        ReadNext();
                                        ah = sub_4225EA();
                                        m_output[dst++] = (byte)(ah + m_output[off++]);

                                        ReadNext();
                                        ah = sub_4225EA();
                                        m_output[dst++] = (byte)(ah + m_output[off++]);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            sbyte sub_4225EA ()
            {
                uint _CF = edx & 1;
                edx >>= 1;
                if (0 != _CF)
                {
                    --ch;
                    return 0;
                }
                uint bits = edx & 3;
                if (2 == bits)
                {
                    edx >>= 2;
                    ch -= 3;
                    return -1;
                }
                if (1 == bits)
                {
                    edx >>= 2;
                    ch -= 3;
                    return 1;
                }
                switch (edx & 7)
                {
                case 7:
                    edx >>= 3;
                    ch -= 4;
                    return -2;
                case 3:
                    edx >>= 3;
                    ch -= 4;
                    return 2;
                case 4:
                    edx >>= 3;
                    ch -= 4;
                    return -3;
                default:
                    switch (edx & 0x3f)
                    {
                    case 0x38:
                        edx >>= 6;
                        ch -= 7;
                        return 3;
                    case 0x18:
                        edx >>= 6;
                        ch -= 7;
                        return -4;
                    case 0x28:
                        edx >>= 6;
                        ch -= 7;
                        return 4;
                    case 0x08:
                        edx >>= 6;
                        ch -= 7;
                        return -5;
                    case 0x30:
                        edx >>= 6;
                        ch -= 7;
                        return 5;
                    case 0x10:
                        edx >>= 6;
                        ch -= 7;
                        return -6;
                    case 0x20:
                        edx >>= 6;
                        ch -= 7;
                        return 6;
                    default:
                        switch (edx & 0xff)
                        {
                        case 0xc0:
                            edx >>= 8;
                            ch -= 9;
                            return -7;
                        case 0x40:
                            edx >>= 8;
                            ch -= 9;
                            return 7;
                        case 0x80:
                            edx >>= 8;
                            ch -= 9;
                            return -8;
                        default:
                            switch (edx & 0x3ff)
                            {
                            case 0x300:
                                edx >>= 10;
                                ch -= 11;
                                return 8;
                            case 0x100:
                                edx >>= 10;
                                ch -= 11;
                                return -9;
                            case 0x200:
                                edx >>= 10;
                                ch -= 11;
                                return 9;
                            default:
                                switch (edx & 0xfff)
                                {
                                case 0xc00:
                                    edx >>= 12;
                                    ch -= 13;
                                    return -10;
                                case 0x400:
                                    edx >>= 12;
                                    ch -= 13;
                                    return 10;
                                case 0x800:
                                    edx >>= 12;
                                    ch -= 13;
                                    return -11;
                                default:
                                    switch (edx & 0x3fff)
                                    {
                                    case 0x3000:
                                        edx >>= 14;
                                        ch -= 15;
                                        return 0x0b;
                                    case 0x1000:
                                        edx >>= 14;
                                        ch -= 15;
                                        return -12;
                                    case 0x2000:
                                        edx >>= 14;
                                        ch -= 15;
                                        return 0x0c;
                                    default:
                                        edx >>= 14;
                                        ch -= 15;
                                        return -13;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            void UnpackV1 ()
            {
                int src = 0; // dword_462E74
                int dst = 0; // dword_462E78
                byte[] frame = new byte[0x1000]; // word_461A28
                PopulateLzssFrame (frame);
                int ebp = 0xfee;
                while (src < m_input.Length)
                {
                    byte ah = m_input[src++];
                    for (int mask = 1; mask != 0x100; mask <<= 1)
                    {
                        if (0 != (ah & mask))
                        {
                            byte al = m_input[src++];
                            frame[ebp++] = al;
                            ebp &= 0xfff;
                            m_output[dst++] = al;
                            m_output[dst++] = al;
                            m_output[dst++] = al;
                        }
                        else
                        {
                            int offset = m_input[src++];
                            int count  = m_input[src++];
                            offset |= (count & 0xf0) << 4;
                            count   = (count & 0x0f) + 3;
                            for (; count != 0; --count)
                            {
                                byte al = frame[offset++];
                                frame[ebp++] = al;
                                offset &= 0xfff;
                                ebp &= 0xfff;
                                m_output[dst++] = al;
                                m_output[dst++] = al;
                                m_output[dst++] = al;
                            }
                        }
                        if (dst >= m_output.Length)
                            return;
                    }
                }
            }

            void UnpackV0 (byte[] input, byte[] output)
            {
                int src = 0;
                int dst = 0;
                byte[] frame = new byte[0x1000]; // word_461A28
                PopulateLzssFrame (frame);
                int ebp = 0xfee;
                while (src < input.Length)
                {
                    byte ah = input[src++];
                    for (int mask = 1; mask != 0x100; mask <<= 1)
                    {
                        if (0 != (ah & mask))
                        {
                            byte al = input[src++];
                            frame[ebp++] = al;
                            ebp &= 0xfff;
                            output[dst++] = al;
                        }
                        else
                        {
                            int offset = input[src++];
                            int count  = input[src++];
                            offset |= (count & 0xf0) << 4;
                            count   = (count & 0x0f) + 3;
                            for (int i = 0; i < count; ++i)
                            {
                                byte al = frame[offset++];
                                frame[ebp++] = al;
                                offset &= 0xfff;
                                ebp &= 0xfff;
                                output[dst++] = al;
                            }
                        }
                        if (dst >= output.Length)
                            return;
                    }
                }
            }

            void PopulateLzssFrame (byte[] frame)
            {
                int fill = 0;
                int ecx;
                for (int al = 0; al < 0x100; ++al)
                    for (ecx = 0x0d; ecx > 0; --ecx)
                        frame[fill++] = (byte)al;
                for (int al = 0; al < 0x100; ++al)
                    frame[fill++] = (byte)al;
                for (int al = 0xff; al >= 0; --al)
                    frame[fill++] = (byte)al;
                for (ecx = 0x80; ecx > 0; --ecx)
                    frame[fill++] = 0;
                for (ecx = 0x6e; ecx > 0; --ecx)
                    frame[fill++] = 0x20;
                for (ecx = 0x12; ecx > 0; --ecx)
                    frame[fill++] = 0;
            }
        }
    }
}
