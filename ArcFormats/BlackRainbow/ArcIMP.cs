//! \file       ArcIMP.cs
//! \date       Sun Feb 19 22:23:48 2017
//! \brief      Black Rainbow image archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.BlackRainbow
{
    internal class ImpArchive : ArcFile
    {
        public readonly uint Key;

        public ImpArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, uint key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class ImpOpener : ArchiveFormat
    {
        public override string         Tag { get { return "IMP"; } }
        public override string Description { get { return "BlackRainbow image archive"; } }
        public override uint     Signature { get { return 0x3D66; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public ImpOpener ()
        {
            Signatures = KnownSchemes.Keys;
        }

        static readonly Dictionary<uint, uint> KnownSchemes = new Dictionary<uint, uint>
        {
            { 0x3D66, 0xCE032ADB }, // Kannagi
            { 0x59E8, 0xD36050EC }, // From M
        };

        public override ArcFile TryOpen (ArcView file)
        {
            uint key = KnownSchemes[file.View.ReadUInt32 (0)];
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint base_offset = 0x404;
            uint offset = file.View.ReadUInt32 (4);
            uint index_offset = 8;
            var dir = new List<Entry>();
            for (int i = 0; i < 0xFF; ++i)
            {
                uint next_offset = file.View.ReadUInt32 (index_offset);
                uint size = next_offset - offset;
                if (size > 0x10)
                {
                    var entry = new Entry {
                        Name = string.Format ("{0}#{1:D3}", base_name, i),
                        Type = "image",
                        Offset = base_offset + offset,
                        Size = size,
                    };
                    dir.Add (entry);
                }
                index_offset += 4;
                offset = next_offset;
            }
            if (0 == dir.Count)
                return null;
            return new ImpArchive (file, this, dir, key);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var imp_arc = (ImpArchive)arc;
            var offset = entry.Offset;
            var info = new ImpMetaData
            {
                Width   = arc.File.View.ReadUInt32 (offset),
                Height  = arc.File.View.ReadUInt32 (offset+4),
                BPP     = 32,
                Key     = imp_arc.Key,
                HasAlpha = arc.File.View.ReadUInt32 (offset+12) != 0,
            };
            uint packed_size = arc.File.View.ReadUInt32 (offset+8);
            var input = arc.File.CreateStream (offset, packed_size+0x10);
            return new ImpDecoder (input, info);
        }
    }

    internal class ImpMetaData : ImageMetaData
    {
        public uint Key;
        public bool HasAlpha;
    }

    internal sealed class ImpDecoder : BinaryImageDecoder
    {
        byte[]  m_key;
        bool    m_has_alpha;

        public ImpDecoder (IBinaryStream input, ImpMetaData info) : base (input, info)
        {
            m_has_alpha = info.HasAlpha;
            m_key = new byte[4];
            LittleEndian.Pack (info.Key, m_key, 0);
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 0x10;
            var pixels = new byte[Info.Width * Info.Height * 4];
            using (var lzs = new ByteStringEncryptedStream (m_input.AsStream, m_key, true))
            using (var input = new LzssStream (lzs))
            {
                if (pixels.Length != input.Read (pixels, 0, pixels.Length))
                    throw new InvalidFormatException();
                var format = m_has_alpha ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
                return ImageData.Create (Info, format, null, pixels);
            }
        }
    }
}
