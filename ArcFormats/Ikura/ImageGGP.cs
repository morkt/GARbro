//! \file       ImageGGP.cs
//! \date       Sun Jun 14 09:06:26 2015
//! \brief      
//
// Copyright (C) 2014-2015 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Ikura
{
    internal class GgpMetaData : ImageMetaData
    {
        public byte[]   Key = new byte[8];
        public uint     Offset;
        public uint     Length;
    }

    [Export(typeof(ImageFormat))]
    public class GgpFormat : PngFormat
    {
        public override string         Tag { get { return "GGP"; } }
        public override string Description { get { return "Digital Romance System encrypted image format"; } }
        public override uint     Signature { get { return 0x46504747u; } } // 'GGPF'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x24];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            if (!Binary.AsciiEqual (header, 0, "GGPFAIKE"))
                return null;
            var info = new GgpMetaData
            {
                Offset = LittleEndian.ToUInt32 (header, 0x14),
                Length = LittleEndian.ToUInt32 (header, 0x18),
            };
            for (int i = 0; i < 8; ++i)
                info.Key[i] = (byte)(header[i] ^ header[i+0xC]);
            stream.Position = info.Offset;
            using (var png = new EncryptedStream (stream, info.Key, true))
            {
                var png_info = base.ReadMetaData (png);
                info.Width   = png_info.Width;
                info.Height  = png_info.Height;
                info.BPP     = png_info.BPP;
                info.OffsetX = png_info.OffsetX;
                info.OffsetY = png_info.OffsetY;
                return info;
            }
        }

        public override ImageData Read (Stream file, ImageMetaData info)
        {
            var meta = info as GgpMetaData;
            if (null == meta)
                throw new ArgumentException ("GgpFormat.Read should be supplied with GgpMetaData", "info");
            using (var input = new StreamRegion (file, meta.Offset, meta.Length, true))
            using (var png = new EncryptedStream (input, meta.Key, true))
                return base.Read (png, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("GgpFormat.Write not implemented");
        }
    }

    internal class EncryptedStream : InputProxyStream
    {
        private byte[]  m_key;
        private long    m_position;

        public EncryptedStream (Stream main, byte[] key, bool leave_open = false)
            : base (main, leave_open)
        {
            if (null == key)
                throw new ArgumentNullException ("key");
            if (key.Length < 8)
                throw new ArgumentException ("key");
            m_key = key;
            m_position = 0;
        }

        public override bool CanSeek  { get { return false; } }
        public override long Length   { get { throw new NotSupportedException(); } }
        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = BaseStream.Read (buffer, offset, count);
            if (read > 0)
            {
                for (int i = 0; i < read; ++i)
                {
                    buffer[offset+i] ^= m_key[m_position++ & 7];
                }
            }
            return read;
        }

        public override int ReadByte ()
        {
            int b = BaseStream.ReadByte();
            if (-1 != b)
            {
                b ^= m_key[m_position++ & 7];
            }
            return b;
        }
    }
}
