//! \file       ArcYK.cs
//! \date       2023 Sep 10
//! \brief      Koei data archive.
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

using GameRes.Utility;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [990312][Koei] Yakusoku no Kizuna

namespace GameRes.Formats.Koei
{
    internal class YkArchive : ArcFile
    {
        public readonly string YkName;

        public YkArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, string name)
            : base (arc, impl, dir)
        {
            YkName = name;
        }
    }

    internal class YkEntry : Entry
    {
        public int Id;
    }

    [Export(typeof(ArchiveFormat))]
    public partial class YkOpener : ArchiveFormat
    {
        public override string         Tag => "YK";
        public override string Description => "Koei resource archive";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".YK"))
                return null;
            var name = Path.GetFileNameWithoutExtension (file.Name).ToUpperInvariant();
            List<Entry> dir = null;
            if ("DATA01" == name)
                dir = GetData01Index (file);
            else if (OffsetTable.ContainsKey (name))
                dir = GetDataIndex (file, OffsetTable[name], name);
            if (null == dir)
                return null;
            return new YkArchive (file, this, dir, name);
        }

        List<Entry> GetData01Index (ArcView file)
        {
            int count = (int)(file.MaxOffset / 0x4B400);
            if (!IsSaneCount (count) || count * 0x4B400 != file.MaxOffset)
                return null;
            uint offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = string.Format ("{0:D5}.BMP", i),
                    Type = "image",
                    Offset = offset,
                    Size = 0x4B400
                };
                dir.Add (entry);
                offset += 0x4B400;
            }
            return dir;
        }

        List<Entry> GetDataIndex (ArcView file, uint[] offsets, string name)
        {
            var dir = new List<Entry> (offsets.Length);
            uint current_offset = 0;
            for (int i = 0; i < offsets.Length; ++i)
            {
                var entry = new YkEntry {
                    Name = string.Format ("{0}#{1:D4}", name, i),
                    Offset = current_offset,
                    Size = offsets[i] - current_offset,
                    Id = i
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if ("DATA02" == name && (i >= 11 || Data02Images.ContainsKey (i)))
                {
                    entry.Name += ".BMP";
                    entry.Type = "image";
                }
                else if (IsAudio.Contains (name))
                {
                    entry.Name += ".WAV";
                    entry.Type = "audio";
                }
                dir.Add (entry);
                current_offset = offsets[i];
            }
            return dir;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var yarc = (YkArchive)arc;
            if ("DATA03" == yarc.YkName)
            {
                var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
                DecryptData03 (data);
                return new BinMemoryStream (data, entry.Name);
            }
            else if (IsAudio.Contains (yarc.YkName))
                return OpenAudio (arc, entry);
            else
                return base.OpenEntry (arc, entry);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var yarc = (YkArchive)arc;
            if (yarc.YkName == "DATA02")
                return OpenData02Image (arc, (YkEntry)entry);
            else if (yarc.YkName != "DATA01")
                return base.OpenImage (arc, entry);
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new YkImageDecoder (input, new ImageMetaData { Width = 640, Height = 480, BPP = 8 });
        }

        IImageDecoder OpenData02Image (ArcFile arc, YkEntry entry)
        {
            ImageMetaData info;
            if (entry.Id >= 189)
                info = new ImageMetaData { Width = 128, Height = 192, BPP = 8 };
            else if (!Data02Images.TryGetValue (entry.Id, out info) || null == info)
                return base.OpenImage (arc, entry);
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new YkImageDecoder (input, info);
        }

        static readonly byte[] RiffHeader = new byte[] {
            (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0,
            (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ',
        };

        Stream OpenAudio (ArcFile arc, Entry entry)
        {
            var header = RiffHeader.Clone() as byte[];
            LittleEndian.Pack (entry.Size+8u, header, 4);
            var wave = arc.File.CreateStream (entry.Offset, entry.Size);
            return new PrefixStream (header, wave);
        }

        unsafe void DecryptData03 (byte[] data)
        {
            int count = data.Length >> 2;
            fixed (byte* data8 = &data[0])
            {
                uint* data32 = (uint*)data8;
                while (count --> 0)
                {
                    *data32++ ^= 0x12C4D65u;
                }
            }
        }
    }

    internal class YkImageDecoder : BinaryImageDecoder
    {
        public YkImageDecoder (IBinaryStream input, ImageMetaData info) : base (input, info)
        {
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 0;
            var palette = ImageFormat.ReadPalette (m_input.AsStream);
            int size = Info.iWidth * Info.iHeight;
            var pixels = m_input.ReadBytes (size);
            return ImageData.CreateFlipped (Info, PixelFormats.Indexed8, palette, pixels, Info.iWidth);
        }
    }
}
