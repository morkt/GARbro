//! \file       ImageGAX.cs
//! \date       Fri Jul 22 04:07:09 2016
//! \brief      ANIM encrypted image.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Security.Cryptography;

namespace GameRes.Formats.Crowd
{
    internal class GaxMetaData : ImageMetaData
    {
        public byte[]   Key;
    }

    [Export(typeof(ImageFormat))]
    public class GaxFormat : ImageFormat
    {
        public override string         Tag { get { return "GAX"; } }
        public override string Description { get { return "ANIM encrypted image"; } }
        public override uint     Signature { get { return 0x01000000; } }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var key = new byte[0x10];
            stream.Position = 4;
            if (key.Length != stream.Read (key, 0, key.Length))
                return null;
            using (var enc = new InputProxyStream (stream, true))
            using (var input = new CryptoStream (enc, new GaxTransform (key), CryptoStreamMode.Read))
            {
                var info = Png.ReadMetaData (input);
                if (null == info)
                    return null;
                return new GaxMetaData
                {
                    OffsetX = info.OffsetX,
                    OffsetY = info.OffsetY,
                    Width   = info.Width,
                    Height  = info.Height,
                    BPP     = info.BPP,
                    Key     = key,
                };
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = (GaxMetaData)info;
            using (var enc = new StreamRegion (stream, 0x14, true))
            using (var input = new CryptoStream (enc, new GaxTransform (meta.Key), CryptoStreamMode.Read))
                return Png.Read (input, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GaxFormat.Write not implemented");
        }
    }

    internal sealed class GaxTransform : ICryptoTransform
    {
        private const int BlockSize = 16;
        private byte[] m_key;

        public bool          CanReuseTransform { get { return false; } }
        public bool CanTransformMultipleBlocks { get { return true; } }
        public int              InputBlockSize { get { return BlockSize; } }
        public int             OutputBlockSize { get { return BlockSize; } }

        public GaxTransform (byte[] key)
        {
            m_key = key.Clone() as byte[];
        }

        public int TransformBlock (byte[] inputBuffer, int inputOffset, int inputCount,
                                   byte[] outputBuffer, int outputOffset)
        {
            int inputEnd = inputOffset + inputCount;
            while (inputOffset < inputEnd)
            {
                int k;
                for (k = 0; k < BlockSize && inputOffset < inputEnd; ++k)
                {
                    outputBuffer[outputOffset++] = (byte)(inputBuffer[inputOffset++] ^ m_key[k]);
                }
                if (k < BlockSize)
                    break;
                byte m = outputBuffer[outputOffset-2];
                switch (m & 7)
                {
                case 0:
                    m_key[0] += m;
                    m_key[3] += (byte)(m + 2);
                    m_key[4]  = (byte)(m_key[2] + m + 11);
                    m_key[8]  = (byte)(m_key[6] + 7);
                    break;
                case 1:
                    m_key[2]  = (byte)(m_key[9] + m_key[10]);
                    m_key[6]  = (byte)(m_key[7] + m_key[15]);
                    m_key[8] += m_key[1];
                    m_key[15] = (byte)(m_key[3] + m_key[5]);
                    break;
                case 2:
                    m_key[1] += m_key[2];
                    m_key[5] += m_key[6];
                    m_key[7] += m_key[8];
                    m_key[10]+= m_key[11];
                    break;
                case 3:
                    m_key[9]  = (byte)(m_key[1]  + m_key[2]);
                    m_key[11] = (byte)(m_key[5]  + m_key[6]);
                    m_key[12] = (byte)(m_key[7]  + m_key[8]);
                    m_key[13] = (byte)(m_key[10] + m_key[11]);
                    break;
                case 4:
                    m_key[0]  = (byte)(m_key[1]  + 0x6F);
                    m_key[3]  = (byte)(m_key[4]  + 0x47);
                    m_key[4]  = (byte)(m_key[5]  + 0x11);
                    m_key[14] = (byte)(m_key[15] + 0x40);
                    break;
                case 5:
                    m_key[2] += m_key[10];
                    m_key[4]  = (byte)(m_key[5]  + m_key[12]);
                    m_key[6]  = (byte)(m_key[8]  + m_key[14]);
                    m_key[8]  = (byte)(m_key[0]  + m_key[11]);
                    break;
                case 6:
                    m_key[9]  = (byte)(m_key[1]  + m_key[11]);
                    m_key[11] = (byte)(m_key[3]  + m_key[13]);
                    m_key[13] = (byte)(m_key[5]  + m_key[15]);
                    m_key[15] = (byte)(m_key[7]  + m_key[9]);
                    goto case 7;
                case 7:
                    m_key[1]  = (byte)(m_key[5]  + m_key[9]);
                    m_key[2]  = (byte)(m_key[6]  + m_key[10]);
                    m_key[3]  = (byte)(m_key[7]  + m_key[11]);
                    m_key[4]  = (byte)(m_key[8]  + m_key[12]);
                    break;
                }
            }
            return inputCount;
        }

        public byte[] TransformFinalBlock (byte[] inputBuffer, int inputOffset, int inputCount)
        {
            byte[] outputBuffer = new byte[inputCount];
            for (int i = 0; i < inputCount; ++i)
            {
                outputBuffer[i] = (byte)(inputBuffer[inputOffset+i] ^ m_key[i]);
            }
            return outputBuffer;
        }

        public void Dispose ()
        {
            System.GC.SuppressFinalize (this);
        }
    }
}
