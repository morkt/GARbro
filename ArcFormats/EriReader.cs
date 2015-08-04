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

        uint            m_nBlockSize;
        uint            m_nBlockArea;
        uint            m_nBlockSamples;
        uint            m_nChannelCount;
        uint            m_nWidthBlocks;
        uint            m_nHeightBlocks;

        int             m_dwBytesPerLine;
        uint            m_dwClippedPixel;

        int             m_ptrDstBlock;
        int             m_nDstLineBytes;
        int             m_nDstPixelBytes;
        uint            m_nDstWidth;
        uint            m_nDstHeight;
        uint            m_fdwDecFlags;

        // buffers for lossless encoding
        byte[]          m_ptrOperations;
        sbyte[]         m_ptrColumnBuf;
        sbyte[]         m_ptrLineBuf;
        sbyte[]         m_ptrDecodeBuf;
        sbyte[]         m_ptrArrangeBuf;
        int[]           m_pArrangeTable = new int[4];

        HuffmanTree     m_pHuffmanTree;

        PtrProcedure[]  m_pfnColorOperation;

        public byte[]           Data { get { return m_output; } }
        public PixelFormat    Format { get; private set; }
        public int            Stride { get { return Math.Abs (m_dwBytesPerLine); } }
        public BitmapPalette Palette { get; private set; }

        public EriReader (Stream stream, EriMetaData info, Color[] palette)
        {
            m_info = info;
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
            switch (m_info.Architecture)
            {
            case EriCode.Nemesis:
                throw new NotSupportedException ("Not supported ERI compression");
            case EriCode.RunlengthHuffman:
            case EriCode.RunlengthGamma:
                break;
            default:
                throw new InvalidFormatException();
            }
            if (0 == m_info.BlockingDegree)
                throw new InvalidFormatException();

            switch (m_info.FormatType & (int)EriImage.TypeMask)
            {
            case (int)EriImage.RGB:
                if (m_info.BPP <= 8)
                    m_nChannelCount = 1;
                else if (0 == (m_info.FormatType & (int)EriImage.WithAlpha))
                    m_nChannelCount = 3;
                else
                    m_nChannelCount = 4;
                break;

            case (int)EriImage.Gray:
                m_nChannelCount = 1;
                break;

            default:
                throw new InvalidFormatException();
            }

            m_nBlockSize = (uint) (1 << m_info.BlockingDegree);
            m_nBlockArea = (uint) (1 << (m_info.BlockingDegree * 2));
            m_nBlockSamples = m_nBlockArea * m_nChannelCount;
            m_nWidthBlocks  = (uint)((m_info.Width  + m_nBlockSize - 1) >> m_info.BlockingDegree);
            m_nHeightBlocks = (uint)((m_info.Height + m_nBlockSize - 1) >> m_info.BlockingDegree);

            m_ptrOperations = new byte[m_nWidthBlocks * m_nHeightBlocks];
            m_ptrColumnBuf  = new sbyte[m_nBlockSize * m_nChannelCount];
            m_ptrLineBuf    = new sbyte[m_nChannelCount * (m_nWidthBlocks << m_info.BlockingDegree)];
            m_ptrDecodeBuf  = new sbyte[m_nBlockSamples];
            m_ptrArrangeBuf = new sbyte[m_nBlockSamples];

            InitializeArrangeTable();
            if (0x00020200 == m_info.Version)
            {
                if (EriCode.RunlengthHuffman == m_info.Architecture)
                {
                    m_pHuffmanTree = new HuffmanTree();
                }
                /*
                else if (EriCode.Nemesis == m_info.Architecture)
                {
                    m_pProbERISA = new ERISA_PROB_MODEL ;
                }
                */
            }
            if (EriCode.RunlengthHuffman == m_info.Architecture)
                m_context = new HuffmanDecodeContext (0x10000);
            else
                m_context = new RLEDecodeContext (0x10000);
        }

        private void InitializeLossy ()
        {
            throw new NotImplementedException ("Lossy ERI compression not implemented");
        }

        int[] m_ptrTable;

        void InitializeArrangeTable ()
        {
            uint i, j, k, l, m;

            m_ptrTable = new int[m_nBlockSamples * 4];
            m_pArrangeTable[0] = 0;
            m_pArrangeTable[1] = (int)m_nBlockSamples;
            m_pArrangeTable[2] = (int)m_nBlockSamples * 2;
            m_pArrangeTable[3] = (int)m_nBlockSamples * 3;

            int ptrNext = m_pArrangeTable[0];
            for (i = 0; i < m_nBlockSamples; ++i)
            {
                m_ptrTable[ptrNext+i] = (int)i;
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
                        m_ptrTable[ptrNext++] = (int)m;
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
                    m_ptrTable[ptrNext++] = (int)k;
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
                        m_ptrTable[ptrNext++] = (int)m;
                        m += m_nBlockArea;
                    }
                }
            }
        }

        private void CreateImageBuffer ()
        {
            uint stride = ((m_info.Width * (uint)m_info.BPP / 8u) + 0x03u) & ~0x03u;
            uint image_bytes = stride * m_info.Height;
            m_output = new byte[image_bytes];
            m_dwBytesPerLine = (int)stride;
            m_dwClippedPixel = 0;
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
            /*
            if (EriCode.Nemesis == m_info.Architecture)
            {
                Debug.Assert (m_pProbERISA != null);
                m_pProbERISA.Initialize();
            }
            */
            int i;
            int ptrNextOperation = 0; // index within m_ptrOperations
            if ((0 != (fEncodeType & 0x01)) && (m_nChannelCount >= 3))
            {
                Debug.Assert (m_info.Architecture != EriCode.Nemesis);
                int nAllBlockCount = (int)(m_nWidthBlocks * m_nHeightBlocks);
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
                if (0 != (fEncodeType & 0x01))
                {
                    context.InitGammaContext();
                }
            }
            else if (EriCode.RunlengthHuffman == m_info.Architecture)
            {
                (context as HuffmanDecodeContext).PrepareToDecodeERINACode();
            }
            /*
            else
            {
                Debug.Assert (EriCode.Nemesis == m_info.Architecture);
                context.PrepareToDecodeERISACode();
            }
            */
            int nWidthSamples = (int)(m_nChannelCount * m_nWidthBlocks * m_nBlockSize);
            for (i = 0; i < nWidthSamples; ++i)
                m_ptrLineBuf[i] = 0;

            int nAllBlockLines = (int)(m_nBlockSize * m_nChannelCount);
            int nLeftHeight = (int)m_info.Height;

            for (int nPosY = 0; nPosY < (int) m_nHeightBlocks; ++nPosY)
            {
                int nColumnBufSamples = (int)(m_nBlockSize * m_nChannelCount);
                for (i = 0; i < nColumnBufSamples; ++i)
                    m_ptrColumnBuf[i] = 0;

                m_ptrDstBlock = m_dst + nPosY * m_dwBytesPerLine * (int)m_nBlockSize;
                m_nDstHeight = m_nBlockSize;
                if ((int)m_nDstHeight > nLeftHeight)
                {
                    m_nDstHeight = (uint)nLeftHeight;
                }
                int nLeftWidth = (int)m_info.Width;
                int ptrNextLineBuf = 0; // m_ptrLineBuf;

                for (int nPosX = 0; nPosX < (int)m_nWidthBlocks; ++nPosX)
                {
                    m_nDstWidth = Math.Min (m_nBlockSize, (uint)nLeftWidth);

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
                        /*
                        else if (m_info.Architecture == EriCode.Nemesis)
                        {
                            dwOperationCode = context.DecodeERISACode (m_pProbERISA);
                        }
                        */
                        else
                        {
                            Debug.Assert (EriCode.RunlengthGamma == m_info.Architecture);
                            dwOperationCode = context.GetNBits (4) | 0xC0;
                            context.InitGammaContext();
                        }
                    }
                    else
                    {
                        if ((int)EriImage.Gray == m_info.FormatType)
                        {
                            dwOperationCode = 0xC0;
                        }
                        else
                        {
                            dwOperationCode = 0x00;
                        }
                        if (0 == (fEncodeType & 0x01) && m_info.Architecture == EriCode.RunlengthGamma)
                        {
                            context.InitGammaContext();
                        }
                    }
                    if (context.DecodeBytes (m_ptrArrangeBuf, m_nBlockSamples) < m_nBlockSamples)
                    {
                        throw new InvalidFormatException();
                    }
                    PerformOperation (dwOperationCode, nAllBlockLines, m_ptrLineBuf, ptrNextLineBuf);
                    ptrNextLineBuf += nColumnBufSamples;

                    pfnRestoreFunc();

                    m_ptrDstBlock += (int)(m_nDstPixelBytes * m_nBlockSize);
                    nLeftWidth -= (int) m_nBlockSize;
                }
                nLeftHeight -= (int)m_nBlockSize;
            }
        }

        private void DecodeLossyImage (HuffmanDecodeContext context)
        {
            throw new NotImplementedException ("Lossy ERI compression not implemented");
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
                Buffer.BlockCopy (m_ptrArrangeBuf, 0, m_ptrDecodeBuf, 0, (int)m_nBlockSamples);
                if (0 == dwOpCode)
                {
                    return;
                }
            }
            else
            {
                int pArrange = m_pArrangeTable[nArrangeCode];
                for (i = 0; i < (int)m_nBlockSamples; i++)
                {
                    m_ptrDecodeBuf[m_ptrTable[pArrange + i]] = m_ptrArrangeBuf[i];
                }
            }
            m_pfnColorOperation[nColorOperation]();

            int ptrNextBuf = 0;     // m_ptrDecodeBuf
            int ptrNextColBuf = 0;  // m_ptrColumnBuf
            if (0 != (nDiffOperation & 0x01))
            {
                for (i = 0; i < nAllBlockLines; i++)
                {
                    sbyte nLastVal = m_ptrColumnBuf[ptrNextColBuf];
                    for (j = 0; j < (int)m_nBlockSize; j++)
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
                    ptrNextBuf += (int)m_nBlockSize;
                }
            }
            int iNextDst = 0;
            for (k = 0; k < (int)m_nChannelCount; k++)
            {
                sbyte[] ptrLastLine = pNextLineBuf;
                int     idxLastLine = iNextLineIdx;
                for (i = 0; i < (int)m_nBlockSize; i++)
                {
                    for (j = 0; j < (int)m_nBlockSize; j++)
                    {
                        m_ptrDecodeBuf[iNextDst+j] += ptrLastLine[idxLastLine+j];
                    }
                    ptrLastLine = m_ptrDecodeBuf;
                    idxLastLine = iNextDst;
                    iNextDst += (int)m_nBlockSize;
                }
                Buffer.BlockCopy (ptrLastLine, idxLastLine, pNextLineBuf, iNextLineIdx, (int)m_nBlockSize);
                iNextLineIdx += (int)m_nBlockSize;
            }
        }

        PtrProcedure GetLLRestoreFunc (int fdwFormatType, int dwBitsPerPixel)
        {
            switch (dwBitsPerPixel)
            {
            case 32:
                if ((int)EriImage.RGBA == fdwFormatType)
                {
                    Format = PixelFormats.Bgra32;
                    return RestoreRGBA32;
                }
                Format = PixelFormats.Bgr32;
                return RestoreRGB24;
            case 24:
                Format = PixelFormats.Bgr24;
                return RestoreRGB24;
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

        void RestoreRGBA32 ()
        {
            int ptrDstLine = m_ptrDstBlock;
            int ptrSrcLine = 0; //m_ptrDecodeBuf;
            int nBlockSamples = (int)m_nBlockArea;
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
                ptrSrcLine += (int)m_nBlockSize;
                ptrDstLine += m_nDstLineBytes;
            }
        }

        void RestoreRGB24()
        {
            int ptrDstLine = m_ptrDstBlock;
            int ptrSrcLine = 0; //m_ptrDecodeBuf;
            int nBytesPerPixel = m_nDstPixelBytes;
            int nBlockSamples = (int)m_nBlockArea;

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
                ptrSrcLine += (int)m_nBlockSize;
                ptrDstLine += m_nDstLineBytes;
            }
        }

        void RestoreRGB16()
        {
            int ptrDstLine = m_ptrDstBlock;
            int ptrSrcLine = 0; //m_ptrDecodeBuf;
            int nBlockSamples = (int)m_nBlockArea;

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
                ptrSrcLine += (int)m_nBlockSize;
                ptrDstLine += m_nDstLineBytes;
            }
        }

        void RestoreGray8()
        {
            int ptrDstLine = m_ptrDstBlock;
            int ptrSrcLine = 0; //m_ptrDecodeBuf;

            for (uint y = 0; y < m_nDstHeight; y++)
            {
                Buffer.BlockCopy (m_ptrDecodeBuf, ptrSrcLine, m_output, ptrDstLine, (int)m_nDstWidth);
                ptrSrcLine += (int)m_nBlockSize;
                ptrDstLine += m_nDstLineBytes;
            }
        }

        void ColorOperation0000 ()
        {
        }

        void ColorOperation0101 ()
        {
            int ptrNext = 0; // m_ptrDecodeBuf;
            uint nChSamples = m_nBlockArea;
            uint nRepCount = m_nBlockArea;
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
            uint nChSamples = m_nBlockArea * 2;
            uint nRepCount = m_nBlockArea;
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
            uint nChSamples = m_nBlockArea;
            uint nRepCount = m_nBlockArea;
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
            uint nChSamples = m_nBlockArea;
            uint nRepCount = m_nBlockArea;
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
            uint nChSamples = m_nBlockArea;
            uint nRepCount = m_nBlockArea;
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
            uint nChSamples = m_nBlockArea;
            uint nRepCount = m_nBlockArea;
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
            uint nChSamples = m_nBlockArea * 2;
            uint nRepCount = m_nBlockArea;
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
            uint nChSamples = m_nBlockArea;
            uint nRepCount = m_nBlockArea;
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
            uint nChSamples = m_nBlockArea;
            uint nRepCount = m_nBlockArea;
            do
            {
                sbyte nBase = m_ptrDecodeBuf[ptrNext + nChSamples * 2];
                m_ptrDecodeBuf[ptrNext] += nBase;
                m_ptrDecodeBuf[ptrNext + nChSamples] += nBase;
                ptrNext ++;
            }
            while (0 != --nRepCount);
        }
    }

    internal static class Erina
    {
        public const int CodeFlag      = int.MinValue;
        public const int HuffmanEscape = 0x7FFFFFFF;
        public const int HuffmanNull   = 0x8000;
        public const int HuffmanMax    = 0x4000;
        public const int HuffmanRoot   = 0x200;
    };

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
                m_iSymLookup[i] = (int)Erina.HuffmanNull;
            }
            m_iEscape = (int)Erina.HuffmanNull;
            m_iTreePointer = (int)Erina.HuffmanRoot;
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
                m_iSymLookup[nNewCode & 0xFF] = i ;

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
                    return nCode ;
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
}
