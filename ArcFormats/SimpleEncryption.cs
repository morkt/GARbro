//! \file       SimpleEncryption.cs
//! \date       Mon Feb 29 08:19:07 2016
//! \brief      Primitive encryption methods implementations.
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

using System.IO;
using System.Security.Cryptography;

namespace GameRes.Formats
{
    public sealed class NotTransform : ICryptoTransform
    {
        private const int BlockSize = 256;

        public bool          CanReuseTransform { get { return true; } }
        public bool CanTransformMultipleBlocks { get { return true; } }
        public int              InputBlockSize { get { return BlockSize; } }
        public int             OutputBlockSize { get { return BlockSize; } }

        public int TransformBlock (byte[] inputBuffer, int inputOffset, int inputCount,
                                   byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; ++i)
            {
                outputBuffer[outputOffset++] = (byte)~inputBuffer[inputOffset+i];
            }
            return inputCount;
        }

        public byte[] TransformFinalBlock (byte[] inputBuffer, int inputOffset, int inputCount)
        {
            byte[] outputBuffer = new byte[inputCount];
            TransformBlock (inputBuffer, inputOffset, inputCount, outputBuffer, 0);
            return outputBuffer;
        }

        public void Dispose ()
        {
            System.GC.SuppressFinalize (this);
        }
    }

    public sealed class XorTransform : ICryptoTransform
    {
        private const int BlockSize = 256;
        private byte m_key;

        public bool          CanReuseTransform { get { return true; } }
        public bool CanTransformMultipleBlocks { get { return true; } }
        public int              InputBlockSize { get { return BlockSize; } }
        public int             OutputBlockSize { get { return BlockSize; } }

        public XorTransform (byte key)
        {
            m_key = key;
        }

        public int TransformBlock (byte[] inputBuffer, int inputOffset, int inputCount,
                                   byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; ++i)
            {
                outputBuffer[outputOffset++] = (byte)(m_key ^ inputBuffer[inputOffset+i]);
            }
            return inputCount;
        }

        public byte[] TransformFinalBlock (byte[] inputBuffer, int inputOffset, int inputCount)
        {
            byte[] outputBuffer = new byte[inputCount];
            TransformBlock (inputBuffer, inputOffset, inputCount, outputBuffer, 0);
            return outputBuffer;
        }

        public void Dispose ()
        {
            System.GC.SuppressFinalize (this);
        }
    }
}
