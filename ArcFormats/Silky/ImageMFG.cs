//! \file       ImageMFG.cs
//! \date       Wed Apr 29 13:47:56 2015
//! \brief      Silky's MFG image format implementation.
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
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Silky
{
    internal class MfgMetaData : ImageMetaData
    {
        public int Type;
        public int Stride;
    }

    [Export (typeof (ImageFormat))]
    public class MfgFormat : ImageFormat
    {
        public override string         Tag { get { return "MFG"; } }
        public override string Description { get { return "Silky's RGB image format"; } }
        public override uint     Signature { get { return 0x5f47464du; } } // 'MFG_'

        public MfgFormat ()
        {
            Signatures = new uint[] { 0x5f47464d, 0x4147464d, 0x4347464d }; // 'MFG_', 'MFGA', 'MFGC'
            Extensions = new string[] { "mfp" }; // made-up
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("MfgFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 3;
            byte id = file.ReadUInt8();
            uint data_size = file.ReadUInt32();
            uint width = file.ReadUInt32();
            uint height = file.ReadUInt32();
            uint stride = file.ReadUInt32();
            if (stride < width)
                throw new NotSupportedException();
            if (stride*height != data_size)
                return null;
            return new MfgMetaData
            {
                Width = width,
                Height = height,
                BPP = (int)(stride*8/width),
                Type = id,
                Stride = (int)stride,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (MfgMetaData)info;
            file.Position = 0x14;
            if ('_' != meta.Type)
                for (uint i = 0; i < meta.Height; ++i)
                {
                    uint n = file.ReadUInt32();
                    file.Seek (n*8, SeekOrigin.Current);
                }
            byte[] pixels = new byte[meta.Stride*info.Height];
            if (pixels.Length != file.Read (pixels, 0, pixels.Length))
                throw new InvalidFormatException ("Unexpected end of file");
            PixelFormat format;
            if (24 == meta.BPP)
                format = PixelFormats.Bgr24;
            else
                format = PixelFormats.Bgra32;
            return ImageData.Create (info, format, null, pixels, meta.Stride);
        }
    }
}
