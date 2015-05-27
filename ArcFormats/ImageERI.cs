//! \file       ImageERI.cs
//! \date       Tue May 26 12:04:30 2015
//! \brief      Entis rasterized image format.
//
// Copyright (C) 2015 by morkt
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

using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Entis
{
    internal class EriMetaData : ImageMetaData
    {
        public int      StreamPos;
        public int      Version;
        public CvType   Transformation;
        public EriCode  Architecture;
        public int      FormatType;
        public bool     VerticalFlip;
        public int      ClippedPixel;
        public int      SamplingFlags;
        public ulong    QuantumizedBits;
        public ulong    AllottedBits;
        public int      BlockingDegree;
        public int      LappedBlock;
        public int      FrameTransform;
        public int      FrameDegree;
    }

    public enum CvType
    {
        Lossless_ERI =  0x03020000,
        DCT_ERI      =  0x00000001,
        LOT_ERI      =  0x00000005,
        LOT_ERI_MSS  =  0x00000105,
    }

    public enum EriCode
    {
        RunlengthGamma      = -1,
        RunlengthHuffman    = -4,
        Nemesis             = -16,
    }

    public enum EriImage
    {
        RGB         = 0x00000001,
        RGBA        = 0x04000001,
        Gray        = 0x00000002,
        TypeMask    = 0x00FFFFFF,
        WithPalette = 0x01000000,
        UseClipping = 0x02000000,
        WithAlpha   = 0x04000000,
        SideBySide  = 0x10000000,
    }

    [Export(typeof(ImageFormat))]
    public class EriFormat : ImageFormat
    {
        public override string         Tag { get { return "ERI"; } }
        public override string Description { get { return "Entis rasterized image format"; } }
        public override uint     Signature { get { return 0x69746e45u; } } // 'Enti'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            byte[] header = new byte[0x40];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            if (0x03000100 != LittleEndian.ToUInt32 (header, 8))
                return null;
            if (!Binary.AsciiEqual (header, 0x10, "Entis Rasterized Image"))
                return null;
            using (var reader = new ArcView.Reader (stream))
            {
                var section = ReadEriSection (reader);
                if (section.Id != "Header  " || section.Length <= 0)
                    return null;
                int header_size = (int)section.Length;
                int stream_pos = 0x50 + header_size;
                EriMetaData info = null;
                while (header_size > 8)
                {
                    section = ReadEriSection (reader);
                    header_size -= 8;
                    if (section.Length <= 0 || section.Length > header_size)
                        break;
                    if ("ImageInf" == section.Id)
                    {
                        int version = reader.ReadInt32();
                        if (version != 0x00020100 && version != 0x00020200)
                            return null;
                        info = new EriMetaData { StreamPos = stream_pos, Version = version };
                        info.Transformation = (CvType)reader.ReadInt32();
                        info.Architecture = (EriCode)reader.ReadInt32();
                        info.FormatType = reader.ReadInt32();
                        int w = reader.ReadInt32();
                        int h = reader.ReadInt32();
                        info.Width  = (uint)Math.Abs (w);
                        info.Height = (uint)Math.Abs (h);
                        info.VerticalFlip = h < 0;
                        info.BPP = reader.ReadInt32();
                        info.ClippedPixel = reader.ReadInt32();
                        info.SamplingFlags = reader.ReadInt32();
                        info.QuantumizedBits = reader.ReadUInt64();
                        info.AllottedBits = reader.ReadUInt64();
                        info.BlockingDegree = reader.ReadInt32();
                        info.LappedBlock = reader.ReadInt32();
                        info.FrameTransform = reader.ReadInt32();
                        info.FrameDegree = reader.ReadInt32();
                        break;
                    }
                    header_size -= (int)section.Length;
                    reader.BaseStream.Seek (section.Length, SeekOrigin.Current);
                }
                return info;
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as EriMetaData;
            if (null == meta)
                throw new ArgumentException ("EriFormat.Read should be supplied with EriMetaData", "info");
            stream.Position = meta.StreamPos;
            using (var input = new ArcView.Reader (stream))
            {
                Color[] palette = null;
                for (;;) // ReadEriSection throws an exception in case of EOF
                {
                    var section = ReadEriSection (input);
                    if ("Stream  " == section.Id)
                        continue;
                    if ("ImageFrm" == section.Id)
                        break;
                    if ("Palette " == section.Id && info.BPP <= 8 && section.Length <= 0x400)
                    {
                        palette = ReadPalette (stream, (int)section.Length);
                        continue;
                    }
                    input.BaseStream.Seek (section.Length, SeekOrigin.Current);
                }
                var reader = new EriReader (stream, meta, palette);
                reader.DecodeImage();
                var bitmap = BitmapSource.Create ((int)info.Width, (int)info.Height,
                                                  ImageData.DefaultDpiX, ImageData.DefaultDpiY,
                                                  reader.Format, reader.Palette, reader.Data, reader.Stride);
                bitmap.Freeze();
                return new ImageData (bitmap, info);
            }
        }

        private Color[] ReadPalette (Stream input, int palette_length)
        {
            var palette_data = new byte[0x400];
            if (palette_length > palette_data.Length)
                throw new InvalidFormatException();
            if (palette_length != input.Read (palette_data, 0, palette_length))
                throw new InvalidFormatException();
            var colors = new Color[256];
            for (int i = 0; i < 256; ++i)
            {
                colors[i] = Color.FromRgb (palette_data[i*4+2], palette_data[i*4+1], palette_data[i*4]);
            }
            return colors;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("EriFormat.Write not implemented");
        }

        internal struct Section
        {
            public string  Id;
            public long    Length;
        }

        static internal Section ReadEriSection (BinaryReader reader)
        {
            var section = new Section();
            section.Id = new string (reader.ReadChars (8));
            section.Length = reader.ReadInt64();
            return section;
        }
    }

    internal class EriReader
    {
        EriMetaData     m_info;
        Color[]         m_palette;
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
            if (EriCode.RunlengthHuffman == m_info.Architecture
                || EriCode.Nemesis       == m_info.Architecture)
                throw new NotSupportedException ("Not supported ERI compression");
            if (EriCode.RunlengthGamma != m_info.Architecture)
                throw new InvalidFormatException();
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
                /*
                if (EriCode.RunlengthHuffman == m_info.Architecture)
                {
                    m_pHuffmanTree = new ERINA_HUFFMAN_TREE ;
                }
                else if (EriCode.Nemesis == m_info.Architecture)
                {
                    m_pProbERISA = new ERISA_PROB_MODEL ;
                }
                */
            }
            m_context = new RLEDecodeContext (0x10000);
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
            int dwPaletteLength = 0;
            if (m_info.BPP <= 8)
            {
                dwPaletteLength = 1 << m_info.BPP;
            }
            if (0 != dwPaletteLength)
                m_palette = new Color[dwPaletteLength];
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
            DecodeLosslessImage (m_context as RLEDecodeContext);
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
            if (EriCode.RunlengthHuffman == m_info.Architecture)
            {
                Debug.Assert (m_pHuffmanTree != null);
                m_pHuffmanTree.Initialize();
            }
            else if (EriCode.Nemesis == m_info.Architecture)
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
                    /*
                    else
                    {
                        Debug.Assert (EriCode.RunlengthHuffman == m_info.Architecture);
                        m_ptrOperations[i] = (byte) context.GetHuffmanCode (m_pHuffmanTree);
                    }
                    */
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
            /*
            else if (EriCode.RunlengthHuffman == m_info.Architecture)
            {
                context.PrepareToDecodeERINACode();
            }
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
                    m_nDstWidth = m_nBlockSize;
                    if ((int)m_nDstWidth > nLeftWidth)
                    {
                        m_nDstWidth = (uint)nLeftWidth;
                    }

                    uint dwOperationCode;
                    if (m_nChannelCount >= 3)
                    {
                        if (0 != (fEncodeType & 1))
                        {
                            dwOperationCode = m_ptrOperations[ptrNextOperation++];
                        }
                        /*
                        else if (m_info.Architecture == EriCode.Nemesis)
                        {
                            dwOperationCode = context.DecodeERISACode (m_pProbERISA);
                        }
                        else if (m_info.Architecture == EriCode.RunlengthHuffman)
                        {
                            dwOperationCode = context.GetHuffmanCode (m_pHuffmanTree);
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
                Format = PixelFormats.Gray8;
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

    internal class RLEDecodeContext : ERISADecodeContext
    {
        int     m_flgZero;
        uint    m_nLength;

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

        int GetGammaCode()
        {
            if (!PrefetchBuffer())
            {
                return 0;
            }
            uint dwIntBuf;
            m_nIntBufCount--;
            dwIntBuf = m_dwIntBuffer;
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
            if ((0 != (~m_dwIntBuffer & 0x55000000)) && (m_nIntBufCount >= 8) )
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
}
