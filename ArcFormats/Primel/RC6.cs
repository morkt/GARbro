//! \file       RC6.cs
//! \date       Mon Oct 03 14:47:13 2016
//! \brief      RC6 encryption implementation.
//

using System;
using System.Linq;
using System.Security.Cryptography;
using GameRes.Utility;

namespace GameRes.Cryptography
{
    public sealed class RC6 : ICryptoTransform
    {
        internal const int BlockSize = 16;
        internal const int DefaultRounds = 20;

        public bool CanTransformMultipleBlocks { get { return true; } }
        public bool          CanReuseTransform { get { return false; } }
        public int              InputBlockSize { get { return BlockSize; } }
        public int             OutputBlockSize { get { return BlockSize; } }

        uint[]      m_state;
        byte[]      m_iv;
	    const uint  P = 0xB7E15163;
	    const uint  Q = 0x9E3779B9;

        public RC6 (byte[] key, byte[] iv)
        {
            m_state = new uint[2 * (DefaultRounds + 2)];
            int key_length = Math.Max ((key.Length + 3) / 4, 1);
            var key_copy = new uint[key_length];
            Buffer.BlockCopy (key, 0, key_copy, 0, key.Length);

            m_state[0] = P;
            for (int i = 1; i < m_state.Length; ++i)
                m_state[i] = m_state[i-1] + Q;

            uint a = 0, b = 0;
            int n = 3 * Math.Max (m_state.Length, key_length);
            for (int h = 0; h < n; ++h)
            {
                a = m_state[h % m_state.Length] = Binary.RotL (m_state[h % m_state.Length] + a + b, 3);
                b = key_copy[h % key_length] = Binary.RotL ((key_copy[h % key_length] + a + b), (int)(a + b));
            }

            m_iv = new byte[BlockSize];
            if (iv != null)
                Buffer.BlockCopy (iv, 0, m_iv, 0, Math.Min (iv.Length, BlockSize));
        }

        public int TransformBlock (byte[] inBuffer, int offset, int count, byte[] outBuffer, int outOffset)
        {
            int out_count = count / BlockSize;
            for (int i = 0; i < out_count; ++i)
            {
                // CFB mode
                Encrypt (m_iv, 0, outBuffer, outOffset);
                for (int j = 0; j < BlockSize; ++j)
                {
                    byte b = inBuffer[offset++];
                    outBuffer[outOffset++] ^= b;
                    m_iv[j] = b;
                }
            }
            return out_count * BlockSize;
        }

        public byte[] TransformFinalBlock (byte[] inBuffer, int offset, int count)
        {
            if (count < BlockSize)
                return new ArraySegment<byte> (inBuffer, offset, count).ToArray();
            var output = new byte[count];
            int tail = count / BlockSize * BlockSize;
            count -= TransformBlock (inBuffer, offset, count, output, 0);
            if (count > 0)
                Buffer.BlockCopy (inBuffer, offset+tail, output, tail, count);
            return output;
        }

        private void Encrypt (byte[] inBuffer, int offset, byte[] outBuffer, int outOffset)
        {
            uint a = LittleEndian.ToUInt32 (inBuffer, offset);
            uint b = LittleEndian.ToUInt32 (inBuffer, offset+4);
            uint c = LittleEndian.ToUInt32 (inBuffer, offset+8);
            uint d = LittleEndian.ToUInt32 (inBuffer, offset+12);

            b += m_state[0];
            d += m_state[1];
            int sptr = 2;

            for (int i = 0; i < DefaultRounds; ++i)
            {
                uint t, u;
                t = Binary.RotL (b * (2 * b + 1), 5);
                u = Binary.RotL (d * (2 * d + 1), 5);
                a = Binary.RotL (a ^ t, (int)u) + m_state[sptr++];
                c = Binary.RotL (c ^ u, (int)t) + m_state[sptr++];
                t = a;
                a = b;
                b = c;
                c = d;
                d = t;
            }
            a += m_state[sptr];
            c += m_state[sptr+1];

            LittleEndian.Pack (a, outBuffer, outOffset);
            LittleEndian.Pack (b, outBuffer, outOffset+4);
            LittleEndian.Pack (c, outBuffer, outOffset+8);
            LittleEndian.Pack (d, outBuffer, outOffset+12);
        }

        private void Decrypt (byte[] inBuffer, int offset, byte[] outBuffer, int outOffset)
        {
            uint a = LittleEndian.ToUInt32 (inBuffer, offset);
            uint b = LittleEndian.ToUInt32 (inBuffer, offset+4);
            uint c = LittleEndian.ToUInt32 (inBuffer, offset+8);
            uint d = LittleEndian.ToUInt32 (inBuffer, offset+12);

            int sptr = m_state.Length - 2;
            c -= m_state[sptr+1];
            a -= m_state[sptr];

            for (int i = 0; i < DefaultRounds; ++i)
            {
                uint t, u;
                sptr -= 2;
                t = a;
                a = d;
                d = c;
                c = b;
                b = t;
                u = Binary.RotL (d*(2*d+1), 5);
                t = Binary.RotL (b*(2*b+1), 5);
                c = Binary.RotR (c-m_state[sptr+1], (int)t) ^ u;
                a = Binary.RotR (a-m_state[sptr  ], (int)u) ^ t;
            }
            d -= m_state[1];
            b -= m_state[0];

            LittleEndian.Pack (a, outBuffer, outOffset);
            LittleEndian.Pack (b, outBuffer, outOffset+4);
            LittleEndian.Pack (c, outBuffer, outOffset+8);
            LittleEndian.Pack (d, outBuffer, outOffset+12);
        }

        #region IDisposable implementation
        public void Dispose ()
        {
            GC.SuppressFinalize (this);
        }
        #endregion
    }
}
