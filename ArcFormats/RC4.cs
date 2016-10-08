//! \file       RC4.cs
//! \date       Sat Oct 08 00:38:00 2016
//! \brief      RC4 encryption algorithm implementation.
//

using System.Security.Cryptography;

namespace GameRes.Cryptography
{
    public sealed class Rc4Transform : ICryptoTransform
    {
        private const int StateLength = 256;
        private const int BlockSize = 1;
        private byte[]	m_state;
        private int		m_x;
        private int		m_y;

        public bool          CanReuseTransform { get { return false; } }
        public bool CanTransformMultipleBlocks { get { return true; } }
        public int              InputBlockSize { get { return BlockSize; } }
        public int             OutputBlockSize { get { return BlockSize; } }

        public byte[]                    State { get { return m_state; } }

        public Rc4Transform (byte[] key)
        {
            m_x = 0;
            m_y = 0;

            m_state = new byte[StateLength];
            for (int i = 0; i < StateLength; ++i)
            {
                m_state[i] = (byte)i;
            }

            int s = 0;
            for (int i = 0; i < StateLength; ++i)
            {
                s = (key[i % key.Length] + m_state[i] + s) & 0xFF;
                byte t = m_state[i];
                m_state[i] = m_state[s];
                m_state[s] = t;
            }
        }

        public byte NextByte ()
        {
            m_x = (m_x + 1) & 0xFF;
            byte a = m_state[m_x];
            m_y = (m_y + a) & 0xFF;
            byte b = m_state[m_y];
            m_state[m_x] = b;
            m_state[m_y] = a;
            return m_state[(a + b) & 0xFF];
        }

        public byte[] GenerateBlock (int length)
        {
            var block = new byte[length];
            for (int i = 0; i < block.Length; ++i)
                block[i] = NextByte();
            return block;
        }

        public int TransformBlock (byte[] inBuf, int inOffset, int inCount, byte[] outBuf, int outOffset)
        {
            for (int i = 0; i < inCount; i++)
            {
                outBuf[i+outOffset] = (byte)(inBuf[i + inOffset] ^ NextByte());
            }
            return inCount;
        }

        public byte[] TransformFinalBlock (byte[] inBuf, int inOffset, int inCount)
        {
            byte[] output = new byte[inCount];
            TransformBlock (inBuf, inOffset, inCount, output, 0);
            return output;
        }

        public void Dispose ()
        {
            System.GC.SuppressFinalize (this);
        }
    }
}
