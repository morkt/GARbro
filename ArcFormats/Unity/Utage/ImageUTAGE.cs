//! \file       ImageUTAGE.cs
//! \date       2018 Feb 05
//! \brief      Utage engine encrypted image.
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
using System.Text;

namespace GameRes.Formats.Unity.Utage
{
    internal class UtageMetaData : ImageMetaData
    {
        public ImageFormat  UFormat;
    }

    [Export(typeof(ImageFormat))]
    public class UtageFormat : ImageFormat
    {
        public override string         Tag { get { return "UTAGE"; } }
        public override string Description { get { return "Utage engine encrypted image"; } }
        public override uint     Signature { get { return 0x323E3EC0; } }

        public UtageFormat ()
        {
            Signatures = new uint[] { 0x323E3EC0, 0 };
        }

        public static readonly byte[] KnownKey = Encoding.UTF8.GetBytes ("InputOriginalKey");

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            using (var input = OpenEncryptedStream (file, KnownKey))
            {
                ImageFormat format = file.Signature == 0x323E3EC0 ? Png : Jpeg;
                var info = format.ReadMetaData (input);
                if (null == info)
                    return null;
                return new UtageMetaData {
                    Width = info.Width, Height = info.Height,
                    OffsetX = info.OffsetX, OffsetY = info.OffsetY,
                    BPP = info.BPP,
                    UFormat = format,
                };
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (UtageMetaData)info;
            using (var input = OpenEncryptedStream (file, KnownKey))
                return meta.UFormat.Read (input, info);
        }

        internal IBinaryStream OpenEncryptedStream (IBinaryStream file, byte[] key)
        {
            var input = new UtageEncryptedStream (file.AsStream, key, true);
            return new BinaryStream (input, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("UtageFormat.Write not implemented");
        }
    }

    internal class UtageEncryptedStream : InputProxyStream
    {
        byte[]  m_key;

        public UtageEncryptedStream (Stream main, byte[] key, bool leave_open = false)
            : base (main, leave_open)
        {
            m_key = key;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int key_length = m_key.Length;
            int start_pos = (int)(BaseStream.Position % key_length);
            int read = BaseStream.Read (buffer, offset, count);
            for (int i = 0; i < read; ++i)
            {
                byte key = m_key[(start_pos + i) % key_length];
                if (buffer[offset+i] != 0 && buffer[offset+i] != key)
                    buffer[offset+i] ^= key;
            }
            return read;
        }

        public override int ReadByte ()
        {
            long pos = BaseStream.Position;
            int b = BaseStream.ReadByte();
            if (b > 0)
            {
                byte key = m_key[pos % m_key.Length];
                if (b != key)
                    b ^= key;
            }
            return b;
        }
    }
}
