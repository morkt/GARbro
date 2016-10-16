//! \file       ImageGRD.cs
//! \date       Sun Apr 12 21:05:44 2015
//! \brief      Silky's GRD image format.
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

namespace GameRes.Formats.Silky
{
    internal class GrdMetaData : ImageMetaData
    {
        public int DataSize;
    }

    [Export(typeof(ImageFormat))]
    public class GrdFormat : ImageFormat
    {
        public override string         Tag { get { return "GRD"; } }
        public override string Description { get { return "Silky's RGB image format"; } }
        public override uint     Signature { get { return 0x5f504d43u; } } // 'CMP_'

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("GrdFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Seek (4, SeekOrigin.Current);
            int data_size = stream.ReadInt32();
            stream.Seek (4, SeekOrigin.Current);
            using (var reader = new Reader (stream.AsStream, 0x22)) // BMP header
            {
                reader.Unpack();
                var bmp = reader.Data;
                if (bmp[0] != 'B' || bmp[1] != 'M')
                    return null;
                int width = LittleEndian.ToInt32 (bmp, 0x12);
                int height = LittleEndian.ToInt32 (bmp, 0x16);
                int bpp = LittleEndian.ToInt16 (bmp, 0x1c);
                return new GrdMetaData
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    BPP = bpp,
                    DataSize = data_size,
                };
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (GrdMetaData)info;
            stream.Position = 12;
            using (var reader = new Reader (stream.AsStream, meta.DataSize))
            {
                reader.Unpack();
                byte[] pixels = reader.Data;
                using (var bmp = new MemoryStream (reader.Data))
                {
                    var decoder = new BmpBitmapDecoder (bmp,
                        BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    BitmapSource frame = decoder.Frames[0];
                    frame.Freeze();
                    return new ImageData (frame, info);
                }
            }
        }
    }

    // custom LZSS reader
    //
    internal class Reader : IDisposable
    {
        Stream          m_input;
        byte[]          m_output;

        public byte[] Data { get { return m_output; } }

        public Reader (Stream file, int dst_size)
        {
            m_input = file;
            m_output = new byte[dst_size];
        }

        public void Unpack ()
        {
            var frame = new byte[0x1000];
            int frame_pos = 0xfee;
            for (int i = 0; i < frame_pos; ++i)
                frame[i] = 32;
            int ctl = 0;
            int dst = 0;
            while (dst < m_output.Length)
            {
                ctl >>= 1;
                if (0 == (ctl & 0x100))
                {
                    int b = m_input.ReadByte();
                    if (-1 == b)
                        break;
                    ctl = b | 0xff00;
                }
                if (0 != (ctl & 1))
                {
                    int b = m_input.ReadByte();
                    if (-1 == b)
                        break;
                    m_output[dst++] = (byte)b;
                    frame[frame_pos++] = (byte)b;
                    frame_pos &= 0xfff;
                }
                else
                {
                    int lo = m_input.ReadByte();
                    if (-1 == lo)
                        break;
                    int hi = m_input.ReadByte();
                    if (-1 == hi)
                        break;
                    int offset = (hi & 0xF0) << 4 | lo;
                    int count = (hi & 0xF) + 3;
                    for (int i = 0; i < count && dst < m_output.Length; ++i)
                    {
                        byte b = frame[(offset + i) & 0xfff];
                        m_output[dst++] = b;
                        frame[frame_pos++] = b;
                        frame_pos &= 0xfff;
                    }
                }
            }
        }

        #region IDisposable Members
        public void Dispose ()
        {
            if (null != m_input)
            {
                m_input = null;
            }
            GC.SuppressFinalize (this);
        }
        #endregion
    }
}
