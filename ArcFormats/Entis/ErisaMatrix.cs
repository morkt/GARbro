//! \file       ErisaMatrix.cs
//! \date       Fri Feb 26 01:12:26 2016
//! \brief      Erisa Library math methods.
//
// *****************************************************************************
//                         E R I S A - L i b r a r y
// ----------------------------------------------------------------------------
//         Copyright (C) 2000-2004 Leshade Entis. All rights reserved.
// *****************************************************************************/
//
// C# port by morkt
//

using System;
using System.Diagnostics;
using GameRes.Utility;

namespace GameRes.Formats.Entis
{
    internal static class Erisa
    {
        public const int MIN_DCT_DEGREE = 2;
        public const int MAX_DCT_DEGREE = 12;

        static readonly float ERI_rCosPI4  = (float)Math.Cos (Math.PI / 4);
        static readonly float ERI_r2CosPI4 = 2 * ERI_rCosPI4;
        static readonly float[] ERI_DCTofK2 = new float[2];

        static readonly float[][] ERI_pMatrixDCTofK = new float[MAX_DCT_DEGREE][]
        {
            null,
            ERI_DCTofK2,    // = cos( (2*i+1) / 8 )
            new float[4],   // = cos( (2*i+1) / 16 )
            new float[8],   // = cos( (2*i+1) / 32 )
            new float[16],  // = cos( (2*i+1) / 64 )
            new float[32],  // = cos( (2*i+1) / 128 )
            new float[64],  // = cos( (2*i+1) / 256 )
            new float[128], // = cos( (2*i+1) / 512 )
            new float[256], // = cos( (2*i+1) / 1024 )
            new float[512], // = cos( (2*i+1) / 2048 )
            new float[1024], // = cos( (2*i+1) / 4096 )
            new float[2048], // = cos( (2*i+1) / 8192 )
        };

        static Erisa ()
        {
            InitializeMatrix();
        }

        public static void InitializeMatrix ()
        {
            for (int i = 1; i < MAX_DCT_DEGREE; i++)
            {
                int     n = (1 << i);
                float[] pDCTofK = ERI_pMatrixDCTofK[i];
                double  nr = Math.PI / (4.0 * n);
                double  dr = nr + nr;
                double  ir = nr;
                for (int j = 0; j < n; j++)
                {
                    pDCTofK[j] = (float)Math.Cos (ir);
                    ir += dr;
                }
            }
        }

        public static void RoundR32ToWordArray (byte[] ptrDst, int dst, int nStep, float[] ptrSrc, int nCount)
        {
            nStep *= 2;
            for (int i = 0; i < nCount; i++)
            {
                int nValue = RoundR32ToInt (ptrSrc[i]);
                if (nValue <= -0x8000)
                {
                    LittleEndian.Pack ((short)-0x8000, ptrDst, dst);
                }
                else if (nValue >= 0x7FFF)
                {
                    LittleEndian.Pack ((short)0x7FFF, ptrDst, dst);
                }
                else
                {
                    LittleEndian.Pack ((short)nValue, ptrDst, dst);
                }
                dst += nStep;
            }
        }

        public static int RoundR32ToInt (float r)
        {
            if (r >= 0.0)
                return (int)Math.Floor (r + 0.5);
            else
                return (int)Math.Ceiling (r - 0.5);
        }

        public static EriSinCos[] CreateRevolveParameter (int nDegreeDCT)
        {
            int nDegreeNum = 1 << nDegreeDCT;
            int lc = 1;
            for (int n = nDegreeNum / 2; n >= 8; n /= 8)
            {
                ++lc;
            }
            EriSinCos[] ptrRevolve = new EriSinCos[lc*8];

            double k = Math.PI / (nDegreeNum * 2);
            int ptrNextRev = 0;
            int nStep = 2;
            do
            {
                for (int i = 0; i < 7; i++)
                {
                    double ws = 1.0;
                    double a = 0.0;
                    for (int j = 0; j < i; j++)
                    {
                        a += nStep;
                        ws = ws * ptrRevolve[ptrNextRev+j].rSin + ptrRevolve[ptrNextRev+j].rCos * Math.Cos (a * k);
                    }
                    double r = Math.Atan2 (ws, Math.Cos ((a + nStep) * k));
                    ptrRevolve[ptrNextRev+i].rSin = (float)Math.Sin (r);
                    ptrRevolve[ptrNextRev+i].rCos = (float)Math.Cos (r);
                }
                ptrNextRev += 7;
                nStep *= 8;
            }
            while (nStep < nDegreeNum);
            return ptrRevolve;
        }

        public static void OddGivensInverseMatrix (float[] ptrSrc, int src, EriSinCos[] ptrRevolve, int nDegreeDCT)
        {
            int nDegreeNum = 1 << nDegreeDCT;
            int index = 1;
            int nStep = 2;
            int lc = (nDegreeNum / 2) / 8;
            int resolve_idx = 0;
            for (;;)
            {
                resolve_idx += 7;
                index += nStep * 7;
                nStep *= 8;
                if (lc <= 8)
                    break;
                lc /= 8;
            }
            int k = index + nStep * (lc - 2);
            int j;
            float r1, r2;
            for (j = lc - 2; j >= 0; j--)
            {
                r1 = ptrSrc[src + k];
                r2 = ptrSrc[src + k + nStep];
                ptrSrc[src + k] = r1 * ptrRevolve[resolve_idx+j].rCos + r2 * ptrRevolve[resolve_idx+j].rSin;
                ptrSrc[src + k + nStep] = r2 * ptrRevolve[resolve_idx+j].rCos - r1 * ptrRevolve[resolve_idx+j].rSin;
                k -= nStep;
            }
            for (; lc <= (nDegreeNum / 2) / 8; lc *= 8)
            {
                resolve_idx -= 7;
                nStep /= 8;
                index -= nStep * 7;
                for (int i = 0; i < lc; i++)
                {
                    k = i * (nStep * 8) + index + nStep * 6;
                    for ( j = 6; j >= 0; j -- )
                    {
                        r1 = ptrSrc[src + k];
                        r2 = ptrSrc[src + k + nStep];
                        ptrSrc[src + k]         =
                            r1 * ptrRevolve[resolve_idx+j].rCos + r2 * ptrRevolve[resolve_idx+j].rSin;
                        ptrSrc[src + k + nStep] =
                            r2 * ptrRevolve[resolve_idx+j].rCos - r1 * ptrRevolve[resolve_idx+j].rSin;
                        k -= nStep;
                    }
                }
            }
        }

        public static void FastIPLOT (float[] ptrSrc, int src, int nDegreeDCT)
        {
            int nDegreeNum = 1 << nDegreeDCT;
            for (int i = 0; i < nDegreeNum; i += 2)
            {
                float r1 = ptrSrc[src + i];
                float r2 = ptrSrc[src + i + 1];
                ptrSrc[src + i]     = 0.5f * (r1 + r2);
                ptrSrc[src + i + 1] = 0.5f * (r1 - r2);
            }
        }

        public static void FastILOT (float[] ptrDst, float[] ptrSrc1, int src1, float[] ptrSrc2, int src2, int nDegreeDCT)
        {
            int nDegreeNum = 1 << nDegreeDCT;
            for (int i = 0; i < nDegreeNum; i += 2)
            {
                float r1 = ptrSrc1[src1 + i];
                float r2 = ptrSrc2[src2 + i + 1];
                ptrDst[i]     = r1 + r2;
                ptrDst[i + 1] = r1 - r2;
            }
        }

        public static void FastDCT (float[] ptrDst, int dst, int nDstInterval, float[] ptrSrc, int src, float[] ptrWorkBuf, int work, int nDegreeDCT)
        {
            Debug.Assert ((nDegreeDCT >= MIN_DCT_DEGREE) && (nDegreeDCT <= MAX_DCT_DEGREE));

            if (nDegreeDCT == MIN_DCT_DEGREE)
            {
                float[] r32Buf = new float[4];
                r32Buf[0] = ptrSrc[src] + ptrSrc[src+3];
                r32Buf[2] = ptrSrc[src] - ptrSrc[src+3];
                r32Buf[1] = ptrSrc[src+1] + ptrSrc[src+2];
                r32Buf[3] = ptrSrc[src+1] - ptrSrc[src+2];

                ptrDst[dst]                = 0.5f * (r32Buf[0] + r32Buf[1]);
                ptrDst[dst+nDstInterval * 2] = ERI_rCosPI4 * (r32Buf[0] - r32Buf[1]);

                r32Buf[2] = ERI_DCTofK2[0] * r32Buf[2];
                r32Buf[3] = ERI_DCTofK2[1] * r32Buf[3];

                r32Buf[0] =                 r32Buf[2] + r32Buf[3];
                r32Buf[1] = ERI_r2CosPI4 * (r32Buf[2] - r32Buf[3]);

                r32Buf[1] -= r32Buf[0];

                ptrDst[dst+nDstInterval]     = r32Buf[0];
                ptrDst[dst+nDstInterval * 3] = r32Buf[1];
            }
            else
            {
                uint i;
                uint nDegreeNum = 1u << nDegreeDCT;
                uint nHalfDegree = nDegreeNum >> 1;
                for (i = 0; i < nHalfDegree; i++)
                {
                    ptrWorkBuf[work+i] = ptrSrc[src+i] + ptrSrc[src + nDegreeNum - i - 1];
                    ptrWorkBuf[work+i + nHalfDegree] = ptrSrc[src+i] - ptrSrc[src + nDegreeNum - i - 1];
                }
                int nDstStep = nDstInterval << 1;
                FastDCT (ptrDst, dst, nDstStep, ptrWorkBuf, work, ptrSrc, src, nDegreeDCT - 1);

                float[] pDCTofK = ERI_pMatrixDCTofK[nDegreeDCT - 1];
                src = (int)(work+nHalfDegree); // ptrSrc = ptrWorkBuf + nHalfDegree;
                dst += nDstInterval;    // ptrDst += nDstInterval;

                for (i = 0; i < nHalfDegree; i++)
                {
                    ptrWorkBuf[src + i] *= pDCTofK[i];
                }

                FastDCT (ptrDst, dst, nDstStep, ptrWorkBuf, src, ptrWorkBuf, work, nDegreeDCT - 1);

                int ptrNext = dst; // within ptrDst;
                for (i = 0; i < nHalfDegree; i++)
                {
                    ptrDst[ptrNext] += ptrDst[ptrNext]; // *ptrNext += *ptrNext;
                    ptrNext += nDstStep;
                }
                ptrNext = dst;
                for (i = 1; i < nHalfDegree; i ++)
                {
                    ptrDst[ptrNext + nDstStep] -= ptrDst[ptrNext];
                    ptrNext += nDstStep;
                }
            }
        }

        public static void FastIDCT (float[] ptrDst, float[] srcBuf, int ptrSrc, int nSrcInterval, float[] ptrWorkBuf, int nDegreeDCT)
        {
            FastIDCT (ptrDst, 0, srcBuf, ptrSrc, nSrcInterval, ptrWorkBuf, nDegreeDCT);
        }

        public static void FastIDCT (float[] dstBuf, int ptrDst, float[] srcBuf, int ptrSrc, int nSrcInterval, float[] ptrWorkBuf, int nDegreeDCT)
        {
            Debug.Assert ((nDegreeDCT >= MIN_DCT_DEGREE) && (nDegreeDCT <= MAX_DCT_DEGREE));

            if (nDegreeDCT == MIN_DCT_DEGREE)
            {
                float[] r32Buf1 = new float[2];
                float[] r32Buf2 = new float[4];

                r32Buf1[0] = srcBuf[ptrSrc];
                r32Buf1[1] = ERI_rCosPI4 * srcBuf[ptrSrc + nSrcInterval * 2];

                r32Buf2[0] = r32Buf1[0] + r32Buf1[1];
                r32Buf2[1] = r32Buf1[0] - r32Buf1[1];

                r32Buf1[0] = ERI_DCTofK2[0] * srcBuf[ptrSrc + nSrcInterval];
                r32Buf1[1] = ERI_DCTofK2[1] * srcBuf[ptrSrc + nSrcInterval * 3];

                r32Buf2[2] =                 r32Buf1[0] + r32Buf1[1];
                r32Buf2[3] = ERI_r2CosPI4 * (r32Buf1[0] - r32Buf1[1]);

                r32Buf2[3] -= r32Buf2[2];

                dstBuf[ptrDst]   = r32Buf2[0] + r32Buf2[2];
                dstBuf[ptrDst+3] = r32Buf2[0] - r32Buf2[2];
                dstBuf[ptrDst+1] = r32Buf2[1] + r32Buf2[3];
                dstBuf[ptrDst+2] = r32Buf2[1] - r32Buf2[3];
            }
            else
            {
                uint nDegreeNum = 1u << nDegreeDCT;
                uint nHalfDegree = nDegreeNum >> 1;
                int nSrcStep = nSrcInterval << 1;
                FastIDCT (dstBuf, ptrDst, srcBuf, ptrSrc, nSrcStep, ptrWorkBuf, nDegreeDCT - 1);

                float[] pDCTofK = ERI_pMatrixDCTofK[nDegreeDCT - 1];
                int pOddDst = ptrDst + (int)nHalfDegree; // within dstBuf
                int ptrNext = ptrSrc + nSrcInterval; // within srcBuf

                uint i;
                for (i = 0; i < nHalfDegree; i++)
                {
                    ptrWorkBuf[i] = srcBuf[ptrNext] * pDCTofK[i];
                    ptrNext += nSrcStep;
                }

                FastDCT (dstBuf, pOddDst, 1, ptrWorkBuf, 0, ptrWorkBuf, (int)nHalfDegree, nDegreeDCT - 1);

                for (i = 0; i < nHalfDegree; i ++)
                {
                    dstBuf[pOddDst + i] += dstBuf[pOddDst + i];
                }

                for (i = 1; i < nHalfDegree; i++)
                {
                    dstBuf[pOddDst + i] -= dstBuf[pOddDst + i - 1];
                }
                float[] r32Buf = new float[4];
                uint nQuadDegree = nHalfDegree >> 1;
                for (i = 0; i < nQuadDegree; i++)
                {
                    r32Buf[0] = dstBuf[ptrDst+i] + dstBuf[nHalfDegree + i];
                    r32Buf[3] = dstBuf[ptrDst+i] - dstBuf[nHalfDegree + i];
                    r32Buf[1] = dstBuf[nHalfDegree - 1 - i] + dstBuf[ptrDst + nDegreeNum - 1 - i];
                    r32Buf[2] = dstBuf[nHalfDegree - 1 - i] - dstBuf[ptrDst + nDegreeNum - 1 - i];

                    dstBuf[ptrDst+i]            = r32Buf[0];
                    dstBuf[nHalfDegree - 1 - i] = r32Buf[1];
                    dstBuf[nHalfDegree + i]     = r32Buf[2];
                    dstBuf[ptrDst+nDegreeNum - 1 - i]  = r32Buf[3];
                }
            }
        }

        public static void Revolve2x2 (float[] buf1, int ptrBuf1, float[] buf2, int ptrBuf2, float rSin, float rCos, int nStep, int nCount)
        {
            for (int i = 0; i < nCount; i++)
            {
                float r1 = buf1[ptrBuf1];
                float r2 = buf2[ptrBuf2];

                buf1[ptrBuf1] = r1 * rCos - r2 * rSin;
                buf2[ptrBuf2] = r1 * rSin + r2 * rCos;

                ptrBuf1 += nStep;
                ptrBuf2 += nStep;
            }
        }

        public static void ConvertArraySByteToFloat (float[] ptrDst, sbyte[] ptrSrc, int src, int nCount)
        {
            for (int i = 0; i < nCount; ++i)
            {
                ptrDst[i] = ptrSrc[src+i];
            }
        }

        public static void VectorMultiply (float[] ptrDst, float[] ptrSrc, int src, int nCount)
        {
            for (int i = 0; i < nCount; ++i)
            {
                ptrDst[i] *= ptrSrc[src+i];
            }
        }

        public static void FastIDCT8x8 (float[] ptrDst)
        {
            var rWork = new float[8];
            var rTemp = new float[64];
            for (int i = 0; i < 8; ++i)
                FastIDCT (rTemp, i * 8, ptrDst, i, 8, rWork, 3);

            for (int i = 0; i < 8; ++i)
                FastIDCT (ptrDst, i * 8, rTemp, i, 8, rWork, 3);
        }

        static readonly EriSinCos[] escRev = new EriSinCos[3]
        {
            new EriSinCos { rSin = 0.734510f, rCos = 0.678598f },
            new EriSinCos { rSin = 0.887443f, rCos = 0.460917f },
            new EriSinCos { rSin = 0.970269f, rCos = 0.242030f }
        };

        public static void FastILOT8x8 (float[] ptrDst, float[] horz, int ptrHorzCur, float[] vert, int ptrVertCur)
        {
            var rWork = new float[8];
            var rTemp = new float[64];
            float s1, s2, r1, r2, r3;
            int i, j, k;

            for (i = 0; i < 8; i++)
            {
                for (j = 2, k = i + 40; j >= 0; j--, k -= 16)
                {
                    r1 = ptrDst[k];
                    r2 = ptrDst[k + 16];
                    ptrDst[k]      = r1 * escRev[j].rCos + r2 * escRev[j].rSin;
                    ptrDst[k + 16] = r2 * escRev[j].rCos - r1 * escRev[j].rSin;
                }
            }
            for (i = 0; i < 64; i += 16)
            {
                for (j = 0; j < 8; j++)
                {
                    k = i + j;
                    s1 = ptrDst[k];
                    s2 = ptrDst[k + 8];
                    r1 = 0.5f * (s1 + s2);
                    r2 = 0.5f * (s1 - s2);

                    r3 = vert[ptrVertCur+k];
                    vert[ptrVertCur+k]     = r1;
                    vert[ptrVertCur+k + 8] = r2;
                    ptrDst[k]     = r3 + r2;
                    ptrDst[k + 8] = r3 - r2;
                }
            }
            for (i = 0; i < 64; i += 8)
            {
                for (j = 2, k = i + 5; j >= 0; j--, k -= 2)
                {
                    r1 = ptrDst[k];
                    r2 = ptrDst[k + 2];
                    ptrDst[k]     = r1 * escRev[j].rCos + r2 * escRev[j].rSin;
                    ptrDst[k + 2] = r2 * escRev[j].rCos - r1 * escRev[j].rSin;
                }
                for ( j = 0; j < 8; j += 2 )
                {
                    k = i + j;
                    s1 = ptrDst[k];
                    s2 = ptrDst[k + 1];
                    r1 = 0.5f * (s1 + s2);
                    r2 = 0.5f * (s1 - s2);
                    r3 = horz[ptrHorzCur+k];
                    horz[ptrHorzCur+k]     = r1;
                    horz[ptrHorzCur+k + 1] = r2;
                    ptrDst[k]     = r3 + r2;
                    ptrDst[k + 1] = r3 - r2;
                }
            }
            for (i = 0; i < 8; i++)
                FastIDCT (rTemp, i * 8, ptrDst, i, 8, rWork, 3);

            for (i = 0; i < 8; i++)
                FastIDCT (ptrDst, i * 8, rTemp, i, 8, rWork, 3);
        }

        public static void ConvertArrayFloatToByte (sbyte[] ptrDst, float[] ptrSrc, int nCount)
        {
            for (int i = 0; i < nCount; i++)
            {
                int	n = RoundR32ToInt (ptrSrc[i]);
                if ((uint)n > 0xFF)
                {
                    n = (~n >> 31) & 0xFF;
                }
                ptrDst[i] = (sbyte)n;
            }
        }

        public static void ConvertArrayFloatToSByte (sbyte[] ptrDst, float[] ptrSrc, int nCount)
        {
            for (int i = 0; i < nCount; i++)
            {
                int	n = RoundR32ToInt (ptrSrc[i]);
                if (n < -0x80)
                    n = -0x80;
                else if (n > 0x7F)
                    n = 0x7F;
                ptrDst[i] = (sbyte)n;
            }
        }
    }
}
