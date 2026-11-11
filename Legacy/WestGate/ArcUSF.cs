//! \file       ArcALH.cs
//! \date       2017 Dec 15
//! \brief      West Gate resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.WestGate
{
    [Export(typeof(ArchiveFormat))]
    public class UsfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "USF"; } }
        public override string Description { get { return "West Gate resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public UsfOpener ()
        {
            Extensions = new string[] { "alh", "usf", "udc", "uwb", "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint first_offset = file.View.ReadUInt32 (0xC);
            if (first_offset >= file.MaxOffset || 0 != (first_offset & 0xF))
                return null;
            int count = (int)(first_offset / 0x10);
            if (!IsSaneCount (count))
                return null;

            var entry_type = "";
            if (IsGraphicArchive (Path.GetFileName (file.Name)))
                entry_type = "image";
            var dir = UcaTool.ReadIndex (file, 0, count, entry_type);
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.OpenBinaryEntry (entry);
            var name = Path.GetFileName (arc.File.Name);
            if (IsGraphicArchive (name) && 0x1C == input.Signature)
            {
                var dir = Path.GetDirectoryName (arc.File.Name);
                try
                {
                    return new AlhBitmapDecoder (input, dir, name);
                }
                catch {}
            }
            return ImageFormatDecoder.Create (input);
        }

        bool IsGraphicArchive (string name)
        {
            return name.ToLowerAscii().StartsWith ("grap");
        }
    }
    
    internal sealed class AlhBitmapDecoder : BinaryImageDecoder
    {
        string m_arc_name, m_pal_name;
        
        public AlhBitmapDecoder (IBinaryStream input, string dir, string name) : base (input)
        {
            var header = m_input.ReadHeader (0x1C);
            uint width = header.ToUInt32 (4);
            uint height = header.ToUInt32 (8);
            uint size = header.ToUInt32 (0xC);
            if (width * height != size)
                throw new InvalidFormatException();

            m_arc_name = Path.Combine (dir, name.Replace ("grap", "pal"));
            if (!File.Exists (m_arc_name))
                throw new FileNotFoundException();
            m_pal_name = header.GetCString (0x10, 0xC);

            Info = new ImageMetaData {
                Width  = width,
                Height = height,
                BPP    = 24,
            };
        }

        protected override ImageData GetImageData ()
        {
            BitmapPalette palette;
            using (var file = new ArcView (m_arc_name))
            using (var arc = Usf.Value.TryOpen (file))
            {
                if (null == arc)
                    return null;
                var pal = arc.Dir.First (e => e.Name == m_pal_name);
                using (var entry = arc.OpenEntry (pal))
                {
                    var pal_data = new byte[entry.Length];
                    entry.Read (pal_data, 0, pal_data.Length);

                    using (var mem = new MemoryStream (pal_data))
                    using (var reader = new BinaryReader (mem))
                    {
                        uint header_len = reader.ReadUInt32();
                        if (header_len != 8)
                            return null;
                        int count = reader.ReadInt32();
                        var colors = new Color[count];
                        for (int i = 0; i < count; i++)
                        {
                            var c = reader.ReadBytes (4);
                            colors[i] = Color.FromRgb (c[0], c[1], c[2]);
                        }
                        palette = new BitmapPalette (colors);
                    }
                }
            }

            m_input.Position = 0x1C;
            var pixels = m_input.ReadBytes (Info.iWidth * Info.iHeight);
            return ImageData.Create (Info, PixelFormats.Indexed8, palette, pixels);
        }
        
        static readonly ResourceInstance<ArchiveFormat> Usf = new ResourceInstance<ArchiveFormat> ("USF");
    }
}
