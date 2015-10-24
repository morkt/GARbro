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
            return new Entry { Name = string.Format ("{0}#{1:D5}", base_name, n) };
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var iarc = arc as IarArchive;
            if (null == iarc)
                return base.OpenEntry (arc, entry);
            int flags = arc.File.View.ReadUInt16 (entry.Offset);
            if (0 != (flags & 0x1000))
                return base.OpenEntry (arc, entry);

            using (var image = new IarImage (iarc, entry))
            {
                byte[] pixels = image.Data;
                if (0 != (flags & 0x800))
                    pixels = CombineImage (pixels, image.Info, iarc);

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
                    writer.Write (pixels.Length);
                    if (null != image.Palette)
                        writer.Write (image.Palette, 0, image.Palette.Length);
                    return new PrefixStream (header, new MemoryStream (pixels));
                }
            }
        }

        byte[] CombineImage (byte[] overlay, IarImageInfo info, IarArchive iarc)
        {
            using (var mem = new MemoryStream (overlay))
            using (var input = new BinaryReader (mem))
            {
                var dir = (List<Entry>)iarc.Dir;
                int base_index = input.ReadInt32();
                if (base_index >= dir.Count)
                    throw new InvalidFormatException ("Invalid base image index");
                int diff_y      = input.ReadInt32();
                int diff_count  = input.ReadInt32();
                using (var base_image = new IarImage (iarc, dir[base_index]))
                {
                    int base_y = (int)base_image.Info.Height - (int)info.Height;
                    byte[] output = base_image.Data;
                    if (base_y != 0 || info.Stride != base_image.Info.Stride)
                    {
                        byte[] src = base_image.Data;
                        int base_stride = Math.Min (info.Stride, base_image.Info.Stride);
                        output = new byte[info.Height * info.Stride];
                        for (int y = base_y; y < base_image.Info.Height; ++y)
                        {
                            Buffer.BlockCopy (src, y * base_image.Info.Stride,
                                              output, (y - base_y) * info.Stride, base_stride);
                        }
                    }
                    int pixel_size = info.BPP / 8;
                    int dst = diff_y * info.Stride;
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
                        dst += info.Stride;
                    }
                    return output;
                }
            }
        }
    }

    internal sealed class IarImage : IDisposable
    {
        BinaryReader    m_input;
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
            var input = iarc.File.CreateStream (offset, input_size);
            m_input = new BinaryReader (input);
            m_output = new byte[m_info.UnpackedSize];
            if (!m_info.Compressed)
                m_input.Read (m_output, 0, m_output.Length);
            else
                Unpack();
        }

        void Unpack ()
        {
            m_bits = 1;
            int dst = 0;
            while (dst < m_output.Length)
            {
                if (1 == GetNextBit())
                {
                    m_output[dst++] = m_input.ReadByte();
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
                Binary.CopyOverlapped (m_output, dst - offset, dst, count);
                dst += count;
            }
        }

        int m_bits = 1;

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
