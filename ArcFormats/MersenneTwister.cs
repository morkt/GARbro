//! \file       MersenneTwister.cs
//! \date       Mon Nov 02 01:57:11 2015
//! \brief      Mersenne Twister pseudorandom number generator.
//
// Copyright (C) 1997 Makoto Matsumoto and Takuji Nishimura.
//
// C# port by morkt
//

namespace GameRes.Cryptography
{
    public class MersenneTwister
    {
        const uint  DefaultSeed     = 4357;

        const int   StateLength     = 624;
        const int   StateM          = 397;
        const uint  MatrixA         = 0x9908B0DF;
        const uint  SignMask        = 0x80000000;
        const uint  LowerMask       = 0x7FFFFFFF;
        const uint  TemperingMaskB  = 0x9D2C5680;
        const uint  TemperingMaskC  = 0xEFC60000;

        uint[]  mt = new uint[StateLength];
        int     mti = StateLength;

        public MersenneTwister (uint seed)
        {
            SRand (seed);
        }

        public void SRand (uint seed)
        {
            for (mti = 0; mti < mt.Length; ++mti)
            {
                uint upper = seed & 0xffff0000;
                seed = 69069 * seed + 1;
                mt[mti] = upper | (seed & 0xffff0000) >> 16;
                seed = 69069 * seed + 1;
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
                    y = (mt[kk] & SignMask) | (mt[kk+1] & LowerMask);
                    mt[kk] = mt[kk + StateM] ^ (y >> 1) ^ mag01[y & 1];
                }
                for (; kk < StateLength-1; kk++)
                {
                    y = (mt[kk] & SignMask) | (mt[kk+1] & LowerMask);
                    mt[kk] = mt[kk + StateM - StateLength] ^ (y >> 1) ^ mag01[y & 1];
                }
                y = (mt[StateLength-1] & SignMask) | (mt[0] & LowerMask);
                mt[StateLength-1] = mt[StateM-1] ^ (y >> 1) ^ mag01[y & 1];

                mti = 0;
            }
        
            y = mt[mti++];
            y ^= y >> 11;
            y ^= (y << 7)  & TemperingMaskB;
            y ^= (y << 15) & TemperingMaskC;
            y ^= y >> 18;

            return y; 
        }
    }
}
