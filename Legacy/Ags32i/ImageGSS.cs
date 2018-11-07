//! \file       ImageGSS.cs
//! \date       2018 Mar 28
//! \brief      AGS32i engine encrypted bitmap.
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
using System.Security.Cryptography;
using System.Windows.Media;
using GameRes.Compression;
using GameRes.Utility;

// [041001][Innocence] China Chaime+
// [041029][Tam's] Fukushuu no Ori ~OL Kankin Ryoujoku no Yakata~
// [041224][Tam's] Volga Series 1

namespace GameRes.Formats.Ags32i
{
    internal class GssMetaData : ImageMetaData
    {
        public uint Key;
        public int  DataOffset;
        public int  UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class GssFormat : ImageFormat
    {
        public override string         Tag { get { return "GSS"; } }
        public override string Description { get { return "AGS32i engine encrypted bitmap"; } }
        public override uint     Signature { get { return 0x20575A52; } }

        public GssFormat ()
        {
            Signatures = new uint[] { 0x20575A52, 0x20574242, 0x20574346, 0 };
        }

        //static readonly uint DefaultKey = 0x20040915;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            uint key = file.Signature ^ 0x535347u;
            using (var ags = OpenStream (file, key))
            {
                var header = new byte[0x28];
                ags.Read (header, 0, 8);
                if (!header.AsciiEqual ("GSS\0"))
                    return null;
                int unpacked_size = header.ToInt32 (4);
                if (unpacked_size <= 0)
                    return null;
                using (var bmp = new ZLibStream (ags, CompressionMode.Decompress))
                {
                    if (0x28 != bmp.Read (header, 0, 0x28))
                        return null;
                    int header_length = header.ToInt32 (0);
                    if (header_length <= 0 || header_length >= unpacked_size)
                        return null;
                    return new GssMetaData {
                        Width  = header.ToUInt32 (4),
                        Height = header.ToUInt32 (8),
                        BPP    = header.ToInt16 (0xE),
                        Key    = key,
                        DataOffset = header.ToInt32 (0),
                        UnpackedSize = unpacked_size,
                    };
                }
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (GssMetaData)info;
            using (var ags = OpenStream (file, meta.Key))
            {
                var header = new byte[meta.DataOffset];
                ags.Read (header, 0, 8);
                using (var bmp = new ZLibStream (ags, CompressionMode.Decompress))
                {
                    bmp.Read (header, 0, header.Length);
                    var pixels = new byte[meta.UnpackedSize - meta.DataOffset];
                    if (pixels.Length != bmp.Read (pixels, 0, pixels.Length))
                        throw new InvalidFormatException();
                    PixelFormat format;
                    if (32 == meta.BPP)
                        format = PixelFormats.Bgra32;
                    else if (24 == meta.BPP)
                        format = PixelFormats.Bgr24;
                    else
                        throw new InvalidFormatException();
                    int stride = (int)meta.Width * meta.BPP / 8;
                    return ImageData.CreateFlipped (info, format, null, pixels, stride);
                }
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GssFormat.Write not implemented");
        }

        Stream OpenStream (IBinaryStream input, uint key)
        {
            var proxy = new ProxyStream (input.AsStream, true);
            return new InputCryptoStream (proxy, new Ags32Transform (key));
        }
    }

    public sealed class Ags32Transform : ICryptoTransform
    {
        readonly uint   m_key;
        int             m_position;

        const int BlockSize = 4;

        public bool          CanReuseTransform { get { return false; } }
        public bool CanTransformMultipleBlocks { get { return true; } }
        public int              InputBlockSize { get { return BlockSize; } }
        public int             OutputBlockSize { get { return BlockSize; } }

        public Ags32Transform (uint key)
        {
            m_key = key;
            m_position = 0;
        }

        public int TransformBlock (byte[] inputBuffer, int inputOffset, int inputCount,
                                   byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; i += 4)
            {
                int rem;
                int div = Math.DivRem ((m_position+i) >> 2, 31, out rem);
                uint t = Binary.RotL (m_key + (uint)div, rem);
                for (int j = 0; j < 4; ++j)
                    outputBuffer[outputOffset++] = (byte)((t >> (j << 3)) ^ inputBuffer[inputOffset++]);
            }
            m_position += inputCount;
            return inputCount;
        }

        public byte[] TransformFinalBlock (byte[] inputBuffer, int inputOffset, int inputCount)
        {
            byte[] outputBuffer = new byte[inputCount];
            int rem;
            int div = Math.DivRem ((m_position) >> 2, 31, out rem);
            uint t = Binary.RotL (m_key + (uint)div, rem);
            for (int i = 0; i < inputCount; i++)
            {
                outputBuffer[i++] = (byte)((t >> (i << 3)) ^ inputBuffer[inputOffset+i]);
            }
            m_position += inputCount;
            return outputBuffer;
        }

        public void Dispose ()
        {
        }
    }
}
