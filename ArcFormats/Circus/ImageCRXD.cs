//! \file       ImageCRXD.cs
//! \date       Thu Oct 13 14:18:47 2016
//! \brief      Circus differential image format.
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

using System.ComponentModel.Composition;
using System.Drawing;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Circus
{
    internal class CrxdMetaData : ImageMetaData
    {
        public string       BaseFileName;
        public uint         BaseOffset;
        public CrxMetaData  DiffInfo;
        public uint         DiffOffset;
    }

    [Export(typeof(ImageFormat))]
    public class CrxdFormat : CrxFormat
    {
        public override string         Tag { get { return "CRXD"; } }
        public override string Description { get { return "Circus differential image format"; } }
        public override uint     Signature { get { return 0x44585243; } } // 'CRXD'

        public CrxdFormat ()
        {
            Extensions = new string[] { "crx" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x24);
            CrxdMetaData info = null;
            if (header.AsciiEqual (0x20, "CRXJ"))
            {
                stream.Position = 0x28;
                uint diff_offset = stream.ReadUInt32();
                using (var crx = OpenByOffset (diff_offset))
                {
                    if (null == crx)
                        return null;
                    info = ReadMetaData (crx) as CrxdMetaData;
                    if (info != null)
                        info.DiffOffset = diff_offset;
                }
            }
            else if (header.AsciiEqual (0x20, "CRXG"))
            {
                using (var crx_input = new StreamRegion (stream.AsStream, 0x20, true))
                using (var crx = new BinaryStream (crx_input, stream.Name))
                {
                    var diff_info = base.ReadMetaData (crx) as CrxMetaData;
                    if (null == diff_info)
                        return null;
                    info = new CrxdMetaData
                    {
                        Width   = diff_info.Width,
                        Height  = diff_info.Height,
                        OffsetX = diff_info.OffsetX,
                        OffsetY = diff_info.OffsetY,
                        BPP     = diff_info.BPP,
                        DiffInfo    = diff_info,
                        DiffOffset  = 0,
                    };
                }
            }
            if (info != null)
            {
                info.BaseOffset = header.ToUInt32 (8);
                info.BaseFileName = header.GetCString (0xC, 0x14);
            }
            return info;
        }

        IBinaryStream OpenByOffset (uint offset)
        {
            var vfs = VFS.Top as ArchiveFileSystem;
            if (null == vfs)
                return null;
            var arc = vfs.Source as CrmArchive;
            if (null == arc)
                return null;
            return arc.OpenByOffset (offset);
        }

        IBinaryStream OpenDiffStream (IBinaryStream diff, CrxdMetaData info)
        {
            Stream input;
            if (0 == info.DiffOffset)
            {
                input = new StreamRegion (diff.AsStream, 0x20, true);
            }
            else
            {
                diff = OpenByOffset (info.DiffOffset);
                if (null == diff)
                    throw new FileNotFoundException ("Referenced diff image not found");
                input = new StreamRegion (diff.AsStream, 0x20);
            }
            return new BinaryStream (input, diff.Name);
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (CrxdMetaData)info;
            IBinaryStream base_file = OpenByOffset (meta.BaseOffset);
            if (null == base_file)
            {
                var dir_name = VFS.GetDirectoryName (meta.FileName);
                var name = VFS.CombinePath (dir_name, meta.BaseFileName);
                if (!VFS.FileExists (name))
                    throw new FileNotFoundException ("Base image not found", meta.BaseFileName);
                base_file = VFS.OpenBinaryStream (name);
            }
            using (base_file)
            {
                var base_info = base.ReadMetaData (base_file) as CrxMetaData;
                if (null == base_info || base_info.BPP != info.BPP)
                    throw new InvalidFormatException ("Invalid base image");
                using (var reader = new Reader (base_file, base_info))
                using (var crx = OpenDiffStream (stream, meta))
                using (var diff_reader = new Reader (crx, meta.DiffInfo))
                {
                    reader.Unpack (true);
                    diff_reader.Unpack (true);
                    var diff_rect = new Rectangle (meta.OffsetX, meta.OffsetY, (int)meta.Width, (int)meta.Height);
                    var base_rect = new Rectangle (base_info.OffsetX, base_info.OffsetY,
                                                   (int)base_info.Width, (int)base_info.Height);
                    diff_rect = Rectangle.Intersect (diff_rect, base_rect);
                    if (diff_rect.IsEmpty)
                        return ImageData.Create (base_info, reader.Format, reader.Palette, reader.Data, reader.Stride);

                    int pixel_size = base_info.BPP / 8;
                    int x = diff_rect.X - base_rect.X;
                    int y = diff_rect.Y - base_rect.Y;
                    int dst = y * reader.Stride + pixel_size * x;
                    var image = reader.Data;

                    int dx = diff_rect.X - meta.OffsetX;
                    int dy = diff_rect.Y - meta.OffsetY;
                    int src = dy * diff_reader.Stride + pixel_size * dx;
                    var diff = diff_reader.Data;

                    int blend_stride = diff_rect.Width * pixel_size;
                    for (int row = 0; row < diff_rect.Height; ++row)
                    {
                        for (int i = 0; i < blend_stride; i += pixel_size)
                        {
                            image[dst+i  ] += diff[src+i];
                            image[dst+i+1] += diff[src+i+1];
                            image[dst+i+2] += diff[src+i+2];
                            if (4 == pixel_size)
                                image[dst+i+3] -= diff[src+i+3];
                        }
                        dst += reader.Stride;
                        src += diff_reader.Stride;
                    }
                    return ImageData.Create (base_info, reader.Format, reader.Palette, image, reader.Stride);
                }
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CrxdFormat.Write not implemented");
        }
    }
}
