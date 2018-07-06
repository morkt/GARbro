//! \file       ArcMMA.cs
//! \date       2018 Jun 03
//! \brief      MNP engine resource archive.
//
// Copyright (C) 2018 by morkt
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
using System.Windows.Media;
using GameRes.Utility;

// [030425][Chise] Izayoi Renka

namespace GameRes.Formats.Mnp
{
    internal class MmaEntry : PackedEntry
    {
        public uint HeaderSize;
        public uint Flags;
    }

    [Export(typeof(ArchiveFormat))]
    public class MmaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MMA"; } }
        public override string Description { get { return "MNP engine resource archive"; } }
        public override uint     Signature { get { return 0x21435241; } } // 'ARC!'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadInt32 (0xC);
            if (version != 1)
                return null;
            int count = file.View.ReadInt32 (0x10);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (4);
            if (index_offset >= file.MaxOffset)
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new MmaEntry {
                    Name         = string.Format ("{0}#{1:D5}", base_name, i),
                    Offset       = file.View.ReadUInt32 (index_offset),
                    UnpackedSize = file.View.ReadUInt32 (index_offset+4),
                    Size         = file.View.ReadUInt32 (index_offset+8),
                    HeaderSize   = file.View.ReadUInt32 (index_offset+0x0C),
                    Flags        = file.View.ReadUInt32 (index_offset+0x10),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                switch (entry.Flags & 0x38)
                {
                case 8:
                case 0x10:
                case 0x18:
                case 0x38:
                    entry.Type = "image";
                    break;
                default:
                    if (0x2D == entry.Flags)
                        entry.Type = "audio";
                    break;
                }
                dir.Add (entry);
                index_offset += 0x14;
            }
            var list_entry = dir[0] as MmaEntry;
            if (0x2F == list_entry.Flags)
            {
                ReadMmaList (file, dir, list_entry);
            }
            return new ArcFile (file, this, dir);
        }

        void ReadMmaList (ArcView file, List<Entry> dir, MmaEntry index_entry)
        {
            using (var packed = file.CreateStream (index_entry.Offset, index_entry.Size))
            using (var unpacked = UnpackEntry (packed, index_entry))
            using (var index = new StreamReader (unpacked, Encodings.cp932))
            {
                for (int i = 0; i < dir.Count; ++i)
                {
                    int type = index.Read();
                    if (-1 == type)
                        break;
                    var name = index.ReadLine();
                    if (null == name)
                        break;
                    dir[i].Name = Path.GetFileName (name);
                }
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var ment = entry as MmaEntry;
            if (null == ment)
                return base.OpenEntry (arc, entry);
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return UnpackEntry (input, ment);
        }

        Stream UnpackEntry (IBinaryStream input, MmaEntry entry)
        {
            uint flags = entry.Flags & 6;
            if (6 == flags && 0 == entry.HeaderSize)
            {
                using (input)
                {
                    var data = new byte[entry.UnpackedSize];
                    UnpackLz (input, data, 0);
                    return new BinMemoryStream (data, entry.Name);
                }
            }
            else if (4 == flags)
            {
                using (input)
                {
                    input.Position = (int)entry.HeaderSize;
                    var data = input.ReadBytes ((int)entry.UnpackedSize);
                    Decrypt (data, 0, data.Length);
                    return new BinMemoryStream (data, entry.Name);
                }
            }
            return input.AsStream;
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var ment = (MmaEntry)entry;
            IBinaryStream input;
            switch (ment.Flags & 0x38)
            {
            case 8:
                input = arc.File.CreateStream (entry.Offset, entry.Size);
                return new MmeImageDecoder (input, ment);

            case 0x10:
            case 0x18:
                input = arc.File.CreateStream (entry.Offset, entry.Size);
                return new MmeMaskDecoder (input, ment);

            default:
                return base.OpenImage (arc, entry);
            }
        }

        internal static void Decrypt (byte[] data, int offset, int length)
        {
            for (int i = 0; i < length; ++i)
            {
                byte x = (byte)(data[offset+i] ^ DefaultKey[i & 0xF]);
                data[offset+i] = Binary.RotByteR (x, 3);
            }
        }

        internal static void UnpackLz (IBinaryStream input, byte[] output, int dst = 0)
        {
            byte id = input.ReadUInt8();
            if (id != 0xC0)
            {
                if (id != 0)
                    throw new InvalidFormatException();
                input.Read (output, 0, output.Length);
                return;
            }
            int ctl = 0;
            int mask = 0;
            while (dst < output.Length)
            {
                if (0 == mask)
                {
                    ctl = input.ReadByte();
                    if (-1 == ctl)
                        break;
                    mask = 0x80;
                }
                if ((ctl & mask) != 0)
                {
                    int offset = input.ReadUInt8() << 8;
                    offset |= input.ReadUInt8();
                    int count = (offset & 0x1F) + 3;
                    offset = (offset >> 5) + 1;
                    Binary.CopyOverlapped (output, dst - offset, dst, count);
                    dst += count;
                }
                else
                {
                    output[dst++] = input.ReadUInt8();
                }
                mask >>= 1;
            }
        }

        static readonly string DefaultKey = "ghTuEiAdjSwElTek";
    }

    internal class MmeBaseDecoder : BinaryImageDecoder
    {
        protected byte[]    m_output;
        protected int       m_header_size;
        protected uint      m_flags;
        protected int       m_stride;

        public PixelFormat Format { get; protected set; }

        protected MmeBaseDecoder (IBinaryStream input, MmaEntry entry) : base (input)
        {
            m_header_size = (int)entry.HeaderSize;
            m_flags = entry.Flags;
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = m_header_size;
            switch (m_flags & 6)
            {
            case 2:
                m_input.Read (m_output, 0, m_output.Length);
                break;
            case 4:
                Decrypt();
                break;
            case 6:
                MmaOpener.UnpackLz (m_input, m_output, 0);
                break;
            default:
                throw new InvalidFormatException();
            }
            return ImageData.Create (Info, Format, null, m_output, m_stride);
        }

        internal void Decrypt ()
        {
            m_input.Read (m_output, 0, m_output.Length);
            MmaOpener.Decrypt (m_output, m_header_size, m_output.Length - m_header_size);
        }
    }

    internal class MmeImageDecoder : MmeBaseDecoder
    {
        int         m_width;
        int         m_height;
        int         m_bpp;

        public MmeImageDecoder (IBinaryStream input, MmaEntry entry) : base (input, entry)
        {
            m_width = m_input.ReadInt32();
            m_height = m_input.ReadInt32();
            m_bpp = m_input.ReadInt32();
            m_stride = m_input.ReadInt32();
            Info = new ImageMetaData {
                Width = (uint)m_width,
                Height = (uint)m_height,
                BPP = m_bpp,
            };
            m_output = new byte[entry.UnpackedSize];
            if (24 == m_bpp)
                Format = PixelFormats.Bgr24;
            else
                Format = PixelFormats.Bgr32;
        }
    }

    internal class MmeMaskDecoder : MmeBaseDecoder
    {
        int         m_width;
        int         m_height;

        public MmeMaskDecoder (IBinaryStream input, MmaEntry entry) : base (input, entry)
        {
            m_width = m_input.ReadInt32();
            m_height = m_input.ReadInt32();
            m_stride = m_width;
            Info = new ImageMetaData {
                Width = (uint)m_width,
                Height = (uint)m_height,
                BPP = 8,
            };
            m_output = new byte[entry.UnpackedSize];
            Format = PixelFormats.Gray8;
        }
    }
}
