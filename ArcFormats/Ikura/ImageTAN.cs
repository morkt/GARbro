//! \file       ImageTAN.cs
//! \date       2018 Jan 16
//! \brief      D.O. animation resource format.
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
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Ikura
{
    internal class TanMetaData : ImageMetaData
    {
        public uint DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class TanFormat : ImageFormat
    {
        public override string         Tag { get { return "TAN"; } }
        public override string Description { get { return "D.O. animation resource"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".tan"))
                return null;
            int count = file.ReadUInt16();
            if (0 == count)
                return null;
            file.Position = 2 + count * 4;
            uint w = file.ReadUInt16();
            uint h = file.ReadUInt16();
            if (0 == w || 0 == h)
                return null;
            return new TanMetaData {
                Width = w, Height = h, BPP = 8,
                DataOffset = (uint)file.Position,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new TanReader (file, (TanMetaData)info);
            var pixels = reader.UnpackFrame (0);
            return ImageData.Create (info, reader.Format, reader.Palette, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("TanFormat.Write not implemented");
        }
    }

    internal class TanReader
    {
        IBinaryStream       m_input;
        byte[]              m_output;
        TanMetaData         m_info;

        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }

        public TanReader (IBinaryStream input, TanMetaData info)
        {
            m_input = input;
            m_output = new byte[info.Width * info.Height];
            m_info = info;
            Format = 8 == m_info.BPP ? PixelFormats.Indexed8 : PixelFormats.Bgr24;
        }

        public byte[] UnpackFrame (int frame)
        {
            m_input.Position = m_info.DataOffset;
            if (8 == m_info.BPP)
                Palette = ImageFormat.ReadPalette (m_input.AsStream);
            int count = m_input.ReadUInt16();
            if (frame >= count)
                throw new InvalidFormatException ("Not enough frames in TAN file.");
            long base_pos = m_input.Position + 4 * count;
            var frame_table = new uint[count];
            for (int i = 0; i < count; ++i)
                frame_table[i] = m_input.ReadUInt32();
            Action Unpack;
            if (8 == m_info.BPP)
                Unpack = Unpack8bpp;
            else
                Unpack = Unpack24bpp;
            for (int i = 0; i <= frame; ++i)
            {
                m_input.Position = base_pos + frame_table[i];
                Unpack();
            }
            return m_output;
        }

        void Unpack8bpp ()
        {
            int dst = 0;
            while (dst < m_output.Length)
            {
                int count;
                byte ctl = m_input.ReadUInt8();
                if (0 == ctl--)
                {
                    count = m_input.ReadUInt8();
                    byte v = m_input.ReadUInt8();
                    while (count --> 0)
                        m_output[dst++] = v;
                }
                else if (0 == ctl--)
                {
                    count = m_input.ReadUInt8();
                    int offset = m_input.ReadUInt8();
                    Binary.CopyOverlapped (m_output, dst-offset, dst, count);
                    dst += count;
                }
                else if (0 == ctl--)
                {
                    count = m_input.ReadUInt8();
                    int offset = m_input.ReadUInt16();
                    Binary.CopyOverlapped (m_output, dst-offset, dst, count);
                    dst += count;
                }
                else if (0 == ctl--)
                {
                    dst += m_input.ReadUInt8();
                }
                else if (0 == ctl)
                {
                    dst += m_input.ReadUInt16();
                }
                else
                {
                    count = ctl;
                    m_input.Read (m_output, dst, count);
                    dst += count;
                }
            }
        }

        void Unpack24bpp ()
        {
            throw new NotImplementedException();
        }
    }
}
