//! \file       ImageDCF.cs
//! \date       Fri Jul 29 14:07:15 2016
//! \brief      AliceSoft incremental image format.
//
// Copyright (C) 2016-2018 by morkt
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.AliceSoft
{
    internal class DcfMetaData : ImageMetaData
    {
        public string   BaseName;
        public long     DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class DcfFormat : ImageFormat
    {
        public override string         Tag { get { return "DCF"; } }
        public override string Description { get { return "AliceSoft System incremental image"; } }
        public override uint     Signature { get { return 0x20666364; } } // 'dcf '

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Seek (4, SeekOrigin.Current);
            uint header_size = stream.ReadUInt32();
            long data_pos = stream.Position + header_size;
            if (stream.ReadInt32() != 1)
                return null;
            uint width  = stream.ReadUInt32();
            uint height = stream.ReadUInt32();
            int bpp = stream.ReadInt32();
            int name_length = stream.ReadInt32();
            if (name_length <= 0)
                return null;
            int shift = (name_length % 7) + 1;
            var name_bits = stream.ReadBytes (name_length);
            for (int i = 0; i < name_length; ++i)
            {
                name_bits[i] = Binary.RotByteL (name_bits[i], shift);
            }
            return new DcfMetaData
            {
                Width = width,
                Height = height,
                BPP = bpp,
                BaseName = Encodings.cp932.GetString (name_bits),
                DataOffset = data_pos,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var reader = new DcfReader (stream, (DcfMetaData)info))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, null, reader.Data, reader.Stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DcfFormat.Write not implemented");
        }
    }

    internal sealed class DcfReader : IDisposable
    {
        IBinaryStream       m_input;
        DcfMetaData         m_info;
        byte[]              m_output;
        byte[]              m_mask = null;
        byte[]              m_base = null;
        int                 m_overlay_bpp;
        int                 m_base_bpp;

        static readonly Lazy<ImageFormat> s_QntFormat = new Lazy<ImageFormat> (() => ImageFormat.FindByTag ("QNT"));

        internal ImageFormat  Qnt { get { return s_QntFormat.Value; } }

        public byte[]        Data { get { return m_output; } }
        public PixelFormat Format { get; private set; }
        public int         Stride { get; private set; }

        public DcfReader (IBinaryStream input, DcfMetaData info)
        {
            m_input = input;
            m_info = info;
        }

        public void Unpack ()
        {
            long next_pos = m_info.DataOffset;
            for (;;)
            {
                m_input.Position = next_pos;
                uint id = m_input.ReadUInt32();
                next_pos += 8 + m_input.ReadUInt32();
                if (0x6C646664 == id) // 'dfdl'
                {
                    int unpacked_size = m_input.ReadInt32();
                    if (unpacked_size <= 0)
                        continue;
                    m_mask = new byte[unpacked_size];
                    using (var input = new ZLibStream (m_input.AsStream, CompressionMode.Decompress, true))
                        input.Read (m_mask, 0, unpacked_size);
                }
                else if (0x64676364 == id) // 'dcgd'
                    break;
            }
            long qnt_pos = m_input.Position;
            if (m_input.ReadUInt32() != Qnt.Signature)
                throw new InvalidFormatException();
            using (var reg = new StreamRegion (m_input.AsStream, qnt_pos, true))
            using (var qnt = new BinaryStream (reg, m_input.Name))
            {
                var qnt_info = Qnt.ReadMetaData (qnt) as QntMetaData;
                if (null == qnt_info)
                    throw new InvalidFormatException();

                var overlay = new QntFormat.Reader (reg, qnt_info);
                overlay.Unpack();
                m_overlay_bpp = overlay.BPP;
                if (m_mask != null)
                    ReadBaseImage();

                if (m_base != null)
                {
                    m_output = ApplyOverlay (overlay.Data);
                    SetFormat ((int)m_info.Width, m_overlay_bpp);
                }
                else
                {
                    m_output = overlay.Data;
                    SetFormat ((int)qnt_info.Width, m_overlay_bpp);
                }
            }
        }

        void SetFormat (int width, int bpp)
        {
            Format = 24 == bpp ? PixelFormats.Bgr24 : PixelFormats.Bgra32;
            Stride = width * (bpp / 8);
        }

        byte[] ApplyOverlay (byte[] overlay)
        {
            int blocks_x = (int)m_info.Width / 0x10;
            int blocks_y = (int)m_info.Height / 0x10;
            int base_step = m_base_bpp / 8;
            int overlay_step = m_overlay_bpp / 8;
            int base_stride = (int)m_info.Width * base_step;
            int overlay_stride = (int)m_info.Width * overlay_step;
            int mask_pos = 4;
            for (int y = 0; y < blocks_y; ++y)
            {
                int base_pos = y * 0x10 * base_stride;
                int dst_pos  = y * 0x10 * overlay_stride;
                for (int x = 0; x < blocks_x; ++x)
                {
                    if (0 == m_mask[mask_pos++])
                        continue;
                    for (int by = 0; by < 0x10; ++by)
                    {
                        int src = base_pos + by * base_stride    + x * 0x10 * base_step;
                        int dst = dst_pos  + by * overlay_stride + x * 0x10 * overlay_step;
                        for (int bx = 0; bx < 0x10; ++bx)
                        {
                            overlay[dst  ] = m_base[src  ];
                            overlay[dst+1] = m_base[src+1];
                            overlay[dst+2] = m_base[src+2];
                            if (4 == overlay_step)
                            {
                                overlay[dst+3] = 4 == base_step ? m_base[src+3] : (byte)0xFF;
                            }
                            src += base_step;
                            dst += overlay_step;
                        }
                    }
                }
            }
            return overlay;
        }

        void ReadBaseImage ()
        {
            try
            {
                string dir_name = VFS.GetDirectoryName (m_info.FileName);
                string base_name = Path.ChangeExtension (m_info.BaseName, "qnt");
                base_name = VFS.CombinePath (dir_name, base_name);
                using (var base_file = VFS.OpenBinaryStream (base_name))
                {
                    var base_info = Qnt.ReadMetaData (base_file) as QntMetaData;
                    if (null != base_info && m_info.Width == base_info.Width && m_info.Height == base_info.Height)
                    {
                        base_info.FileName = base_name;
                        var reader = new QntFormat.Reader (base_file.AsStream, base_info);
                        reader.Unpack();
                        m_base_bpp = reader.BPP;
                        m_base = reader.Data;
                    }
                }
            }
            catch (Exception X)
            {
                Trace.WriteLine (X.Message, "[DCF]");
            }
        }

        #region IDisposable Members
        public void Dispose ()
        {
        }
        #endregion
    }
}
