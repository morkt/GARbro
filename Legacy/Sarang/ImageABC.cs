//! \file       ImageABC.cs
//! \date       2018 Sep 01
//! \brief      Sarang compressed bitmap.
//
// Copyright (C) 2018 by morkt
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

// [010928][Sarang] Rege ~Saisei no Ubugoe~

namespace GameRes.Formats.Sarang
{
    internal class AbcMetaData : ImageMetaData
    {
        public int              UnpackedSize;
        public ImageFormat      SourceFormat;
        public ImageMetaData    SourceInfo;
    }

    [Export(typeof(ImageFormat))]
    public class AbcFormat : ImageFormat
    {
        public override string         Tag { get { return "ABC"; } }
        public override string Description { get { return "Sarang compressed bitmap"; } }
        public override uint     Signature { get { return 0; } }

        const int MinUnpackedSize = 0x38;
        const int MaxUnpackedSize = 4096 * 4096 * 4;

        static readonly ResourceInstance<ImageFormat> s_DdsFormat = new ResourceInstance<ImageFormat> ("DDS");

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            int unpacked_size = file.ReadInt32();
            if (unpacked_size < MinUnpackedSize || unpacked_size > MaxUnpackedSize)
                return null;
            uint signature = file.ReadUInt32();
            if ((signature & 0xFFFF) != 0x1321 && signature != 0x620A1122)
                return null;
            using (var reader = new AbcDecoder (file.AsStream, Math.Min (0x6C, unpacked_size)))
            {
                var data = reader.Unpack();
                ImageFormat format;
                if (data.AsciiEqual (0, "BM"))
                    format = Bmp;
                else if (data.AsciiEqual (0, "DDS "))
                    format = s_DdsFormat.Value;
                else
                    return null;
                using (var input = new BinMemoryStream (data))
                {
                    var info = format.ReadMetaData (input);
                    if (null == info)
                        return null;
                    return new AbcMetaData {
                        Width = info.Width,
                        Height = info.Height,
                        BPP = info.BPP,
                        UnpackedSize = unpacked_size,
                        SourceFormat = format,
                        SourceInfo = info,
                    };
                }
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (AbcMetaData)info;
            using (var reader = new AbcDecoder (file.AsStream, meta.UnpackedSize))
            {
                var data = reader.Unpack();
                using (var input = new BinMemoryStream (data))
                    return meta.SourceFormat.Read (input, meta.SourceInfo);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AbcFormat.Write not implemented");
        }
    }

    internal sealed class AbcDecoder : IDisposable
    {
        MsbBitStream    m_input;
        byte[]          m_output;

        byte[]  m_root = new byte[0x1000];

        int[]   m_dict1 = new int[0x1000];
        int[]   m_dict2 = new int[0x1000];
        int[]   m_dict3 = new int[0x1000];
        int[]   m_dict4 = new int[0x1000];

        int     m_last_token;
        int[]   m_table1 = new int[0x1000];
        int[]   m_table2 = new int[0x1000];

        int     m_field_4;
        int     m_field_8;
        int[]   m_buffer = new int[MaxStringLength];
        int     m_token_bits;
        int     m_token_limit;

        const int MaxStringLength = 100;

        public AbcDecoder (Stream input, int unpacked_size)
        {
            m_input = new MsbBitStream (input, true);
            m_output = new byte[unpacked_size];
        }

        void InitDecoder ()
        {
            m_input.Input.Position = 4;
            m_last_token = 256;
            m_field_4 = 0x1000;
            m_field_8 = 0x1000;
            m_token_bits = 1;
            m_token_limit = 2;
            for (int i = 0; i < 0x100; ++i)
            {
                m_root[i] = (byte)i;
                m_dict1[i] = 0x1000;
                m_dict2[i] = 0x1000;
                m_dict3[i] = 0x1000;
                m_dict4[i] = 0x1000;
            }
        }

        public byte[] Unpack ()
        {
            InitDecoder();
            int prev_count = 0;
            int prev_token = 0x1000;
            int dst = 0;
            while (dst < m_output.Length)
            {
                int current_token = ReadToken();
                int count = 0;
                int pos = MaxStringLength;
                for (int token = current_token; token != 0x1000; token = m_dict1[token])
                {
                    if (token >= 256 && token != m_field_4)
                    {
                        sub_411760 (token);
                        sub_4117B0 (token, m_field_4);
                    }
                    m_buffer[--pos] = m_root[token];
                    ++count;
                }
                int src = MaxStringLength - count;
                int copy_count = Math.Min (count, m_output.Length - dst);
                for (int i = 0; i < copy_count; ++i)
                {
                    m_output[dst++] = (byte)m_buffer[src++];
                }
                if (dst >= m_output.Length)
                    break;
                sub_4118F0 (MaxStringLength - count, count, prev_token, prev_count);
                prev_token = current_token;
                prev_count = count;
            }
            return m_output;
        }

        void sub_411760 (int token)
        {
            if (token == m_field_8)
            {
                m_field_8 = m_table1[token];
                m_table2[m_field_8] = 0x1000;
            }
            else
            {
                int t1 = m_table1[token];
                int t2 = m_table2[token];
                m_table1[t2] = t1;
                m_table2[t1] = t2;
            }
        }

        void sub_4117B0 (int a2, int a3)
        {
            int v3 = m_field_4;
            if (v3 == 0x1000)
            {
                m_table1[a2] = 0x1000;
                m_table2[a2] = 0x1000;
                m_field_8 = a2;
                m_field_4 = a2;
            }
            else if (a3 == 0x1000)
            {
                m_table2[a2] = 0x1000;
                m_table1[a2] = m_field_8;
                m_table2[m_field_8] = a2;
                m_field_8 = a2;
            }
            else if (a3 == v3)
            {
                m_table2[a2] = v3;
                m_table1[a2] = 0x1000;
                m_table1[m_field_4] = a2;
                m_field_4 = a2;
            }
            else
            {
                m_table2[a2] = a3;
                int t1 = m_table1[a3];
                m_table1[a2] = t1;
                m_table2[t1] = a2;
                m_table1[a3] = a2;
            }
        }

        int sub_411870 (int prev_token, int symbol)
        {
            int token = m_dict2[prev_token];
            while (token != 0x1000 && symbol != m_root[token])
            {
                token = m_dict3[token];
            }
            return token;
        }

        void sub_4118F0 (int src, int count, int prev_token, int prev_count)
        {
            if (prev_token == 0x1000)
                return;
            for (int i = 0; i < count; ++i)
            {
                if (++prev_count > MaxStringLength)
                    break;
                int symbol = m_buffer[src];
                int token = sub_411870 (prev_token, symbol);
                if (token == 0x1000)
                {
                    token = m_last_token;
                    if (token >= 0x1000)
                    {
                        token = m_field_8;
                        if (prev_token == token)
                            return;
                        sub_411760 (m_field_8);
                        sub_411A40 (token);
                    }
                    else
                    {
                        m_last_token = token + 1;
                    }
                    sub_4119E0 (prev_token, token, (byte)symbol);
                    if (prev_token >= 256)
                        sub_4117B0 (token, m_table2[prev_token]);
                    else
                        sub_4117B0 (token, m_field_4);
                }
                prev_token = token;
                ++src;
            }
        }

        void sub_4119E0 (int prev_token, int token, byte symbol)
        {
            m_root[token] = symbol;
            m_dict1[token] = prev_token;
            m_dict2[token] = 0x1000;
            m_dict4[token] = 0x1000;
            int d2 = m_dict2[prev_token];
            m_dict3[token] = d2;
            if (d2 != 0x1000)
                m_dict4[d2] = token;
            m_dict2[prev_token] = token;
        }

        void sub_411A40 (int a2)
        {
            int d4 = m_dict4[a2];
            int d3 = m_dict3[a2];
            if (d4 == 0x1000)
                m_dict2[m_dict1[a2]] = d3;
            else
                m_dict3[d4] = d3;
            if (d3 != 0x1000)
                m_dict4[d3] = d4;
        }

        int ReadToken ()
        {
            if (m_last_token - 256 >= m_token_limit)
            {
                m_token_limit <<= 1;
                m_token_bits  += 1;
            }
            int token = m_input.GetNextBit();
            if (token > 0)
            {
                token = m_input.GetBits (m_token_bits);
                if (token != -1)
                    token += 256;
            }
            else if (0 == token)
            {
                token = m_input.GetBits (8);
            }
            return token;
        }

        #region IDisposable Members
        bool m_disposed = false;

        public void Dispose ()
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
