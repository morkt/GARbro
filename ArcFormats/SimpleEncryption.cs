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
    public abstract class ByteTransform : ICryptoTransform
    {
        const int BlockSize = 1;

        public bool          CanReuseTransform { get { return true; } }
        public bool CanTransformMultipleBlocks { get { return true; } }
        public int              InputBlockSize { get { return BlockSize; } }
        public int             OutputBlockSize { get { return BlockSize; } }

        public abstract int TransformBlock (byte[] inputBuffer, int inputOffset, int inputCount,
                                            byte[] outputBuffer, int outputOffset);

        public byte[] TransformFinalBlock (byte[] inputBuffer, int inputOffset, int inputCount)
        {
            byte[] outputBuffer = new byte[inputCount];
            TransformBlock (inputBuffer, inputOffset, inputCount, outputBuffer, 0);
            return outputBuffer;
        }

        public void Dispose ()
        {
            Dispose (true);
            System.GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
        }
    }

    public sealed class NotTransform : ByteTransform
    {
        public override int TransformBlock (byte[] inputBuffer, int inputOffset, int inputCount,
                                            byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; ++i)
            {
                outputBuffer[outputOffset++] = (byte)~inputBuffer[inputOffset+i];
            }
            return inputCount;
        }
    }

    public sealed class XorTransform : ByteTransform
    {
        private byte m_key;

        public XorTransform (byte key)
        {
            m_key = key;
        }

        public override int TransformBlock (byte[] inputBuffer, int inputOffset, int inputCount,
                                            byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; ++i)
            {
                outputBuffer[outputOffset++] = (byte)(m_key ^ inputBuffer[inputOffset+i]);
            }
            return inputCount;
        }
    }

    public class ByteStringEncryptedStream : InputProxyStream
    {
        byte[]  m_key;
        int     m_base_pos;

        public ByteStringEncryptedStream (Stream main, byte[] key, bool leave_open = false)
            : this (main, 0, key, leave_open)
        {
        }

        public ByteStringEncryptedStream (Stream main, long start_pos, byte[] key, bool leave_open = false)
            : base (main, leave_open)
        {
            m_key = key;
            m_base_pos = (int)(start_pos % m_key.Length);
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int start_pos = (int)((m_base_pos + BaseStream.Position) % m_key.Length);
            int read = BaseStream.Read (buffer, offset, count);
            for (int i = 0; i < read; ++i)
            {
                buffer[offset+i] ^= m_key[(start_pos + i) % m_key.Length];
            }
            return read;
        }

        public override int ReadByte ()
        {
            long pos = BaseStream.Position;
            int b = BaseStream.ReadByte();
            if (-1 != b)
            {
                b ^= m_key[(m_base_pos + pos) % m_key.Length];
            }
            return b;
        }
    }

    /// <summary>
    /// CryptoStream that disposes transformation object upon close.
    /// </summary>
    public class InputCryptoStream : CryptoStream
    {
        ICryptoTransform    m_transform;

        public InputCryptoStream (Stream input, ICryptoTransform transform)
            : base (input, transform, CryptoStreamMode.Read)
        {
            m_transform = transform;
        }

        protected override void Dispose (bool disposing)
        {
            base.Dispose (disposing);
            if (disposing && m_transform != null)
            {
                m_transform.Dispose();
                m_transform = null;
            }
        }
    }
}
