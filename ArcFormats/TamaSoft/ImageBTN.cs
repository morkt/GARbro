//! \file       ImageBTN.cs
//! \date       Sat Sep 24 10:34:11 2016
//! \brief      TamaSoft button image format.
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
using System.IO;

namespace GameRes.Formats.Tama
{
    internal class BtnMetaData : ImageMetaData
    {
        public int SurOffset;
    }

    [Export(typeof(ImageFormat))]
    public class BtnFormat : SurFormat
    {
        public override string         Tag { get { return "BTN/SUR"; } }
        public override string Description { get { return "TamaSoft ADV system button image"; } }
        public override uint     Signature { get { return 0x4E544245; } } // 'EBTN'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Position = 4;
            int count = stream.ReadInt32();
            int offset = 0x30 + count * 4;
            using (var data = new StreamRegion (stream.AsStream, offset, true))
            using (var input = new BinaryStream (data, stream.Name))
            {
                var info = base.ReadMetaData (input);
                if (null == info)
                    return null;
                return new BtnMetaData
                {
                    Width = info.Width,
                    Height = info.Height,
                    BPP = info.BPP,
                    SurOffset = offset,
                };
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (BtnMetaData)info;
            using (var data = new StreamRegion (stream.AsStream, meta.SurOffset, true))
            using (var input = new BinaryStream (data, stream.Name))
                return base.Read (input, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BtnFormat.Write not implemented");
        }
    }
}
