//! \file       ImageCHR.cs
//! \date       2018 Apr 10
//! \brief      Tigerman Project compound image.
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

namespace GameRes.Formats.Tigerman
{
    internal class ChrMetaData : ImageMetaData
    {
        public uint             Offset;
        public uint             Length;
        public ImageMetaData    ZitInfo;
    }

    [Export(typeof(ImageFormat))]
    public class ChrFormat : ImageFormat
    {
        public override string         Tag { get { return "CHR/TIGERMAN"; } }
        public override string Description { get { return "Tigerman Project compound image"; } }
        public override uint     Signature { get { return 0; } }

        public ChrFormat ()
        {
            Extensions = new string[] { "chr", "cls", "ev" };
            Signatures = new uint[] { 0x01B1, 0 };
        }

        static readonly ResourceInstance<ImageFormat> s_zit_format = new ResourceInstance<ImageFormat> ("ZIT");

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            uint base_offset = file.ReadUInt32();
            if (base_offset >= file.Length)
                return null;
            uint base_length = file.ReadUInt32();
            if (base_offset + base_length > file.Length)
                return null;
            file.Position = base_offset;
            uint signature = file.ReadUInt32();
            if (!s_zit_format.Value.Signatures.Contains (signature))
                return null;
            using (var zit = OpenZitStream (file, base_offset, base_length))
            {
                var info = s_zit_format.Value.ReadMetaData (zit);
                if (null == info)
                    return null;
                return new ChrMetaData {
                    Width = info.Width,
                    Height = info.Height,
                    BPP = info.BPP,
                    Offset = base_offset,
                    Length = base_length,
                    ZitInfo = info,
                };
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (ChrMetaData)info;
            using (var zit = OpenZitStream (file, meta.Offset, meta.Length))
                return s_zit_format.Value.Read (zit, meta.ZitInfo);
        }

        IBinaryStream OpenZitStream (IBinaryStream file, uint offset, uint size)
        {
            var input = new StreamRegion (file.AsStream, offset, size, true);
            return new BinaryStream (input, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ChrFormat.Write not implemented");
        }
    }
}
