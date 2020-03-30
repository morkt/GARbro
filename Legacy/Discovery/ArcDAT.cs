//! \file       ArcDAT.cs
//! \date       2018 Feb 06
//! \brief      Discovery resource archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;

// [000225][Discovery] Tsukiyo no Hitomi wa Kurenai ni
// [001102][Discovery] Twins Rhapsody

namespace GameRes.Formats.Discovery
{
    internal class BDataEntry : PackedEntry
    {
        public uint     Width;
        public uint     Height;
        public int      BPP;
        public int      Colors;
        public int      Extra;
    }

    internal class EDataEntry : PackedEntry
    {
        public uint     HeaderSize;
        public uint     HeaderUnpacked;
        public uint     BodyOffset;
        public uint     BodySize;
        public uint     BodyUnpacked;
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/DISCOVERY"; } }
        public override string Description { get { return "Discovery resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".dat"))
                return null;
            int count = file.View.ReadInt32 (file.MaxOffset-4);
            if (!IsSaneCount (count))
                return null;
            var arc_name = Path.GetFileName (file.Name);
            List<Entry> dir = null;
            if (arc_name.StartsWith ("BData", StringComparison.OrdinalIgnoreCase))
                dir = ReadBDataIndex (file, count);
            else if (arc_name.StartsWith ("EData", StringComparison.OrdinalIgnoreCase))
                dir = ReadEDataIndex (file, count);
            else if (arc_name.StartsWith ("VData", StringComparison.OrdinalIgnoreCase))
                dir = ReadVDataIndex (file, count);
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }

        List<Entry> ReadBDataIndex (ArcView file, int count)
        {
            int entry_size = 0x3C;
            int index_size = count * entry_size;
            if (index_size+4 >= file.MaxOffset)
                return null;
            var index = file.View.ReadBytes (file.MaxOffset - 4 - index_size, (uint)index_size);
            int index_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                Decrypt (index, index_offset, entry_size);
                int name_length = index[index_offset];
                if (0 == name_length || name_length > entry_size - 0x18)
                    return null;
                var name = Encodings.cp932.GetString (index, index_offset+0x18, name_length);
                var entry = FormatCatalog.Instance.Create<BDataEntry> (name);
                entry.Size         = index.ToUInt32 (index_offset+4);
                entry.UnpackedSize = index.ToUInt32 (index_offset+8);
                entry.Offset       = index.ToUInt32 (index_offset+0xC);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.Width  = index.ToUInt32 (index_offset+0x10);
                entry.Height = index.ToUInt32 (index_offset+0x14);
                entry.BPP    = index[index_offset+1];
                entry.Extra  = index.ToUInt16 (index_offset+0x28);
                entry.Colors = index.ToUInt16 (index_offset+0x2A);
                dir.Add (entry);
                index_offset += entry_size;
            }
            return dir;
        }

        List<Entry> ReadEDataIndex (ArcView file, int count)
        {
            int entry_size = 0x2C;
            int index_size = count * entry_size;
            if (index_size+4 >= file.MaxOffset)
                return null;
            var index = file.View.ReadBytes (file.MaxOffset - 4 - index_size, (uint)index_size);
            int index_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                Decrypt (index, index_offset, entry_size);
                int name_length = index[index_offset];
                if (0 == name_length || name_length > entry_size - 0x1C)
                    return null;
                var name = Encodings.cp932.GetString (index, index_offset+0x1C, name_length);
                var entry = FormatCatalog.Instance.Create<EDataEntry> (name);
                entry.BodySize       = index.ToUInt32 (index_offset+4);
                entry.BodyUnpacked   = index.ToUInt32 (index_offset+8);
                entry.BodyOffset     = index.ToUInt32 (index_offset+0xC);
                entry.HeaderSize     = index.ToUInt32 (index_offset+0x10);
                entry.HeaderUnpacked = index.ToUInt32 (index_offset+0x14);
                entry.Offset         = index.ToUInt32 (index_offset+0x18);
                entry.Size = entry.HeaderSize + entry.BodySize;
                entry.UnpackedSize = entry.HeaderUnpacked + entry.BodyUnpacked;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.IsPacked = true;
                dir.Add (entry);
                index_offset += entry_size;
            }
            return dir;
        }

        List<Entry> ReadVDataIndex (ArcView file, int count)
        {
            int entry_size = 0x20;
            int index_size = count * entry_size;
            if (index_size+4 >= file.MaxOffset)
                return null;
            var index = file.View.ReadBytes (file.MaxOffset - 4 - index_size, (uint)index_size);
            for (int i = 0; i < index.Length; ++i)
                index[i] ^= 0xDE;
            int index_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int name_length = index[index_offset];
                if (0 == name_length || name_length > entry_size - 0x10)
                    return null;
                var name = Encodings.cp932.GetString (index, index_offset+0x10, name_length);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Size   = index.ToUInt32 (index_offset+8);
                entry.Offset = index.ToUInt32 (index_offset+0xC);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += entry_size;
            }
            return dir;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var eent = entry as EDataEntry;
            if (null == eent || !eent.IsPacked)
                return base.OpenEntry (arc, entry);
            var header = new byte[eent.HeaderUnpacked];
            using (var input = arc.File.CreateStream (eent.Offset, eent.HeaderSize))
            using (var lzss = new LzssStream (input))
                lzss.Read (header, 0, header.Length);
            Stream body = arc.File.CreateStream (eent.BodyOffset, eent.BodySize);
            body = new LzssStream (body);
            return new PrefixStream (header, body);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var bent = entry as BDataEntry;
            if (null == bent)
                return base.OpenImage (arc, entry);
            return new BDataDecoder (arc, bent);
        }

        void Decrypt (byte[] data, int pos, int length)
        {
            Descramble32 (data, pos, length, 13);
            Descramble8 (data, pos, length, 7);
            for (int i = 0; i < length; ++i)
                data[pos+i] ^= 0xD6;
        }

        unsafe void Descramble32 (byte[] data, int pos, int length, int seed)
        {
            length /= 4;
            fixed (byte* data8 = &data[pos])
            {
                uint* data32 = (uint*)data8;
                for (int i = 0; i < length; ++i)
                {
                    int s = 0;
                    uint x = ~(data32[i] & 1) & data32[i];
                    int shift = 0;
                    for (int j = 0; j < 31; ++j)
                    {
                        shift = s - seed;
                        if (shift < 0)
                            shift += ((31 - shift) >> 5) << 5;
                        uint bit = x & (1u << shift);
                        uint a = ~bit & x;
                        uint b;
                        if (shift <= s)
                            b = bit << (s - shift);
                        else
                            b = bit >> (shift - s);
                        x = b | a;
                        s = shift;
                    }
                    data32[i] = x | ((data32[i] & 1) << shift);
                }
            }
        }

        void Descramble8 (byte[] data, int pos, int length, int seed)
        {
            byte first = data[pos];
            int x = 0;
            int i = 0;
            for (int count = length - 1; count > 0; --count)
            {
                i = x - seed;
                while (i < 0)
                    i += length;
                data[pos+x] = data[pos+i];
                x = i;
            }
            data[pos+i] = first;
        }
    }

    internal sealed class BDataDecoder : IImageDecoder
    {
        IBinaryStream   m_input;
        byte[]          m_output;
        ImageData       m_image;
        int             m_colors;
        int             m_extra;

        public Stream            Source { get { m_input.Position = 0; return m_input.AsStream; } }
        public ImageFormat SourceFormat { get { return null; } }
        public ImageMetaData       Info { get; private set; }
        public PixelFormat       Format { get; private set; }
        public int               Stride { get; private set; }
        public BitmapPalette    Palette { get; private set; }

        public ImageData          Image { get { return m_image ?? (m_image = Unpack()); } }

        public BDataDecoder (ArcFile arc, BDataEntry entry)
        {
            Info = new ImageMetaData { Width = entry.Width, Height = entry.Height, BPP = entry.BPP };
            uint total_size = entry.Size;
            total_size += (uint)entry.Colors * 4;
            if (entry.Extra > 0)
                total_size += 10 * (uint)entry.Extra + 2;
            Stride = ((int)entry.Width * entry.BPP / 8 + 3) & ~3;
            if (8 == entry.BPP)
                Format = PixelFormats.Indexed8;
            else
                Format = PixelFormats.Bgr24;
            m_output = new byte[Stride * (int)entry.Height];
            m_colors = entry.Colors;
            m_extra = entry.Extra;
            m_input = arc.File.CreateStream (entry.Offset, total_size);
        }

        private ImageData Unpack ()
        {
            m_input.Position = 0;
            if (m_colors > 0)
                Palette = ImageFormat.ReadPalette (m_input.AsStream, m_colors);
            if (m_extra > 0)
                m_input.Seek (10 * m_extra + 2, SeekOrigin.Current);
            using (var lzss = new LzssStream (m_input.AsStream, LzssMode.Decompress, true))
                lzss.Read (m_output, 0, m_output.Length);
            return ImageData.CreateFlipped (Info, Format, Palette, m_output, Stride);
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
