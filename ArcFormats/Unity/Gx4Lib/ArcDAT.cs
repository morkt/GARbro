//! \file       ArcDAT.cs
//! \date       2018 Aug 30
//! \brief      Unity GX4Lib resource archive.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows.Media;
using GameRes.Gx4Lib;
using GameRes.Utility;

namespace GameRes.Formats.Unity.Gx4Lib
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/GX4LIB"; } }
        public override string Description { get { return "Unity GX4Lib resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_size = file.View.ReadUInt32 (0);
            if (index_size <= 12 || index_size >= file.MaxOffset)
                return null;
            if (file.View.ReadInt32 (5) != 1 || file.View.ReadInt32 (9) != -1)
                return null;
            using (var hstream = file.CreateStream (4, index_size))
            {
                var pf = new PackageFile();
                var index = pf.Deserialize (hstream);
                if (null == index)
                    return null;
                string type = "";
                if (index is PFAudioHeaders)
                    type = "audio";
                else if (index is PFImageHeaders)
                    type = "image";
                uint data_offset = index_size + 4;
                var dir = index.headers.Select (h => new PackedEntry {
                    Name = h.FileName,
                    Type = type,
                    Offset = h.readStartBytePos + data_offset,
                    Size = (uint)h.ByteLength
                } as Entry).ToList();
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (arc.File.View.AsciiEqual (entry.Offset, "UnityRaw"))
                return OpenUnityRaw (arc, entry);
            var pent = (PackedEntry)entry;
            if (!pent.IsPacked)
            {
                byte id = arc.File.View.ReadByte (entry.Offset);
                uint packed_size = arc.File.View.ReadUInt32 (entry.Offset+1);
                if ((id & ~1) != 0x5E || packed_size != entry.Size)
                    return base.OpenEntry (arc, entry);
                pent.IsPacked = 0 != (id & 1);
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset+5);
            }
            if (!pent.IsPacked)
                return arc.File.CreateStream (pent.Offset+9, pent.UnpackedSize);
            var data = new byte[pent.UnpackedSize];
            using (var input = arc.File.CreateStream (pent.Offset+9, pent.Size))
                QlzUnpack (input, data);
            return new BinMemoryStream (data, entry.Name);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.OpenBinaryEntry (entry);
            int length = (int)(input.Length - 0x10);
            if ((length & 3) != 0)
                return ImageFormatDecoder.Create (input);
            uint width  = input.ReadUInt32();
            uint height = input.ReadUInt32();
            int buf_width  = input.ReadInt32();
            int buf_height = input.ReadInt32();
            var info = new ImageMetaData { Width = width, Height = height, BPP = 32 };
            return new Gx4ImageDecoder (input, info);
        }

        Stream OpenUnityRaw (ArcFile arc, Entry entry)
        {
            using (var stream = arc.File.CreateStream (entry.Offset, entry.Size))
            using (var input = new AssetReader (stream))
            {
                var signature = input.ReadCString();
                if (signature != "UnityRaw")
                    return base.OpenEntry (arc, entry);
                int format = input.ReadInt32();
                input.ReadCString();
                input.ReadCString();
                uint file_size = input.ReadUInt32();
                if (file_size != entry.Size)
                    return base.OpenEntry (arc, entry);
                uint header_size = input.ReadUInt32();
                int entry_count = input.ReadInt32();
                int bundle_count = input.ReadInt32();
                if (entry_count != 1 || bundle_count != 1)
                    return base.OpenEntry (arc, entry);

                input.Position = header_size;
                int count = input.ReadInt32();
                long asset_pos = input.Position;
                input.ReadCString(); // asset_name
                header_size = input.ReadUInt32();
                uint asset_size = input.ReadUInt32();
                long base_pos = asset_pos + header_size - 4;

                input.Position = base_pos;
                var index = new ResourcesAssetsDeserializer (arc.File.Name);
                var dir = index.Parse (input, base_pos);
                if (null == dir || 0 == dir.Count)
                    return base.OpenEntry (arc, entry);;
                return arc.File.CreateStream (entry.Offset + dir[0].Offset, dir[0].Size);
            }
        }

        void QlzUnpack (IBinaryStream input, byte[] output)
        {
            int dst = 0;
            uint bits = 1;
            int output_last = output.Length - 11;
            while (dst < output.Length)
            {
                if (1 == bits)
                {
                    bits = input.ReadUInt32();
                }
                if ((bits & 1) == 1)
                {
                    int ctl = input.PeekByte();
                    int offset, count = 3;
                    if ((ctl & 3) == 0)
                    {
                        offset = input.ReadUInt8() >> 2;
                    }
                    else if ((ctl & 2) == 0)
                    {
                        offset = input.ReadUInt16() >> 2;
                    }
                    else if ((ctl & 1) == 0)
                    {
                        offset = input.ReadUInt16() >> 6;
                        count += ((ctl >> 2) & 0xF);
                    }
                    else if ((ctl & 0x7F) != 3)
                    {
                        offset = (input.ReadInt24() >> 7) & 0x1FFFF;
                        count += ((ctl >> 2) & 0x1F) - 1;
                    }
                    else
                    {
                        uint v = input.ReadUInt32();
                        offset = (int)(v >> 15);
                        count += (int)((v >> 7) & 0xFF);
                    }
                    Binary.CopyOverlapped (output, dst-offset, dst, count);
                    dst += count;
                }
                else
                {
                    if (dst > output_last)
                        break;
                    output[dst++] = input.ReadUInt8();
                }
                bits >>= 1;
            }
            while (dst < output.Length)
            {
                if (1 == bits)
                {
                    input.Seek (4, SeekOrigin.Current);
                    bits = 0x80000000u;
                }
                output[dst++] = input.ReadUInt8();
                bits >>= 1;
            }
        }
    }

    internal class Gx4ImageDecoder : BinaryImageDecoder
    {
        public Gx4ImageDecoder (IBinaryStream input, ImageMetaData info) : base (input, info)
        {
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 0x10;
            int stride = (int)Info.Width * 4;
            var pixels = m_input.ReadBytes (stride * (int)Info.Height);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte t = pixels[i];
                pixels[i] = pixels[i+2];
                pixels[i+2] = t;
            }
            return ImageData.CreateFlipped (Info, PixelFormats.Bgra32, null, pixels, stride);
        }
    }
}
