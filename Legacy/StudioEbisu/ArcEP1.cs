//! \file       ArcEP1.cs
//! \date       2019 Mar 13
//! \brief      Studio Ebisu resource archive.
//
// Copyright (C) 2019 by morkt
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
using System.Windows.Media;
using GameRes.Compression;

namespace GameRes.Formats.Ebisu
{
    internal class Ep1MetaData : ImageMetaData
    {
        public int  Method;
    }

    internal class Ep1Entry : Entry
    {
        public Ep1MetaData  Info;
    }

    [Export(typeof(ArchiveFormat))]
    public class Ep1Opener : ArchiveFormat
    {
        public override string         Tag { get { return "EP1"; } }
        public override string Description { get { return "Studio Ebisu resource archive"; } }
        public override uint     Signature { get { return 0x315045; } } // 'EP1'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            long index_offset = 8;
            var dir = new List<Entry>();
            while (index_offset < file.MaxOffset)
            {
                var name = file.View.ReadString (index_offset, 0x20);
                if (string.IsNullOrEmpty (name))
                    return null;
                var entry = new Ep1Entry {
                    Name = name,
                    Type = "image",
                    Offset = index_offset + 0x30,
                    Size = file.View.ReadUInt32 (index_offset+0x2C),
                    Info = new Ep1MetaData {
                        Width  = file.View.ReadUInt32 (index_offset+0x20),
                        Height = file.View.ReadUInt32 (index_offset+0x24),
                        Method = file.View.ReadInt32  (index_offset+0x28),
                        BPP    = 32,
                    },
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x30 + entry.Size;
            }
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var ep1ent = (Ep1Entry)entry;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new Ep1BitmapDecoder (input, ep1ent.Info);
        }
    }

    internal class Ep1BitmapDecoder : BinaryImageDecoder
    {
        int     m_method;

        bool IsCompressed { get { return m_method >= 4 && m_method <= 7; } }

        public Ep1BitmapDecoder (IBinaryStream input, Ep1MetaData info) : base (input, info)
        {
            m_method = info.Method;
        }

        protected override ImageData GetImageData ()
        {
            var pixels = new byte[Info.iWidth * Info.iHeight * 4];
            if (IsCompressed)
            {
                using (var lzss = new LzssStream (m_input.AsStream, LzssMode.Decompress, true))
                {
                    lzss.Config.FrameInitPos = 0xFF0;
                    lzss.Read (pixels, 0, pixels.Length);
                }
            }
            else
            {
                m_input.Read (pixels, 0, pixels.Length);
            }
            return ImageData.Create (Info, PixelFormats.Bgra32, null, pixels);
        }
    }
}
