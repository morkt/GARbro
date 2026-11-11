//! \file       ImageRCG.cs
//! \date       Sat Aug 15 2026
//! \brief      Rave RCG image format.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Compression;

namespace GameRes.Formats.Rave
{
    internal class RcgMetaData : ImageMetaData
    {
        public bool IsCompressed;
    }

    [Export(typeof(ImageFormat))]
    public class RcgFormat : ImageFormat
    {
        public override string         Tag { get { return "RCG"; } }
        public override string Description { get { return "'Sweet Stuff' image format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var meta = new ImageMetaData ();
            meta.BPP = 24;
            meta.Width = stream.ReadUInt16 ();
            meta.Height = stream.ReadUInt16 ();
            meta.OffsetX = 0;
            meta.OffsetY = 0;
            if (stream.Length != meta.Width * meta.Height * 3 + 4)
                return null;
            return meta;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 4;
            var data = new byte[info.iWidth * info.iHeight * 3];
            stream.Read (data, 0, data.Length);
            return ImageData.Create (info, PixelFormats.Bgr24, null, data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("RCGFormat.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class R0Format : RcgFormat
    {
        public override string         Tag { get { return "RCG/R0"; } }
        public override string Description { get { return "'Violet Plan' image format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var meta = new RcgMetaData ();
            meta.Width = stream.ReadUInt16 ();
            meta.Height = stream.ReadUInt16 ();
            meta.OffsetX = 0;
            meta.OffsetY = 0;

            var flags = new bool[2];
            for (int i = 0; i < 2; i++)
            {
                ushort flag = stream.ReadUInt16();
                if (flag > 1)
                    return null;
                flags[i] = flag != 0;
            }
            int bytes_per_pixel = flags[0] ? 4 : 3;
            meta.BPP = bytes_per_pixel * 8;
            meta.IsCompressed = flags[1];

            if (!meta.IsCompressed && stream.Length < meta.Width * meta.Height * bytes_per_pixel + 8)
                return null;
            return meta;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (RcgMetaData)info;
            stream.Position = 8;
            int bytes_per_pixel = meta.BPP / 8;
            var data = new byte[meta.iWidth * meta.iHeight * bytes_per_pixel];
            if (meta.IsCompressed)
            {
                using (var zstream = new ZLibStream (stream.AsStream, CompressionMode.Decompress, true))
                {
                    zstream.Read (data, 0, data.Length);
                }
            }
            else
            {
                stream.Read (data, 0, data.Length);
            }
            ReverseChannels (data, bytes_per_pixel);
            return ImageData.Create (info, bytes_per_pixel == 3 ? PixelFormats.Bgr24 : PixelFormats.Bgra32, null, data);
        }

        internal void ReverseChannels (byte[] pixels, int bytes_per_pixel)
        {
            for (int i = 0; i < pixels.Length; i += bytes_per_pixel)
            {
                for (int j = i, k = i + bytes_per_pixel - 1; j < k; j++, k--)
                {
                    byte t = pixels[j];
                    pixels[j] = pixels[k];
                    pixels[k] = t;
                }
            }
        }
    }

    [Export(typeof(ImageFormat))]
    public class R1Format : R0Format
    {
        public override string         Tag { get { return "RCG/R1"; } }
        public override string Description { get { return "Rave RCG image format"; } }
        public override uint     Signature { get { return 0x31520018; } }
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Position = 4;
            var meta = new ImageMetaData ();
            meta.Width = stream.ReadUInt16 ();
            meta.Height = stream.ReadUInt16 ();
            meta.OffsetX = 0;
            meta.OffsetY = 0;

            var flags = new bool[3];
            for (int i = 0; i < 3; i++)
            {
                ushort flag = stream.ReadUInt16();
                if (flag > 1)
                    return null;
                flags[i] = flag != 0;
            }
            stream.Position += 2;
            int bytes_per_pixel = flags[0] ? 4 : 3;
            meta.BPP = bytes_per_pixel * 8;

            var compressed_size = stream.ReadUInt32 ();
            if (compressed_size != stream.Length - 24)
                throw new System.NotImplementedException ("multi-frame RCG not implemented");
            var uncompressed_size = stream.ReadUInt32 ();
            if (uncompressed_size != meta.Width * meta.Height * bytes_per_pixel)
                return null;
            return meta;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 20;
            var uncompressed_size = stream.ReadUInt32 ();
            var data = new byte[uncompressed_size];
            using (var zstream = new ZLibStream (stream.AsStream, CompressionMode.Decompress, true))
            {
                if (uncompressed_size != zstream.Read (data, 0, (int)uncompressed_size))
                    return null;
                ReverseChannels (data, info.BPP / 8);
                return ImageData.Create (info, info.BPP == 24 ? PixelFormats.Bgr24 : PixelFormats.Bgra32, null, data);
            }
        }
    }
}