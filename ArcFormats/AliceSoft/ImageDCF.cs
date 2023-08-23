//! \file       ImageDCF.cs
//! \date       Fri Jul 29 14:07:15 2016
//! \brief      AliceSoft incremental image format.
//
// Copyright (C) 2016-2022 by morkt
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
using System.Text;
using System.Windows.Media;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.AliceSoft
{
    internal class DcfMetaData : ImageMetaData
    {
        public string   BaseName;
        public long     DataOffset;
        public bool     IsPcf;
    }

    internal interface IBaseImageReader
    {
        int     BPP { get; }
        byte[] Data { get; }

        void Unpack ();
    }

    [Export(typeof(ImageFormat))]
    public class DcfFormat : ImageFormat
    {
        public override string         Tag { get { return "DCF"; } }
        public override string Description { get { return "AliceSoft System incremental image"; } }
        public override uint     Signature { get { return 0x20666364; } } // 'dcf '

        public DcfFormat ()
        {
            Extensions = new[] { "dcf", "pcf" };
            Signatures = new[] { 0x20666364u, 0x20666370u };
        }

        static readonly ResourceInstance<AfaOpener> Afa = new ResourceInstance<AfaOpener> ("AFA");

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x1C);
            uint header_size = header.ToUInt32 (4);
            long data_pos = 8 + header_size;
            if (header.ToInt32 (8) != 1)
                return null;
            uint width  = header.ToUInt32 (0x0C);
            uint height = header.ToUInt32 (0x10);
            int bpp = header.ToInt32 (0x14);
            int name_length = header.ToInt32 (0x18);
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
                BaseName = Afa.Value.NameEncoding.GetString (name_bits),
                DataOffset = data_pos,
                IsPcf = stream.Signature == 0x20666370u,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var reader = new DcfReader (stream, (DcfMetaData)info);
            reader.Unpack();
            return ImageData.Create (reader.Info, reader.Format, null, reader.Data, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DcfFormat.Write not implemented");
        }
    }

    internal sealed class DcfReader : IBaseImageReader
    {
        IBinaryStream       m_input;
        DcfMetaData         m_info;
        byte[]              m_output;
        byte[]              m_mask = null;
        byte[]              m_base = null;
        int                 m_overlay_bpp;
        int                 m_base_bpp;

        static readonly ResourceInstance<ImageFormat> s_QntFormat = new ResourceInstance<ImageFormat> ("QNT");
        static readonly ResourceInstance<ImageFormat> s_DcfFormat = new ResourceInstance<ImageFormat> ("DCF");

        internal ImageFormat  Qnt { get { return s_QntFormat.Value; } }
        internal ImageFormat  Dcf { get { return s_DcfFormat.Value; } }

        public int            BPP { get { return m_base_bpp; } }
        public ImageMetaData Info { get; private set; }
        public byte[]        Data { get { return m_output; } }
        public PixelFormat Format { get; private set; }
        public int         Stride { get; private set; }

        public DcfReader (IBinaryStream input, DcfMetaData info)
        {
            m_input = input;
            m_info = info;
            Info = info;
        }

        public void Unpack ()
        {
            int pt_x = 0;
            int pt_y = 0;
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
                else if (0x6C647470 == id) // 'ptdl'
                {
                    pt_x = m_input.ReadInt32();
                    pt_y = m_input.ReadInt32();
                }
                else if (0x64676364 == id || 0x64676370 == id) // 'dcgd' || 'pcgd'
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
                if (m_mask != null || m_info.IsPcf)
                    ReadBaseImage();

                if (m_info.IsPcf)
                {
                    if (null == m_base)
                        SetEmptyBase();
                    qnt_info.OffsetX = pt_x;
                    qnt_info.OffsetY = pt_y;
                    BlendOverlay (qnt_info, overlay.Data);
                    m_output = m_base;
                    SetFormat (m_info.iWidth, m_base_bpp);
                }
                else if (m_base != null)
                {
                    m_output = MaskOverlay (overlay.Data);
                    SetFormat (m_info.iWidth, m_overlay_bpp);
                }
                else
                {
                    m_output = overlay.Data;
                    SetFormat (qnt_info.iWidth, m_overlay_bpp);
                }
            }
        }

        void SetFormat (int width, int bpp)
        {
            Format = 24 == bpp ? PixelFormats.Bgr24 : PixelFormats.Bgra32;
            Stride = width * (bpp / 8);
        }

        void SetEmptyBase ()
        {
            m_base_bpp = 32;
            m_base = new byte[m_info.Width * m_info.Height * 4];
        }

        byte[] MaskOverlay (byte[] overlay)
        {
            int blocks_x = m_info.iWidth / 0x10;
            int blocks_y = m_info.iHeight / 0x10;
            int base_step = m_base_bpp / 8;
            int overlay_step = m_overlay_bpp / 8;
            int base_stride = m_info.iWidth * base_step;
            int overlay_stride = m_info.iWidth * overlay_step;
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

        void BlendOverlay (ImageMetaData overlay_info, byte[] overlay)
        {
            int ovl_x = overlay_info.OffsetX;
            int ovl_y = overlay_info.OffsetY;
            int ovl_width = overlay_info.iWidth;
            int ovl_height = overlay_info.iHeight;
            int base_width = m_info.iWidth;
            int base_height = m_info.iHeight;
            if (checked(ovl_x + ovl_width) > base_width)
                ovl_width = base_width - ovl_x;
            if (checked(ovl_y + ovl_height) > base_height)
                ovl_height = base_height - ovl_y;
            if (ovl_height <= 0 || ovl_width <= 0)
                return;

            int dst_stride = m_info.iWidth * 4;
            int src_stride = overlay_info.iWidth * 4;
            int dst = ovl_y * dst_stride + ovl_x * 4;
            int src = 0;
            int gap = dst_stride - src_stride;
            for (int y = 0; y < overlay_info.iHeight; ++y)
            {
                for (int x = 0; x < overlay_info.iWidth; ++x)
                {
                    byte src_alpha = overlay[src+3];
                    if (src_alpha != 0)
                    {
                        if (0xFF == src_alpha || 0 == m_base[dst+3])
                        {
                            m_base[dst]   = overlay[src];
                            m_base[dst+1] = overlay[src+1];
                            m_base[dst+2] = overlay[src+2];
                            m_base[dst+3] = src_alpha;
                        }
                        else
                        {
                            m_base[dst+0] = (byte)((overlay[src+0] * src_alpha
                                                    + m_base[dst+0] * (0xFF - src_alpha)) / 0xFF);
                            m_base[dst+1] = (byte)((overlay[src+1] * src_alpha
                                                    + m_base[dst+1] * (0xFF - src_alpha)) / 0xFF);
                            m_base[dst+2] = (byte)((overlay[src+2] * src_alpha
                                                    + m_base[dst+2] * (0xFF - src_alpha)) / 0xFF);
                            m_base[dst+3] = (byte)Math.Max (src_alpha, m_base[dst+3]);
                        }
                    }
                    dst += 4;
                    src += 4;
                }
                dst += gap;
            }
        }

        void ReadBaseImage ()
        {
            try
            {
                string dir_name = VFS.GetDirectoryName (m_info.FileName);
                string base_name = Path.ChangeExtension (m_info.BaseName, "qnt");
                base_name = VFS.CombinePath (dir_name, base_name);
                ImageFormat base_format = null;
                Func<IBinaryStream, ImageMetaData, IBaseImageReader> create_reader;
                if (VFS.FileExists (base_name))
                {
                    base_format = Qnt;
                    create_reader = (s, m) => new QntFormat.Reader (s.AsStream, (QntMetaData)m);
                }
                else
                {
                    base_name = Path.ChangeExtension (m_info.BaseName, "pcf");
                    if (VFS.IsPathEqualsToFileName (m_info.FileName, base_name))
                        return;
                    base_name = VFS.CombinePath (dir_name, base_name);
                    base_format = Dcf;
                    create_reader = (s, m) => new DcfReader (s, (DcfMetaData)m);
                }
                using (var base_file = VFS.OpenBinaryStream (base_name))
                {
                    var base_info = base_format.ReadMetaData (base_file);
                    if (null != base_info && m_info.Width == base_info.Width && m_info.Height == base_info.Height)
                    {
                        base_info.FileName = base_name;
                        var reader = create_reader (base_file, base_info);
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
    }
}
