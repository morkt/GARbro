//! \file       QlieMersenneTwister.cs
//! \date       Fri Nov 06 08:17:07 2015
//! \brief      Mersenne Twister random number generator modified for QLIE decryption.
//
// Copyright (C) 1997 Makoto Matsumoto and Takuji Nishimura.
//
// C# port by morkt
//

using System;
namespace GameRes.Formats.Qlie
{
    /// <summary>
    /// Mersenne Twister random number generator modified for QLIE decryption.
    /// <seealso cref="GameRes.Utility.MersenneTwister"/>
    /// </summary>
    internal class QlieMersenneTwister
    {
        const uint  DefaultSeed     = 5489;

        const int   StateLength     = 64;
        const int   StateM          = 39;
        const uint  MatrixA         = 0x9908B0DF;
        const uint  SignMask        = 0x80000000;
        const uint  LowerMask       = 0x7FFFFFFF;
        const uint  TemperingMaskB  = 0x9C4F88E3;
        const uint  TemperingMaskC  = 0xE7F70000;

        uint[]  mt = new uint[StateLength];
        int     mti = StateLength;

        public QlieMersenneTwister (uint seed)
        {
            SRand (seed);
        }

        public void SRand (uint seed)
        {
            mt[0] = seed;
            for (mti = 1; mti < mt.Length; ++mti)
            {
                mt[mti] = (0x6611BC19u * (mt[mti-1] ^ (mt[mti-1] >> 30)) + (uint)mti); 
            }
        }

        public void XorState (byte[] hash)
        {
            int length = Math.Min (hash.Length / 4, StateLength);
            if (0 == length)
                return;
            unsafe
            {
                fixed (byte* hash_fixed = hash)
                {
                    uint* hash32 = (uint*)hash_fixed;
                    for (int i = 0; i < length; ++i)
                        mt[i] ^= hash32[i];
                }
            }
        }

        uint[] mag01 = { 0, MatrixA };

        public uint Rand ()
        {
            uint y;

            if (mti >= StateLength)
            {
                int kk;
                for (kk = 0; kk < StateLength - StateM; kk++)
                {
                    y = (mt[kk] & SignMask) | (mt[kk+1] & LowerMask) >> 1;
                    mt[kk] = mt[kk + StateM] ^ y ^ mag01[mt[kk+1] & 1];
                }
                for (; kk < StateLength-1; kk++)
                {
                    y = (mt[kk] & SignMask) | (mt[kk+1] & LowerMask) >> 1;
                    mt[kk] = mt[kk + StateM - StateLength] ^ y ^ mag01[mt[kk+1] & 1];
                }
                y = (mt[StateLength-1] & SignMask) | (mt[0] & LowerMask) >> 1;
                mt[StateLength-1] = mt[StateM-1] ^ y ^ mag01[mt[kk-1] & 1];

                mti = 0;
            }
        
            y = mt[mti++];
            y ^= y >> 11;
            y ^= (y << 7)  & TemperingMaskB;
            y ^= (y << 15) & TemperingMaskC;
            y ^= y >> 18;

            return y; 
        }

        public ulong Rand64 ()
        {
            // unlike C/C++, in C# order of the function calls in sub-expressions is well-defined
            // (left-to-right), but it still feels safer to split expressions with side effects.
            ulong v = Rand();
            return v | (ulong)Rand() << 32;
        }
    }
}
