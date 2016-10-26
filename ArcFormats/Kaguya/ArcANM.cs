//! \file       ArcANM.cs
//! \date       Sat Jan 23 04:23:39 2016
//! \brief      KaGuYa script engine animation resource.
//
// Copyright (C) 2016 by morkt
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

namespace GameRes.Formats.Kaguya
{
    internal class AnmArchive : ArcFile
    {
        public readonly ImageMetaData   ImageInfo;

        public AnmArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, ImageMetaData base_info)
            : base (arc, impl, dir)
        {
            ImageInfo = base_info;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class AnmOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ANM/KAGUYA"; } }
        public override string Description { get { return "KaGuYa script engine animation resource"; } }
        public override uint     Signature { get { return 0x30304E41; } } // 'AN00'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public AnmOpener ()
        {
            Extensions = new string[] { "anm" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int frame_count = file.View.ReadInt16 (0x14);
            uint current_offset = 0x18 + (uint)frame_count * 4;
            int count = file.View.ReadInt16 (current_offset);
            if (!IsSaneCount (count))
                return null;
            var base_info = new ImageMetaData
            {
                OffsetX     = file.View.ReadInt32 (4),
                OffsetY     = file.View.ReadInt32 (8),
                Width       = file.View.ReadUInt32 (0x0C),
                Height      = file.View.ReadUInt32 (0x10),
                BPP         = 32,
            };
            current_offset += 2;
            string base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint width  = file.View.ReadUInt32 (current_offset+8);
                uint height = file.View.ReadUInt32 (current_offset+12);
                var entry = new Entry
                {
                    Name = string.Format ("{0}#{1:D2}", base_name, i),
                    Type = "image",
                    Offset = current_offset,
                    Size = 0x10 + 4*width*height,
                };
                dir.Add (entry);
                current_offset += entry.Size;
            }
            return new AnmArchive (file, this, dir, base_info);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var base_info = ((AnmArchive)arc).ImageInfo;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new An00Decoder (input, base_info);
        }
    }

    internal class An00Decoder : BinaryImageDecoder
    {
        public An00Decoder (IBinaryStream input, ImageMetaData base_info) : base (input)
        {
            Info = new ImageMetaData
            {
                OffsetX = base_info.OffsetX + m_input.ReadInt32(),
                OffsetY = base_info.OffsetY + m_input.ReadInt32(),
                Width   = m_input.ReadUInt32(),
                Height  = m_input.ReadUInt32(),
                BPP     = 32,
            };
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 0x10;
            int stride = 4*(int)Info.Width;
            var pixels = m_input.ReadBytes (stride*(int)Info.Height);
            return ImageData.CreateFlipped (Info, PixelFormats.Bgra32, null, pixels, stride);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class An20Opener : ArchiveFormat
    {
        public override string         Tag { get { return "AN20/KAGUYA"; } }
        public override string Description { get { return "KaGuYa script engine animation resource"; } }
        public override uint     Signature { get { return 0x30324E41; } } // 'AN20'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public An20Opener ()
        {
            Extensions = new string[] { "anm" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int table_count = file.View.ReadInt16 (4);
            uint current_offset = 8;
            for (int i = 0; i < table_count; ++i)
            {
                switch (file.View.ReadByte (current_offset++))
                {
                case 0: break;
                case 1: current_offset += 8; break;
                case 2:
                case 3:
                case 4:
                case 5: current_offset += 4; break;
                default: return null;
                }
            }
            current_offset += 2 + file.View.ReadUInt16 (current_offset) * 8u;
            int count = file.View.ReadInt16 (current_offset);
            if (!IsSaneCount (count))
                return null;
            current_offset += 2;
            var base_info = new ImageMetaData
            {
                OffsetX     = file.View.ReadInt32 (current_offset),
                OffsetY     = file.View.ReadInt32 (current_offset+4),
                Width       = file.View.ReadUInt32 (current_offset+8),
                Height      = file.View.ReadUInt32 (current_offset+12),
                BPP         = 32,
            };
            current_offset += 0x10;
            string base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint width  = file.View.ReadUInt32 (current_offset+8);
                uint height = file.View.ReadUInt32 (current_offset+0x0C);
                uint depth  = file.View.ReadUInt32 (current_offset+0x10);
                var entry = new Entry
                {
                    Name = string.Format ("{0}#{1:D2}", base_name, i),
                    Type = "image",
                    Offset = current_offset,
                    Size = 0x14 + depth*width*height,
                };
                dir.Add (entry);
                current_offset += entry.Size;
            }
            return new AnmArchive (file, this, dir, base_info);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var base_info = ((AnmArchive)arc).ImageInfo;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new An20Decoder (input, base_info);
        }
    }

    internal class An20Decoder : BinaryImageDecoder
    {
        public An20Decoder (IBinaryStream input, ImageMetaData base_info) : base (input)
        {
            Info = new ImageMetaData
            {
                OffsetX     = base_info.OffsetX + m_input.ReadInt32(),
                OffsetY     = base_info.OffsetY + m_input.ReadInt32(),
                Width       = m_input.ReadUInt32(),
                Height      = m_input.ReadUInt32(),
                BPP         = m_input.ReadInt32() * 8,
            };
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 0x14;
            int stride = info.BPP/8*(int)Info.Width;
            var pixels = m_input.ReadBytes (stride*(int)info.Height);
            return ImageData.CreateFlipped (Info, GetFormat(), null, pixels, stride);
        }

        PixelFormat GetFormat ()
        {
            switch (Info.BPP)
            {
            case  8: return PixelFormats.Gray8;
            case 24: return PixelFormats.Bgr24;
            case 32: return PixelFormats.Bgra32;
            default: throw new InvalidFormatException();
            }
        }
    }
}
