//! \file       ImageCGF.cs
//! \date       Thu Feb 23 15:12:04 2017
//! \brief      Cadath image format.
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Cadath
{
    internal class CgfMetaData : ImageMetaData
    {
        public int  Method;
    }

    [Export(typeof(ImageFormat))]
    public class CgfFormat : ImageFormat
    {
        public override string         Tag { get { return "CGF/CADATH"; } }
        public override string Description { get { return "Cadath image format"; } }
        public override uint     Signature { get { return 0x1A464743; } } // 'CGF'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (10);
            int method = header[4];
            if (method < 1 || method > 3)
                return null;
            return new CgfMetaData
            {
                Width   = header.ToUInt16 (6),
                Height  = header.ToUInt16 (8),
                BPP     = header[5],
                Method  = method,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new CgfDecoder (file, (CgfMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CgfFormat.Write not implemented");
        }
    }

    internal sealed class CgfDecoder
    {
        IBinaryStream   m_input;
        CgfMetaData     m_info;
        byte[]          m_output;

        public PixelFormat Format { get; private set; }

        public CgfDecoder (IBinaryStream input, CgfMetaData info)
        {
            m_input = input;
            m_info = info;
            m_output = new byte[(int)m_info.Width * (int)m_info.Height * m_info.BPP / 8];
            if (32 == m_info.BPP)
                Format = PixelFormats.Bgra32;
            else
                Format = PixelFormats.Bgr24;
        }

        public byte[] Unpack ()
        {
            m_input.Position = 10;
            Action<byte[], int, byte[]> unpack_channel;
            if (1 == m_info.Method)
                unpack_channel = UnpackZLibV1;
            else if (2 == m_info.Method)
                unpack_channel = UnpackRle;
            else if (3 == m_info.Method)
                unpack_channel = UnpackZLibV3;
            else
                throw new NotSupportedException();
            int pixel_size = m_info.BPP / 8;
            var channel = new byte[m_info.Width * m_info.Height];
            byte[] buffer = null;
            for (int i = 0; i < pixel_size; ++i)
            {
                int channel_length = m_input.ReadInt32();
                if (channel_length < 0)
                    throw new InvalidFormatException();
                if (null == buffer || channel_length > buffer.Length)
                    buffer = new byte[channel_length];
                if (channel_length != m_input.Read (buffer, 0, channel_length))
                    throw new EndOfStreamException();
                unpack_channel (buffer, channel_length, channel);
                int dst = i;
                for (int j = 0; j < channel.Length; ++j)
                {
                    m_output[dst] = channel[j];
                    dst += pixel_size;
                }
            }
            return m_output;
        }

        void UnpackZLibV1 (byte[] input, int length, byte[] output)
        {
            Decrypt (input, length);
            UnpackZLib (input, length, output);
        }

        void UnpackZLibV3 (byte[] input, int length, byte[] output)
        {
            UnpackZLib (input, length, output);
            byte px = 0;
            for (int i = 0; i < output.Length; ++i)
            {
                px ^= output[i];
                output[i] = px;
            }
        }

        void UnpackZLib (byte[] input, int length, byte[] output)
        {
            using (var zinput = new MemoryStream (input, 4, length - 4))
            using (var z = new ZLibStream (zinput, CompressionMode.Decompress))
                z.Read (output, 0, output.Length);
        }

        void UnpackRle (byte[] input, int length, byte[] output)
        {
            int unpacked_length = LittleEndian.ToInt32 (input, 0);
            if (unpacked_length < 0 || unpacked_length > output.Length)
                throw new InvalidFormatException();
            int src = 4;
            int dst = 0;
            byte last_byte = 0;
            while (dst < unpacked_length)
            {
                byte b = input[src++];
                output[dst++] = b;
                if (b == last_byte)
                {
                    int count = input[src++];
                    for (int i = 0; i < count; ++i)
                    {
                        output[dst++] = b;
                    }
                }
                last_byte = b;
            }
        }

        unsafe internal static void Decrypt (byte[] data, int length)
        {
            if (length < 4)
                return;
            if (length > data.Length)
                throw new ArgumentOutOfRangeException ("length");
            fixed (byte* data8 = data)
            {
                uint* data32 = (uint*)data8;
                const uint seed = 0x3977141B;
                uint key = seed;
                for (int i = 0; i < length; i += 4)
                {
                    key = Binary.RotL (key, 3);
                    *data32++ ^= key;
                    key += seed;
                }
            }
        }
    }
}
