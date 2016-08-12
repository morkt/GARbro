// *****************************************************************************
//                         E R I S A - L i b r a r y
// -----------------------------------------------------------------------------
//    Copyright (C) 2002-2004 Leshade Entis, Entis-soft. All rights reserved.
// *****************************************************************************
//
// C# port by morkt
//

using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Entis
{
    internal class EriReader
    {
        EriMetaData     m_info;
        byte[]          m_output;
        ERISADecodeContext m_context;
        int             m_dst;

        int             m_nBlockSize;
        int             m_nBlockArea;
        int             m_nBlockSamples;
        int             m_nChannelCount;
        int             m_nWidthBlocks;
        int             m_nHeightBlocks;

        int             m_dwBytesPerLine;

        int             m_ptrDstBlock;
        int             m_nDstLineBytes;
        int             m_nDstPixelBytes;
        int             m_nDstWidth;
        int             m_nDstHeight;

        // buffers for lossless encoding
        byte[]          m_ptrOperations;
        sbyte[]         m_ptrColumnBuf;
        sbyte[]         m_ptrLineBuf;
        sbyte[]         m_ptrDecodeBuf;
        sbyte[]         m_ptrArrangeBuf;
        int[]           m_pArrangeTable = new int[4];

        // lossy encoding
        int             m_nBlocksetCount;
        int             m_nYUVLineBytes;
        int             m_nYUVPixelBytes;
        sbyte[]         m_ptrLossyOps;
        float[]         m_ptrVertBufLOT;
        float[]         m_ptrHorzBufLOT;
        float[][]       m_ptrBlocksetBuf;
        float[]         m_ptrMatrixBuf;
        float[]         m_ptrIQParamBuf;
        byte[]          m_ptrIQParamTable;

        sbyte[]         m_ptrBlockLineBuf;
        sbyte[]         m_ptrNextBlockBuf;
        sbyte[]         m_ptrImageBuf;
        sbyte[]         m_ptrYUVImage;

        sbyte[]         m_ptrMovingVector;
        sbyte[]         m_ptrMoveVecFlags;
        int[]           m_ptrMovePrevBlocks;
        int[]           m_ptrNextPrevBlocks;

        HuffmanTree     m_pHuffmanTree;
        ErisaProbModel  m_pProbERISA;

        PtrProcedure[]  m_pfnColorOperation;
        byte[]          m_src_frame;

        public byte[]           Data { get { return m_output; } }
        public PixelFormat    Format { get; private set; }
        public int            Stride { get { return Math.Abs (m_dwBytesPerLine); } }
        public BitmapPalette Palette { get; private set; }

        public EriReader (Stream stream, EriMetaData info, Color[] palette, byte[] key_frame = null)
        {
            m_info = info;
            m_src_frame = key_frame;
            switch (m_info.Architecture)
            {
            case EriCode.Nemesis:
            case EriCode.RunlengthHuffman:
            case EriCode.RunlengthGamma:
                if (CvType.Lossless_ERI == m_info.Transformation && 0 == m_info.BlockingDegree)
                    throw new InvalidFormatException();
                break;
            case EriCode.ArithmeticCode:
                if (CvType.Lossless_ERI != m_info.Transformation)
                    throw new InvalidFormatException();
                break;
            default:
                throw new InvalidFormatException();
            }
            switch (m_info.FormatType & EriType.Mask)
            {
            case EriType.RGB:
                if (m_info.BPP <= 8)
                    m_nChannelCount = 1;
                else if (0 == (m_info.FormatType & EriType.WithAlpha))
                    m_nChannelCount = 3;
                else
                    m_nChannelCount = 4;
                break;

            case EriType.Gray:
                m_nChannelCount = 1;
                break;

            default:
                throw new InvalidFormatException();
            }

            if (CvType.Lossless_ERI == m_info.Transformation)
                InitializeLossless();
            else if (CvType.LOT_ERI == m_info.Transformation
                     || CvType.DCT_ERI == m_info.Transformation)
                InitializeLossy();
            else
                throw new NotSupportedException ("Not supported ERI compression");

            if (null != palette)
                Palette = new BitmapPalette (palette);
            CreateImageBuffer();
            m_context.AttachInputFile (stream);

            m_pfnColorOperation = new PtrProcedure[0x10]
            {
                ColorOperation0000,
                ColorOperation0000,
                ColorOperation0000,
                ColorOperation0000,
                ColorOperation0000,
                ColorOperation0101,
                ColorOperation0110,
                ColorOperation0111,
                ColorOperation0000,
                ColorOperation1001,
                ColorOperation1010,
                ColorOperation1011,
                ColorOperation0000,
                ColorOperation1101,
                ColorOperation1110,
                ColorOperation1111
            };
        }

        private void InitializeLossless ()
        {
            if (0 != m_info.BlockingDegree)
            {
                m_nBlockSize = 1 << m_info.BlockingDegree;
                m_nBlockArea = 1 << (m_info.BlockingDegree * 2);
                m_nBlockSamples = m_nBlockArea * m_nChannelCount;
                m_nWidthBlocks  = ((int)m_info.Width  + m_nBlockSize - 1) >> m_info.BlockingDegree;
                m_nHeightBlocks = ((int)m_info.Height + m_nBlockSize - 1) >> m_info.BlockingDegree;

                m_ptrOperations = new byte[m_nWidthBlocks * m_nHeightBlocks];
                m_ptrColumnBuf  = new sbyte[m_nBlockSize * m_nChannelCount];
                m_ptrLineBuf    = new sbyte[m_nChannelCount * (m_nWidthBlocks << m_info.BlockingDegree)];
                m_ptrDecodeBuf  = new sbyte[m_nBlockSamples];
                m_ptrArrangeBuf = new sbyte[m_nBlockSamples];

                InitializeArrangeTable();
            }
            if (0x00020200 == m_info.Version)
            {
                if (EriCode.RunlengthHuffman == m_info.Architecture)
                {
                    m_pHuffmanTree = new HuffmanTree();
                }
                else if (EriCode.Nemesis == m_info.Architecture)
                {
                    m_pProbERISA = new ErisaProbModel();
                }
            }
            if (EriCode.RunlengthHuffman == m_info.Architecture)
                m_context = new HuffmanDecodeContext (0x10000);
            else if (EriCode.Nemesis == m_info.Architecture)
                m_context = new ProbDecodeContext (0x10000);
            else
                m_context = new RLEDecodeContext (0x10000);
        }

        private void InitializeLossy ()
        {
            if (3 != m_info.BlockingDegree)
                throw new InvalidFormatException();

            m_nBlockSize = 1 << m_info.BlockingDegree;
            m_nBlockArea = 1 << (m_info.BlockingDegree * 2);
            m_nBlockSamples = m_nBlockArea * m_nChannelCount;
            m_nWidthBlocks  = ((int)m_info.Width + m_nBlockSize * 2 - 1) >> (m_info.BlockingDegree + 1);
            m_nHeightBlocks = ((int)m_info.Height + m_nBlockSize * 2 - 1) >> (m_info.BlockingDegree + 1);

            if (CvType.LOT_ERI == m_info.Transformation)
            {
                ++m_nWidthBlocks;
                ++m_nHeightBlocks;
            }

            if (EriSampling.YUV_4_4_4  == m_info.SamplingFlags)
            {
                m_nBlocksetCount = m_nChannelCount * 4;
            }
            else if (EriSampling.YUV_4_1_1 == m_info.SamplingFlags)
            {
                switch (m_nChannelCount)
                {
                case 1:
                    m_nBlocksetCount = 4;
                    break;
                case 3:
                    m_nBlocksetCount = 6;
                    break;
                case 4:
                    m_nBlocksetCount = 10;
                    break;
                default:
                    throw new InvalidFormatException();
                }
            }
            else
                throw new InvalidFormatException();

            m_ptrDecodeBuf  = new sbyte[m_nBlockArea * 16];
            m_ptrVertBufLOT = new float[m_nBlockSamples * 2 * m_nWidthBlocks];
            m_ptrHorzBufLOT = new float[m_nBlockSamples * 2];
            m_ptrBlocksetBuf = new float[16][];
            m_ptrMatrixBuf = new float[m_nBlockArea * 16];
            m_ptrIQParamBuf = new float[m_nBlockArea * 2];
            m_ptrIQParamTable = new byte[m_nBlockArea * 2];

            int dwTotalBlocks = m_nWidthBlocks * m_nHeightBlocks;
            m_ptrLossyOps = new sbyte[dwTotalBlocks * 2];
            m_ptrImageBuf = new sbyte[dwTotalBlocks * m_nBlockArea * m_nBlocksetCount];
            m_ptrMovingVector = new sbyte[dwTotalBlocks * 4];
            m_ptrMoveVecFlags = new sbyte[dwTotalBlocks];
            m_ptrMovePrevBlocks = new int[dwTotalBlocks * 4];

            for (int i = 0; i < 16; i ++)
            {
                m_ptrBlocksetBuf[i] = new float[m_nBlockArea];
            }

            m_nYUVPixelBytes = m_nChannelCount;
            if (3 == m_nYUVPixelBytes)
            {
                m_nYUVPixelBytes = 4;
            }
            m_nYUVLineBytes = ((m_nYUVPixelBytes * m_nWidthBlocks * m_nBlockSize * 2) + 0xF) & (~0xF);
            int nYUVImageSize = m_nYUVLineBytes * m_nHeightBlocks * m_nBlockSize * 2;
            m_ptrBlockLineBuf = new sbyte[m_nYUVLineBytes * 16];
            m_ptrYUVImage = new sbyte[nYUVImageSize];

            InitializeZigZagTable();

            m_pHuffmanTree = new HuffmanTree();
            m_pProbERISA = new ErisaProbModel();

            m_context = new HuffmanDecodeContext (0x10000);
        }

        int[] m_ptrTable;

        void InitializeArrangeTable ()
        {
            int i, j, k, l, m;

            m_ptrTable = new int[m_nBlockSamples * 4];
            m_pArrangeTable[0] = 0;
            m_pArrangeTable[1] = m_nBlockSamples;
            m_pArrangeTable[2] = m_nBlockSamples * 2;
            m_pArrangeTable[3] = m_nBlockSamples * 3;

            int ptrNext = m_pArrangeTable[0];
            for (i = 0; i < m_nBlockSamples; ++i)
            {
                m_ptrTable[ptrNext+i] = i;
            }

            ptrNext = m_pArrangeTable[1];
            l = 0;
            for (i = 0; i < m_nChannelCount; i++)
            {
                for (j = 0; j < m_nBlockSize; j++)
                {
                    m = l + j;
                    for (k = 0; k < m_nBlockSize; k++)
                    {
                        m_ptrTable[ptrNext++] = m;
                        m += m_nBlockSize;
                    }
                }
                l += m_nBlockArea;
            }
            ptrNext = m_pArrangeTable[2];
            for (i = 0; i < m_nBlockArea; i++)
            {
                k = i;
                for (j = 0; j < m_nChannelCount; j++)
                {
                    m_ptrTable[ptrNext++] = k;
                    k += m_nBlockArea;
                }
            }
            ptrNext = m_pArrangeTable[3];
            for (i = 0; i < m_nBlockSize; i++)
            {
                l = i;
                for (j = 0; j < m_nBlockSize; j++)
                {
                    m = l;
                    l += m_nBlockSize;
                    for (k = 0; k < m_nChannelCount; k++)
                    {
                        m_ptrTable[ptrNext++] = m;
                        m += m_nBlockArea;
                    }
                }
            }
        }

        void InitializeZigZagTable ()
        {
            m_ptrTable = new int[m_nBlockArea];
            m_pArrangeTable[0] = 0;

            uint i = 0;
            int x = 0, y = 0;
            for (;;)
            {
                for (;;)
                {
                    m_ptrTable[i++] = x + y * m_nBlockSize;
                    if (i >= m_nBlockArea)
                        return;
                    ++x;
                    --y;
                    if (x >= m_nBlockSize)
                    {
                        --x;
                        y += 2;
                        break;
                    }
                    else if (y < 0)
                    {
                        y = 0;
                        break;
                    }
                }
                for (;;)
                {
                    m_ptrTable[i++] = x + y * m_nBlockSize;
                    if (i >= m_nBlockArea)
                        return;
                    ++y;
                    --x;
                    if (y >= m_nBlockSize)
                    {
                        --y;
                        x += 2;
                        break;
                    }
                    else if (x < 0)
                    {
                        x = 0;
                        break;
                    }
                }
            }
        }

        private void CreateImageBuffer ()
        {
            m_dwBytesPerLine = (((int)m_info.Width * m_info.BPP / 8) + 3) & ~3;
            m_output = new byte[m_dwBytesPerLine * (int)m_info.Height];
            if (!m_info.VerticalFlip)
            {
                m_dst = ((int)m_info.Height - 1) * m_dwBytesPerLine;
                m_dwBytesPerLine = -m_dwBytesPerLine;
            }
            else
            {
                m_dst = 0;
            }
        }

        public void DecodeImage ()
        {
            if (CvType.Lossless_ERI == m_info.Transformation)
                DecodeLosslessImage (m_context as RLEDecodeContext);
            else
                DecodeLossyImage (m_context as HuffmanDecodeContext);
        }

        private delegate void PtrProcedure ();

        private void DecodeLosslessImage (RLEDecodeContext context)
        {
            context.FlushBuffer();

            uint nERIVersion = context.GetNBits (8);
            uint fOpTable = context.GetNBits (8);
            uint fEncodeType = context.GetNBits (8);
            uint nBitCount = context.GetNBits (8);

            if (0 != fOpTable || 0 != (fEncodeType & 0xFE))
            {
                throw new InvalidFormatException();
            }
            switch (nERIVersion)
            {
            case 1:
                if (nBitCount != 0)
                    throw new InvalidFormatException();
                break;
            case 2:
                if (nBitCount != 0 || fEncodeType != 0)
                    throw new InvalidFormatException();
                DecodeType2Image (context);
                return;
            case 8:
                if (nBitCount != 8)
                    throw new InvalidFormatException();
                break;
            case 16:
                if ((nBitCount != 8) || (fEncodeType != 0))
                    throw new InvalidFormatException();
                break;
            default:
                throw new InvalidFormatException();
            }
            m_nDstPixelBytes = m_info.BPP >> 3;
            m_nDstLineBytes = m_dwBytesPerLine;
            var pfnRestoreFunc = GetLLRestoreFunc (m_info.FormatType, m_info.BPP);
            if (null == pfnRestoreFunc)
                throw new InvalidFormatException();

            if (EriCode.Nemesis == m_info.Architecture)
            {
                Debug.Assert (m_pProbERISA != null);
                m_pProbERISA.Initialize();
            }
            int i;
            int ptrNextOperation = 0; // index within m_ptrOperations
            if ((0 != (fEncodeType & 1)) && (m_nChannelCount >= 3))
            {
                if (m_info.Architecture == EriCode.Nemesis)
                    throw new InvalidFormatException();
                int nAllBlockCount = m_nWidthBlocks * m_nHeightBlocks;
                for (i = 0; i < nAllBlockCount; i++)
                {
                    if (EriCode.RunlengthGamma == m_info.Architecture)
                    {
                        m_ptrOperations[i] = (byte)(context.GetNBits(4) | 0xC0);
                    }
                    else
                    {
                        Debug.Assert (EriCode.RunlengthHuffman == m_info.Architecture);
                        m_ptrOperations[i] = (byte)(context as HuffmanDecodeContext).GetHuffmanCode (m_pHuffmanTree);
                    }
                }
            }
            if (context.GetABit() != 0)
                throw new InvalidFormatException();

            if (EriCode.RunlengthGamma == m_info.Architecture)
            {
                if (0 != (fEncodeType & 1))
                {
                    context.InitGammaContext();
                }
            }
            else if (EriCode.RunlengthHuffman == m_info.Architecture)
            {
                (context as HuffmanDecodeContext).PrepareToDecodeERINACode();
            }
            else
            {
                Debug.Assert (EriCode.Nemesis == m_info.Architecture);
                (context as ProbDecodeContext).PrepareToDecodeERISACode();
            }
            int nWidthSamples = m_nChannelCount * m_nWidthBlocks * m_nBlockSize;
            for (i = 0; i < nWidthSamples; ++i)
                m_ptrLineBuf[i] = 0;

            int nAllBlockLines = m_nBlockSize * m_nChannelCount;
            int nLeftHeight = (int)m_info.Height;

            for (int nPosY = 0; nPosY < m_nHeightBlocks; ++nPosY)
            {
                int nColumnBufSamples = m_nBlockSize * m_nChannelCount;
                for (i = 0; i < nColumnBufSamples; ++i)
                    m_ptrColumnBuf[i] = 0;

                m_ptrDstBlock = m_dst + nPosY * m_dwBytesPerLine * m_nBlockSize;
                m_nDstHeight = Math.Min (m_nBlockSize, nLeftHeight);
                int nLeftWidth = (int)m_info.Width;
                int ptrNextLineBuf = 0; // m_ptrLineBuf;

                for (int nPosX = 0; nPosX < m_nWidthBlocks; ++nPosX)
                {
                    m_nDstWidth = Math.Min (m_nBlockSize, nLeftWidth);

                    uint dwOperationCode;
                    if (m_nChannelCount >= 3)
                    {
                        if (0 != (fEncodeType & 1))
                        {
                            dwOperationCode = m_ptrOperations[ptrNextOperation++];
                        }
                        else if (m_info.Architecture == EriCode.RunlengthHuffman)
                        {
                            dwOperationCode = (uint)(context as HuffmanDecodeContext).GetHuffmanCode (m_pHuffmanTree);
                        }
                        else if (m_info.Architecture == EriCode.Nemesis)
                        {
                            dwOperationCode = (uint)(context as ProbDecodeContext).DecodeERISACode (m_pProbERISA);
                        }
                        else
                        {
                            Debug.Assert (EriCode.RunlengthGamma == m_info.Architecture);
                            dwOperationCode = context.GetNBits (4) | 0xC0;
                            context.InitGammaContext();
                        }
                    }
                    else
                    {
                        if (EriType.Gray == m_info.FormatType)
                        {
                            dwOperationCode = 0xC0;
                        }
                        else
                        {
                            dwOperationCode = 0;
                        }
                        if (0 == (fEncodeType & 1) && m_info.Architecture == EriCode.RunlengthGamma)
                        {
                            context.InitGammaContext();
                        }
                    }
                    if (context.DecodeBytes (m_ptrArrangeBuf, (uint)m_nBlockSamples) < m_nBlockSamples)
                    {
                        throw new InvalidFormatException();
                    }
                    PerformOperation (dwOperationCode, nAllBlockLines, m_ptrLineBuf, ptrNextLineBuf);
                    ptrNextLineBuf += nColumnBufSamples;

                    pfnRestoreFunc();

                    m_ptrDstBlock += m_nDstPixelBytes * m_nBlockSize;
                    nLeftWidth -= m_nBlockSize;
                }
                nLeftHeight -= m_nBlockSize;
            }
        }

        #pragma warning disable 162 // unreachable code

        private void DecodeType2Image (RLEDecodeContext context)
        {
            if (m_info.BPP != 8)
                throw new InvalidFormatException();

            throw new NotImplementedException ("Arithmetic compression not implemented");

            if (EriCode.ArithmeticCode == m_info.Architecture)
            {
//                (context as ArithmeticContext).InitArithmeticContext (8);
                m_ptrLineBuf = new sbyte[m_info.Width*4];
            }
            else
                throw new NotImplementedException();

            int dst = m_dst;
            for (int nPosY = 0; nPosY < (int)m_info.Height; ++nPosY)
            {
                if (context.DecodeBytes (m_ptrLineBuf, m_info.Width) < m_info.Width)
                    throw new InvalidFormatException();
                for (int x = 0; x < (int)m_info.Width; ++x)
                    m_output[dst+x] = (byte)m_ptrLineBuf[4 * x];
                dst += m_dwBytesPerLine;
            }
        }

        private void DecodeLossyImage (HuffmanDecodeContext context)
        {
            context.FlushBuffer();

            uint nERIVersion = context.GetNBits (8);
            uint fOpTable = context.GetNBits (8);
            uint fEncodeType = context.GetNBits (8);
            uint nBitCount = context.GetNBits (8);

            var orig_trans = m_info.Transformation;
            CalcImageSizeInBlocks ((fEncodeType == 1) ? CvType.DCT_ERI : orig_trans);

            m_ptrDstBlock = m_dst;
            m_nDstPixelBytes = m_info.BPP >> 3;
            m_nDstLineBytes = m_dwBytesPerLine;
            m_nDstWidth = (int)m_info.Width;
            m_nDstHeight = (int)m_info.Height;

            if (9 == nERIVersion)
            {
                if (fOpTable != 0 || (fEncodeType & 0xFE) != 0 || nBitCount != 8)
                    throw new InvalidFormatException();
                DecodeLossyV9 (context, fEncodeType);
                return;
            }

            var pfnRestoreFunc = GetLSRestoreFunc (m_info.FormatType, m_info.BPP);
            if (null == pfnRestoreFunc)
                throw new InvalidFormatException();

            if (context.GetABit() != 0)
                throw new InvalidFormatException();

            if (0x28 == nERIVersion)
            {
                if (EriCode.RunlengthGamma != m_info.Architecture)
                    throw new InvalidFormatException();
                Debug.Assert (m_pHuffmanTree != null);
                context.PrepareToDecodeERINACode (HuffmanDecodeContext.efERINAOrder0);
            }
            else
                throw new InvalidFormatException();

            throw new NotImplementedException ("Lossy ERI compression not implemented");

            for (int i = 0; i < m_nBlockArea * 2; ++i)
            {
                m_ptrIQParamTable[i] = (byte)context.GetHuffmanCode (m_pHuffmanTree);
            }
            int nTotalBlocks = m_nWidthBlocks * m_nHeightBlocks;
            int nTotalSamples = nTotalBlocks * m_nBlockArea * m_nBlocksetCount;
            context.InitGammaContext();
            if (context.DecodeGammaCodeBytes (m_ptrLossyOps, (uint)nTotalBlocks * 2) < nTotalBlocks * 2)
                throw new InvalidFormatException();

            Debug.Assert (8 == m_nBlockSize);
            const int nBlockSize = 16;
            uint nWidthDivBlocks  = (m_info.Width + (nBlockSize - 1)) / nBlockSize;
            uint nHeightDivBlocks = (m_info.Height + (nBlockSize - 1)) / nBlockSize;
            uint nTotalDivBlocks = nWidthDivBlocks * nHeightDivBlocks;

            if (0 != (fOpTable & 1))
            {
                context.InitGammaContext();
                if (context.DecodeGammaCodeBytes (m_ptrMoveVecFlags, nTotalDivBlocks) < nTotalDivBlocks)
                    throw new InvalidFormatException();
                context.InitGammaContext();
                if (context.DecodeGammaCodeBytes (m_ptrMovingVector, nTotalDivBlocks * 4) < nTotalDivBlocks * 4 )
                    throw new InvalidFormatException();
            }
            else if (null != m_src_frame)
            {
                for (uint i = 0; i < nTotalDivBlocks; ++i)
                    m_ptrMoveVecFlags[i] = 1;
                for (uint i = 0; i < nTotalDivBlocks*4; ++i)
                    m_ptrMovingVector[i] = 0;
            }
            if (null != m_src_frame)
            {
                SetupMovingVector();
            }
            if (CvType.LOT_ERI == m_info.Transformation)
            {
                for (int i = 0; i < m_ptrVertBufLOT.Length; ++i)
                    m_ptrVertBufLOT[i] = 0;
            }

            Action<float[], int> pfnBlockMatrix;
            if (CvType.LOT_ERI == m_info.Transformation)
                pfnBlockMatrix = MatrixILOT8x8;
            else
                pfnBlockMatrix = MatrixIDCT8x8;

            Action<int, int> pfnBlockScaling;
            if (EriSampling.YUV_4_1_1 == m_info.SamplingFlags)
                pfnBlockScaling = BlockScaling411;
            else
                pfnBlockScaling = BlockScaling444;

            int ptrQParam = 0; // m_ptrLossyOps
            int nLineBlockSamples = m_nWidthBlocks * m_nBlockArea * m_nBlocksetCount;

            m_ptrNextPrevBlocks = m_ptrMovePrevBlocks;
            for (int nPosY = 0; nPosY < m_nHeightBlocks; ++nPosY)
            {
                if (CvType.LOT_ERI == m_info.Transformation)
                {
                    for (int i = 0; i < m_ptrHorzBufLOT.Length; ++i)
                        m_ptrHorzBufLOT[i] = 0;
                }
                int ptrVertBufLOT = 0; // m_ptrVertBufLOT;
                m_ptrNextBlockBuf = m_ptrBlockLineBuf;

                if (context.DecodeBytes (m_ptrImageBuf, (uint)nLineBlockSamples) < nLineBlockSamples)
                    throw new InvalidFormatException();

                int ptrSrcData = 0; // m_ptrImageBuf;

                for (int nPosX = 0; nPosX < m_nWidthBlocks; ++nPosX)
                {
                    ArrangeAndIQuantumize (ptrSrcData, ptrQParam);
                    ptrSrcData += m_nBlockArea * m_nBlocksetCount;
                    ptrQParam += 2;

                    pfnBlockMatrix (m_ptrVertBufLOT, ptrVertBufLOT);
                    ptrVertBufLOT += m_nBlockArea * 2 * m_nChannelCount;
                    pfnBlockScaling (nPosX, nPosY);
                }
            }
            pfnRestoreFunc();

            if (0 != (fOpTable & 0xC))
            {
                throw new NotImplementedException ("Filtering operations not implemented");
            }
            m_info.Transformation = orig_trans;
        }

        void DecodeLossyV9 (HuffmanDecodeContext context, uint fEncodeType)
        {
            throw new NotImplementedException();
            /*
            if (m_nChannelCount < 3)
                throw new InvalidFormatException();
            m_nDstPixelBytes = m_info.BPP >> 3;

            var pfnRestoreFunc = GetLSRestoreFunc (m_info.FormatType, m_info.BPP);
            if (null == pfnRestoreFunc)
                throw new InvalidFormatException();

            if (EriCode.RunlengthHuffman == m_info.Architecture)
                context.PrepareToDecodeERINACode();

            if (context.GetABit() != 0)
                throw new InvalidFormatException();

            float field_1A8 = 256.0f / (context.GetNBits (8) + 1);
            uint v9 = context.GetNBits (8);
            double field_1A4 = 2.0 / (double)m_nBlockSize;
            int nTotalBlocks = m_nHeightBlocks * m_nWidthBlocks;
            bool is_encode_type_1 = (fEncodeType & 1) != 0;
            field_1A8 = (float)(field_1A8 * field_1A4);
            float field_1AC = (float)(256.0 / (v9 + 1) * field_1A4);
            if (is_encode_type_1)
            {
                uint v12 = (uint)(nTotalBlocks * m_nBlocksetCount);
                context.InitGammaContext();
                if (context.DecodeGammaCodeBytes (m_ptrMoveVecFlags, v12) < v12)
                    throw new InvalidFormatException();

                m_pHuffmanTree.Initialize();
                int nAllBlockCount = 4 * nTotalBlocks;
                for (int i = 0; i < nAllBlockCount; ++i)
                {
                    m_ptrIQParamTable[i] = (byte)context.GetHuffmanCode (m_pHuffmanTree);
                }
                if (m_info.Architecture != EriCode.RunlengthHuffman)
                    context.InitGammaContext();
            }
            var field_70 = new byte[m_nBlocksetCount * 4];

            int image_height = (int)m_info.Height;
            int ptrSrcData = 0; // this->m_ptrMoveVecFlags;
            int ptrQParam = 0; // this->m_ptrIQParamTable;
            for (int nPosY = 0; nPosY < m_nHeightBlocks; ++nPosY)
            {
                int image_dst = (m_dwBytesPerLine * nPosY) << (m_info.BlockingDegree + 1);
                int v48 = m_nBlockSize;
                int v43 = m_nBlockSize;
                if (image_height < m_nBlockSize)
                {
                    v48 = image_height;
                    v43 = 0;
                }
                else if (image_height < 2 * m_nBlockSize)
                {
                    v43 = image_height - m_nBlockSize;
                }
                int image_width = (int)m_info.Width;
                for (int i = 0; i < m_nBlocksetCount; ++i)
                {
                    m_ptrHorzBufLOT[i] = 0;
                }
                for (int nPosX = 0; nPosX < m_nWidthBlocks; ++nPosX)
                {
                    if (is_encode_type_1)
                    {
                        Buffer.BlockCopy (m_ptrMoveVecFlags, ptrSrcData, field_70, 0, 4 * m_nBlocksetCount);
                        ptrSrcData += 4 * m_nBlocksetCount;
                    }
                    else
                    {
                        context.InitGammaContext();
                        if (context.DecodeGammaCodeBytes (field_70, m_nBlocksetCount) < m_nBlocksetCount)
                            throw new InvalidFormatException();
                        m_ptrIQParamTable[ptrQParam  ] = (byte)context.GetHuffmanCode (m_pHuffmanTree);
                        m_ptrIQParamTable[ptrQParam+1] = (byte)context.GetHuffmanCode (m_pHuffmanTree);
                        m_ptrIQParamTable[ptrQParam+2] = (byte)context.GetHuffmanCode (m_pHuffmanTree);
                        m_ptrIQParamTable[ptrQParam+3] = (byte)context.GetHuffmanCode (m_pHuffmanTree);
                    }
                    int v23 = m_nBlocksetCount * (m_nBlockArea - 1);
                    if (EriCode.RunlengthHuffman == m_info.dwArchitecture)
                    {
                        if (sub_439080 (&this->field_70[4 * m_nBlocksetCount], v23) < v23 )
                            throw new InvalidFormatException();
                    }
                    else
                    {
                        if (!is_encode_type_1)
                            context.InitGammaContext();
                        if (context.DecodeGammaCodeBytes ((SBYTE *)&this->field_70[4 * this->m_nBlocksetCount], v23) < v23)
                            throw new InvalidFormatException();
                    }
                    for (int v24 = 0; v24 < m_nBlocksetCount; ++v24)
                    {
                        uint v27 = LittleEndian.ToUInt32 (field_70, v24 * 4) + m_ptrHorzBufLOT[v24];
                        m_ptrHorzBufLOT[v24] = v27;
                        LittleEndian.Pack (v27, field_70, v24 * 4);
                    }
                    sub_43ADE0 (this, (int)ptrQParam, (int)ptrQParam);
                    ptrQParam += 4;
                    sub_425A49 (this);
                    field_94 (this);
                    sub_43B1B0 (this);

                    int v29 = m_nBlockSize;
                    int v30 = m_nBlockSize;
                    if (image_width < m_nBlockSize)
                    {
                        v29 = image_width;
                        v30 = 0;
                    }
                    else if (image_width < 2 * m_nBlockSize)
                    {
                        v30 = image_width - m_nBlockSize;
                    }
                    int v59 = v30;
                    int v61 = v30;
                    int v58 = v29;
                    int v54 = v48;
                    int v55 = v48;
                    int v60 = v29;
                    int v56 = v43;
                    int v57 = v43;
                    int v31 = m_nDstPixelBytes;
                    int v62 = 0;
                    v32 = (float *)this->m_ptrBlocksetBuf;
                    v63 = m_nBlockSize * v31;
                    v33 = m_dwBytesPerLine;
                    v64 = m_nBlockSize * v33;
                    v65 = m_nBlockSize * (v33 + v31);
                    for (int v34 = 0; v34 < 16; v34 += 4)
                    {
                        v35 = v66;
                        v36 = v32;
                        v37 = 4;
                        do
                        {
                            v38 = *v36;
                            v36 += 4;
                            *v35 = v38;
                            ++v35;
                            --v37;
                        }
                        while ( v37 );
                        pfnRestoreFunc (
                            &image_dst[*(int *)((char *)&v62 + v34)],
                            imginf->dwBytesPerLine,
                            v66,
                            *(uint *)((char *)&v58 + v34),
                            *(uint *)((char *)&v54 + v34));
                        ++v32;
                    }
                    image_width -= 2 * m_nBlockSize;
                    image_dst += m_nDstPixelBytes << (m_info.BlockingDegree + 1);
                }
                image_height -= 2 * m_nBlockSize;
            }
            */
        }

        void CalcImageSizeInBlocks (CvType fdwTransformation)
        {
            m_info.Transformation = fdwTransformation;

            m_nWidthBlocks = (((int)m_info.Width + m_nBlockSize * 2 - 1) >> (m_info.BlockingDegree + 1));
            m_nHeightBlocks = ((int)m_info.Height + m_nBlockSize * 2 - 1) >> (m_info.BlockingDegree + 1);

            if (CvType.LOT_ERI == fdwTransformation)
            {
                ++m_nWidthBlocks;
                ++m_nHeightBlocks;
            }
        }

        void SetupMovingVector ()
        {
            throw new NotImplementedException ("Lossy delta compression not implemented");
        }

        void PerformOperation (uint dwOpCode, int nAllBlockLines, sbyte[] pNextLineBuf, int iNextLineIdx )
        {
            int     i, j, k;
            uint    nArrangeCode, nColorOperation, nDiffOperation;
            nColorOperation = dwOpCode & 0x0F;
            nArrangeCode = (dwOpCode >> 4) & 0x03;
            nDiffOperation = (dwOpCode >> 6) & 0x03;

            if (0 == nArrangeCode)
            {
                Buffer.BlockCopy (m_ptrArrangeBuf, 0, m_ptrDecodeBuf, 0, m_nBlockSamples);
                if (0 == dwOpCode)
                {
                    return;
                }
            }
            else
            {
                int pArrange = m_pArrangeTable[nArrangeCode];
                for (i = 0; i < m_nBlockSamples; i++)
                {
                    m_ptrDecodeBuf[m_ptrTable[pArrange + i]] = m_ptrArrangeBuf[i];
                }
            }
            m_pfnColorOperation[nColorOperation]();

            int ptrNextBuf = 0;     // m_ptrDecodeBuf
            int ptrNextColBuf = 0;  // m_ptrColumnBuf
            if (0 != (nDiffOperation & 1))
            {
                for (i = 0; i < nAllBlockLines; i++)
                {
                    sbyte nLastVal = m_ptrColumnBuf[ptrNextColBuf];
                    for (j = 0; j < m_nBlockSize; j++)
                    {
                        nLastVal += m_ptrDecodeBuf[ptrNextBuf];
                        m_ptrDecodeBuf[ptrNextBuf++] = nLastVal;
                    }
                    m_ptrColumnBuf[ptrNextColBuf++] = nLastVal;
                }
            }
            else
            {
                for (i = 0; i < nAllBlockLines; i ++)
                {
                    m_ptrColumnBuf[ptrNextColBuf++] = m_ptrDecodeBuf[ptrNextBuf + m_nBlockSize - 1];
                    ptrNextBuf += m_nBlockSize;
                }
            }
            int iNextDst = 0;
            for (k = 0; k < m_nChannelCount; k++)
            {
                sbyte[] ptrLastLine = pNextLineBuf;
                int     idxLastLine = iNextLineIdx;
                for (i = 0; i < m_nBlockSize; i++)
                {
                    for (j = 0; j < m_nBlockSize; j++)
                    {
                        m_ptrDecodeBuf[iNextDst+j] += ptrLastLine[idxLastLine+j];
                    }
                    ptrLastLine = m_ptrDecodeBuf;
                    idxLastLine = iNextDst;
                    iNextDst += m_nBlockSize;
                }
                Buffer.BlockCopy (ptrLastLine, idxLastLine, pNextLineBuf, iNextLineIdx, m_nBlockSize);
                iNextLineIdx += m_nBlockSize;
            }
        }

        PtrProcedure GetLLRestoreFunc (EriType fdwFormatType, int dwBitsPerPixel)
        {
            switch (dwBitsPerPixel)
            {
            case 32:
                if (EriType.RGBA == fdwFormatType)
                {
                    Format = PixelFormats.Bgra32;
                    if (null == m_src_frame)
                        return RestoreRGBA32;
                    else
                        return RestoreDeltaRGBA32;
                }
                Format = PixelFormats.Bgr32;
                if (null == m_src_frame)
                    return RestoreRGB24;
                else
                    return RestoreDeltaRGB24;
            case 24:
                Format = PixelFormats.Bgr24;
                if (null == m_src_frame)
                    return RestoreRGB24;
                else
                    return RestoreDeltaRGB24;
            case 16:
                Format = PixelFormats.Bgr555;
                return RestoreRGB16;
            case 8:
                if (null == Palette)
                    Format = PixelFormats.Gray8;
                else
                    Format = PixelFormats.Indexed8;
                return RestoreGray8;
            }
            return null;
        }

        PtrProcedure GetLSRestoreFunc (EriType fdwFormatType, int dwBitsPerPixel)
        {
            switch (dwBitsPerPixel)
            {
            case 32:
                if (EriType.RGBA == fdwFormatType)
                {
                    Format = PixelFormats.Bgra32;
                    if (null == m_src_frame)
                        return LossyRestoreRGBA32;
                    else
                        return LossyRestoreDeltaRGBA32;
                }
                Format = PixelFormats.Bgr32;
                if (null == m_src_frame)
                    return LossyRestoreRGB24;
                else
                    return LossyRestoreDeltaRGB24;
            case 24:
                Format = PixelFormats.Bgr24;
                if (null == m_src_frame)
                    return LossyRestoreRGB24;
                else
                    return LossyRestoreDeltaRGB24;
            case    8:
                Format = PixelFormats.Gray8;
                if (null == m_src_frame)
                    return LossyRestoreGray8;
                else
                    return LossyRestoreDeltaGray8;
            }
            return null;
        }

        void RestoreRGBA32 ()
        {
            int ptrDstLine = m_ptrDstBlock;
            int ptrSrcLine = 0; //m_ptrDecodeBuf;
            int nBlockSamples = m_nBlockArea;
            int nBlockSamplesX3 = nBlockSamples * 3;

            for (uint y = 0; y < m_nDstHeight; y++)
            {
                int ptrDstNext = ptrDstLine;
                int ptrSrcNext = ptrSrcLine;

                for (uint x = 0; x < m_nDstWidth; x++)
                {
                    m_output[ptrDstNext++] = (byte)m_ptrDecodeBuf[ptrSrcNext];
                    m_output[ptrDstNext++] = (byte)m_ptrDecodeBuf[ptrSrcNext + nBlockSamples];
                    m_output[ptrDstNext++] = (byte)m_ptrDecodeBuf[ptrSrcNext + nBlockSamples * 2];
                    m_output[ptrDstNext++] = (byte)m_ptrDecodeBuf[ptrSrcNext + nBlockSamplesX3];
                    ptrSrcNext ++;
                }
                ptrSrcLine += m_nBlockSize;
                ptrDstLine += m_nDstLineBytes;
            }
        }

        void RestoreRGB24()
        {
            int ptrDstLine = m_ptrDstBlock;
            int ptrSrcLine = 0; //m_ptrDecodeBuf;
            int nBytesPerPixel = m_nDstPixelBytes;
            int nBlockSamples = m_nBlockArea;

            for (uint y = 0; y < m_nDstHeight; y++)
            {
                int ptrDstNext = ptrDstLine;
                int ptrSrcNext = ptrSrcLine;

                for (uint x = 0; x < m_nDstWidth; x++)
                {
                    m_output[ptrDstNext]   = (byte)m_ptrDecodeBuf[ptrSrcNext];
                    m_output[ptrDstNext+1] = (byte)m_ptrDecodeBuf[ptrSrcNext + nBlockSamples];
                    m_output[ptrDstNext+2] = (byte)m_ptrDecodeBuf[ptrSrcNext + nBlockSamples * 2];
                    ptrSrcNext ++;
                    ptrDstNext += nBytesPerPixel;
                }
                ptrSrcLine += m_nBlockSize;
                ptrDstLine += m_nDstLineBytes;
            }
        }

        void RestoreDeltaRGBA32 ()
        {
            int ptrDstLine = m_ptrDstBlock;
            int ptrSrcLine = 0; //m_ptrDecodeBuf;
            int nBlockSamples = m_nBlockArea;
            int nBlockSamplesX3 = nBlockSamples * 3;

            for (uint y = 0; y < m_nDstHeight; y++)
            {
                int ptrDstNext = ptrDstLine;
                int ptrSrcNext = ptrSrcLine;

                for (uint x = 0; x < m_nDstWidth; x++)
                {
                    m_output[ptrDstNext]   = (byte)(m_src_frame[ptrDstNext]   + m_ptrDecodeBuf[ptrSrcNext]);
                    m_output[ptrDstNext+1] = (byte)(m_src_frame[ptrDstNext+1] + m_ptrDecodeBuf[ptrSrcNext + nBlockSamples]);
                    m_output[ptrDstNext+2] = (byte)(m_src_frame[ptrDstNext+2] + m_ptrDecodeBuf[ptrSrcNext + nBlockSamples * 2]);
                    m_output[ptrDstNext+3] = (byte)(m_src_frame[ptrDstNext+3] + m_ptrDecodeBuf[ptrSrcNext + nBlockSamplesX3]);
                    ptrSrcNext ++;
                    ptrDstNext += 4;
                }
                ptrSrcLine += m_nBlockSize;
                ptrDstLine += m_nDstLineBytes;
            }
        }

        void RestoreDeltaRGB24()
        {
            int ptrDstLine = m_ptrDstBlock;
            int ptrSrcLine = 0; //m_ptrDecodeBuf;
            int nBytesPerPixel = m_nDstPixelBytes;
            int nBlockSamples = m_nBlockArea;

            for (uint y = 0; y < m_nDstHeight; y++)
            {
                int ptrDstNext = ptrDstLine;
                int ptrSrcNext = ptrSrcLine;

                for (uint x = 0; x < m_nDstWidth; x++)
                {
                    m_output[ptrDstNext]   = (byte)(m_src_frame[ptrDstNext] + m_ptrDecodeBuf[ptrSrcNext]);
                    m_output[ptrDstNext+1] = (byte)(m_src_frame[ptrDstNext+1] + m_ptrDecodeBuf[ptrSrcNext + nBlockSamples]);
                    m_output[ptrDstNext+2] = (byte)(m_src_frame[ptrDstNext+2] + m_ptrDecodeBuf[ptrSrcNext + nBlockSamples * 2]);
                    ptrSrcNext ++;
                    ptrDstNext += nBytesPerPixel;
                }
                ptrSrcLine += m_nBlockSize;
                ptrDstLine += m_nDstLineBytes;
            }
        }

        void RestoreRGB16()
        {
            int ptrDstLine = m_ptrDstBlock;
            int ptrSrcLine = 0; //m_ptrDecodeBuf;
            int nBlockSamples = m_nBlockArea;

            for (uint y = 0; y < m_nDstHeight; y++)
            {
                int ptrDstNext = ptrDstLine;
                int ptrSrcNext = ptrSrcLine;

                for (uint x = 0; x < m_nDstWidth; x++)
                {
                    int word = (m_ptrDecodeBuf[ptrSrcNext] & 0x1F) |
                              ((m_ptrDecodeBuf[ptrSrcNext + nBlockSamples] & 0x1F) << 5) |
                              ((m_ptrDecodeBuf[ptrSrcNext + nBlockSamples * 2] & 0x1F) << 10);
                    m_output[ptrDstNext++] = (byte)word;
                    m_output[ptrDstNext++] = (byte)(word >> 8);
                    ptrSrcNext ++;
                }
                ptrSrcLine += m_nBlockSize;
                ptrDstLine += m_nDstLineBytes;
            }
        }

        void RestoreGray8()
        {
            int ptrDstLine = m_ptrDstBlock;
            int ptrSrcLine = 0; //m_ptrDecodeBuf;

            for (uint y = 0; y < m_nDstHeight; y++)
            {
                Buffer.BlockCopy (m_ptrDecodeBuf, ptrSrcLine, m_output, ptrDstLine, m_nDstWidth);
                ptrSrcLine += m_nBlockSize;
                ptrDstLine += m_nDstLineBytes;
            }
        }

        void LossyRestoreRGB24 ()
        {
            ConvertImageYUVtoRGB();

            int nSrcLineBytes = m_nYUVLineBytes;
            int nSrcPixelBytes = m_nYUVPixelBytes;
            int ptrSrcImage = 0; // m_ptrYUVImage;
            int nDstLineBytes = m_nDstLineBytes;
            int nDstPixelBytes = m_nDstPixelBytes;
            int ptrDstImage = m_ptrDstBlock;
            int nWidth = m_nDstWidth;

            for (uint y = 0; y < m_nDstHeight; ++y)
            {
                int ptrSrcLine = ptrSrcImage;
                int ptrDstLine = ptrDstImage;
                for (uint x = 0; x < nWidth; ++x)
                {
                    m_output[ptrDstLine]   = (byte)m_ptrYUVImage[ptrSrcLine];
                    m_output[ptrDstLine+1] = (byte)m_ptrYUVImage[ptrSrcLine+1];
                    m_output[ptrDstLine+2] = (byte)m_ptrYUVImage[ptrSrcLine+2];
                    ptrSrcLine += nSrcPixelBytes;
                    ptrDstLine += nDstPixelBytes;
                }
                ptrSrcImage += nSrcLineBytes;
                ptrDstImage += nDstLineBytes;
            }
        }

        void LossyRestoreRGBA32 ()
        {
            ConvertImageYUVtoRGB();

            int nSrcLineBytes = m_nYUVLineBytes;
            int ptrSrcImage = 0; //m_ptrYUVImage;
            int nDstLineBytes = m_nDstLineBytes;
            int ptrDstImage = m_ptrDstBlock;
            int nLineBytes = m_nDstWidth*4;

            for (uint y = 0; y < m_nDstHeight; ++y)
            {
                Buffer.BlockCopy (m_ptrYUVImage, ptrSrcImage, m_output, ptrDstImage, nLineBytes);
                ptrSrcImage += nSrcLineBytes;
                ptrDstImage += nDstLineBytes;
            }
        }

        void LossyRestoreDeltaRGB24 ()
        {
            MoveImageWithVector();
            ConvertImageYUVtoRGB (m_src_frame != null);

            int nSrcLineBytes = m_nYUVLineBytes;
            int nSrcPixelBytes = m_nYUVPixelBytes;
            int ptrSrcImage = 0; //m_ptrYUVImage;
            int nDstLineBytes = m_nDstLineBytes;
            int nDstPixelBytes = m_nDstPixelBytes;
            int ptrDstImage = m_ptrDstBlock;
            int nWidth = m_nDstWidth;

            for (uint y = 0; y < m_nDstHeight; ++y)
            {
                int ptrSrcLine = ptrSrcImage;
                int ptrDstLine = ptrDstImage;
                for (uint x = 0; x < nWidth; ++x)
                {
                    int b = m_src_frame[ptrDstLine]   + (m_ptrYUVImage[ptrSrcLine]   << 1);
                    int g = m_src_frame[ptrDstLine+1] + (m_ptrYUVImage[ptrSrcLine+1] << 1);
                    int r = m_src_frame[ptrDstLine+2] + (m_ptrYUVImage[ptrSrcLine+2] << 1);

                    if ((uint)b > 0xFF)
                    {
                        b = (~b >> 31) & 0xFF;
                    }
                    if ((uint)g > 0xFF)
                    {
                        g = (~g >> 31) & 0xFF;
                    }
                    if ((uint)r > 0xFF)
                    {
                        r = (~r >> 31) & 0xFF;
                    }
                    m_output[ptrDstLine]   = (byte)b;
                    m_output[ptrDstLine+1] = (byte)g;
                    m_output[ptrDstLine+2] = (byte)r;
                    ptrSrcLine += nSrcPixelBytes;
                    ptrDstLine += nDstPixelBytes;
                }
                ptrSrcImage += nSrcLineBytes;
                ptrDstImage += nDstLineBytes;
            }
        }

        void LossyRestoreDeltaRGBA32 ()
        {
            MoveImageWithVector();
            ConvertImageYUVtoRGB (m_src_frame != null);

            int nSrcLineBytes = m_nYUVLineBytes;
            int ptrSrcImage = 0; // m_ptrYUVImage;
            int nDstLineBytes = m_nDstLineBytes;
            int ptrDstImage = m_ptrDstBlock;
            int nWidth = m_nDstWidth;

            for (uint y = 0; y < m_nDstHeight; ++y)
            {
                int ptrSrcLine = ptrSrcImage;
                int ptrDstLine = ptrDstImage;
                for (uint x = 0; x < nWidth; ++x)
                {
                    int b = m_src_frame[ptrDstLine]   + (m_ptrYUVImage[ptrSrcLine]   << 1);
                    int g = m_src_frame[ptrDstLine+1] + (m_ptrYUVImage[ptrSrcLine+1] << 1);
                    int r = m_src_frame[ptrDstLine+2] + (m_ptrYUVImage[ptrSrcLine+2] << 1);
                    int a = m_src_frame[ptrDstLine+3] + (m_ptrYUVImage[ptrSrcLine+3] << 1);

                    if ((uint)b > 0xFF)
                    {
                        b = (~b >> 31) & 0xFF;
                    }
                    if ((uint)g > 0xFF)
                    {
                        g = (~g >> 31) & 0xFF;
                    }
                    if ((uint)r > 0xFF)
                    {
                        r = (~r >> 31) & 0xFF;
                    }
                    if ((uint) a > 0xFF)
                    {
                        a = (~a >> 31) & 0xFF;
                    }
                    m_output[ptrDstLine]   = (byte)b;
                    m_output[ptrDstLine+1] = (byte)g;
                    m_output[ptrDstLine+2] = (byte)r;
                    m_output[ptrDstLine+3] = (byte)a;
                    ptrSrcLine += 4;
                    ptrDstLine += 4;
                }
                ptrSrcImage += nSrcLineBytes;
                ptrDstImage += nDstLineBytes;
            }
        }

        void LossyRestoreGray8 ()
        {
            int nSrcLineBytes = m_nYUVLineBytes;
            int ptrSrcImage = 0; // m_ptrYUVImage
            int nDstLineBytes = m_nDstLineBytes;
            int ptrDstImage = m_ptrDstBlock;
            for (uint y = 0; y < m_nDstHeight; ++y)
            {
                Buffer.BlockCopy (m_ptrYUVImage, ptrSrcImage, m_output, ptrDstImage, m_nDstWidth);
                ptrSrcImage += nSrcLineBytes;
                ptrDstImage += nDstLineBytes;
            }
        }

        void LossyRestoreDeltaGray8 ()
        {
            int nSrcLineBytes = m_nYUVLineBytes;
            int ptrSrcImage = 0; // m_ptrYUVImage
            int nDstLineBytes = m_nDstLineBytes;
            int ptrDstImage = m_ptrDstBlock;
            int nWidth = m_nDstWidth;

            for (uint y = 0; y < m_nDstHeight; ++y)
            {
                int ptrSrcLine = ptrSrcImage;
                int ptrDstLine = ptrDstImage;
                for (int x = 0; x < nWidth; ++x)
                {
                    int g = m_output[ptrDstLine+x] + (m_ptrYUVImage[ptrSrcLine+x] << 1);
                    if ((uint)g > 0xFF)
                    {
                        g = (~g >> 31) & 0xFF;
                    }
                    m_output[ptrDstLine+x] = (byte)g;
                }
                ptrSrcImage += nSrcLineBytes;
                ptrDstImage += nDstLineBytes;
            }
        }

        void MoveImageWithVector ()
        {
            throw new NotImplementedException ("Lossy delta compression not implemented");
        }

        void ColorOperation0000 ()
        {
        }

        void ColorOperation0101 ()
        {
            int ptrNext = 0; // m_ptrDecodeBuf;
            int nChSamples = m_nBlockArea;
            int nRepCount = m_nBlockArea;
            do
            {
                sbyte nBase = m_ptrDecodeBuf[ptrNext];
                m_ptrDecodeBuf[ptrNext++ + nChSamples] += nBase;
            }
            while (0 != --nRepCount);
        }

        void ColorOperation0110 ()
        {
            int ptrNext = 0; // m_ptrDecodeBuf;
            int nChSamples = m_nBlockArea * 2;
            int nRepCount = m_nBlockArea;
            do
            {
                sbyte nBase = m_ptrDecodeBuf[ptrNext];
                m_ptrDecodeBuf[ptrNext++ + nChSamples] += nBase;
            }
            while (0 != --nRepCount);
        }

        void ColorOperation0111 ()
        {
            int ptrNext = 0; // m_ptrDecodeBuf;
            int nChSamples = m_nBlockArea;
            int nRepCount = m_nBlockArea;
            do
            {
                sbyte nBase = m_ptrDecodeBuf[ptrNext];
                m_ptrDecodeBuf[ptrNext + nChSamples] += nBase;
                m_ptrDecodeBuf[ptrNext + nChSamples * 2] += nBase;
                ptrNext ++;
            }
            while (0 != --nRepCount);
        }

        void ColorOperation1001 ()
        {
            int ptrNext = 0; //m_ptrDecodeBuf;
            int nChSamples = m_nBlockArea;
            int nRepCount = m_nBlockArea;
            do
            {
                sbyte nBase = m_ptrDecodeBuf[ptrNext + nChSamples];
                m_ptrDecodeBuf[ptrNext++] += nBase;
            }
            while (0 != --nRepCount);
        }

        void ColorOperation1010 ()
        {
            int ptrNext = 0; // m_ptrDecodeBuf;
            int nChSamples = m_nBlockArea;
            int nRepCount = m_nBlockArea;
            do
            {
                sbyte nBase = m_ptrDecodeBuf[ptrNext + nChSamples];
                m_ptrDecodeBuf[ptrNext++ + nChSamples * 2] += nBase;
            }
            while (0 != --nRepCount);
        }

        void ColorOperation1011 ()
        {
            int ptrNext = 0; //m_ptrDecodeBuf;
            int nChSamples = m_nBlockArea;
            int nRepCount = m_nBlockArea;
            do
            {
                sbyte nBase = m_ptrDecodeBuf[ptrNext + nChSamples];
                m_ptrDecodeBuf[ptrNext] += nBase;
                m_ptrDecodeBuf[ptrNext + nChSamples * 2] += nBase;
                ptrNext ++;
            }
            while (0 != --nRepCount);
        }

        void ColorOperation1101 ()
        {
            int ptrNext = 0; //m_ptrDecodeBuf;
            int nChSamples = m_nBlockArea * 2;
            int nRepCount = m_nBlockArea;
            do
            {
                sbyte nBase = m_ptrDecodeBuf[ptrNext + nChSamples];
                m_ptrDecodeBuf[ptrNext++] += nBase;
            }
            while (0 != --nRepCount);
        }

        void ColorOperation1110 ()
        {
            int ptrNext = 0; // m_ptrDecodeBuf;
            int nChSamples = m_nBlockArea;
            int nRepCount = m_nBlockArea;
            do
            {
                sbyte nBase = m_ptrDecodeBuf[ptrNext + nChSamples * 2];
                m_ptrDecodeBuf[ptrNext++ + nChSamples] += nBase;
            }
            while (0 != --nRepCount);
        }

        void ColorOperation1111 ()
        {
            int ptrNext = 0; // m_ptrDecodeBuf;
            int nChSamples = m_nBlockArea;
            int nRepCount = m_nBlockArea;
            do
            {
                sbyte nBase = m_ptrDecodeBuf[ptrNext + nChSamples * 2];
                m_ptrDecodeBuf[ptrNext] += nBase;
                m_ptrDecodeBuf[ptrNext + nChSamples] += nBase;
                ptrNext ++;
            }
            while (0 != --nRepCount);
        }

        void ArrangeAndIQuantumize (int ptrSrcData, int ptrCoefficient)
        {
            int i, j, k;
            float rMatrixScale = (float)(512.0 / m_nBlockSize);
            var pIQParamPtr = new int[2];
            for (i = 0; i < 2; ++i)
            {
                float rScale = 1.0f;
                if (0 != (m_ptrLossyOps[ptrCoefficient+i] & 1))
                {
                    rScale = 1.5f;
                }
                rScale *= (float)Math.Pow (2.0, (m_ptrLossyOps[ptrCoefficient+i] / 2));
                rScale *= rMatrixScale;

                int pIQParamTable = i * m_nBlockArea; // m_ptrIQParamTable 
                pIQParamPtr[i] = pIQParamTable; // m_ptrIQParamBuf 
                for (j = 0; j < m_nBlockArea; ++j)
                {
                    m_ptrIQParamBuf[pIQParamPtr[i]+j] = (float)(rScale * (m_ptrIQParamTable[pIQParamTable+j] + 1));
                }
            }
            if (CvType.DCT_ERI == m_info.Transformation)
            {
                m_ptrImageBuf[ptrSrcData+m_nBlockArea]   += m_ptrImageBuf[ptrSrcData];
                m_ptrImageBuf[ptrSrcData+m_nBlockArea*2] += m_ptrImageBuf[ptrSrcData];
                m_ptrImageBuf[ptrSrcData+m_nBlockArea*3] += m_ptrImageBuf[ptrSrcData];

                if (EriSampling.YUV_4_4_4 == m_info.SamplingFlags)
                {
                    k = m_nBlockArea * 4;
                    j = 1;
                }
                else
                {
                    k = m_nBlockArea * 6;
                    j = 3;
                }
                for (i = j; i < m_nChannelCount; ++i)
                {
                    m_ptrImageBuf[ptrSrcData + k + m_nBlockArea]   += m_ptrImageBuf[ptrSrcData+k];
                    m_ptrImageBuf[ptrSrcData + k + m_nBlockArea*2] += m_ptrImageBuf[ptrSrcData+k];
                    m_ptrImageBuf[ptrSrcData + k + m_nBlockArea*3] += m_ptrImageBuf[ptrSrcData+k];
                    k += m_nBlockArea * 4;
                }
            }
            var pIQParam = new int[16]; // within m_ptrIQParamBuf
            pIQParam[0] = pIQParam[1] = pIQParam[2] = pIQParam[3] = pIQParamPtr[0];
            if (EriSampling.YUV_4_4_4 == m_info.SamplingFlags)
            {
                for (i = 4; i < 12; ++i)
                {
                    pIQParam[i] = pIQParamPtr[1];
                }
                for (i = 12; i < m_nBlocksetCount; ++i)
                {
                    pIQParam[i] = pIQParamPtr[0];
                }
            }
            else
            {
                pIQParam[4] = pIQParam[5] = pIQParamPtr[1];
                for (i = 6; i < m_nBlocksetCount; ++i)
                {
                    pIQParam[i] = pIQParamPtr[0];
                }
            }
            int pArrange = m_pArrangeTable[0];
            for (i = 0; i < m_nBlocksetCount; ++i)
            {
                float[] ptrDst = m_ptrBlocksetBuf[i];
                Erisa.ConvertArraySByteToFloat (m_ptrMatrixBuf, m_ptrImageBuf, ptrSrcData, m_nBlockArea);
                ptrSrcData += m_nBlockArea;
                Erisa.VectorMultiply (m_ptrMatrixBuf, m_ptrIQParamBuf, pIQParam[i], m_nBlockArea);
                for (j = 0; j < m_nBlockArea; ++j)
                {
                    ptrDst[m_ptrTable[pArrange + j]] = m_ptrMatrixBuf[j];
                }
            }
        }

        void MatrixIDCT8x8 (float[] matrix, int index)
        {
            for (int i = 0; i < m_nBlocksetCount; i ++ )
            {
                Erisa.FastIDCT8x8 (m_ptrBlocksetBuf[i]);
            }
        }

        void MatrixILOT8x8 (float[] matrix, int ptrVertBufLOT)
        {
            int i, j, k, l = 0;
            int ptrHorzBufLOT = 0; // m_ptrHorzBufLOT;
            for (i = 0; i < 2; ++i)
            {
                for (j = 0; j < 2; ++j)
                {
                    Erisa.FastILOT8x8 (m_ptrBlocksetBuf[l], m_ptrHorzBufLOT, ptrHorzBufLOT, matrix, ptrVertBufLOT + j * m_nBlockArea);
                    ++l;
                }
                ptrHorzBufLOT += m_nBlockArea;
            }
            ptrVertBufLOT += m_nBlockArea * 2;
            if (m_nChannelCount < 3)
                return;

            if (EriSampling.YUV_4_4_4 == m_info.SamplingFlags)
            {
                for (k = 0; k < 2; k++)
                {
                    for (i = 0; i < 2; i++)
                    {
                        for (j = 0; j < 2; j++)
                        {
                            Erisa.FastILOT8x8 (m_ptrBlocksetBuf[l], m_ptrHorzBufLOT, ptrHorzBufLOT, matrix, ptrVertBufLOT + j * m_nBlockArea );
                            l++;
                        }
                        ptrHorzBufLOT += m_nBlockArea;
                    }
                    ptrVertBufLOT += m_nBlockArea * 2;
                }
            }
            else if (EriSampling.YUV_4_1_1 == m_info.SamplingFlags)
            {
                for (k = 0; k < 2; k++)
                {
                    Erisa.FastILOT8x8 (m_ptrBlocksetBuf[l], m_ptrHorzBufLOT, ptrHorzBufLOT, matrix, ptrVertBufLOT);
                    l++;
                    ptrHorzBufLOT += m_nBlockArea;
                    ptrVertBufLOT += m_nBlockArea;
                }
            }
            else
                return;
            if (m_nChannelCount < 4)
                return;

            for (i = 0; i < 2; i++)
            {
                for (j = 0; j < 2; j++)
                {
                    Erisa.FastILOT8x8 (m_ptrBlocksetBuf[l], m_ptrHorzBufLOT, ptrHorzBufLOT, matrix, ptrVertBufLOT + j * m_nBlockArea);
                    l++;
                }
                ptrHorzBufLOT += m_nBlockArea;
            }
            ptrVertBufLOT += m_nBlockArea * 2;
        }

        void BlockScaling444 (int x, int y)
        {
            int nBlockOffset = m_info.Transformation == CvType.LOT_ERI ? 1 : 0;
            for (int i = 0; i < 2; i++)
            {
                int yPos = y * 2 + i - nBlockOffset;
                if (yPos < 0)
                {
                    continue;
                }
                for (int j = 0; j < 2; j++)
                {
                    int xPos = x * 2 + j - nBlockOffset;
                    if (xPos < 0)
                        continue;
                    int k = i * 2 + j;
                    if (null != m_src_frame)
                    {
                        Erisa.ConvertArrayFloatToSByte (m_ptrDecodeBuf, m_ptrBlocksetBuf[k], m_nBlockArea);
                    }
                    else
                    {
                        Erisa.ConvertArrayFloatToByte (m_ptrDecodeBuf, m_ptrBlocksetBuf[k], m_nBlockArea);
                    }
                    StoreYUVImageChannel (xPos, yPos, 0);

                    if (m_nChannelCount < 3)
                    {
                        continue;
                    }
                    Erisa.ConvertArrayFloatToSByte (m_ptrDecodeBuf, m_ptrBlocksetBuf[k + 4], m_nBlockArea );
                    StoreYUVImageChannel (xPos, yPos, 1);

                    Erisa.ConvertArrayFloatToSByte (m_ptrDecodeBuf, m_ptrBlocksetBuf[k + 8], m_nBlockArea);
                    StoreYUVImageChannel (xPos, yPos, 2);

                    if (m_nChannelCount < 4)
                        continue;
                    if (null != m_src_frame)
                    {
                        Erisa.ConvertArrayFloatToSByte (m_ptrDecodeBuf, m_ptrBlocksetBuf[k + 12], m_nBlockArea);
                    }
                    else
                    {
                        Erisa.ConvertArrayFloatToByte (m_ptrDecodeBuf, m_ptrBlocksetBuf[k + 12], m_nBlockArea);
                    }
                    StoreYUVImageChannel (xPos, yPos, 3);
                }
            }
        }

        void BlockScaling411 (int x, int y)
        {
            int nBlockOffset = m_info.Transformation == CvType.LOT_ERI ? 1 : 0;
            for (int i = 0; i < 2; i++)
            {
                int yPos = y * 2 + i - nBlockOffset * 2;
                if (yPos < 0)
                    continue;
                for (int j = 0; j < 2; j++)
                {
                    int xPos = x * 2 + j - nBlockOffset * 2;
                    if (xPos < 0)
                        continue;
                    int k = i * 2 + j;
                    if (null != m_src_frame)
                    {
                        Erisa.ConvertArrayFloatToSByte (m_ptrDecodeBuf, m_ptrBlocksetBuf[k], m_nBlockArea );
                    }
                    else
                    {
                        Erisa.ConvertArrayFloatToByte (m_ptrDecodeBuf, m_ptrBlocksetBuf[k], m_nBlockArea );
                    }
                    StoreYUVImageChannel (xPos, yPos, 0);

                    if (m_nChannelCount < 4)
                        continue;
                    if (null != m_src_frame)
                    {
                        Erisa.ConvertArrayFloatToSByte (m_ptrDecodeBuf, m_ptrBlocksetBuf[k + 6], m_nBlockArea);
                    }
                    else
                    {
                        Erisa.ConvertArrayFloatToByte (m_ptrDecodeBuf, m_ptrBlocksetBuf[k + 6], m_nBlockArea);
                    }
                    StoreYUVImageChannel (xPos, yPos, 3);
                }
            }
            if (m_nChannelCount < 3)
                return;

            y -= nBlockOffset;
            x -= nBlockOffset;
            if (y < 0 || x < 0)
                return;

            Erisa.ConvertArrayFloatToSByte (m_ptrDecodeBuf, m_ptrBlocksetBuf[4], m_nBlockArea);
            StoreYUVImageChannelX2 (x, y, 1);

            Erisa.ConvertArrayFloatToSByte (m_ptrDecodeBuf, m_ptrBlocksetBuf[5], m_nBlockArea);
            StoreYUVImageChannelX2 (x, y, 2);
        }

        void ConvertImageYUVtoRGB (bool differential = false)
        {
            if (m_nChannelCount < 3)
                return;

            int nPixelBytes = m_nYUVPixelBytes;
            int nWidth = m_nDstWidth;
            int nHeight = m_nDstHeight;
            int ptrYUVLine = 0; // m_ptrYUVImage

            for (uint y = 0; y < nHeight; ++y)
            {
                int ptrYUVPixel = ptrYUVLine;
                if (differential)
                {
                    for (uint x = 0; x < nWidth; ++x)
                    {
                        int Cy = m_ptrYUVImage[ptrYUVPixel];
                        int u = m_ptrYUVImage[ptrYUVPixel+1];
                        int v = m_ptrYUVImage[ptrYUVPixel+2];
                        int b = Cy + ((u * 7) >> 2) + 0x80;
                        int g = Cy - ((u * 3 + v * 6) >> 3) + 0x80;
                        int r = Cy + ((v * 3) >> 1) + 0x80;
                        if ((uint)b > 0xFF)
                        {
                            b = (~b >> 31) & 0xFF;
                        }
                        m_ptrYUVImage[ptrYUVPixel] = (sbyte)(b - 0x80);
                        if ((uint)g > 0xFF)
                        {
                            g = (~g >> 31) & 0xFF;
                        }
                        m_ptrYUVImage[ptrYUVPixel+1] = (sbyte)(g - 0x80);
                        if ((uint)r > 0xFF)
                        {
                            r = (~r >> 31) & 0xFF;
                        }
                        m_ptrYUVImage[ptrYUVPixel+2] = (sbyte)(r - 0x80);
                        ptrYUVPixel += nPixelBytes;
                    }
                }
                else
                {
                    for (uint x = 0; x < nWidth; ++x)
                    {
                        int Cy = (byte)m_ptrYUVImage[ptrYUVPixel];
                        int u = m_ptrYUVImage[ptrYUVPixel+1];
                        int v = m_ptrYUVImage[ptrYUVPixel+2];
                        int b = Cy + ((u * 7) >> 2);
                        int g = Cy - ((u * 3 + v * 6) >> 3);
                        int r = Cy + ((v * 3) >> 1);
                        if ((uint)b > 0xFF)
                        {
                            b = (~b >> 31) & 0xFF;
                        }
                        m_ptrYUVImage[ptrYUVPixel] = (sbyte)b;
                        if ((uint)g > 0xFF)
                        {
                            g = (~g >> 31) & 0xFF;
                        }
                        m_ptrYUVImage[ptrYUVPixel+1] = (sbyte)g;
                        if ((uint)r > 0xFF)
                        {
                            r = (~r >> 31) & 0xFF;
                        }
                        m_ptrYUVImage[ptrYUVPixel+2] = (sbyte)r;
                        ptrYUVPixel += nPixelBytes;
                    }
                }
                ptrYUVLine += m_nYUVLineBytes;
            }
        }

        void StoreYUVImageChannel (int xBlock, int yBlock, int iChannel)
        {
            int nPixelBytes = m_nYUVPixelBytes;
            int nBlockSize = m_nBlockSize;
            int nBlockArea = m_nBlockArea;
            int ptrDstYUV = (yBlock * nBlockSize * m_nYUVLineBytes)
                          + (xBlock * nBlockSize * nPixelBytes) + iChannel;
            int ptrSrcYUV = 0; // m_ptrDecodeBuf;

            for (int y = 0; y < nBlockSize; y++)
            {
                int ptrDstLine = ptrDstYUV;
                for (int x = 0; x < nBlockSize; x++)
                {
                    m_ptrYUVImage[ptrDstLine] = m_ptrDecodeBuf[ptrSrcYUV++];
                    ptrDstLine += nPixelBytes;
                }
                ptrDstYUV += m_nYUVLineBytes;
            }
        }

        void StoreYUVImageChannelX2 (int xBlock, int yBlock, int iChannel)
        {
            int nPixelBytes = m_nYUVPixelBytes;
            int nLineBytes = m_nYUVLineBytes;
            int nBlockSize = m_nBlockSize;
            int nBlockArea = m_nBlockArea;
            int ptrDstYUV = (yBlock * nBlockSize * 2 * nLineBytes)
                          + (xBlock * nBlockSize * 2 * nPixelBytes) + iChannel;
            int ptrSrcYUV = 0; // m_ptrDecodeBuf;

            for (int y = 0; y < nBlockSize; y++)
            {
                int ptrDstLine = ptrDstYUV;
                for (int x = 0; x < nBlockSize; x++)
                {
                    sbyte d = m_ptrDecodeBuf[ptrSrcYUV++];
                    m_ptrYUVImage[ptrDstLine + nLineBytes + nPixelBytes] = d;
                    m_ptrYUVImage[ptrDstLine + nLineBytes] = d;
                    m_ptrYUVImage[ptrDstLine + nPixelBytes] = d;
                    m_ptrYUVImage[ptrDstLine] = d;
                    ptrDstLine += nPixelBytes * 2;
                }
                ptrDstYUV += nLineBytes * 2;
            }
        }
    }

    internal static class Erina
    {
        public const int CodeFlag      = int.MinValue;
        public const int HuffmanEscape = 0x7FFFFFFF;
        public const int HuffmanNull   = 0x8000;
        public const int HuffmanMax    = 0x4000;
        public const int HuffmanRoot   = 0x200;
    }

    internal class HuffmanNode
    {
        public ushort  Weight;
        public ushort  Parent;
        public int     ChildCode;

        public void CopyFrom (HuffmanNode other)
        {
            this.Weight = other.Weight;
            this.Parent = other.Parent;
            this.ChildCode = other.ChildCode;
        }
    }

    internal class HuffmanTree
    {
        public HuffmanNode[]    m_hnTree = new HuffmanNode[0x201];
        public int[]            m_iSymLookup = new int[0x100];
        public int              m_iEscape;
        public int              m_iTreePointer;

        public HuffmanTree ()
        {
            Initialize();
        }

        public void Initialize ()
        {
            for (int i = 0; i < 0x201; i++)
            {
                m_hnTree[i] = new HuffmanNode();
            }
            for (int i = 0; i < 0x100; i++)
            {
                m_iSymLookup[i] = Erina.HuffmanNull;
            }
            m_iEscape = Erina.HuffmanNull;
            m_iTreePointer = Erina.HuffmanRoot;
            m_hnTree[Erina.HuffmanRoot].Weight = 0;
            m_hnTree[Erina.HuffmanRoot].Parent = Erina.HuffmanNull;
            m_hnTree[Erina.HuffmanRoot].ChildCode = Erina.HuffmanNull;
        }

        public void IncreaseOccuredCount (int iEntry)
        {
            m_hnTree[iEntry].Weight++;
            Normalize (iEntry);
            if (m_hnTree[Erina.HuffmanRoot].Weight >= Erina.HuffmanMax)
            {
                HalfAndRebuild();
            }
        }

        private void RecountOccuredCount (int iParent)
        {
            int iChild = m_hnTree[iParent].ChildCode;
            m_hnTree[iParent].Weight = (ushort)(m_hnTree[iChild].Weight + m_hnTree[iChild + 1].Weight);
        }

        private void Normalize (int iEntry)
        {
            while (iEntry < Erina.HuffmanRoot)
            {
                int iSwap = iEntry + 1;
                ushort weight = m_hnTree[iEntry].Weight;
                while (iSwap < Erina.HuffmanRoot)
                {
                    if (m_hnTree[iSwap].Weight >= weight)
                        break;
                    ++iSwap;
                }
                if (iEntry == --iSwap)
                {
                    iEntry = m_hnTree[iEntry].Parent;
                    RecountOccuredCount (iEntry);
                    continue;
                }
                int iChild, nCode;
                if (0 == (m_hnTree[iEntry].ChildCode & Erina.CodeFlag))
                {
                    iChild = m_hnTree[iEntry].ChildCode;
                    m_hnTree[iChild].Parent = (ushort)iSwap;
                    m_hnTree[iChild + 1].Parent = (ushort)iSwap;
                }
                else
                {
                    nCode = m_hnTree[iEntry].ChildCode & ~Erina.CodeFlag;
                    if (nCode != Erina.HuffmanEscape)
                        m_iSymLookup[nCode & 0xFF] = iSwap;
                    else
                        m_iEscape = iSwap;
                }
                if (0 == (m_hnTree[iSwap].ChildCode & Erina.CodeFlag))
                {
                    iChild = m_hnTree[iSwap].ChildCode;
                    m_hnTree[iChild].Parent = (ushort)iEntry;
                    m_hnTree[iChild+1].Parent = (ushort)iEntry;
                }
                else
                {
                    nCode = m_hnTree[iSwap].ChildCode & ~Erina.CodeFlag;
                    if (nCode != Erina.HuffmanEscape)
                        m_iSymLookup[nCode & 0xFF] = iEntry;
                    else
                        m_iEscape = iEntry;
                }
                var node = m_hnTree[iSwap]; // XXX
                ushort iEntryParent = m_hnTree[iEntry].Parent;
                ushort iSwapParent  = m_hnTree[iSwap].Parent;

                m_hnTree[iSwap] = m_hnTree[iEntry];
                m_hnTree[iEntry] = node;
                m_hnTree[iSwap].Parent = iSwapParent;
                m_hnTree[iEntry].Parent = iEntryParent;

                RecountOccuredCount (iSwapParent);
                iEntry = iSwapParent;
            }
        }

        public void AddNewEntry (int nNewCode)
        {
            if (m_iTreePointer > 0)
            {
                m_iTreePointer -= 2;
                int i = m_iTreePointer;
                var phnNew = m_hnTree[i];
                phnNew.Weight = 1;
                phnNew.ChildCode = Erina.CodeFlag | nNewCode;
                m_iSymLookup[nNewCode & 0xFF] = i;

                var phnRoot = m_hnTree[Erina.HuffmanRoot];
                if (phnRoot.ChildCode != Erina.HuffmanNull)
                {
                    var phnParent = m_hnTree[i + 2];
                    var phnChild  = m_hnTree[i + 1];
                    phnChild.CopyFrom (phnParent); // m_hnTree[i + 1] = m_hnTree[i + 2];

                    if (0 != (phnChild.ChildCode & Erina.CodeFlag))
                    {
                        int nCode = phnChild.ChildCode & ~Erina.CodeFlag;
                        if (nCode != Erina.HuffmanEscape)
                            m_iSymLookup[nCode & 0xFF] = i + 1;
                        else
                            m_iEscape = i + 1;
                    }
                    phnParent.Weight = (ushort)(phnNew.Weight + phnChild.Weight);
                    phnParent.Parent = phnChild.Parent;
                    phnParent.ChildCode = i;

                    phnNew.Parent = phnChild.Parent = (ushort)(i + 2);
                    Normalize (i + 2);
                }
                else
                {
                    phnNew.Parent = Erina.HuffmanRoot;
                    m_iEscape = i + 1;
                    var phnEscape = m_hnTree[m_iEscape];
                    phnEscape.Weight = 1;
                    phnEscape.Parent = Erina.HuffmanRoot;
                    phnEscape.ChildCode = Erina.CodeFlag | Erina.HuffmanEscape;

                    phnRoot.Weight = 2;
                    phnRoot.ChildCode = i;
                }
            }
            else
            {
                int i = m_iTreePointer;
                var phnEntry = m_hnTree[i];
                if (phnEntry.ChildCode == (Erina.CodeFlag | Erina.HuffmanEscape))
                {
                    phnEntry = m_hnTree[i + 1];
                }
                phnEntry.ChildCode = Erina.CodeFlag | nNewCode;
            }
        }

        private void HalfAndRebuild ()
        {
            int i;
            int iNextEntry = Erina.HuffmanRoot;
            for (i = Erina.HuffmanRoot - 1; i >= m_iTreePointer; i--)
            {
                if (0 != (m_hnTree[i].ChildCode & Erina.CodeFlag))
                {
                    m_hnTree[i].Weight = (ushort)((m_hnTree[i].Weight + 1) >> 1);
                    m_hnTree[iNextEntry--].CopyFrom (m_hnTree[i]);
                }
            }
            ++iNextEntry;

            int iChild, nCode;
            i = m_iTreePointer;
            for (;;)
            {
                m_hnTree[i].CopyFrom (m_hnTree[iNextEntry]);
                m_hnTree[i + 1].CopyFrom (m_hnTree[iNextEntry + 1]);
                iNextEntry += 2;
                var phnChild1 = m_hnTree[i];
                var phnChild2 = m_hnTree[i + 1];

                if (0 == (phnChild1.ChildCode & Erina.CodeFlag))
                {
                    iChild = phnChild1.ChildCode; 
                    m_hnTree[iChild].Parent = (ushort)i;
                    m_hnTree[iChild + 1].Parent = (ushort)i;
                }
                else
                {
                    nCode = phnChild1.ChildCode & ~Erina.CodeFlag;
                    if (Erina.HuffmanEscape == nCode)
                        m_iEscape = i;
                    else
                        m_iSymLookup[nCode & 0xFF] = i;
                }
                if (0 == (phnChild2.ChildCode & Erina.CodeFlag))
                {
                    iChild = phnChild2.ChildCode;
                    m_hnTree[iChild].Parent = (ushort)(i + 1);
                    m_hnTree[iChild + 1].Parent = (ushort)(i + 1);
                }
                else
                {
                    nCode = phnChild2.ChildCode & ~Erina.CodeFlag;
                    if (Erina.HuffmanEscape == nCode)
                        m_iEscape = i + 1;
                    else
                        m_iSymLookup[nCode & 0xFF] = i + 1;
                }
                ushort weight = (ushort)(phnChild1.Weight + phnChild2.Weight);

                if (iNextEntry <= Erina.HuffmanRoot)
                {
                    int j = iNextEntry;
                    for (;;)
                    {
                        if (weight <= m_hnTree[j].Weight)
                        {
                            m_hnTree[j - 1].Weight = weight;
                            m_hnTree[j - 1].ChildCode = i;
                            break;
                        }
                        m_hnTree[j - 1].CopyFrom (m_hnTree[j]);
                        if (++j > Erina.HuffmanRoot)
                        {
                            m_hnTree[Erina.HuffmanRoot].Weight = weight;
                            m_hnTree[Erina.HuffmanRoot].ChildCode = i;
                            break;
                        }
                    }
                    --iNextEntry;
                }
                else
                {
                    m_hnTree[Erina.HuffmanRoot].Weight = weight;
                    m_hnTree[Erina.HuffmanRoot].Parent = Erina.HuffmanNull;
                    m_hnTree[Erina.HuffmanRoot].ChildCode = i;
                    phnChild1.Parent = Erina.HuffmanRoot;
                    phnChild2.Parent = Erina.HuffmanRoot;
                    break;
                }
                i += 2;
            }
        }
    }

    internal class RLEDecodeContext : ERISADecodeContext
    {
        protected int     m_flgZero;
        protected uint    m_nLength;

        public RLEDecodeContext (uint nBufferingSize) : base (nBufferingSize)
        {
        }

        public void InitGammaContext ()
        {
            m_flgZero = GetABit();
            m_nLength = 0;
        }

        public override uint DecodeBytes (Array ptrDst, uint nCount)
        {
            return DecodeGammaCodeBytes (ptrDst as sbyte[], nCount);
        }

        public uint DecodeGammaCodeBytes (sbyte[] ptrDst, uint nCount)
        {
            int     dst = 0;
            uint    nDecoded = 0;

            if (m_nLength == 0)
            {
                m_nLength = (uint)GetGammaCode();
                if (0 == m_nLength)
                {
                    return nDecoded;
                }
            }
            for (;;)
            {
                uint nRepeat = Math.Min (m_nLength, nCount);
                Debug.Assert (nRepeat > 0);
                m_nLength -= nRepeat;
                nCount -= nRepeat;

                if (0 == m_flgZero)
                {
                    nDecoded += nRepeat;
                    do
                    {
                        ptrDst[dst++] = 0;
                    }
                    while (0 != --nRepeat);
                }
                else
                {
                    do
                    {
                        sbyte nSign = (sbyte)GetABit();
                        sbyte nCode = (sbyte)GetGammaCode();
                        if (0 == nCode)
                        {
                            return nDecoded;
                        }
                        nDecoded ++;
                        ptrDst[dst++] = (sbyte)((nCode ^ nSign) - nSign);
                    }
                    while (0 != --nRepeat);
                }
                if (0 == nCount)
                {
                    if (0 == m_nLength)
                    {
                        m_flgZero = ~m_flgZero;
                    }
                    return nDecoded;
                }
                m_flgZero = ~m_flgZero;
                m_nLength = (uint) GetGammaCode();
                if (0 == m_nLength)
                {
                    return nDecoded;
                }
            }
        }

        protected int GetGammaCode()
        {
            if (!PrefetchBuffer())
            {
                return 0;
            }
            m_nIntBufCount--;
            uint dwIntBuf = m_dwIntBuffer;
            m_dwIntBuffer <<= 1;
            if (0 == (dwIntBuf & 0x80000000))
            {
                return 1;
            }
            if (!PrefetchBuffer())
            {
                return 0;
            }
            int nCode = 0;
            if ((0 != (~m_dwIntBuffer & 0x55000000)) && (m_nIntBufCount >= 8))
            {
                uint i = (m_dwIntBuffer >> 24) << 1;
                nCode = nGammaCodeLookup[i];
                int nBitCount = nGammaCodeLookup[i + 1];
                Debug.Assert (nBitCount <= m_nIntBufCount);
                Debug.Assert (nCode > 0);
                m_nIntBufCount -= nBitCount;
                m_dwIntBuffer <<= nBitCount;
                return nCode;
            }
            int nBase = 2;
            for (;;)
            {
                if (m_nIntBufCount >= 2)
                {
                    dwIntBuf = m_dwIntBuffer;
                    m_dwIntBuffer <<= 2;
                    nCode = (int)(((uint)nCode << 1) | (dwIntBuf >> 31));
                    m_nIntBufCount -= 2;
                    if (0 == (dwIntBuf & 0x40000000))
                    {
                        return nCode + nBase;
                    }
                    nBase <<= 1;
                }
                else
                {
                    if (!PrefetchBuffer())
                    {
                        return 0;
                    }
                    nCode = (int)(((uint)nCode << 1) | (m_dwIntBuffer >> 31));
                    m_nIntBufCount --;
                    m_dwIntBuffer <<= 1;

                    if (!PrefetchBuffer())
                    {
                        return 0;
                    }
                    dwIntBuf = m_dwIntBuffer;
                    m_nIntBufCount --;
                    m_dwIntBuffer <<= 1;
                    if (0 == (dwIntBuf & 0x80000000))
                    {
                        return nCode + nBase;
                    }
                    nBase <<= 1;
                }
            }
        }

        static readonly byte[] nGammaCodeLookup = new byte[0x200]
        {
            2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,
            2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,
            2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,
            2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,
            2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,
            2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,
            2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,
            2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,   2,  2,
            4,  4,   4,  4,   4,  4,   4,  4,   4,  4,   4,  4,   4,  4,   4,  4,
            4,  4,   4,  4,   4,  4,   4,  4,   4,  4,   4,  4,   4,  4,   4,  4,
            8,  6,   8,  6,   8,  6,   8,  6,  16,  8,  0xff, 0xff,  17,  8,  0xff, 0xff,
            9,  6,   9,  6,   9,  6,   9,  6,  18,  8,  0xff, 0xff,  19,  8,  0xff, 0xff,
            5,  4,   5,  4,   5,  4,   5,  4,   5,  4,   5,  4,   5,  4,   5,  4,
            5,  4,   5,  4,   5,  4,   5,  4,   5,  4,   5,  4,   5,  4,   5,  4,
            10,  6,  10,  6,  10,  6,  10,  6,  20,  8,  0xff, 0xff,  21,  8,  0xff, 0xff,
            11,  6,  11,  6,  11,  6,  11,  6,  22,  8,  0xff, 0xff,  23,  8,  0xff, 0xff,
            3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,
            3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,
            3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,
            3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,
            3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,
            3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,
            3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,
            3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,   3,  2,
            6,  4,   6,  4,   6,  4,   6,  4,   6,  4,   6,  4,   6,  4,   6,  4,
            6,  4,   6,  4,   6,  4,   6,  4,   6,  4,   6,  4,   6,  4,   6,  4,
            12,  6,  12,  6,  12,  6,  12,  6,  24,  8,  0xff, 0xff,  25,  8,  0xff, 0xff,
            13,  6,  13,  6,  13,  6,  13,  6,  26,  8,  0xff, 0xff,  27,  8,  0xff, 0xff,
            7,  4,   7,  4,   7,  4,   7,  4,   7,  4,   7,  4,   7,  4,   7,  4,
            7,  4,   7,  4,   7,  4,   7,  4,   7,  4,   7,  4,   7,  4,   7,  4,
            14,  6,  14,  6,  14,  6,  14,  6,  28,  8,  0xff, 0xff,  29,  8,  0xff, 0xff,
            15,  6,  15,  6,  15,  6,  15,  6,  30,  8,  0xff, 0xff,  31,  8,  0xff, 0xff
        };
    }

    internal class HuffmanDecodeContext : RLEDecodeContext
    {
        int             m_dwERINAFlags;
        HuffmanTree     m_pLastHuffmanTree;
        HuffmanTree[]   m_ppHuffmanTree;

        // ERINAEncodingFlag
        public const int efERINAOrder0 = 0x0000;
        public const int efERINAOrder1 = 0x0001;

        public HuffmanDecodeContext (uint nBufferingSize) : base (nBufferingSize)
        {
        }

        public void PrepareToDecodeERINACode (int flags = efERINAOrder1)
        {
            int i;
            if (null == m_ppHuffmanTree)
            {
                m_ppHuffmanTree = new HuffmanTree[0x101];
            }
            m_dwERINAFlags = flags;
            m_nLength = 0;
            if (efERINAOrder0 == flags)
            {
                m_ppHuffmanTree[0] = new HuffmanTree();
                m_ppHuffmanTree[0x100] = new HuffmanTree();
                for (i = 1; i < 0x100; i++)
                {
                    m_ppHuffmanTree[i] = m_ppHuffmanTree[0];
                }
            }
            else
            {
                for (i = 0; i < 0x101; i++)
                {
                    m_ppHuffmanTree[i] = new HuffmanTree();
                }
            }
            m_pLastHuffmanTree = m_ppHuffmanTree[0];
        }

        public override uint DecodeBytes (Array ptrDst, uint nCount)
        {
            return DecodeErinaCodeBytes (ptrDst as sbyte[], nCount);
        }

        public uint DecodeErinaCodeBytes (sbyte[] ptrDst, uint nCount)
        {
            var tree = m_pLastHuffmanTree;
            int symbol, length;
            uint i = 0;
            if (m_nLength > 0)
            {
                length = (int)Math.Min (m_nLength, nCount);
                m_nLength -= (uint)length;
                do
                {
                    ptrDst[i++] = 0;
                }
                while (0 != --length);
            }
            while (i < nCount)
            {
                symbol = GetHuffmanCode (tree);
                if (Erina.HuffmanEscape == symbol)
                {
                    break;
                }
                ptrDst[i++] = (sbyte)symbol;

                if (0 == symbol)
                {
                    length = GetLengthHuffman (m_ppHuffmanTree[0x100]);
                    if (Erina.HuffmanEscape == length)
                    {
                        break;
                    }
                    if (0 != --length)
                    {
                        m_nLength = (uint)length;
                        if (i + length > nCount)
                        {
                            length = (int)(nCount - i);
                        }
                        m_nLength -= (uint)length;
                        while (length > 0)
                        {
                            ptrDst[i++] = 0;
                            --length;
                        }
                    }
                }
                tree = m_ppHuffmanTree[symbol & 0xFF];
            }
            m_pLastHuffmanTree = tree;
            return i;
        }

        private int GetLengthHuffman (HuffmanTree tree)
        {
            int nCode;
            if (tree.m_iEscape != Erina.HuffmanNull)
            {
                int iEntry = Erina.HuffmanRoot;
                int iChild = tree.m_hnTree[Erina.HuffmanRoot].ChildCode;
                do
                {
                    if (!PrefetchBuffer())
                    {
                        return Erina.HuffmanEscape;
                    }
                    iEntry = iChild + (int)(m_dwIntBuffer >> 31);
                    iChild = tree.m_hnTree[iEntry].ChildCode;
                    m_dwIntBuffer <<= 1;
                    --m_nIntBufCount;
                }
                while (0 == (iChild & Erina.CodeFlag));

                if ((m_dwERINAFlags != efERINAOrder0) ||
                    (tree.m_hnTree[Erina.HuffmanRoot].Weight < Erina.HuffmanMax-1))
                {
                    tree.IncreaseOccuredCount (iEntry);
                }
                nCode = iChild & ~Erina.CodeFlag;
                if (nCode != Erina.HuffmanEscape)
                {
                    return nCode;
                }
            }
            nCode = GetGammaCode();
            if (-1 == nCode)
            {
                return Erina.HuffmanEscape;
            }
            tree.AddNewEntry (nCode);
            return nCode;
        }

        public int GetHuffmanCode (HuffmanTree tree)
        {
            int nCode;
            if (tree.m_iEscape != Erina.HuffmanNull)
            {
                int iEntry = Erina.HuffmanRoot;
                int iChild = tree.m_hnTree[Erina.HuffmanRoot].ChildCode;
                do
                {
                    if (!PrefetchBuffer())
                    {
                        return Erina.HuffmanEscape;
                    }
                    iEntry = iChild + (int)(m_dwIntBuffer >> 31);
                    iChild = tree.m_hnTree[iEntry].ChildCode;
                    m_dwIntBuffer <<= 1;
                    --m_nIntBufCount;
                }
                while (0 == (iChild & Erina.CodeFlag));

                if ((m_dwERINAFlags != efERINAOrder0) ||
                    (tree.m_hnTree[Erina.HuffmanRoot].Weight < Erina.HuffmanMax-1))
                {
                    tree.IncreaseOccuredCount (iEntry);
                }
                nCode = iChild & ~Erina.CodeFlag;
                if (nCode != Erina.HuffmanEscape)
                {
                    return  nCode;
                }
            }
            nCode = (int)GetNBits (8);
            tree.AddNewEntry (nCode);

            return nCode;
        }
    }

    internal class ProbDecodeContext : RLEDecodeContext
    {
        protected uint      m_dwCodeRegister;
        protected uint      m_dwAugendRegister;
        protected int       m_nPostBitCount;

        protected ErisaProbModel m_pPhraseLenProb = new ErisaProbModel();
        protected ErisaProbModel m_pPhraseIndexProb = new ErisaProbModel();
        protected ErisaProbModel m_pRunLenProb = new ErisaProbModel();
        protected ErisaProbModel m_pLastERISAProb;
        protected ErisaProbModel[] m_ppTableERISA;

        public ProbDecodeContext (uint nBufferingSize) : base (nBufferingSize)
        {
        }

        public void PrepareToDecodeERISACode ()
        {
            if (null == m_ppTableERISA)
            {
                m_ppTableERISA = new ErisaProbModel[0x101];
                for (int i = 0; i < 0x101; ++i)
                {
                    m_ppTableERISA[i] = new ErisaProbModel();
                    m_ppTableERISA[i].Initialize();
                }
            }
            m_pLastERISAProb = m_ppTableERISA[0];
            m_pPhraseLenProb.Initialize();
            m_pPhraseIndexProb.Initialize();
            m_pRunLenProb.Initialize();

            InitializeERISACode();
        }

        public override uint DecodeBytes (Array ptrDst, uint nCount)
        {
            return DecodeERISACodeBytes (ptrDst as sbyte[], nCount);
        }

        uint DecodeERISACodeBytes (sbyte[] ptrDst, uint nCount)
        {
            var pProb = m_pLastERISAProb;
            int nSymbol, iSym;
            uint i = 0;
            while (i < nCount)
            {
                if (m_nLength > 0)
                {
                    uint nCurrent = nCount - i;
                    if (nCurrent > m_nLength)
                        nCurrent = m_nLength;
                    m_nLength -= nCurrent;
                    for (uint j = 0; j < nCurrent; j++)
                    {
                        ptrDst[i++] = 0;
                    }
                    continue;
                }
                iSym = DecodeERISACodeIndex (pProb);
                if (iSym < 0)
                    break;
                nSymbol = pProb.SymTable[iSym].Symbol;
                pProb.IncreaseSymbol (iSym);
                ptrDst[i++] = (sbyte)nSymbol;

                if (0 == nSymbol)
                {
                    iSym = DecodeERISACodeIndex (m_pRunLenProb);
                    if (iSym < 0)
                        break;
                    m_nLength = (uint)m_pRunLenProb.SymTable[iSym].Symbol;
                    m_pRunLenProb.IncreaseSymbol (iSym);
                }
                pProb = m_ppTableERISA[nSymbol & 0xFF];
            }
            m_pLastERISAProb = pProb;
            return i;
        }

        void InitializeERISACode ()
        {
            m_nLength = 0;
            m_dwCodeRegister = GetNBits (32);
            m_dwAugendRegister = 0xFFFF;
            m_nPostBitCount = 0;
        }

        public int DecodeERISACode (ErisaProbModel pModel)
        {
            int iSym = DecodeERISACodeIndex (pModel);
            int nSymbol = ErisaProbModel.EscCode;
            if (iSym >= 0)
            {
                nSymbol = pModel.SymTable[iSym].Symbol;
                pModel.IncreaseSymbol (iSym);
            }
            return nSymbol;
        }

        protected int DecodeERISACodeIndex (ErisaProbModel pModel)
        {
            uint dwAcc = m_dwCodeRegister * pModel.TotalCount / m_dwAugendRegister;
            if (dwAcc >= ErisaProbModel.TotalLimit)
            {
                return -1;
            }
            int     iSym = 0;
            ushort wAcc = (ushort)dwAcc;
            ushort wFs = 0;
            ushort wOccured;
            for (;;)
            {
                wOccured = pModel.SymTable[iSym].Occured;
                if (wAcc < wOccured)
                    break;
                wAcc -= wOccured;
                wFs += wOccured;
                if (++iSym >= pModel.SymbolSorts)
                    return -1;
            }
            m_dwCodeRegister -= (m_dwAugendRegister * wFs + pModel.TotalCount - 1) / pModel.TotalCount;
            m_dwAugendRegister = m_dwAugendRegister * wOccured / pModel.TotalCount;
            Debug.Assert (m_dwAugendRegister != 0);

            while (0 == (m_dwAugendRegister & 0x8000))
            {
                int nNextBit = GetABit();
                if (1 == nNextBit)
                {
                    if ((++m_nPostBitCount) >= 256)
                        return -1;
                    nNextBit = 0;
                }
                m_dwCodeRegister = (m_dwCodeRegister << 1) | ((uint)nNextBit & 1);
                m_dwAugendRegister <<= 1;
            }
            m_dwCodeRegister &= 0xFFFF;
            return iSym;
        }
    }

    internal class ErisaProbModel
    {
        public const int TotalLimit    = 0x2000;
        public const int SymbolSortMax = 0x101;
        public const int SubSortMax    = 0x80;
        public const int ProbSlotMax   = 0x800;
        public const short EscCode     = -1;

        internal struct CodeSymbol
        {
            public ushort   Occured;
            public short    Symbol;
        }

        public uint         TotalCount;
        public int          SymbolSorts;
        public CodeSymbol[] SymTable = new CodeSymbol[SymbolSortMax];
        public CodeSymbol[] SubModel = new CodeSymbol[SubSortMax];

        public void Initialize ()
        {
            TotalCount = SymbolSortMax;
            SymbolSorts = SymbolSortMax;

            for (short i = 0; i < 0x100; ++i)
            {
                SymTable[i].Occured = 1;
                SymTable[i].Symbol = i;
            }
            SymTable[0x100].Occured = 1;
            SymTable[0x100].Symbol = EscCode;

            for (short i = 0; i < SubSortMax; ++i)
            {
                SubModel[i].Occured = 0;
                SubModel[i].Symbol = -1;
            }
        }

        public int AccumulateProb (short wSymbol)
        {
            int index = FindSymbol (wSymbol);
            Debug.Assert (index >= 0);
            int occured = SymTable[index].Occured;
            int i = 0;
            while (occured < TotalCount)
            {
                occured <<= 1;
                i++;
            }
            return i;
        }

        public void HalfOccuredCount ()
        {
            TotalCount = 0;
            for (int i = 0; i < SymbolSorts; ++i)
            {
                TotalCount += SymTable[i].Occured = (ushort)((SymTable[i].Occured + 1) >> 1);
            }
            for (int i = 0; i < SubSortMax; ++i)
            {
                SubModel[i].Occured >>= 1;
            }
        }

        public int IncreaseSymbol (int index)
        {
            ushort occured = ++SymTable[index].Occured;
            short symbol = SymTable[index].Symbol;

            while (--index >= 0)
            {
                if (SymTable[index].Occured >= occured)
                    break;
                SymTable[index + 1] = SymTable[index];
            }
            SymTable[++index].Occured = occured;
            SymTable[index].Symbol = symbol;

            if (++TotalCount >= TotalLimit)
            {
                HalfOccuredCount();
            }
            return index;
        }

        public int FindSymbol (short symbol)
        {
            for (int index = 0; index < SymbolSorts; ++index)
            {
                if (SymTable[index].Symbol == symbol)
                    return index;
            }
            return -1;
        }

        public int AddSymbol (short symbol)
        {
            int index = SymbolSorts++;
            TotalCount++;
            SymTable[index].Symbol = symbol;
            SymTable[index].Occured = 1;
            return index;
        }
    }
}
