//! \file       ImageGTO.cs
//! \date       2018 Dec 20
//! \brief      Yaneurao obfuscated bitmap.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [000428][Apple Pie] Seishun Ouka

namespace GameRes.Formats.Yaneurao
{
    [Export(typeof(ImageFormat))]
    public class GtoFormat : ImageFormat
    {
        public override string         Tag { get { return "GTO"; } }
        public override string Description { get { return "Yaneurao obfuscated bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (2);
            if (!header.AsciiEqual ("NY"))
                return null;
            file.Position = 0;
            using (var input = OpenGtoStream (file))
                return Bmp.ReadMetaData (input);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var input = OpenGtoStream (file))
                return Bmp.Read (input, info);
        }

        internal IBinaryStream OpenGtoStream (IBinaryStream file)
        {
            var input = new SubFilterStream (file.AsStream, 0xC, true);
            return new BinaryStream (input, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GtoFormat.Write not implemented");
        }
    }

    public class SubFilterStream : ProxyStream
    {
        private byte        m_key;

        public override bool CanWrite { get { return false; } }

        public SubFilterStream (Stream stream, byte key, bool leave_open = false)
            : base (stream, leave_open)
        {
            m_key = key;
        }

        #region System.IO.Stream methods
        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = BaseStream.Read (buffer, offset, count);
            for (int i = 0; i < read; ++i)
            {
                buffer[offset+i] -= m_key;
            }
            return read;
        }

        public override int ReadByte ()
        {
            int b = BaseStream.ReadByte();
            if (-1 != b)
            {
                b = (b - m_key) & 0xFF;
            }
            return b;
        }
        #endregion
    }
}
