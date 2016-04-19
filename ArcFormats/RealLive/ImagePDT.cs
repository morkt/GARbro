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
        public uint AlphaOffset;
    }

    [Export(typeof(ImageFormat))]
    public class PdtFormat : ImageFormat
    {
        public override string         Tag { get { return "PDT"; } }
        public override string Description { get { return "AVG32 engine image format"; } }
        public override uint     Signature { get { return 0x31544450; } } // 'PDT1'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[32];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            if (!Binary.AsciiEqual (header, "PDT10\0"))
                return null;
            return new PdtMetaData
            {
                Width   = LittleEndian.ToUInt32 (header, 0x0C),
                Height  = LittleEndian.ToUInt32 (header, 0x10),
                BPP     = 32,
                AlphaOffset = LittleEndian.ToUInt32 (header, 0x1C),
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
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
        BinaryReader    m_input;
        byte[]          m_output;
        PdtMetaData     m_info;

        public byte[]           Data { get { return m_output; } }
        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }

        public PdtReader (Stream input, PdtMetaData info)
        {
            m_input = new ArcView.Reader (input);
            m_info = info;
            m_output = new byte[m_info.Width * m_info.Height * 4];
            if (0 == m_info.AlphaOffset)
                Format = PixelFormats.Bgr32;
            else
                Format = PixelFormats.Bgra32;
        }

        public void Unpack ()
        {
            m_input.BaseStream.Position = 0x20;
            Unpack24();
            if (0 != m_info.AlphaOffset)
            {
                m_input.BaseStream.Position = m_info.AlphaOffset;
                var alpha = Unpack8();
                int src = 0;
                for (int i = 3; i < m_output.Length; i += 4)
                {
                    m_output[i] = alpha[src++];
                }
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
                    bits = m_input.ReadByte();
                    mask = 0x80;
                }
                if (0 != (bits & mask))
                {
                    output[dst++] = m_input.ReadByte();
                }
                else
                {
                    int count = 2 + m_input.ReadByte();
                    int offset = 1 + m_input.ReadByte();
                    Binary.CopyOverlapped (output, dst-offset, dst, count);
                    dst += count;
                }
            }
            return output;
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
        }
        #endregion
    }
}
