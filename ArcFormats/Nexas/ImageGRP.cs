//! \file       ImageGRP.cs
//! \date       Fri May 20 20:30:01 2016
//! \brief      NeXAS GRP image format.
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
using GameRes.Utility;

namespace GameRes.Formats.NeXAS
{
    internal class GrpMetaData : ImageMetaData
    {
        public int  UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class GrpFormat : ImageFormat
    {
        public override string         Tag { get { return "GR3"; } }
        public override string Description { get { return "NeXAS engine image format"; } }
        public override uint     Signature { get { return 0x18335247; } }

        public GrpFormat ()
        {
            Extensions = new string[] { "grp" };
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x11];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            return new GrpMetaData
            {
                Width   = LittleEndian.ToUInt32 (header, 5),
                Height  = LittleEndian.ToUInt32 (header, 9),
                BPP     = LittleEndian.ToUInt16 (header, 3),
                UnpackedSize = LittleEndian.ToInt32 (header, 0xD),
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            using (var reader = new GrpReader (stream, (GrpMetaData)info))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, null, reader.Data);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GrpFormat.Write not implemented");
        }
    }

    internal sealed class GrpReader : IDisposable
    {
        BinaryReader    m_input;
        byte[]          m_output;

        public byte[]        Data { get { return m_output; } }
        public PixelFormat Format { get; private set; }

        public GrpReader (Stream input, GrpMetaData info)
        {
            m_input = new ArcView.Reader (input);
            m_output = new byte[info.UnpackedSize];
            if (24 == info.BPP)
                Format = PixelFormats.Bgr24;
            else if (32 == info.BPP)
                Format = PixelFormats.Bgr32;
            else
                throw new NotSupportedException ("Not supported GRP image color depth");
        }

        public void Unpack ()
        {
            m_input.BaseStream.Position = 0x11;
            int ctl_length = (m_input.ReadInt32() + 7) / 8;
            var ctl_bytes = m_input.ReadBytes (ctl_length);
            m_input.ReadInt32();
            using (var ctl_mem = new MemoryStream (ctl_bytes))
            using (var bits = new LsbBitStream (ctl_mem))
            {
                int dst = 0;
                while (dst < m_output.Length)
                {
                    int bit = bits.GetNextBit();
                    if (-1 == bit)
                        break;
                    if (0 == bit)
                    {
                        m_output[dst++] = m_input.ReadByte();
                    }
                    else
                    {
                        int offset = m_input.ReadUInt16();
                        int count = (offset & 7) + 1;
                        offset = (offset >> 3) + 1;
                        Binary.CopyOverlapped (m_output, dst - offset, dst, count);
                        dst += count;
                    }
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
