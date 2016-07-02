//! \file       ImageEENC.cs
//! \date       Sat Jun 18 06:26:23 2016
//! \brief      SepterApp encrypted images.
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Bruns
{
    internal class EencMetaData : ImageMetaData
    {
        public uint             Key;
        public ImageMetaData    Info;
        public ImageFormat      Format;
        public bool             Compressed;
    }

    [Export(typeof(ImageFormat))]
    public class EencFormat : ImageFormat
    {
        public override string         Tag { get { return "EENC"; } }
        public override string Description { get { return "Bruns system encrypted image"; } }
        public override uint     Signature { get { return 0x434E4545; } } // 'EENC'

        static readonly uint EencKey =  0xDEADBEEF;

        public EencFormat ()
        {
            Extensions = new string[] { "brs", "png", "bmp" };
            Signatures = new uint[] { 0x434E4545, 0x5A4E4545 }; // 'EENC', 'EENZ'
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[8];
            if (8 != stream.Read (header, 0, 8))
                return null;
            bool compressed = 'Z' == header[3];
            uint key = LittleEndian.ToUInt32 (header, 4) ^ EencKey;
            Stream input = new StreamRegion (stream, 8, true);
            try
            {
                input = new EencStream (input, key);
                if (compressed)
                {
                    input = new ZLibStream (input, CompressionMode.Decompress);
                    input = new SeekableStream (input);
                }
                var format = FindFormat (input);
                if (null == format)
                    return null;
                return new EencMetaData
                {
                    Width = format.Item2.Width,
                    Height = format.Item2.Height,
                    BPP = format.Item2.BPP,
                    Key = key,
                    Info = format.Item2,
                    Format = format.Item1,
                    Compressed = compressed,
                };
            }
            finally
            {
                input.Dispose();
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = (EencMetaData)info;
            meta.Info.FileName = info.FileName;
            Stream input = new StreamRegion (stream, 8, true);
            try
            {
                input = new EencStream (input, meta.Key);
                if (meta.Compressed)
                    input = new ZLibStream (input, CompressionMode.Decompress);
                return meta.Format.Read (input, meta.Info);
            }
            finally
            {
                input.Dispose();
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("EencFormat.Write not implemented");
        }
    }

    internal class EencStream : InputProxyStream
    {
        uint    m_key;

        public EencStream (Stream main, uint key, bool leave_open = false)
            : base (main, leave_open)
        {
            m_key = key;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int pos = (int)Position & 3;
            int read = BaseStream.Read (buffer, offset, count);
            for (int i = 0; i < read; ++i)
            {
                buffer[offset+i] ^= (byte)(m_key >> (pos << 3));
                pos = (pos + 1) & 3;
            }
            return read;
        }

        public override int ReadByte ()
        {
            int pos = (int)Position & 3;
            int b = BaseStream.ReadByte();
            if (-1 != b)
                b ^= (byte)(m_key >> (pos << 3));
            return b;
        }
    }
}
