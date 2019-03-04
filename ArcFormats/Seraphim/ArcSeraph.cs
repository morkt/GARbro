//! \file       ArcSeraph.cs
//! \date       Fri Jul 17 13:47:34 2015
//! \brief      Seraphim engine resource archives.
//
// Copyright (C) 2015-2017 by morkt
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
using System.Linq;
using System.Windows.Media;
using GameRes.Compression;

namespace GameRes.Formats.Seraphim
{
    internal class ArchEntry : PackedEntry
    {
        public short    DirIndex;
        public short    FileIndex;
    }

    [Serializable]
    public class SeraphScheme : ResourceScheme
    {
        public IDictionary<string, ArchPacScheme>   KnownSchemes;
    }

    [Serializable]
    public class ArchPacScheme
    {
        public long     IndexOffset;
        public short    EventDir;
        public IDictionary<short, short> EventMap;
    }

    internal class SeraphArchive : ArcFile
    {
        public readonly short                       EventDir;
        public readonly IDictionary<short, short>   EventMap;

        public SeraphArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, ArchPacScheme scheme)
            : base (arc, impl, dir)
        {
            EventDir = scheme.EventDir;
            EventMap = scheme.EventMap;
        }
    }

    /// <summary>
    /// this archive format has different index offsets hardcoded into game executable.
    /// </summary>
    [Export(typeof(ArchiveFormat))]
    public class ArchPacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SERAPH/ARCH"; } }
        public override string Description { get { return "Seraphim engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }
        public          bool   IsAmbiguous { get { return true; } }

        public ArchPacOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset > uint.MaxValue
                || !VFS.IsPathEqualsToFileName (file.Name, "ArchPac.dat"))
                return null;
            foreach (var scheme in KnownSchemes.Values.Where (s => s.IndexOffset < file.MaxOffset).OrderBy (s => s.IndexOffset))
            {
                var dir = ReadIndex (file, scheme.IndexOffset, file.MaxOffset);
                if (dir != null)
                {
                    if (scheme.EventMap != null)
                        return new SeraphArchive (file, this, dir, scheme);
                    else
                        return new ArcFile (file, this, dir);
                }
            }
            var scnpac_name = VFS.ChangeFileName (file.Name, "ScnPac.dat");
            if (!VFS.FileExists (scnpac_name))
                return null;
            using (var scnpac = VFS.OpenView (scnpac_name))
            {
                uint first_offset = scnpac.View.ReadUInt32 (4);
                uint index_offset = scnpac.View.ReadUInt32 (first_offset-4);
                var dir = ReadIndex (scnpac, index_offset, file.MaxOffset);
                if (dir != null)
                    return new ArcFile (file, this, dir);
            }
            return null;
        }

        // 3 @ ScnPac.Dat : FF 18 05 XX XX XX XX
        //                  FF 16 05 XX XX XX XX

        List<Entry> ReadIndex (ArcView file, long index_offset, long max_offset)
        {
            int base_count = file.View.ReadInt32 (index_offset);
            int file_count = file.View.ReadInt32 (index_offset + 4);
            index_offset += 8;
            if (base_count <= 0 || base_count > 0x40 || !IsSaneCount (file_count))
                return null;
            var base_offsets = new List<Tuple<uint, int>> (base_count);
            int total_count = 0;
            for (int i = 0; i < base_count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                int count = file.View.ReadInt32 (index_offset+4);
                if (count <= 0 || count > file_count || offset > max_offset)
                    return null;
                total_count += count;
                if (total_count > file_count)
                    return null;
                base_offsets.Add (Tuple.Create (offset, count));
                index_offset += 8;
            }
            if (total_count != file_count)
                return null;
            var dir = new List<Entry> (file_count);
            for (int j = base_count-1; j >= 0; --j)
            {
                uint next_offset = file.View.ReadUInt32 (index_offset);
                index_offset += 4;
                for (int i = 0; i < base_offsets[j].Item2; ++i)
                {
                    var entry = new ArchEntry {
                        Name = FormatEntryName (j, i),
                        Type = "image",
                        DirIndex = (short)j,
                        FileIndex = (short)i,
                        Offset = next_offset,
                    };
                    next_offset = file.View.ReadUInt32 (index_offset);
                    index_offset += 4;
                    if (next_offset < entry.Offset)
                        return null;
                    entry.Size = (uint)(next_offset - entry.Offset);
                    entry.Offset += base_offsets[j].Item1;
                    if (!entry.CheckPlacement (max_offset))
                        return null;
                    if (entry.Size > 0)
                        dir.Add (entry);
                }
            }
            if (0 == dir.Count)
                return null;
            return dir;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (0 == entry.Size)
                return Stream.Null;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pent = entry as PackedEntry;
            if (null == pent)
                return input;
            if (0x9C78 == (input.Signature & 0xFFFF))
            {
                pent.IsPacked = true;
                return new ZLibStream (input, CompressionMode.Decompress);
            }
            if (1 == input.Signature && arc.File.View.ReadByte (entry.Offset+4) == 0x78)
            {
                pent.IsPacked = true;
                input.Position = 4;
                return new ZLibStream (input, CompressionMode.Decompress);
            }
            return input;
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var sarc = arc as SeraphArchive;
            var aent = entry as ArchEntry;
            if (null == sarc || null == aent || null == sarc.EventMap
                || aent.DirIndex != sarc.EventDir || !sarc.EventMap.ContainsKey (aent.FileIndex))
                return OpenRawImage (arc, entry);
            var base_index = sarc.EventMap[aent.FileIndex];
            var base_entry = sarc.Dir.Cast<ArchEntry>()
                .FirstOrDefault (e => e.DirIndex == sarc.EventDir && e.FileIndex == base_index);
            if (null == base_entry)
                return base.OpenImage (arc, entry);
            var base_img = OpenCtImage (arc, base_entry);
            if (null == base_img)
                return base.OpenImage (arc, entry);
            var input = arc.OpenBinaryEntry (entry);
            return new CtOverlayDecoder (input, base_img);
        }

        IImageDecoder OpenRawImage (ArcFile arc, Entry entry)
        {
            var input = arc.OpenBinaryEntry (entry);
            try
            {
                uint width  = input.ReadUInt16();
                uint height = input.ReadUInt16();
                if (width > 0x4100 || 0 == width || 0 == height || width * height * 3 + 4 != input.Length)
                {
                    input.Position = 0;
                    return new ImageFormatDecoder (input);
                }
                else
                {
                    var info = new ImageMetaData { Width = width, Height = height, BPP = 24 };
                    return new RawBitmapDecoder (input, info);
                }
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        SeraphReader OpenCtImage (ArcFile arc, Entry entry)
        {
            using (var input = arc.OpenBinaryEntry (entry))
            {
                if (input.Signature != CtFormat.Value.Signature)
                    return null;
                var info = CtFormat.Value.ReadMetaData (input) as SeraphMetaData;
                if (null == info)
                    return null;
                var reader = new SeraphReader (input.AsStream, info);
                reader.UnpackCt();
                return reader;
            }
        }

        internal static string FormatEntryName (int dir_index, int file_index)
        {
            return string.Format ("{0}-{1:D5}.cts", dir_index, file_index);
        }

        internal static ResourceInstance<ImageFormat> CtFormat = new ResourceInstance<ImageFormat> ("CT");

        SeraphScheme m_scheme = new SeraphScheme { KnownSchemes = new Dictionary<string, ArchPacScheme>() };

        public override ResourceScheme Scheme
        {
            get { return m_scheme; }
            set { m_scheme = (SeraphScheme)value; }
        }

        public IDictionary<string, ArchPacScheme> KnownSchemes { get { return m_scheme.KnownSchemes; } }
    }

    internal class CtOverlayDecoder : BinaryImageDecoder
    {
        SeraphReader    m_baseline;

        public CtOverlayDecoder (IBinaryStream input, SeraphReader baseline)
            : base (input, baseline.Info)
        {
            m_baseline = baseline;
        }

        protected override ImageData GetImageData ()
        {
            var info = ArchPacOpener.CtFormat.Value.ReadMetaData (m_input) as SeraphMetaData;
            if (null == info)
                return ImageFormat.Read (m_input);
            var overlay = new SeraphReader (m_input.AsStream, info);
            overlay.UnpackCt();
            var dst = m_baseline.Data;
            var src = overlay.Data;
            if (dst.Length != src.Length)
                return ImageData.Create (overlay.Info, PixelFormats.Bgra32, null, src);
            for (int i = 0; i < src.Length; i += 4)
            {
                if (src[i+3] != 0)
                {
                    dst[i  ] = src[i  ];
                    dst[i+1] = src[i+1];
                    dst[i+2] = src[i+2];
                    dst[i+3] = src[i+3];
                }
                else
                {
                    dst[i+3] = 0xFF;
                }
            }
            return ImageData.Create (Info, PixelFormats.Bgra32, null, dst);
        }
    }

    internal class RawBitmapDecoder : BinaryImageDecoder
    {
        public RawBitmapDecoder (IBinaryStream input, ImageMetaData info) : base (input, info)
        {
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 4;
            int stride = (int)Info.Width * 3;
            var pixels = m_input.ReadBytes ((int)Info.Height * stride);
            return ImageData.Create (Info, PixelFormats.Bgr24, null, pixels, stride);
        }
    }
}
