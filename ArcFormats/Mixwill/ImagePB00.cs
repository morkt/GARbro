//! \file       ImagePB00.cs
//! \date       Sat Jan 14 23:58:54 2017
//! \brief      Mixwill soft image format.
//
// Copyright (C) 2017 by morkt
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

namespace GameRes.Formats.Mixwill
{
    internal class Pb00MetaData :ImageMetaData
    {
        public readonly int[]   ChannelTable = new int[4];
    }

    [Export(typeof(ImageFormat))]
    public class Pb00Format : ImageFormat
    {
        public override string         Tag { get { return "PB00"; } }
        public override string Description { get { return "Mixwill soft image format"; } }
        public override uint     Signature { get { return 0x30304250; } } // 'PB00'

        public Pb00Format ()
        {
            Extensions = new string[] { "pb" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            var info = new Pb00MetaData
            {
                Width   = header.ToUInt32 (8),
                Height  = header.ToUInt32 (0xC),
                BPP     = header.ToInt32 (4) * 8,
            };
            for (int i = 0; i < 4; ++i)
                info.ChannelTable[i] = header.ToInt32 (0x10 + i * 4);

            return info;
        }

        static readonly byte[] RgbOrder = { 2, 1, 0, 3 };

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (Pb00MetaData)info;
            int channels = info.BPP/8;
            var pixels = new byte[info.Width * info.Height * channels];
            long channel_pos = 0x20;
            for (int i = 0; i < channels; ++i)
            {
                file.Position = channel_pos;
                int length = meta.ChannelTable[i];
                channel_pos += length;
                int dst = RgbOrder[i];
                for (int j = 0; j < length; ++j)
                {
                    int b = file.ReadInt8();
                    if (b >= 0)
                    {
                        for (int count = b + 1; count > 0; --count)
                        {
                            pixels[dst] = file.ReadUInt8();
                            dst += channels;
                            ++j;
                        }
                    }
                    else
                    {
                        byte c = file.ReadUInt8();
                        for (int count = 1 - b; count > 0; --count)
                        {
                            pixels[dst] = c;
                            dst += channels;
                        }
                        ++j;
                    }
                }
            }
            PixelFormat format;
            if (4 == channels)
                format = PixelFormats.Bgra32;
            else
                format = PixelFormats.Bgr24;
            return ImageData.Create (info, format, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Pb00Format.Write not implemented");
        }
    }
}
