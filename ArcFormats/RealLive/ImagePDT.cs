//! \file       ImagePDT.cs
//! \date       Tue Apr 19 15:32:31 2016
//! \brief      AVG32 engine image format.
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
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.RealLive
{
    internal class PdtMetaData : ImageMetaData
    {
        public int  Version;
        public uint AlphaOffset;
    }

    [Export(typeof(ImageFormat))]
    public class PdtFormat : ImageFormat
    {
        public override string         Tag { get { return "PDT"; } }
        public override string Description { get { return "AVG32 engine image format"; } }
        public override uint     Signature { get { return 0x31544450; } } // 'PDT1'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (32);
            int version = header[4] - '0';
            if (version < 0 || version > 1)
                return null;
            return new PdtMetaData
            {
                Width   = header.ToUInt32 (0x0C),
                Height  = header.ToUInt32 (0x10),
                BPP     = 32,
                Version     = version,
                AlphaOffset = header.ToUInt32 (0x1C),
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var reader = new PdtReader (stream, (PdtMetaData)info))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, reader.Palette, reader.Data);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("PdtFormat.Write not implemented");
        }
    }

    internal sealed class PdtReader : IDisposable
    {
        IBinaryStream   m_input;
        byte[]          m_output;
        PdtMetaData     m_info;

        public byte[]           Data { get { return m_output; } }
        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }

        public PdtReader (IBinaryStream input, PdtMetaData info)
        {
            m_input = input;
            m_info = info;
            if (0 != m_info.AlphaOffset)
                Format = PixelFormats.Bgra32;
            else if (1 == m_info.Version)
                Format = PixelFormats.Indexed8;
            else
                Format = PixelFormats.Bgr32;

            m_output = new byte[m_info.Width * m_info.Height * Format.BitsPerPixel / 8];
        }

        public void Unpack ()
        {
            m_input.Position = 0x20;
            if (0 == m_info.Version)
                UnpackV0();
            else
                UnpackV1();
        }

        void UnpackV0 ()
        {
            Unpack24();
            if (0 != m_info.AlphaOffset)
            {
                m_input.Position = m_info.AlphaOffset;
                var alpha = Unpack8();
                int src = 0;
                for (int i = 3; i < m_output.Length; i += 4)
                {
                    m_output[i] = alpha[src++];
                }
            }
        }

        void UnpackV1 ()
        {
            Palette = ImageFormat.ReadPalette (m_input.AsStream);
            var offsets = new int[16];
            for (int i = 0; i < offsets.Length; ++i)
                offsets[i] = m_input.ReadInt32();
            LzUnpack (offsets);

            if (0 != m_info.AlphaOffset)
            {
                m_input.Position = m_info.AlphaOffset;
                var alpha = Unpack8();
            }
        }

        void Unpack24 ()
        {
            int dst = 0;
            int bits = 0;
            int mask = 0;
            while (dst < m_output.Length)
            {
                mask >>= 1;
                if (0 == mask)
                {
                    bits = m_input.ReadByte();
                    mask = 0x80;
                }
                if (0 != (bits & mask))
                {
                    m_input.Read (m_output, dst, 3);
                    dst += 4;
                }
                else
                {
                    int offset = m_input.ReadUInt16();
                    int count = (1 + (offset & 0xF)) * 4;
                    offset = (1 + (offset >> 4)) * 4;
                    Binary.CopyOverlapped (m_output, dst-offset, dst, count);
                    dst += count;
                }
            }
        }

        byte[] Unpack8 ()
        {
            var output = new byte[m_info.Width * m_info.Height];
            int dst = 0;
            int bits = 0;
            int mask = 0;
            while (dst < output.Length)
            {
                mask >>= 1;
                if (0 == mask)
                {
                    bits = m_input.ReadUInt8();
                    mask = 0x80;
                }
                if (0 != (bits & mask))
                {
                    output[dst++] = m_input.ReadUInt8();
                }
                else
                {
                    int count = 2 + m_input.ReadUInt8();
                    int offset = 1 + m_input.ReadUInt8();
                    Binary.CopyOverlapped (output, dst-offset, dst, count);
                    dst += count;
                }
            }
            return output;
        }

        void LzUnpack (int[] offsets)
        {
            int dst = 0;
            int bits = 0;
            int mask = 0;
            while (dst < m_output.Length)
            {
                mask >>= 1;
                if (0 == mask)
                {
                    bits = m_input.ReadUInt8();
                    mask = 0x80;
                }
                if (0 != (bits & mask))
                {
                    m_output[dst++] = m_input.ReadUInt8();
                }
                else
                {
                    int offset = m_input.ReadUInt8();
                    int count = Math.Min (2 + (offset >> 4), m_output.Length - dst);
                    offset = offsets[offset & 0xF];
                    if (dst < offset)
                    {
                        int gap = Math.Min (offset - dst, count);
                        dst += gap;
                        count -= gap;
                    }
                    if (count > 0)
                    {
                        Binary.CopyOverlapped (m_output, dst-offset, dst, count);
                        dst += count;
                    }
                }
            }
        }

        #region IDisposable Members
        public void Dispose ()
        {
        }
        #endregion
    }
}
