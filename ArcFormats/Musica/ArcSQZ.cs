//! \file       ArcSQZ.cs
//! \date       Sun Oct 09 03:29:54 2016
//! \brief      Musica engine multi-frame images.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Compression;

namespace GameRes.Formats.Musica
{
    internal class SqzArchive : ArcFile
    {
        public readonly ImageMetaData Info;
        public readonly int FPS;

        public SqzArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, ImageMetaData info, int fps)
            : base (arc, impl, dir)
        {
            Info = info;
            FPS = fps;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class SqzOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SQZ"; } }
        public override string Description { get { return "Musica engine animated frames"; } }
        public override uint     Signature { get { return 0x315A5153; } } // 'SQZ1'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            var info = new ImageMetaData
            {
                Width   = file.View.ReadUInt32 (8),
                Height  = file.View.ReadUInt32 (0xC),
                BPP     = 32,
            };
            int fps = file.View.ReadInt32(0x10);
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint index_offset = 0x14;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D4}", base_name, i),
                    Type = "image",
                    Offset = file.View.ReadUInt32 (index_offset),
                    Size   = file.View.ReadUInt32 (index_offset+4),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new SqzArchive (file, this, dir, info, fps);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var sqarc = (SqzArchive)arc;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var zs = new ZLibStream (input, CompressionMode.Decompress);
            return new SqzReader (zs, sqarc.Info);
        }

        internal sealed class SqzReader : IImageDecoder
        {
            Stream          m_input;
            ImageMetaData   m_info;
            ImageData       m_image;

            public Stream            Source { get { return m_input; } }
            public ImageFormat SourceFormat { get { return null; } }
            public ImageMetaData       Info { get { return m_info; } }
            public ImageData Image
            {
                get
                {
                    if (null == m_image)
                    {
                        var pixels = new byte[m_info.Width * m_info.Height * 4];
                        m_input.Read (pixels, 0, pixels.Length);
                        m_image = ImageData.Create (m_info, PixelFormats.Bgra32, null, pixels);
                    }
                    return m_image;
                }
            }

            public SqzReader (Stream input, ImageMetaData info)
            {
                m_input = input;
                m_info = info;
            }

            bool m_disposed = false;
            public void Dispose ()
            {
                if (!m_disposed)
                {
                    m_input.Dispose();
                    m_disposed = true;
                }
            }
        }
    }
}
