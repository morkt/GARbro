//! \file       ImageIPH.cs
//! \date       Sun Nov 08 12:09:06 2015
//! \brief      TechnoBrain's "Inteligent Picture Format" (original spelling)
//
// Copyright (C) 2015-2016 by morkt
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

using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.TechnoBrain
{
    internal class IphMetaData : ImageMetaData
    {
        public int  PackedSize;
        public bool IsCompressed;
    }

    [Export(typeof(ImageFormat))]
    public class IphFormat : ImageFormat
    {
        public override string         Tag { get { return "IPH"; } }
        public override string Description { get { return "TechnoBrain's 'Inteligent Picture Format'"; } }
        public override uint     Signature { get { return 0; } } // 'RIFF'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            // 'RIFF' isn't included into signature to avoid auto-detection of the WAV files as IPH images.
            if (0x46464952 != FormatCatalog.ReadSignature (stream)) // 'RIFF'
                return null;
            using (var reader = new ArcView.Reader (stream))
            {
                if (0x38 != reader.ReadInt32())
                    return null;
                var signature = reader.ReadInt32();
                if (signature != 0x20485049 && signature != 0x00485049) // 'IPH'
                    return null;
                if (0x20746D66 != reader.ReadInt32()) // 'fmt '
                    return null;
                reader.BaseStream.Position = 0x38;
                if (0x20706D62 != reader.ReadInt32()) // 'bmp '
                    return null;
                var info = new IphMetaData();
                info.PackedSize = reader.ReadInt32();
                info.Width  = reader.ReadUInt16();
                info.Height = reader.ReadUInt16();
                reader.BaseStream.Position = 0x50;
                info.BPP = reader.ReadUInt16();
                info.IsCompressed = 0 != reader.ReadInt16();
                // XXX int16@[0x54] is a transparency color or 0xFFFF if image is not transparent
                return info;
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            if (info.BPP != 16)
                throw new NotSupportedException ("Not supported IPH color depth");
            using (var reader = new IphReader (stream, (IphMetaData)info))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, null, reader.Data);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("IphFormat.Write not implemented");
        }
    }

    internal sealed class IphReader : IDisposable
    {
        BinaryReader        m_input;
        byte[]              m_output;
        int                 m_width;
        int                 m_height;
        IphMetaData         m_info;

        public PixelFormat Format { get { return PixelFormats.Bgr555; } }
        public byte[]        Data { get { return m_output; } }

        public IphReader (Stream input, IphMetaData info)
        {
            m_info = info;
            m_input = new ArcView.Reader (input);
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            m_output = new byte[m_width*m_height*2];
        }

        public void Unpack ()
        {
            m_input.BaseStream.Position = 0x58;
            if (!m_info.IsCompressed)
            {
                m_input.Read (m_output, 0, m_output.Length);
                return;
            }
            int stride = m_width * 2;
            var extra_line = new byte[stride];
            for (int y = 0; y < m_height; ++y)
            {
                int row = stride * y;
                byte ctl = m_input.ReadByte();
                if (ctl != 0)
                {
                    int dst = row;
                    int pixel = 0;
                    while (dst < m_output.Length)
                    {
                        ctl = m_input.ReadByte();
                        if (0xFF == ctl)
                            break;
                        if (0xFE == ctl)
                        {
                            int count = m_input.ReadByte() + 1;
                            pixel = m_input.ReadUInt16();
                            for (int j = 0; j < count; ++j)
                            {
                                LittleEndian.Pack ((ushort)pixel, m_output, dst);
                                dst += 2;
                            }
                        }
                        else if (ctl < 0x80)
                        {
                            byte lo = m_input.ReadByte();
                            m_output[dst++] = lo;
                            m_output[dst++] = ctl;
                            pixel = ctl << 8 | lo;
                        }
                        else
                        {
                            ctl &= 0x7F;
                            int r = (pixel & 0x7C00) >> 10;
                            int g = (pixel & 0x3E0) >> 5;
                            int b = pixel & 0x1F;
                            pixel = (b + ctl / 25 % 5 - 2)
                                  | (g + ctl / 5 % 5 - 2) << 5
                                  | (r + ctl % 5 - 2) << 10;
                            LittleEndian.Pack ((ushort)pixel, m_output, dst);
                            dst += 2;
                        }
                    }
                }
                else
                {
                    m_input.Read (m_output, row, stride);
                    m_input.ReadByte();
                }
                ctl = m_input.ReadByte();
                if (0 != ctl)
                {
                    int dst = 0;
                    for (;;)
                    {
                        ctl = m_input.ReadByte();
                        if (0xFF == ctl)
                            break;
                        if (ctl >= 0x80)
                        {
                            byte b = (byte)(ctl & 0x7F);
                            int count = m_input.ReadByte() + 1;
                            for (int j = 0; j < count; ++j)
                            {
                                extra_line[dst++] = b;
                            }
                        }
                        else
                        {
                            extra_line[dst++] = ctl;
                        }
                    }
                    dst = row + 1;
                    for (int i = 0; i < m_width; ++i)
                    {
                        int v46 = extra_line[i / 6];
                        if (0 != ((32 >> i % 6) & v46))
                            m_output[dst] |= 0x80;
                        dst += 2;
                    }
                }
                else
                {
                    m_input.ReadByte();
                }
            }
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
