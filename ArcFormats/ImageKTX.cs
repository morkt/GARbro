//! \file       ImageKTX.cs
//! \date       2026 March 01
//! \brief      Khronos texture format.
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

using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using BCnEncoder.Shared.ImageFiles;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Reflection;
using System.Windows.Media;

namespace GameRes.Formats
{
    internal class KtxMetaData : ImageMetaData
    {
        public KtxHeader Header;
    }

    [Export(typeof(ImageFormat))]
    public class KtxFormat : ImageFormat
    {
        public override string         Tag { get { return "KTX"; } }
        public override string Description { get { return "Khronos texture format"; } }
        public override uint     Signature { get { return 0x58544BAB; } }
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var signature = file.ReadInt32 ();
            if (0x58544BAB != signature)
                return null;
            file.Position = 0xC;
            var header = new KtxHeader
            {
                Endianness            = file.ReadUInt32 (),
                GlType                = (GlType) file.ReadUInt32 (),
                GlTypeSize            = file.ReadUInt32 (),
                GlFormat              = (GlFormat) file.ReadUInt32 (),
                GlInternalFormat      = (GlInternalFormat) file.ReadUInt32 (),
                GlBaseInternalFormat  = (GlFormat) file.ReadUInt32 (),
                PixelWidth            = file.ReadUInt32 (),
                PixelHeight           = file.ReadUInt32 (),
                PixelDepth            = file.ReadUInt32 (),
                NumberOfArrayElements = file.ReadUInt32 (),
                NumberOfFaces         = file.ReadUInt32 (),
                NumberOfMipmapLevels  = file.ReadUInt32 (),
                BytesOfKeyValueData   = file.ReadUInt32 (),
            };
            var ktx = new KtxFile (header);
            var decoder = new BcDecoder ();
            if (!decoder.IsSupportedFormat (ktx))
                return null;
            return new KtxMetaData
            {
                Width  = header.PixelWidth,
                Height = header.PixelHeight,
                BPP    = 32,
                Header = header,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var ktx = KtxFile.Load (file.AsStream);
            var decoder = new BcDecoder ();
            var buffer = decoder.Decode (ktx);
            var pixels = new byte[buffer.Length*4];
            var src = 0;
            var dst = 0;
            while (src < buffer.Length)
            {
                pixels[dst  ] = buffer[src].b;
                pixels[dst+1] = buffer[src].g;
                pixels[dst+2] = buffer[src].r;
                pixels[dst+3] = buffer[src].a;
                src += 1;
                dst += 4;
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("KtxFormat.Write not implemented");
        }
    }
}
