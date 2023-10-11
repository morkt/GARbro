//! \file       ImageAN1.cs
//! \date       2023 Oct 05
//! \brief      Discovery animation resource (PC-98).
//
// Copyright (C) 2023 by morkt
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

namespace GameRes.Formats.Discovery
{
    //[Export(typeof(ImageFormat))]
    public class An1Format : Pr1Format
    {
        public override string         Tag => "AN1";
        public override string Description => "Discovery animation resource";
        public override uint     Signature => 0;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".AN1"))
                return null;
            return base.ReadMetaData (file);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new AnReader (file, (PrMetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("An1Format.Write not implemented");
        }
    }

    internal class AnReader : PrReader
    {
        public AnReader (IBinaryStream file, PrMetaData info) : base (file, info)
        {
        }

        public new ImageData Unpack ()
        {
            UnpackPlanes();
            int frame_count = m_planes[0].ToUInt16 (2);
            int frame_width = 0x20;
            int frame_height = frame_count * 0x20;
            int output_stride = frame_width >> 1;
            var output = new byte[output_stride * frame_height];
            int src = frame_count * 0x16 + 6;
            m_plane_size = (output_stride >> 2) * frame_height;
            FlattenPlanes (src, output);
            Info.Width = (uint)frame_width;
            Info.Height = (uint)frame_height;
            return ImageData.Create (Info, PixelFormats.Indexed4, m_palette, output, output_stride);
        }
    }
}
