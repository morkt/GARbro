//! \file       ImageMGO.cs
//! \date       2017 Dec 31
//! \brief      Melody compressed image.
//
// Copyright (C) 2017 by morkt
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
using System.Windows.Media.Imaging;
using GameRes.Compression;

// [000303][Melody] Rendezvous

namespace GameRes.Formats.Melody
{
    internal class MgoMetaData : ImageMetaData
    {
        public int  UnpackedSize;
        public int  BmpOffset;
    }

    [Export(typeof(ImageFormat))]
    public class MgoFormat : ImageFormat
    {
        public override string         Tag { get { return "MGO"; } }
        public override string Description { get { return "Melody compressed bitmap"; } }
        public override uint     Signature { get { return 0x424F474D; } } // 'MGOB'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (12);
            if (header.ToInt32 (4) != 5)
                return null;
            using (var lzss = new PackedStream<LzssDecompressor> (file.AsStream, true))
            using (var stream = new SeekableStream (lzss))
            using (var mgo = new BinaryStream (stream, file.Name))
            {
                var name = mgo.ReadCString();
                mgo.Position = ((int)(mgo.Position - 1) & ~3) + 4;
                int count = mgo.ReadInt32();
                mgo.Seek (count * 0x10, SeekOrigin.Current);
                int bmp_position = (int)mgo.Position;
                var bmp_header = mgo.ReadBytes (0x36);
                if (!bmp_header.AsciiEqual ("BM"))
                    return null;
                return new MgoMetaData {
                    Width = bmp_header.ToUInt32 (0x12),
                    Height = bmp_header.ToUInt32 (0x16),
                    BPP = bmp_header.ToInt32 (0x1C),
                    UnpackedSize = header.ToInt32 (8),
                    BmpOffset = bmp_position,
                };
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (MgoMetaData)info;
            file.Position = 12;
            using (var lzss = new PackedStream<LzssDecompressor> (file.AsStream, true))
            {
                var header = new byte[meta.BmpOffset];
                lzss.Read (header, 0, header.Length);
                var decoder = new BmpBitmapDecoder (lzss,
                    BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                frame.Freeze();
                return new ImageData (frame, info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MgoFormat.Write not implemented");
        }
    }

    internal class LzssDecompressor : Decompressor
    {
        LsbBitStream        m_input;

        public override void Initialize (Stream input)
        {
            m_input = new LsbBitStream (input, true);
        }

        protected override IEnumerator<int> Unpack ()
        {
            byte[] frame = new byte[0x1000];
            int frame_pos = 1;
            for (;;)
            {
                int ctl = m_input.GetNextBit();
                if (-1 == ctl)
                    yield break;
                if (ctl != 0)
                {
                    byte v = (byte)m_input.GetBits (8);
                    m_buffer[m_pos++] = frame[frame_pos++ & 0xFFF] = v;
                    if (0 == --m_length)
                        yield return m_pos;
                }
                else
                {
                    int offset = m_input.GetBits (12);
                    if (-1 == offset)
                        yield break;
                    int count = m_input.GetBits (4) + 2;
                    while (count --> 0)
                    {
                        byte v = frame[offset++ & 0xFFF];
                        m_buffer[m_pos++] = frame[frame_pos++ & 0xFFF] = v;
                        if (0 == --m_length)
                            yield return m_pos;
                    }
                }
            }
        }

        #region IDisposable Members
        bool m_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                base.Dispose (disposing);
                if (disposing && m_input != null)
                    m_input.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }
}
