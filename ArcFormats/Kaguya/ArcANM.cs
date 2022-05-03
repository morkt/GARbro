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

    internal class AnmEntry : Entry
    {
        public long ImageDataOffset;
        public uint ImageDataSize;
    }

    internal interface IAnmReader
    {
        List<Entry> GetFramesList (IBinaryStream input);
    }

    public abstract class AnmOpenerBase : ArchiveFormat, IAnmReader
    {
        public override string Description { get { return "KaGuYa script engine animation resource"; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public AnmOpenerBase ()
        {
            Extensions = new string[] { "anm" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            using (var input = file.CreateStream())
            {
                var dir = GetFramesList (input);
                if (null == dir)
                    return null;
                var base_info = GetBaseInfo (input);
                string base_name = Path.GetFileNameWithoutExtension (file.Name);
                int i = 0;
                foreach (var entry in dir)
                {
                    entry.Name = string.Format ("{0}#{1:D2}", base_name, i++);
                    entry.Type = "image";
                }
                return new AnmArchive (file, this, dir, base_info);
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var base_info = ((AnmArchive)arc).ImageInfo;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return CreateDecoder (input, base_info);
        }

        internal virtual ImageMetaData GetBaseInfo (IBinaryStream input)
        {
            input.Position = 4;
            return new ImageMetaData
            {
                OffsetX     = input.ReadInt32(),
                OffsetY     = input.ReadInt32(),
                Width       = input.ReadUInt32(),
                Height      = input.ReadUInt32(),
                BPP         = 32,
            };
        }

        public abstract List<Entry> GetFramesList (IBinaryStream input);

        public abstract IImageDecoder CreateDecoder (IBinaryStream input, ImageMetaData info);
    }

    [Export(typeof(ArchiveFormat))]
    public class AnmOpener : AnmOpenerBase
    {
        public override string         Tag { get { return "ANM/KAGUYA"; } }
        public override uint     Signature { get { return 0x30304E41; } } // 'AN00'

        public override List<Entry> GetFramesList (IBinaryStream file)
        {
            file.Position = 0x14;
            int frame_count = file.ReadInt16();
            file.Position = 0x18 + frame_count * 4;
            int count = file.ReadInt16();
            if (!IsSaneCount (count))
                return null;
            var current_offset = file.Position;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                file.Position = current_offset + 8;
                uint width  = file.ReadUInt32();
                uint height = file.ReadUInt32();
                uint image_size = 4*width*height;
                var entry = new AnmEntry
                {
                    Offset = current_offset,
                    Size = 0x10 + image_size,
                    ImageDataOffset = current_offset + 0x10,
                    ImageDataSize = image_size,
                };
                dir.Add (entry);
                current_offset += entry.Size;
            }
            return dir;
        }

        public override IImageDecoder CreateDecoder (IBinaryStream input, ImageMetaData info)
        {
            return new An00Decoder (input, info);
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
    public class An10Opener : AnmOpenerBase, IAnmReader
    {
        public override string         Tag { get { return "AN10/KAGUYA"; } }
        public override uint     Signature { get { return 0x30314E41; } } // 'AN10'

        public override List<Entry> GetFramesList (IBinaryStream file)
        {
            file.Position = 0x14;
            int frame_count = file.ReadInt16();
            file.Position = 0x18 + frame_count * 4;
            int count = file.ReadInt16();
            if (!IsSaneCount (count))
                return null;
            var current_offset = file.Position;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                file.Position = current_offset + 8;
                uint width  = file.ReadUInt32();
                uint height = file.ReadUInt32();
                uint channels = file.ReadUInt32();
                uint image_size = channels*width*height;
                var entry = new AnmEntry
                {
                    Offset = current_offset,
                    Size = 0x14 + image_size,
                    ImageDataOffset = current_offset + 0x14,
                    ImageDataSize = image_size,
                };
                dir.Add (entry);
                current_offset += entry.Size;
            }
            return dir;
        }

        public override IImageDecoder CreateDecoder (IBinaryStream input, ImageMetaData info)
        {
            return new An10Decoder (input, info);
        }
    }

    internal class An10Decoder : BinaryImageDecoder
    {
        public An10Decoder (IBinaryStream input, ImageMetaData base_info) : base (input)
        {
            Info = new ImageMetaData
            {
                OffsetX = base_info.OffsetX + m_input.ReadInt32(),
                OffsetY = base_info.OffsetY + m_input.ReadInt32(),
                Width   = m_input.ReadUInt32(),
                Height  = m_input.ReadUInt32(),
                BPP     = m_input.ReadInt32() * 8,
            };
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 0x14;
            int stride = Info.BPP / 8 * Info.iWidth;
            var pixels = m_input.ReadBytes (stride*Info.iHeight);
            PixelFormat format = 24 == Info.BPP ? PixelFormats.Bgr24 : PixelFormats.Bgra32;
            return ImageData.CreateFlipped (Info, format, null, pixels, stride);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class An20Opener : AnmOpenerBase
    {
        public override string         Tag { get { return "AN20/KAGUYA"; } }
        public override uint     Signature { get { return 0x30324E41; } } // 'AN20'

        public override List<Entry> GetFramesList (IBinaryStream file)
        {
            if (!SkipFrameTable (file))
                return null;
            int count = file.ReadInt16();
            if (!IsSaneCount (count))
                return null;
            long current_offset = file.Position + 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                file.Position = current_offset + 8;
                uint width  = file.ReadUInt32();
                uint height = file.ReadUInt32();
                uint depth  = file.ReadUInt32();
                uint image_size = depth*width*height;
                var entry = new AnmEntry
                {
                    Offset = current_offset,
                    Size = 0x14 + image_size,
                    ImageDataOffset = current_offset + 0x14,
                    ImageDataSize = image_size,
                };
                dir.Add (entry);
                current_offset += entry.Size;
            }
            return dir;
        }

        internal override ImageMetaData GetBaseInfo (IBinaryStream input)
        {
            SkipFrameTable (input);
            input.ReadInt16();
            return new ImageMetaData
            {
                OffsetX     = input.ReadInt32(),
                OffsetY     = input.ReadInt32(),
                Width       = input.ReadUInt32(),
                Height      = input.ReadUInt32(),
                BPP         = 32,
            };
        }

        bool SkipFrameTable (IBinaryStream file)
        {
            file.Position = 4;
            int table_count = file.ReadInt16();
            file.Position = 8;
            for (int i = 0; i < table_count; ++i)
            {
                switch (file.ReadByte())
                {
                case 0: break;
                case 1: file.Seek (8, SeekOrigin.Current); break;
                case 2:
                case 3:
                case 4:
                case 5: file.Seek (4, SeekOrigin.Current); break;
                default: return false;
                }
            }
            int count = file.ReadUInt16();
            file.Seek (count * 8, SeekOrigin.Current);
            return true;
        }

        public override IImageDecoder CreateDecoder (IBinaryStream input, ImageMetaData info)
        {
            return new An20Decoder (input, info);
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
            int stride = Info.BPP/8*(int)Info.Width;
            var pixels = m_input.ReadBytes (stride*(int)Info.Height);
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
