//! \file       ArcIAR.cs
//! \date       Fri Oct 23 12:31:15 2015
//! \brief      Sas5 engine image archive.
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;
using System.Text;
using System.Diagnostics;
using System.Drawing;

namespace GameRes.Formats.Sas5
{
    internal class IarArchive : ArcFile
    {
        public readonly int Version;

        public IarArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, int version)
            : base (arc, impl, dir)
        {
            Version = version;
        }
    }

    internal class IarImageInfo : ImageMetaData
    {
        public int  Flags;
        public bool Compressed;
        public uint PaletteSize;
        public int  PackedSize;
        public int  UnpackedSize;
        public int  Stride;
    }

    [Export(typeof(ArchiveFormat))]
    public class IarOpener : ArchiveFormat
    {
        public override string         Tag { get { return "IAR"; } }
        public override string Description { get { return "SAS5 engine images archive"; } }
        public override uint     Signature { get { return 0x20726169; } } // 'iar '
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadInt16 (4);
            if (version < 1 || version > 4)
                return null;
            int file_count = file.View.ReadInt32 (0x18);
            int count = file.View.ReadInt32 (0x1C);
            if (count < file_count || !IsSaneCount (count))
                return null;

            var index = Sec5Opener.LookupIndex (file.Name);
            string base_name = Path.GetFileNameWithoutExtension (file.Name);
            Func<int, Entry> CreateEntry;
            if (null == index)
                CreateEntry = n => GetDefaultEntry (base_name, n);
            else
                CreateEntry = (n) => {
                    Entry entry;
                    if (index.TryGetValue (n, out entry))
                        return new Entry { Name = entry.Name, Type = entry.Type };
                    return GetDefaultEntry (base_name, n);
                };

            uint offset_size = version < 3 ? 4u : 8u;
            Func<uint, long> ReadOffset;
            if (version < 3)
                ReadOffset = x => file.View.ReadUInt32 (x);
            else
                ReadOffset = x => file.View.ReadInt64 (x);

            uint index_offset = 0x20;
            var dir = new List<Entry> (count);
            var next_offset = ReadOffset (index_offset);
            for (int i = 0; i < count; ++i)
            {
                var entry = CreateEntry (i);
                entry.Offset = next_offset;
                index_offset += offset_size;
                next_offset = (i + 1) == count ? file.MaxOffset : ReadOffset (index_offset);
                entry.Size = (uint)(next_offset - entry.Offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new IarArchive (file, this, dir, version);
        }

        static Entry GetDefaultEntry (string base_name, int n)
        {
            return new Entry {
                Name = string.Format ("{0}#{1:D5}", base_name, n),
                Type = "image",
            };
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var iarc = arc as IarArchive;
            if (null == iarc)
                return base.OpenEntry (arc, entry);
            try
            {
                int flags = arc.File.View.ReadUInt16 (entry.Offset);

                var image = new IarImage (iarc, entry);
                if (0 != (flags & 0x1000))
                    image = CombineLayers (image, iarc);
                else if (0 != (flags & 0x800))
                    image = CombineImage (image, iarc);
                if (null == image)
                    return base.OpenEntry (arc, entry);

                // internal 'IAR SAS5' format
                var header = new byte[0x28+image.Info.PaletteSize];
                using (var mem = new MemoryStream (header))
                using (var writer = new BinaryWriter (mem))
                {
                    writer.Write (0x00524149); // 'IAR'
                    writer.Write (0x35534153); // 'SAS5'
                    writer.Write (image.Info.Width);
                    writer.Write (image.Info.Height);
                    writer.Write (image.Info.OffsetX);
                    writer.Write (image.Info.OffsetY);
                    writer.Write (image.Info.BPP);
                    writer.Write (image.Info.Stride);
                    writer.Write (image.Info.PaletteSize);
                    writer.Write (image.Data.Length);
                    if (null != image.Palette)
                        writer.Write (image.Palette, 0, image.Palette.Length);
                    return new PrefixStream (header, new MemoryStream (image.Data));
                }
            }
            catch (Exception X)
            {
                Trace.WriteLine (X.Message, entry.Name);
                return base.OpenEntry (arc, entry);
            }
        }

        IarImage CombineImage (IarImage overlay, IarArchive iarc)
        {
            using (var mem = new MemoryStream (overlay.Data))
            using (var input = new BinaryReader (mem))
            {
                var dir = (List<Entry>)iarc.Dir;
                int base_index = input.ReadInt32();
                if (base_index >= dir.Count)
                    throw new InvalidFormatException ("Invalid base image index");
                int diff_y      = input.ReadInt32();
                int diff_count  = input.ReadInt32();

                var overlay_info = overlay.Info;
                var base_image = new IarImage (iarc, dir[base_index]);
                byte[] output = base_image.Data;
                if (overlay_info.Height != base_image.Info.Height || overlay_info.Stride != base_image.Info.Stride)
                {
                    int src_y = 0;
                    int dst_pos = 0;
                    if (base_image.Info.Height > overlay_info.Height)
                        src_y = (int)(base_image.Info.Height - overlay_info.Height);
                    else if (base_image.Info.Height < overlay_info.Height)
                        dst_pos = (int)(overlay_info.Height - base_image.Info.Height) * overlay_info.Stride;
                    byte[] src = base_image.Data;
                    int base_stride = Math.Min (overlay_info.Stride, base_image.Info.Stride);
                    output = new byte[overlay_info.Height * overlay_info.Stride];
                    for (int y = src_y; y < base_image.Info.Height; ++y)
                    {
                        Buffer.BlockCopy (src, y * base_image.Info.Stride, output, dst_pos, base_stride);
                        dst_pos += overlay_info.Stride;
                    }
                }
                int pixel_size = overlay_info.BPP / 8;
                int dst = diff_y * overlay_info.Stride;
                for (int i = 0; i < diff_count; ++i)
                {
                    int chunk_count = input.ReadUInt16();
                    int x = 0;
                    for (int j = 0; j < chunk_count; ++j)
                    {
                        int skip_count = pixel_size * input.ReadUInt16();
                        int copy_count = pixel_size * input.ReadUInt16();

                        x += skip_count;
                        input.Read (output, dst+x, copy_count);
                        x += copy_count;
                    }
                    dst += overlay_info.Stride;
                }
                return new IarImage (overlay_info, output, overlay.Palette);
            }
        }

        IarImage CombineLayers (IarImage layers, IarArchive iarc)
        {
            layers.Info.Stride = (int)layers.Info.Width * 4;
            layers.Info.BPP = 32;
            var pixels = new byte[layers.Info.Stride * (int)layers.Info.Height];
            var output = new IarImage (layers.Info, pixels);
            using (var mem = new MemoryStream (layers.Data))
            using (var input = new BinaryReader (mem))
            {
                int offset_x = 0, offset_y = 0;
                var dir = (List<Entry>)iarc.Dir;
                while (input.BaseStream.Position < input.BaseStream.Length)
                {
                    int cmd = input.ReadByte();
                    switch (cmd)
                    {
                    case 0x21:
                        offset_x += input.ReadInt16();
                        offset_y += input.ReadInt16();
                        break;

                    case 0x00:
                    case 0x20:
                        {
                            int index = input.ReadInt32();
                            if (index < 0 || index >= dir.Count)
                                throw new InvalidFormatException ("Invalid image layer index");
                            var layer = new IarImage (iarc, dir[index]);
                            layer.Info.OffsetX -= offset_x;
                            layer.Info.OffsetY -= offset_y;
                            if (0x20 == cmd)
                                output.ApplyMask (layer);
                            else
                                output.Blend (layer);
                        }
                        break;

                    default:
                        Trace.WriteLine (string.Format ("Unknown layer type 0x{0:X2}", cmd), "IAR");
                        break;
                    }
                }
                return output;
            }
        }
    }

    internal class IarImage
    {
        IarImageInfo    m_info;
        byte[]          m_palette;
        byte[]          m_output;

        public IarImageInfo Info { get { return m_info; } }
        public byte[]    Palette { get { return m_palette; } }
        public byte[]       Data { get { return m_output; } }

        public IarImage (IarArchive iarc, Entry entry)
        {
            int flags = iarc.File.View.ReadUInt16 (entry.Offset);
            int bpp;
            switch (flags & 0x3E)
            {
            case 0x02:  bpp = 8; break;
            case 0x1C:  bpp = 24; break;
            case 0x3C:  bpp = 32; break;
            default:    throw new NotSupportedException ("Not supported IAR image format");
            }
            var offset = entry.Offset;
            m_info = new IarImageInfo
            {
                Flags   = flags,
                BPP     = bpp,
                Compressed = iarc.File.View.ReadByte (offset+3) != 0,
                Width   = iarc.File.View.ReadUInt32 (offset+0x20),
                Height  = iarc.File.View.ReadUInt32 (offset+0x24),
                Stride  = iarc.File.View.ReadInt32 (offset+0x28),
                OffsetX = iarc.File.View.ReadInt32 (offset+0x18),
                OffsetY = iarc.File.View.ReadInt32 (offset+0x1C),
                UnpackedSize = iarc.File.View.ReadInt32 (offset+8),
                PaletteSize = iarc.File.View.ReadUInt32 (offset+0xC),
                PackedSize = iarc.File.View.ReadInt32 (offset+0x10),
            };
            uint header_size = 1 == iarc.Version ? 0x30u : iarc.Version < 4 ? 0x40u : 0x48u;
            offset += header_size;
            uint input_size = entry.Size - header_size;

            if (m_info.PaletteSize > 0)
            {
                m_palette = new byte[m_info.PaletteSize];
                iarc.File.View.Read (offset, m_palette, 0, m_info.PaletteSize);
                offset += m_info.PaletteSize;
                input_size -= m_info.PaletteSize;
            }
            m_output = new byte[m_info.UnpackedSize];
            using (var input = iarc.File.CreateStream (offset, input_size))
            {
                if (m_info.Compressed)
                {
                    using (var reader = new IarDecompressor (input))
                        reader.Unpack (m_output);
                }
                else
                    input.Read (m_output, 0, m_output.Length);
            }
        }

        public IarImage (IarImageInfo info, byte[] pixels, byte[] palette = null)
        {
            m_info = info;
            m_output = pixels;
            m_palette = palette;
        }

        public void Blend (IarImage overlay)
        {
            int pixel_size = Info.Stride / (int)Info.Width;
            if (pixel_size < 4)
                return;
            var self = new Rectangle (-Info.OffsetX, -Info.OffsetY, (int)Info.Width, (int)Info.Height);
            var src = new Rectangle (-overlay.Info.OffsetX, -overlay.Info.OffsetY,
                                     (int)overlay.Info.Width, (int)overlay.Info.Height);
            var blend = Rectangle.Intersect (self, src);
            if (blend.IsEmpty)
                return;
            src.X = blend.Left - src.Left;
            src.Y = blend.Top - src.Top;
            src.Width = blend.Width;
            src.Height= blend.Height;
            if (src.Width <= 0 || src.Height <= 0)
                return;

            int x = blend.Left - self.Left;
            int y = blend.Top - self.Top;
            int dst = y * Info.Stride + x * pixel_size;
            int ov = src.Top * overlay.Info.Stride + src.Left * pixel_size;
            for (int row = 0; row < src.Height; ++row)
            {
                for (int col = 0; col < src.Width; ++col)
                {
                    int src_pixel = ov + col*pixel_size;
                    int src_alpha = overlay.Data[src_pixel+3];
                    if (src_alpha > 0)
                    {
                        int dst_pixel = dst + col*pixel_size;
                        if (0xFF == src_alpha || 0 == m_output[dst_pixel+3])
                        {
                            Buffer.BlockCopy (overlay.Data, src_pixel, m_output, dst_pixel, pixel_size);
                        }
                        else
                        {
                            m_output[dst_pixel+0] = (byte)((overlay.Data[src_pixel+0] * src_alpha
                                                     + m_output[dst_pixel+0] * (0xFF - src_alpha)) / 0xFF);
                            m_output[dst_pixel+1] = (byte)((overlay.Data[src_pixel+1] * src_alpha
                                                     + m_output[dst_pixel+1] * (0xFF - src_alpha)) / 0xFF);
                            m_output[dst_pixel+2] = (byte)((overlay.Data[src_pixel+2] * src_alpha
                                                     + m_output[dst_pixel+2] * (0xFF - src_alpha)) / 0xFF);
                            m_output[dst_pixel+3] = (byte)Math.Max (src_alpha, m_output[dst_pixel+3]);
                        }
                    }
                }
                dst += Info.Stride;
                ov  += overlay.Info.Stride;
            }
        }

        public void ApplyMask (IarImage mask)
        {
            int pixel_size = Info.Stride / (int)Info.Width;
            if (pixel_size < 4 || mask.Info.BPP != 8)
                return;
            var self = new Rectangle (-Info.OffsetX, -Info.OffsetY, (int)Info.Width, (int)Info.Height);
            var mask_region = new Rectangle (-mask.Info.OffsetX, -mask.Info.OffsetY,
                                             (int)mask.Info.Width, (int)mask.Info.Height);
            var masked = Rectangle.Intersect (self, mask_region);
            if (masked.IsEmpty)
                return;
            mask_region.X = masked.Left - mask_region.Left;
            mask_region.Y = masked.Top - mask_region.Top;
            mask_region.Width = masked.Width;
            mask_region.Height= masked.Height;
            if (mask_region.Width <= 0 || mask_region.Height <= 0)
                return;

            int x = masked.Left - self.Left;
            int y = masked.Top - self.Top;
            int dst = y * Info.Stride + x * pixel_size;
            int src = mask_region.Top * mask.Info.Stride + mask_region.Left;
            for (int row = 0; row < mask_region.Height; ++row)
            {
                int dst_pixel = dst+3;
                for (int col = 0; col < mask_region.Width; ++col)
                {
                    m_output[dst_pixel] = mask.Data[src+col];
                    dst_pixel += pixel_size;
                }
                dst += Info.Stride;
                src += mask.Info.Stride;
            }
        }
    }

    internal sealed class IarDecompressor : IDisposable
    {
        BinaryReader    m_input;

        public IarDecompressor (Stream input)
        {
            m_input = new ArcView.Reader (input);
        }

        int m_bits = 1;

        public void Unpack (byte[] output)
        {
            m_bits = 1;
            int dst = 0;
            while (dst < output.Length)
            {
                if (1 == GetNextBit())
                {
                    output[dst++] = m_input.ReadByte();
                    continue;
                }
                int offset, count;
                if (1 == GetNextBit())
                {
                    int tmp = GetNextBit();
                    if (1 == GetNextBit())
                        offset = 1;
                    else if (1 == GetNextBit())
                        offset = 0x201;
                    else
                    {
                        tmp = (tmp << 1) | GetNextBit();
                        if (1 == GetNextBit())
                            offset = 0x401;
                        else
                        {
                            tmp = (tmp << 1) | GetNextBit();
                            if (1 == GetNextBit())
                                offset = 0x801;
                            else
                            {
                                offset = 0x1001;
                                tmp = (tmp << 1) | GetNextBit();
                            }
                        }
                    }
                    offset += (tmp << 8) | m_input.ReadByte();
                    if (1 == GetNextBit())
                        count = 3;
                    else if (1 == GetNextBit())
                        count = 4;
                    else if (1 == GetNextBit())
                        count = 5;
                    else if (1 == GetNextBit())
                        count = 6;
                    else if (1 == GetNextBit())
                        count = 7 + GetNextBit();
                    else if (1 == GetNextBit())
                        count = 17 + m_input.ReadByte();
                    else
                    {
                        count = GetNextBit() << 2;
                        count |= GetNextBit() << 1;
                        count |= GetNextBit();
                        count += 9;
                    }
                }
                else
                {
                    count = 2;
                    if (1 == GetNextBit())
                    {
                        offset = GetNextBit() << 10;
                        offset |= GetNextBit() << 9;
                        offset |= GetNextBit() << 8;
                        offset = (offset | m_input.ReadByte()) + 0x100;
                    }
                    else
                    {
                        offset = 1 + m_input.ReadByte();
                        if (0x100 == offset)
                            break;
                    }
                }
                Binary.CopyOverlapped (output, dst - offset, dst, count);
                dst += count;
            }
        }

        int GetNextBit ()
        {
            if (1 == m_bits)
            {
                m_bits = m_input.ReadUInt16() | 0x10000;
            }
            int b = m_bits & 1;
            m_bits >>= 1;
            return b;
        }

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }
}
