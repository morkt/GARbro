//! \file       ImageGAM.cs
//! \date       2018 Oct 23
//! \brief      Project-μ image format.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

// [031219][Project-μ] Gin no Hebi Kuro no Tsuki
// [040528][Lakshmi] Mabuta Tojireba Soko ni...

namespace GameRes.Formats.ProjectMu
{
    [Export(typeof(ImageFormat))]
    public class GamFormat : ImageFormat
    {
        public override string         Tag { get { return "GAM"; } }
        public override string Description { get { return "Project-μ image format"; } }
        public override uint     Signature { get { return 0x4D4147; } } // 'GAM'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            using (var input = OpenGamStream (file))
                return Bmp.ReadMetaData (input);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var input = OpenGamStream (file))
                return Bmp.Read (input, info);
        }

        IBinaryStream OpenGamStream (IBinaryStream input)
        {
            input.Position = 8;
            var unpacked = new PackedStream<GamDecompressor> (input.AsStream, true);
            return new BinaryStream (unpacked, input.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GamFormat.Write not implemented");
        }
    }

    internal class GamDecompressor : Decompressor
    {
        Stream      m_input;

        public override void Initialize (Stream input)
        {
            m_input = input;
        }

        protected override IEnumerator<int> Unpack ()
        {
            var frame = new byte[0x100];
            int frame_pos = 0;
            int bits = 1;
            for (;;)
            {
                if (1 == bits)
                {
                    int b = m_input.ReadByte();
                    if (-1 == b)
                        yield break;
                    bits = b;
                    b = m_input.ReadByte();
                    if (-1 == b)
                        yield break;
                    bits |= b << 8 | 0x10000;
                }
                if (0 == (bits & 1))
                {
                    int b = m_input.ReadByte();
                    if (-1 == b)
                        yield break;
                    frame[frame_pos++ & 0xFF] = (byte)b;
                    m_buffer[m_pos++] = (byte)b;
                    if (0 == --m_length)
                        yield return m_pos;
                }
                else
                {
                    int offset = m_input.ReadByte();
                    if (-1 == offset)
                        yield break;
                    int count = m_input.ReadByte();
                    if (-1 == count)
                        yield break;
                    while (count --> 0)
                    {
                        byte v = frame[(frame_pos - offset) & 0xFF];
                        frame[frame_pos++ & 0xFF] = v;
                        m_buffer[m_pos++] = v;
                        if (0 == --m_length)
                            yield return m_pos;
                    }
                }
                bits >>= 1;
            }
        }
    }
}
