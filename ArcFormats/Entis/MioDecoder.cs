// *****************************************************************************
//                         E R I S A - L i b r a r y
// -----------------------------------------------------------------------------
//    Copyright (C) 2002-2007 Leshade Entis, Entis-soft. All rights reserved.
// *****************************************************************************
//
// C# port by morkt
//

using System;
using System.Diagnostics;
using GameRes.Utility;

namespace GameRes.Formats.Entis
{
    internal class MioInfoHeader
    {
        public int      Version;
        public CvType   Transformation;
        public EriCode  Architecture;
        public int      ChannelCount;
        public uint     SamplesPerSec;
        public uint     BlocksetCount;
        public int      SubbandDegree;
        public uint     AllSampleCount;
        public uint     LappedDegree;
        public uint     BitsPerSample;
    }

    internal class MioDataHeader
    {
        public byte Version;
        public byte Flags;
        public uint SampleCount;
    }

    internal struct EriSinCos
    {
        public float rSin;
        public float rCos;
    }

    internal class MioDecoder
    {
        MioInfoHeader       m_mioih;

        uint                m_nBufLength = 0;
        int[]               m_ptrBuffer1;
        int[]               m_ptrBuffer2;
        sbyte[]             m_ptrBuffer3;
        byte[]              m_ptrBuffer4;
        byte[]              m_ptrDivisionTable;
        byte[]              m_ptrRevolveCode;
        int[]               m_ptrWeightCode;
        int[]               m_ptrCoefficient;

        float[]             m_ptrMatrixBuf;
        float[]             m_ptrInternalBuf;
        float[]             m_ptrWorkBuf;
        float[]             m_ptrWeightTable;
        float[]             m_ptrLastDCT;

        int                 m_ptrNextDivision;
        int                 m_ptrNextRevCode;
        int                 m_ptrNextWeight;
        int                 m_ptrNextCoefficient;
        int                 m_ptrNextSource;
        int                 m_ptrLastDCTBuf;
        int                 m_nSubbandDegree;
        int                 m_nDegreeNum;
        EriSinCos[]         m_pRevolveParam;
        readonly int[]      m_nFrequencyPoint = new int[7];

        public MioDecoder (MioInfoHeader info)
        {
            m_nBufLength = 0;
            m_mioih = info;

            if (!Initialize())
                throw new InvalidFormatException();
        }

        bool Initialize ()
        {
            if ((m_mioih.ChannelCount != 1) && (m_mioih.ChannelCount != 2))
            {
                return false;
            }

            if (m_mioih.Transformation == CvType.Lossless_ERI)
            {
                if (m_mioih.Architecture != EriCode.RunlengthHuffman)
                {
                    return false;
                }
                if ((m_mioih.BitsPerSample != 8) && (m_mioih.BitsPerSample != 16))
                {
                    return false;
                }
            }
            else if ((m_mioih.Transformation == CvType.LOT_ERI)
                    || (m_mioih.Transformation == CvType.LOT_ERI_MSS))
            {
                if ((m_mioih.Architecture != EriCode.RunlengthGamma)
                    && (m_mioih.Architecture != EriCode.RunlengthHuffman)
                    && (m_mioih.Architecture != EriCode.Nemesis))
                {
                    return false;
                }
                if (m_mioih.BitsPerSample != 16)
                {
                    return false;
                }
                if ((m_mioih.SubbandDegree < 8) || (m_mioih.SubbandDegree > Erisa.MAX_DCT_DEGREE))
                {
                    return false;
                }
                if (m_mioih.LappedDegree != 1)
                {
                    return false;
                }
                int subband = (sizeof(float) << m_mioih.SubbandDegree) / sizeof(float);
                int block_size = m_mioih.ChannelCount * subband;
                m_ptrBuffer1 = new int[block_size];
                m_ptrMatrixBuf = new float[block_size];
                m_ptrInternalBuf = new float[block_size];
                m_ptrWorkBuf = new float[subband];

                m_ptrWeightTable = new float[subband];

                uint nBlocksetSamples = (uint)(m_mioih.ChannelCount << m_mioih.SubbandDegree);
                uint nLappedSamples = nBlocksetSamples * m_mioih.LappedDegree;
                if (nLappedSamples > 0)
                {
                    m_ptrLastDCT = new float[nLappedSamples];
                }
                InitializeWithDegree (m_mioih.SubbandDegree);
            }
            else
            {
                return false;
            }
            return true;
        }

        public bool DecodeSound (ERISADecodeContext context, MioDataHeader datahdr, byte[] ptrWaveBuf, int wave_pos)
        {
            context.FlushBuffer();

            if (m_mioih.Transformation == CvType.Lossless_ERI)
            {
                if (m_mioih.BitsPerSample == 8)
                {
                    return DecodeSoundPCM8 (context, datahdr, ptrWaveBuf, wave_pos);
                }
                else if (m_mioih.BitsPerSample == 16)
                {
                    return DecodeSoundPCM16 (context, datahdr, ptrWaveBuf, wave_pos);
                }
            }
            else if ((m_mioih.Transformation == CvType.LOT_ERI)
                     || (m_mioih.Transformation == CvType.LOT_ERI_MSS))
            {
                if ((m_mioih.ChannelCount != 2) || (m_mioih.Transformation == CvType.LOT_ERI))
                {
                    return DecodeSoundDCT (context, datahdr, ptrWaveBuf, wave_pos);
                }
                else
                {
                    return DecodeSoundDCT_MSS (context, datahdr, ptrWaveBuf, wave_pos);
                }
            }
            return false;
        }

        bool DecodeSoundPCM8 (ERISADecodeContext context, MioDataHeader datahdr, byte[] ptrWaveBuf, int wave_pos)
        {
            uint nSampleCount = datahdr.SampleCount;
            if (nSampleCount > m_nBufLength)
            {
                m_ptrBuffer3 = new sbyte [nSampleCount * m_mioih.ChannelCount];
                m_nBufLength = nSampleCount;
            }
            if (0 != (datahdr.Flags & MIO_LEAD_BLOCK))
            {
                (context as HuffmanDecodeContext).PrepareToDecodeERINACode();
            }
            uint nBytes = nSampleCount * (uint)m_mioih.ChannelCount;
            if (context.DecodeBytes (m_ptrBuffer3, nBytes) < nBytes)
            {
                return false;
            }
            int ptrSrcBuf = 0; // (PBYTE) m_ptrBuffer3;
            int nStep = m_mioih.ChannelCount;
            for (int i = 0; i < m_mioih.ChannelCount; i ++ )
            {
                int ptrDstBuf = wave_pos + i;
                sbyte bytValue = 0;
                for (uint j = 0; j < nSampleCount; j++)
                {
                    bytValue += m_ptrBuffer3[ptrSrcBuf++];
                    ptrWaveBuf[ptrDstBuf] = (byte)bytValue;
                    ptrDstBuf += nStep;
                }
            }
            return true;
        }

        bool DecodeSoundPCM16 (ERISADecodeContext context, MioDataHeader datahdr, byte[] ptrWaveBuf, int wave_pos)
        {
            uint nSampleCount = datahdr.SampleCount;
            uint nChannelCount = (uint)m_mioih.ChannelCount;
            uint nAllSampleCount = nSampleCount * nChannelCount;
            uint nBytes = nAllSampleCount * sizeof(short);

            if (ptrWaveBuf.Length < wave_pos + (int)nBytes)
                return false;

            if (nSampleCount > m_nBufLength)
            {
                m_ptrBuffer3 = new sbyte[nBytes];
                m_ptrBuffer4 = new byte[nBytes];
                m_nBufLength = nSampleCount;
            }
            if (0 != (datahdr.Flags & MIO_LEAD_BLOCK))
            {
                (context as HuffmanDecodeContext).PrepareToDecodeERINACode();
            }
            if (context.DecodeBytes (m_ptrBuffer3, nBytes) < nBytes)
            {
                return false;
            }
            int pbytSrcBuf1, pbytSrcBuf2, pbytDstBuf;
            for (int i = 0; i < m_mioih.ChannelCount; i++)
            {
                int nOffset = i * (int)nSampleCount * sizeof(short);
                pbytSrcBuf1 = nOffset; // ((PBYTE) m_ptrBuffer3) + nOffset;
                pbytSrcBuf2 = pbytSrcBuf1 + (int)nSampleCount; // pbytSrcBuf1 + nSampleCount;
                pbytDstBuf = nOffset; // ((PBYTE) m_ptrBuffer4) + nOffset;

                for (uint j = 0; j < nSampleCount; j ++)
                {
                    sbyte bytLow  = m_ptrBuffer3[pbytSrcBuf2 + j];
                    sbyte bytHigh = m_ptrBuffer3[pbytSrcBuf1 + j];
                    m_ptrBuffer4[pbytDstBuf + j * sizeof(short) + 0] = (byte)bytLow;
                    m_ptrBuffer4[pbytDstBuf + j * sizeof(short) + 1] = (byte)(bytHigh ^ (bytLow >> 7));
                }
            }
            if (m_ptrBuffer4.Length < nBytes)
                return false;
            unsafe
            {
                fixed (byte* rawBuffer4 = m_ptrBuffer4, rawWaveBuf = &ptrWaveBuf[wave_pos])
                {
                    int nStep = m_mioih.ChannelCount;
                    short* ptrSrcBuf = (short*)rawBuffer4;
                    for (int i = 0; i < m_mioih.ChannelCount; i++)
                    {
                        short* ptrDstBuf = (short*)rawWaveBuf + i; // (SWORD*) ptrWaveBuf;
                        short wValue = 0;
                        short wDelta = 0;
                        for (uint j = 0; j < nSampleCount; j++)
                        {
                            wDelta += *ptrSrcBuf++;
                            wValue += wDelta;
                            *ptrDstBuf = wValue;
                            ptrDstBuf += nStep;
                        }
                    }
                }
            }
            return true;
        }

        static readonly int[] FreqWidth = new int[7] { -6, -6, -5, -4, -3, -2, -1 };

        void InitializeWithDegree (int nSubbandDegree)
        {
            m_pRevolveParam = Erisa.CreateRevolveParameter (nSubbandDegree);
            for (int i = 0, j = 0; i < 7; i ++)
            {
                int nFrequencyWidth = 1 << (nSubbandDegree + FreqWidth[i]);
                m_nFrequencyPoint[i] = j + (nFrequencyWidth / 2);
                j += nFrequencyWidth;
            }
            m_nSubbandDegree = nSubbandDegree;
            m_nDegreeNum = 1 << nSubbandDegree;
        }

        const uint MIO_LEAD_BLOCK = 0x01;

        bool DecodeSoundDCT (ERISADecodeContext context, MioDataHeader datahdr, byte[] ptrWaveBuf, int wave_pos)
        {
            uint i, j, k;
            uint nDegreeWidth = 1u << m_mioih.SubbandDegree;
            uint nSampleCount = (datahdr.SampleCount + nDegreeWidth - 1) & ~(nDegreeWidth - 1);
            uint nSubbandCount = (nSampleCount >> m_mioih.SubbandDegree);
            uint nChannelCount = (uint)m_mioih.ChannelCount;
            uint nAllSampleCount = nSampleCount * nChannelCount;
            uint nAllSubbandCount = nSubbandCount * nChannelCount;

            if (nSampleCount > m_nBufLength)
            {
                m_ptrBuffer2 = new int[nAllSampleCount];
                m_ptrBuffer3 = new sbyte[nAllSampleCount * sizeof(short)];
                m_ptrDivisionTable = new byte[nAllSubbandCount];
                m_ptrWeightCode = new int[nAllSubbandCount * 5];
                m_ptrCoefficient = new int[nAllSubbandCount * 5];
                m_nBufLength = nSampleCount;
            }
            if (context.GetABit() != 0)
            {
                return  false;
            }
            int[] pLastDivision = new int [nChannelCount];
            m_ptrNextDivision = 0; // within m_ptrDivisionTable;
            m_ptrNextWeight = 0; // within m_ptrWeightCode;
            m_ptrNextCoefficient = 0; // within m_ptrCoefficient;

            for (i = 0; i < nChannelCount; i++)
            {
                pLastDivision[i] = -1;
            }
            for (i = 0; i < nSubbandCount; i++)
            {
                for (j = 0; j < nChannelCount; j++)
                {
                    int nDivisionCode = (int)context.GetNBits(2);
                    m_ptrDivisionTable[m_ptrNextDivision++] = (byte)nDivisionCode;

                    if (nDivisionCode != pLastDivision[j])
                    {
                        if (i != 0)
                        {
                            m_ptrWeightCode[m_ptrNextWeight++] = (int)context.GetNBits (32);
                            m_ptrCoefficient[m_ptrNextCoefficient++] = (int)context.GetNBits (16);
                        }
                        pLastDivision[j] = nDivisionCode;
                    }

                    uint nDivisionCount = 1u << nDivisionCode;
                    for (k = 0; k < nDivisionCount; k ++)
                    {
                        m_ptrWeightCode[m_ptrNextWeight++] = (int)context.GetNBits (32);
                        m_ptrCoefficient[m_ptrNextCoefficient++] = (int)context.GetNBits (16);
                    }
                }
            }
            if (nSubbandCount > 0)
            {
                for (i = 0; i < nChannelCount; i++)
                {
                    m_ptrWeightCode[m_ptrNextWeight++] = (int)context.GetNBits (32);
                    m_ptrCoefficient[m_ptrNextCoefficient++] = (int)context.GetNBits (16);
                }
            }

            if (context.GetABit() != 0)
            {
                return  false;
            }
            if (0 != (datahdr.Flags & MIO_LEAD_BLOCK))
            {
                if (m_mioih.Architecture != EriCode.Nemesis)
                {
                    (context as HuffmanDecodeContext).PrepareToDecodeERINACode();
                }
                else
                {
                    throw new NotImplementedException ("Nemesis encoding not implemented");
//                    context.PrepareToDecodeERISACode();
                }
            }
            else if (m_mioih.Architecture == EriCode.Nemesis)
            {
                throw new NotImplementedException ("Nemesis encoding not implemented");
//                context.InitializeERISACode();
            }
            if (m_mioih.Architecture != EriCode.Nemesis)
            {
                if (context.DecodeBytes (m_ptrBuffer3, nAllSampleCount * 2 ) < nAllSampleCount * 2)
                {
                    return false;
                }
                int ptrHBuf = 0; // within m_ptrBuffer3;
                int ptrLBuf = (int)nAllSampleCount; // within m_ptrBuffer3

                for (i = 0; i < nDegreeWidth; i++)
                {
                    int ptrQuantumized = (int)i; // within (PINT) m_ptrBuffer2
                    for (j = 0; j < nAllSubbandCount; j++)
                    {
                        int nLow  = m_ptrBuffer3[ptrLBuf++];
                        int nHigh = m_ptrBuffer3[ptrHBuf++] ^ (nLow >> 8);
                        m_ptrBuffer2[ptrQuantumized] = (nLow & 0xFF) | (nHigh << 8);
                        ptrQuantumized += (int)nDegreeWidth;
                    }
                }
            }
            else
            {
                throw new NotImplementedException ("Nemesis encoding not implemented");
                /*
                if (context.DecodeERISACodeWords (m_ptrBuffer3, nAllSampleCount) < nAllSampleCount)
                {
                    return false;
                }
                for (i = 0; i < nAllSampleCount; i++)
                {
                    ((PINT)m_ptrBuffer2)[i] = ((SWORD*)m_ptrBuffer3)[i];
                }
                */
            }
            uint nSamples;
            uint[] pRestSamples = new uint [nChannelCount];
            int[] ptrDstBuf = new int [nChannelCount]; // indices within ptrWaveBuf

            m_ptrNextDivision = 0; // within m_ptrDivisionTable;
            m_ptrNextWeight = 0; // within m_ptrWeightCode;
            m_ptrNextCoefficient = 0; // within m_ptrCoefficient;
            m_ptrNextSource = 0; // within (PINT) m_ptrBuffer2;

            for (i = 0; i < nChannelCount; i++)
            {
                pLastDivision[i] = -1;
                pRestSamples[i] = datahdr.SampleCount;
                ptrDstBuf[i] = wave_pos + (int)i*sizeof(short);
            }
            int nCurrentDivision = -1;

            for (i = 0; i < nSubbandCount; i++)
            {
                for (j = 0; j < nChannelCount; j++)
                {
                    int nDivisionCode = m_ptrDivisionTable[m_ptrNextDivision++];
                    int nDivisionCount = 1 << nDivisionCode;
                    int nChannelStep = (int)(nDegreeWidth * m_mioih.LappedDegree * j);
                    m_ptrLastDCTBuf = nChannelStep; // within m_ptrLastDCT

                    bool fLeadBlock = false;
                    if (pLastDivision[j] != nDivisionCode)
                    {
                        if (i != 0)
                        {
                            if (nCurrentDivision != pLastDivision[j])
                            {
                                InitializeWithDegree (m_mioih.SubbandDegree - pLastDivision[j]);
                                nCurrentDivision = pLastDivision[j];
                            }
                            nSamples = pRestSamples[j];
                            if (nSamples > m_nDegreeNum)
                            {
                                nSamples = (uint)m_nDegreeNum;
                            }
                            DecodePostBlock (ptrWaveBuf, ptrDstBuf[j], nSamples);
                            pRestSamples[j] -= nSamples;
                            ptrDstBuf[j] += (int)(nSamples * nChannelCount * sizeof(short));
                        }
                        pLastDivision[j] = (int)nDivisionCode;
                        fLeadBlock = true;
                    }
                    if (nCurrentDivision != nDivisionCode)
                    {
                        InitializeWithDegree (m_mioih.SubbandDegree - nDivisionCode);
                        nCurrentDivision = nDivisionCode;
                    }
                    for (k = 0; k < nDivisionCount; k++)
                    {
                        if (fLeadBlock)
                        {
                            DecodeLeadBlock();
                            fLeadBlock = false;
                        }
                        else
                        {
                            nSamples = pRestSamples[j];
                            if (nSamples > m_nDegreeNum)
                            {
                                nSamples = (uint)m_nDegreeNum;
                            }
                            DecodeInternalBlock (ptrWaveBuf, ptrDstBuf[j], nSamples);
                            pRestSamples[j] -= nSamples;
                            ptrDstBuf[j] += (int)(nSamples * nChannelCount * sizeof(short));
                        }
                    }
                }
            }
            if (nSubbandCount > 0)
            {
                for (i = 0; i < nChannelCount; i ++)
                {
                    int nChannelStep = (int)(nDegreeWidth * m_mioih.LappedDegree * i);
                    m_ptrLastDCTBuf = nChannelStep; // within m_ptrLastDCT

                    if (nCurrentDivision != pLastDivision[i])
                    {
                        InitializeWithDegree (m_mioih.SubbandDegree - pLastDivision[i]);
                        nCurrentDivision = pLastDivision[i];
                    }
                    nSamples = pRestSamples[i];
                    if (nSamples > m_nDegreeNum)
                    {
                        nSamples = (uint)m_nDegreeNum;
                    }
                    DecodePostBlock (ptrWaveBuf, ptrDstBuf[i], nSamples);
                    pRestSamples[i] -= nSamples;
                    ptrDstBuf[i] += (int)(nSamples * nChannelCount * sizeof(short));
                }
            }
            return true;
        }

        void DecodeInternalBlock (byte[] ptrDst, int iDst, uint nSamples)
        {
            int nWeightCode = m_ptrWeightCode[m_ptrNextWeight++];
            int nCoefficient = m_ptrCoefficient[m_ptrNextCoefficient++];
            IQuantumize (m_ptrMatrixBuf, 0, m_ptrBuffer2, m_ptrNextSource, m_nDegreeNum, nWeightCode, nCoefficient);
            m_ptrNextSource += (int)m_nDegreeNum;

            Erisa.OddGivensInverseMatrix (m_ptrMatrixBuf, 0, m_pRevolveParam, m_nSubbandDegree);
            Erisa.FastIPLOT (m_ptrMatrixBuf, 0, m_nSubbandDegree);
            Erisa.FastILOT (m_ptrWorkBuf, m_ptrLastDCT, m_ptrLastDCTBuf, m_ptrMatrixBuf, 0, m_nSubbandDegree);

            Array.Copy (m_ptrMatrixBuf, 0, m_ptrLastDCT,   m_ptrLastDCTBuf, m_nDegreeNum);
            Array.Copy (m_ptrWorkBuf,   0, m_ptrMatrixBuf, 0,               m_nDegreeNum);

            Erisa.FastIDCT (m_ptrInternalBuf, m_ptrMatrixBuf, 0, 1, m_ptrWorkBuf, m_nSubbandDegree);
            if (nSamples != 0)
            {
                Erisa.RoundR32ToWordArray (ptrDst, iDst, m_mioih.ChannelCount, m_ptrInternalBuf, (int)nSamples);
            }
        }

        void DecodeLeadBlock ()
        {
            int nWeightCode = m_ptrWeightCode[m_ptrNextWeight++];
            int nCoefficient = m_ptrCoefficient[m_ptrNextCoefficient++];
            uint i;
            uint nHalfDegree = (uint)m_nDegreeNum / 2;
            for (i = 0; i < nHalfDegree; i++)
            {
                m_ptrBuffer1[i * 2]   = 0;
                m_ptrBuffer1[i * 2 + 1] = m_ptrBuffer2[m_ptrNextSource++];
            }
            IQuantumize (m_ptrLastDCT, m_ptrLastDCTBuf, m_ptrBuffer1, 0, m_nDegreeNum, nWeightCode, nCoefficient);
            Erisa.OddGivensInverseMatrix (m_ptrLastDCT, m_ptrLastDCTBuf, m_pRevolveParam, m_nSubbandDegree);
            for (i = 0; i < m_nDegreeNum; i += 2)
            {
                m_ptrLastDCT[m_ptrLastDCTBuf + i] = m_ptrLastDCT[m_ptrLastDCTBuf + i + 1];
            }
            Erisa.FastIPLOT (m_ptrLastDCT, m_ptrLastDCTBuf, m_nSubbandDegree);
        }

        void DecodePostBlock (byte[] ptrDst, int iDst, uint nSamples)
        {
            int nWeightCode = m_ptrWeightCode[m_ptrNextWeight++];
            int nCoefficient = m_ptrCoefficient[m_ptrNextCoefficient++];
            uint i;
            uint nHalfDegree = (uint)m_nDegreeNum / 2;
            for (i = 0; i < nHalfDegree; i++)
            {
                m_ptrBuffer1[i * 2] = 0;
                m_ptrBuffer1[i * 2 + 1] = m_ptrBuffer2[m_ptrNextSource++];
            }
            IQuantumize (m_ptrMatrixBuf, 0, m_ptrBuffer1, 0, m_nDegreeNum, nWeightCode, nCoefficient);
            Erisa.OddGivensInverseMatrix (m_ptrMatrixBuf, 0, m_pRevolveParam, m_nSubbandDegree);

            for (i = 0; i < m_nDegreeNum; i += 2)
            {
                m_ptrMatrixBuf[i] = - m_ptrMatrixBuf[i + 1];
            }

            Erisa.FastIPLOT (m_ptrMatrixBuf, 0, m_nSubbandDegree);
            Erisa.FastILOT (m_ptrWorkBuf, m_ptrLastDCT, m_ptrLastDCTBuf, m_ptrMatrixBuf, 0, m_nSubbandDegree);

            Array.Copy (m_ptrWorkBuf, 0, m_ptrMatrixBuf, 0, m_nDegreeNum);

            Erisa.FastIDCT (m_ptrInternalBuf, m_ptrMatrixBuf, 0, 1, m_ptrWorkBuf, m_nSubbandDegree);
            if (nSamples != 0)
            {
                Erisa.RoundR32ToWordArray (ptrDst, iDst, m_mioih.ChannelCount, m_ptrInternalBuf, (int)nSamples);
            }
        }

        bool DecodeSoundDCT_MSS (ERISADecodeContext context, MioDataHeader datahdr, byte[] ptrWaveBuf, int wave_pos)
        {
            uint nDegreeWidth = 1u << m_mioih.SubbandDegree;
            uint nSampleCount = (datahdr.SampleCount + nDegreeWidth - 1) & ~(nDegreeWidth - 1);
            uint nSubbandCount = (nSampleCount >> m_mioih.SubbandDegree);
            uint nChannelCount = (uint)m_mioih.ChannelCount;
            uint nAllSampleCount = nSampleCount * nChannelCount;
            uint nAllSubbandCount = nSubbandCount;

            if (nSampleCount > m_nBufLength)
            {
                m_ptrBuffer2 = new int[nAllSampleCount];
                m_ptrBuffer3 = new sbyte[nAllSampleCount * sizeof(short)];
                m_ptrDivisionTable = new byte[nAllSubbandCount];
                m_ptrRevolveCode = new byte[nAllSubbandCount * 10];
                m_ptrWeightCode = new int[nAllSubbandCount * 10];
                m_ptrCoefficient = new int[nAllSubbandCount * 10];
                m_nBufLength = nSampleCount;
            }
            if (context.GetABit() != 0)
            {
                return false;
            }

            int nLastDivision = -1;
            m_ptrNextDivision = 0; // within m_ptrDivisionTable;
            m_ptrNextRevCode = 0; // within m_ptrRevolveCode;
            m_ptrNextWeight = 0; // within m_ptrWeightCode;
            m_ptrNextCoefficient = 0; // within m_ptrCoefficient;

            uint i, j, k;
            for (i = 0; i < nSubbandCount; i ++)
            {
                int nDivisionCode = (int)context.GetNBits (2);
                m_ptrDivisionTable[m_ptrNextDivision++] = (byte)nDivisionCode;

                bool fLeadBlock = false;
                if (nDivisionCode != nLastDivision)
                {
                    if (i != 0)
                    {
                        m_ptrRevolveCode[m_ptrNextRevCode++] = (byte)context.GetNBits (2);
                        m_ptrWeightCode[m_ptrNextWeight++] = (int)context.GetNBits (32);
                        m_ptrCoefficient[m_ptrNextCoefficient++] = (int)context.GetNBits (16);
                    }
                    fLeadBlock = true;
                    nLastDivision = nDivisionCode;
                }
                uint nDivisionCount = 1u << nDivisionCode;
                for (k = 0; k < nDivisionCount; k++)
                {
                    if (fLeadBlock)
                    {
                        m_ptrRevolveCode[m_ptrNextRevCode++] = (byte)context.GetNBits (2);
                        fLeadBlock = false;
                    }
                    else
                    {
                        m_ptrRevolveCode[m_ptrNextRevCode++] = (byte)context.GetNBits (4);
                    }
                    m_ptrWeightCode[m_ptrNextWeight++] = (int)context.GetNBits (32);
                    m_ptrCoefficient[m_ptrNextCoefficient++] = (int)context.GetNBits (16);
                }
            }
            if (nSubbandCount > 0)
            {
                m_ptrRevolveCode[m_ptrNextRevCode++] = (byte)context.GetNBits (2);
                m_ptrWeightCode[m_ptrNextWeight++] = (int)context.GetNBits (32);
                m_ptrCoefficient[m_ptrNextCoefficient++] = (int)context.GetNBits (16);
            }
            if (context.GetABit() != 0)
            {
                return false;
            }
            if (0 != (datahdr.Flags & MIO_LEAD_BLOCK))
            {
                if (m_mioih.Architecture != EriCode.Nemesis)
                {
                    (context as HuffmanDecodeContext).PrepareToDecodeERINACode();
                }
                else
                {
                    throw new NotImplementedException ("Nemesis encoding not implemented");
//                    context.PrepareToDecodeERISACode( );
                }
            }
            else if (m_mioih.Architecture == EriCode.Nemesis)
            {
                throw new NotImplementedException ("Nemesis encoding not implemented");
//                context.InitializeERISACode( );
            }
            if (m_mioih.Architecture != EriCode.Nemesis)
            {
                if (context.DecodeBytes (m_ptrBuffer3, nAllSampleCount * 2) < nAllSampleCount * 2)
                {
                    return false;
                }
                int ptrHBuf = 0; // within m_ptrBuffer3;
                int ptrLBuf = (int)nAllSampleCount; // within m_ptrBuffer3

                for (i = 0; i < nDegreeWidth * 2; i++)
                {
                    int ptrQuantumized = (int)i; // within (PINT) m_ptrBuffer2
                    for (j = 0; j < nAllSubbandCount; j++)
                    {
                        int nLow  = m_ptrBuffer3[ptrLBuf++];
                        int nHigh = m_ptrBuffer3[ptrHBuf++] ^ (nLow >> 8);
                        m_ptrBuffer2[ptrQuantumized] = (nLow & 0xFF) | (nHigh << 8);
                        ptrQuantumized += (int)nDegreeWidth * 2;
                    }
                }
            }
            else
            {
                throw new NotImplementedException ("Nemesis encoding not implemented");
                /*
                if ( context.DecodeERISACodeWords
                        ( (SWORD*) m_ptrBuffer3, nAllSampleCount ) < nAllSampleCount )
                {
                    return false;
                }
                for ( i = 0; i < nAllSampleCount; i ++ )
                {
                    ((PINT)m_ptrBuffer2)[i] = ((SWORD*)m_ptrBuffer3)[i];
                }
                */
            }
            uint nSamples;
            uint nRestSamples = datahdr.SampleCount;
//            int ptrDstBuf = wave_pos; // within (SWORD*) ptrWaveBuf;

            nLastDivision = -1;
            m_ptrNextDivision = 0; // m_ptrDivisionTable;
            m_ptrNextRevCode = 0; // m_ptrRevolveCode;
            m_ptrNextWeight = 0; // m_ptrWeightCode;
            m_ptrNextCoefficient = 0; // m_ptrCoefficient;
            m_ptrNextSource = 0; // (PINT) m_ptrBuffer2;

            for (i = 0; i < nSubbandCount; i++)
            {
                int nDivisionCode = m_ptrDivisionTable[m_ptrNextDivision++];
                uint nDivisionCount = 1u << nDivisionCode;

                bool fLeadBlock = false;
                if (nLastDivision != nDivisionCode)
                {
                    if (i != 0)
                    {
                        nSamples = Math.Min (nRestSamples, (uint)m_nDegreeNum);
                        DecodePostBlock_MSS (ptrWaveBuf, wave_pos, nSamples);
                        nRestSamples -= nSamples;
                        wave_pos += (int)(nSamples * nChannelCount * sizeof(short));
                    }
                    InitializeWithDegree (m_mioih.SubbandDegree - nDivisionCode);
                    nLastDivision = nDivisionCode;
                    fLeadBlock = true;
                }
                for (k = 0; k < nDivisionCount; k++)
                {
                    if (fLeadBlock)
                    {
                        DecodeLeadBlock_MSS();
                        fLeadBlock = false;
                    }
                    else
                    {
                        nSamples = nRestSamples;
                        if (nSamples > m_nDegreeNum)
                        {
                            nSamples = (uint)m_nDegreeNum;
                        }
                        DecodeInternalBlock_MSS (ptrWaveBuf, wave_pos, nSamples);
                        nRestSamples -= nSamples;
                        wave_pos += (int)(nSamples * nChannelCount * sizeof(short));
                    }
                }
            }
            if (nSubbandCount > 0)
            {
                nSamples = nRestSamples;
                if (nSamples > m_nDegreeNum)
                {
                    nSamples = (uint)m_nDegreeNum;
                }
                DecodePostBlock_MSS (ptrWaveBuf, wave_pos, nSamples);
                nRestSamples -= nSamples;
                wave_pos += (int)(nSamples * nChannelCount) * sizeof(short);
            }
            return true;
        }

        void DecodeLeadBlock_MSS ()
        {
            uint i, j;
            uint nHalfDegree = (uint)m_nDegreeNum / 2;
            int nWeightCode = m_ptrWeightCode[m_ptrNextWeight++];
            int nCoefficient = m_ptrCoefficient[m_ptrNextCoefficient++];
            int ptrLapBuf = 0; // within m_ptrLastDCT;

            for (i = 0; i < 2; i++)
            {
                int ptrSrcBuf = 0; // within (PINT) m_ptrBuffer1;
                for (j = 0; j < nHalfDegree; j++)
                {
                    m_ptrBuffer1[ptrSrcBuf + j * 2] = 0;
                    m_ptrBuffer1[ptrSrcBuf + j * 2 + 1] = m_ptrBuffer2[m_ptrNextSource++];
                }
                IQuantumize (m_ptrLastDCT, ptrLapBuf, m_ptrBuffer1, ptrSrcBuf, m_nDegreeNum, nWeightCode, nCoefficient);
                ptrLapBuf += (int)m_nDegreeNum;
            }
            int nRevCode = m_ptrRevolveCode[m_ptrNextRevCode++];

            int ptrLapBuf1 = 0; // m_ptrLastDCT;
            int ptrLapBuf2 = (int)m_nDegreeNum; // m_ptrLastDCT 

            float rSin = (float)Math.Sin (nRevCode * Math.PI / 8);
            float rCos = (float)Math.Cos (nRevCode * Math.PI / 8);
            Erisa.Revolve2x2 (m_ptrLastDCT, ptrLapBuf1, m_ptrLastDCT, ptrLapBuf2, rSin, rCos, 1, m_nDegreeNum);

            ptrLapBuf = 0; //m_ptrLastDCT;
            for (i = 0; i < 2; i++)
            {
                Erisa.OddGivensInverseMatrix (m_ptrLastDCT, ptrLapBuf, m_pRevolveParam, m_nSubbandDegree);

                for (j = 0; j < m_nDegreeNum; j += 2)
                {
                    m_ptrLastDCT[ptrLapBuf + j] = m_ptrLastDCT[ptrLapBuf + j + 1];
                }
                Erisa.FastIPLOT (m_ptrLastDCT, ptrLapBuf, m_nSubbandDegree);
                ptrLapBuf += (int)m_nDegreeNum;
            }
        }

        void DecodeInternalBlock_MSS (byte[] ptrDst, int iDst, uint nSamples)
        {
            int ptrSrcBuf = 0; // m_ptrMatrixBuf;
            int ptrLapBuf = 0; // m_ptrLastDCT;

            int nWeightCode  = m_ptrWeightCode[m_ptrNextWeight++];
            int nCoefficient = m_ptrCoefficient[m_ptrNextCoefficient++];

            for (int i = 0; i < 2; i++)
            {
                IQuantumize (m_ptrMatrixBuf, ptrSrcBuf, m_ptrBuffer2, m_ptrNextSource, m_nDegreeNum, nWeightCode, nCoefficient);
                m_ptrNextSource += m_nDegreeNum;
                ptrSrcBuf += m_nDegreeNum;
            }
            int nRevCode = m_ptrRevolveCode[m_ptrNextRevCode++];
            int nRevCode1 = (nRevCode >> 2) & 0x03;
            int nRevCode2 = (nRevCode & 0x03);

            int ptrSrcBuf1 = 0; // m_ptrMatrixBuf;
            int ptrSrcBuf2 = m_nDegreeNum; // m_ptrMatrixBuf + m_nDegreeNum;

            float rSin = (float) Math.Sin (nRevCode1 * Math.PI / 8);
            float rCos = (float) Math.Cos (nRevCode1 * Math.PI / 8);
            Erisa.Revolve2x2 (m_ptrMatrixBuf, ptrSrcBuf1, m_ptrMatrixBuf, ptrSrcBuf2, rSin, rCos, 2, m_nDegreeNum / 2);

            rSin = (float) Math.Sin (nRevCode2 * Math.PI / 8);
            rCos = (float) Math.Cos (nRevCode2 * Math.PI / 8);
            Erisa.Revolve2x2 (m_ptrMatrixBuf, ptrSrcBuf1 + 1, m_ptrMatrixBuf, ptrSrcBuf2 + 1, rSin, rCos, 2, m_nDegreeNum / 2);

            ptrSrcBuf = 0; // m_ptrMatrixBuf;

            for (int i = 0; i < 2; i++)
            {
                Erisa.OddGivensInverseMatrix (m_ptrMatrixBuf, ptrSrcBuf, m_pRevolveParam, m_nSubbandDegree);
                Erisa.FastIPLOT (m_ptrMatrixBuf, ptrSrcBuf, m_nSubbandDegree);
                Erisa.FastILOT (m_ptrWorkBuf, m_ptrLastDCT, ptrLapBuf, m_ptrMatrixBuf, ptrSrcBuf, m_nSubbandDegree);

                Array.Copy (m_ptrMatrixBuf, ptrSrcBuf, m_ptrLastDCT,   ptrLapBuf, m_nDegreeNum);
                Array.Copy (m_ptrWorkBuf,   0,         m_ptrMatrixBuf, ptrSrcBuf, m_nDegreeNum);

                Erisa.FastIDCT (m_ptrInternalBuf, m_ptrMatrixBuf, ptrSrcBuf, 1, m_ptrWorkBuf, m_nSubbandDegree);
                if (nSamples != 0)
                {
                    Erisa.RoundR32ToWordArray (ptrDst, iDst + (int)i*2, 2, m_ptrInternalBuf, (int)nSamples);
                }
                ptrSrcBuf += m_nDegreeNum;
                ptrLapBuf += m_nDegreeNum;
            }
        }

        void DecodePostBlock_MSS (byte[] ptrDst, int iDst, uint nSamples)
        {
            int ptrLapBuf = 0; // m_ptrLastDCT;
            int ptrSrcBuf = 0; // m_ptrMatrixBuf;

            int i, j;
            uint nHalfDegree = (uint)m_nDegreeNum / 2u;
            int nWeightCode  = m_ptrWeightCode[m_ptrNextWeight++];
            int nCoefficient = m_ptrCoefficient[m_ptrNextCoefficient++];

            for (i = 0; i < 2; i++)
            {
                for (j = 0; j < nHalfDegree; j++)
                {
                    m_ptrBuffer1[j * 2] = 0;
                    m_ptrBuffer1[j * 2 + 1] = m_ptrBuffer2[m_ptrNextSource++];
                }
                IQuantumize (m_ptrMatrixBuf, ptrSrcBuf, m_ptrBuffer1, 0, m_nDegreeNum, nWeightCode, nCoefficient);
                ptrSrcBuf += m_nDegreeNum;
            }
            float rSin, rCos;
            int   nRevCode = m_ptrRevolveCode[m_ptrNextRevCode++];

            int ptrSrcBuf1 = 0; // m_ptrMatrixBuf;
            int ptrSrcBuf2 = m_nDegreeNum; // m_ptrMatrixBuf + m_nDegreeNum;

            rSin = (float) Math.Sin (nRevCode * Math.PI / 8);
            rCos = (float) Math.Cos (nRevCode * Math.PI / 8);
            Erisa.Revolve2x2 (m_ptrMatrixBuf, ptrSrcBuf1, m_ptrMatrixBuf, ptrSrcBuf2, rSin, rCos, 1, m_nDegreeNum);

            ptrSrcBuf = 0; // m_ptrMatrixBuf;

            for (i = 0; i < 2; i ++)
            {
                Erisa.OddGivensInverseMatrix (m_ptrMatrixBuf, ptrSrcBuf, m_pRevolveParam, m_nSubbandDegree);

                for (j = 0; j < m_nDegreeNum; j += 2)
                {
                    m_ptrMatrixBuf[ptrSrcBuf + j] = -m_ptrMatrixBuf[ptrSrcBuf + j + 1];
                }
                Erisa.FastIPLOT (m_ptrMatrixBuf, ptrSrcBuf, m_nSubbandDegree);
                Erisa.FastILOT (m_ptrWorkBuf, m_ptrLastDCT, ptrLapBuf, m_ptrMatrixBuf, ptrSrcBuf, m_nSubbandDegree);

                Array.Copy (m_ptrWorkBuf, 0, m_ptrMatrixBuf, ptrSrcBuf, m_nDegreeNum);

                Erisa.FastIDCT (m_ptrInternalBuf, m_ptrMatrixBuf, ptrSrcBuf, 1, m_ptrWorkBuf, m_nSubbandDegree);
                if (nSamples != 0)
                {
                    Erisa.RoundR32ToWordArray (ptrDst, iDst + (int)i*2, 2, m_ptrInternalBuf, (int)nSamples);
                }
                ptrLapBuf += m_nDegreeNum;
                ptrSrcBuf += m_nDegreeNum;
            }
        }

        void IQuantumize (float[] ptrDestination, int dst, int[] ptrQuantumized, int qsrc, int nDegreeNum, int nWeightCode, int nCoefficient)
        {
            int i, j;
            double rMatrixScale = Math.Sqrt (2.0 / nDegreeNum);
            double rCoefficient = rMatrixScale * nCoefficient;
            double[] rAvgRatio = new double[7];
            for (i = 0; i < 6; i++)
            {
                rAvgRatio[i] = 1.0 / Math.Pow (2.0, (((nWeightCode >> (i * 5)) & 0x1F) - 15) * 0.5);
            }
            rAvgRatio[6] = 1.0;
            for (i = 0; i < m_nFrequencyPoint[0]; i++)
            {
                m_ptrWeightTable[i] = (float) rAvgRatio[0];
            }
            for (j = 1; j < 7; j++)
            {
                double a = rAvgRatio[j - 1];
                double k = (rAvgRatio[j] - a) / (m_nFrequencyPoint[j] - m_nFrequencyPoint[j - 1]);
                while (i < m_nFrequencyPoint[j])
                {
                    m_ptrWeightTable[i] = (float)(k * (i - m_nFrequencyPoint[j - 1]) + a);
                    i++;
                }
            }
            while (i < nDegreeNum)
            {
                m_ptrWeightTable[i++] = (float)rAvgRatio[6];
            }
            float rOddWeight = (float)((((nWeightCode >> 30) & 0x03) + 0x02) / 2.0);
            for (i = 15; i < nDegreeNum; i += 16)
            {
                m_ptrWeightTable[i] *= rOddWeight;
            }
            m_ptrWeightTable[nDegreeNum-1] = (float) nCoefficient;
            for (i = 0; i < nDegreeNum; i++)
            {
                m_ptrWeightTable[i] = 1.0F / m_ptrWeightTable[i];
            }
            for (i = 0; i < nDegreeNum; i ++)
            {
                ptrDestination[dst + i] = (float) (rCoefficient * m_ptrWeightTable[i] * ptrQuantumized[qsrc+i]);
            }
        }
    }
}
