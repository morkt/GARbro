//! \file       ImageAGF.cs
//! \date       Sun Sep 20 16:17:19 2015
//! \brief      Eushully image format.
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

using GameRes.Compression;
using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Eushully
{
    internal class AgfMetaData : ImageMetaData
    {
        public int      SourceBPP;
        public uint     DataOffset;
        public Color[]  Palette;
    }

    [Export(typeof(ImageFormat))]
    public class AgfFormat : ImageFormat
    {
        public override string         Tag { get { return "AGF"; } }
        public override string Description { get { return "Eushully image format"; } }
        public override uint     Signature { get { return 0x46474341; } } // 'ACGF'

        public AgfFormat ()
        {
            Signatures = new uint[] { 0x46474341, 0 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = new byte[0x20];
            if (0x18 != stream.Read (header, 0, 0x18))
                return null;
            uint id = LittleEndian.ToUInt32 (header, 0);
            if (Signature != id && 0 != id)
                return null;
            int type = LittleEndian.ToInt32 (header, 4);
            if (type != 1 && type != 2)
                return null;
            int unpacked_size = LittleEndian.ToInt32 (header, 0x10);
            int packed_size = LittleEndian.ToInt32 (header, 0x14);
            using (var unpacked = AgfReader.OpenSection (stream.AsStream, unpacked_size, packed_size))
            using (var reader = new BinaryReader (unpacked))
            {
                if (0x20 != reader.Read (header, 0, 0x20))
                    return null;
                var info = new AgfMetaData
                {
                    Width       = LittleEndian.ToUInt32 (header, 0x14),
                    Height      = LittleEndian.ToUInt32 (header, 0x18),
                    BPP         = 1 == type ? 24 : 32,
                    SourceBPP   = LittleEndian.ToInt16 (header, 0x1E),
                    DataOffset  = 0x18 + (uint)packed_size,
                };
                if (0 == info.SourceBPP)
                    return null;
                if (8 == info.SourceBPP)
                {
                    reader.Read (header, 0, 0x18); // skip rest of the header
                    info.Palette = ReadColorMap (reader.BaseStream);
                }
                return info;
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var reader = new AgfReader (stream, (AgfMetaData)info))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, null, reader.Data);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AgfFormat.Write not implemented");
        }
    }

    internal sealed class AgfReader : IDisposable
    {
        IBinaryStream   m_input;
        byte[]          m_output;
        int             m_width;
        int             m_height;
        int             m_bpp;
        int             m_source_bpp;
        Color[]         m_palette;

        public PixelFormat    Format { get; private set; }
        public byte[]           Data { get { return m_output; } }

        public AgfReader (IBinaryStream input, AgfMetaData info)
        {
            m_input = input;
            m_bpp = info.BPP;
            m_source_bpp = info.SourceBPP;
            input.Position = info.DataOffset;

            m_width = (int)info.Width;
            m_height = (int)info.Height;
            m_output = new byte[m_height * m_width * m_bpp / 8];
            m_palette = info.Palette;
        }

        public static Stream OpenSection (Stream stream, int unpacked_size, int packed_size)
        {
            if (unpacked_size != packed_size)
                return new LzssStream (stream, LzssMode.Decompress, true);
            else
                return new StreamRegion (stream, stream.Position, packed_size, true);
        }

        public void Unpack ()
        {
            m_input.ReadInt32();
            int data_size = m_input.ReadInt32();
            int packed_size = m_input.ReadInt32();
            var data_pos = m_input.Position;
            var bmp_data = new byte[data_size];
            using (var unpacked = OpenSection (m_input.AsStream, data_size, packed_size))
                if (data_size != unpacked.Read (bmp_data, 0, data_size))
                    throw new EndOfStreamException();
            byte[] alpha = null;
            if (32 == m_bpp)
            {
                m_input.Position = data_pos + packed_size;
                alpha = ReadAlphaChannel();
                if (null == alpha)
                    m_bpp = 24;
            }
            Format = 32 == m_bpp ? PixelFormats.Bgra32 : PixelFormats.Bgr24;
            int src_pixel_size = m_source_bpp / 8;
            int dst_pixel_size = m_bpp / 8;
            int src_stride = (m_width * src_pixel_size + 3) & ~3;
            int dst_stride = m_width * dst_pixel_size;

            int src_row = (m_height - 1) * src_stride;
            int dst_row = 0;
            int src_alpha = 0;
            RowUnpacker repack_row = RepackRowTrue;
            if (1 == src_pixel_size)
                repack_row = RepackRow8;
            while (src_row >= 0)
            {
                repack_row (bmp_data, src_row, src_pixel_size, dst_row, dst_pixel_size, alpha, src_alpha);
                src_row -= src_stride;
                dst_row += dst_stride;
                src_alpha += m_width;
            }
        }

        delegate void RowUnpacker (byte[] bmp, int src, int src_pixel_size, int dst, int dst_pixel_size, byte[] alpha, int src_alpha);

        void RepackRow8 (byte[] bmp, int src, int src_pixel_size, int dst, int dst_pixel_size, byte[] alpha, int src_alpha)
        {
            for (int i = 0; i < m_width; ++i)
            {
                var color = m_palette[bmp[src++]];
                m_output[dst] = color.B;
                m_output[dst+1] = color.G;
                m_output[dst+2] = color.R;
                if (null != alpha)
                    m_output[dst+3] = alpha[src_alpha++];
                dst += dst_pixel_size;
            }
        }

        void RepackRowTrue (byte[] bmp, int src, int src_pixel_size, int dst, int dst_pixel_size, byte[] alpha, int src_alpha)
        {
            for (int i = 0; i < m_width; ++i)
            {
                m_output[dst] = bmp[src];
                m_output[dst+1] = bmp[src+1];
                m_output[dst+2] = bmp[src+2];
                if (null != alpha)
                    m_output[dst+3] = alpha[src_alpha++];
                src += src_pixel_size;
                dst += dst_pixel_size;
            }
        }

        byte[] ReadAlphaChannel ()
        {
            var header = new byte[0x24];
            if (0x24 != m_input.Read (header, 0, header.Length))
                return null;
            if (!Binary.AsciiEqual (header, 0, "ACIF"))
                return null;
            int unpacked_size = LittleEndian.ToInt32 (header, 0x1C);
            int packed_size = LittleEndian.ToInt32 (header, 0x20);
            if (m_width*m_height != unpacked_size)
                return null;
            var alpha = new byte[unpacked_size];
            using (var unpacked = OpenSection (m_input.AsStream, unpacked_size, packed_size))
                if (unpacked_size != unpacked.Read (alpha, 0, unpacked_size))
                    return null;
            return alpha;
        }

        #region IDisposable methods
        public void Dispose ()
        {
        }
        #endregion
    }
}
