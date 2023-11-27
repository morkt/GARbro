//! \file       ArcARC.cs
//! \date       2023 Aug 26
//! \brief      Kogado resource archive.
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

using GameRes.Compression;
using GameRes.Formats.DirectDraw;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Windows.Media;

// [070302][G-mode] Keitai Shoujo

namespace GameRes.Formats.Kogado
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get => "ARC/KOGADO"; }
        public override string Description { get => "Kogado engine resource archive"; }
        public override uint     Signature { get => 0xA8BCADBE; } // 'ARCW' ^ 0xFFFFFFFF
        public override bool  IsHierarchic { get => true; }
        public override bool      CanWrite { get => false; }

        public override ArcFile TryOpen (ArcView file)
        {
            using (var reader = new ArcIndexReader (file))
            {
                var dir = reader.ReadIndex();
                return new ArcFile(file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            input = new XoredStream (input, 0xFF);
            var ova = entry as OvaEntry;
            if (ova != null)
                return new PrefixStream (ova.Header, input);
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return input;
            return new LimitStream (new LzssStream (input), pent.UnpackedSize);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var dds = entry as DdsEntry;
            if (null == dds)
                return base.OpenImage (arc, entry);
            var input = this.OpenEntry (arc, entry);
            return new ImageDecoder (input, dds.Info);
        }
    }

    internal class OvaEntry : PackedEntry
    {
        public byte[] Header;
    }

    internal class DdsEntry : PackedEntry
    {
        public DdsInfo Info;
    }

    internal class DdsInfo : ImageMetaData
    {
        public DdsPF    Flags;
    }

    internal sealed class ArcIndexReader : IDisposable
    {
        IBinaryStream   m_input;
        uint            m_base_offset;
        long            m_max_offset;

        public ArcIndexReader (ArcView arc)
        {
            m_base_offset = arc.View.ReadUInt32 (0xC);
            m_max_offset = arc.MaxOffset;
            m_input = arc.CreateStream();
        }

        byte[]  m_filenames;
        byte[]  m_index;
        List<Entry> m_dir;

        public List<Entry> ReadIndex ()
        {
            m_input.Position = 0x10;
            m_filenames = ReadChunk();
            m_index = ReadChunk();
            m_dir = new List<Entry>();
            int section_count = m_index.ToInt32 (0);
            int pos = 4;
            for (int i = 0; i < section_count; ++i)
            {
                int count = m_index.ToInt32 (pos+8);
                if (m_dir.Capacity < m_dir.Count + count)
                    m_dir.Capacity = m_dir.Count + count;
                int section_size = m_index.ToInt32 (pos+0xC);
                int name_pos = pos + 0x10;
                int layout_pos = name_pos + 4 * count;
                if (m_index.AsciiEqual (pos, "DDS\0"))
                    ReadDdsSection (name_pos, layout_pos, count);
                else if (m_index.AsciiEqual (pos, "OVA\0"))
                    ReadOvaSection (name_pos, layout_pos, count);
                else
                    ReadSection (name_pos, layout_pos, count);
                pos += 0x10 + section_size;
            }
            return m_dir;
        }

        void ReadSection (int name_pos, int layout_pos, int count)
        {
            for (int j = 0; j < count; ++j)
            {
                var name = ReadFileName (m_index.ToInt32 (name_pos));
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);

                entry.Offset = m_index.ToUInt32 (layout_pos) + m_base_offset;
                entry.Size = m_index.ToUInt32 (layout_pos+4);
                entry.UnpackedSize = m_index.ToUInt32 (layout_pos+0xC);
                entry.IsPacked = true;
                if (!entry.CheckPlacement (m_max_offset+0x14))
                    throw new InvalidFormatException();

                m_dir.Add (entry);
                name_pos += 4;
                layout_pos += 0x10;
            }
        }

        void ReadDdsSection (int name_pos, int layout_pos, int count)
        {
            int header_count = m_index.ToInt32 (layout_pos);
            var headers = new DdsInfo[header_count];
            layout_pos += 4;
            for (int i = 0; i < header_count; ++i)
            {
                headers[i] = new DdsInfo {
                    Width  = m_index.ToUInt32 (layout_pos+4),
                    Height = m_index.ToUInt32 (layout_pos+8),
                    BPP    = 32,
                    Flags  = (DdsPF)m_index.ToUInt32 (layout_pos),
                };
                layout_pos += 0xC;
            }
            for (int j = 0; j < count; ++j)
            {
                var name = ReadFileName (m_index.ToInt32 (name_pos));
                var entry = FormatCatalog.Instance.Create<DdsEntry> (name);

                entry.Offset = m_index.ToUInt32 (layout_pos) + m_base_offset;
                entry.Size = m_index.ToUInt32 (layout_pos+4);
                entry.UnpackedSize = m_index.ToUInt32 (layout_pos+0xC);
                int header_id = m_index.ToInt32 (layout_pos+0x10);
                entry.Info = headers[header_id];
                entry.IsPacked = true;
                if (!entry.CheckPlacement (m_max_offset+0x14))
                    throw new InvalidFormatException();

                m_dir.Add (entry);
                name_pos += 4;
                layout_pos += 0x14;
            }
        }

        void ReadOvaSection (int name_pos, int layout_pos, int count)
        {
            int header_count = m_index.ToInt32 (layout_pos+4);
            var headers = new byte[header_count][];
            layout_pos += 8;
            for (int i = 0; i < header_count; ++i)
            {
                int header_len = m_index.ToInt32 (layout_pos+8);
                int header_pos = layout_pos + 12;
                headers[i] = new CowArray<byte> (m_index, header_pos, header_len).ToArray();
                layout_pos = header_pos + header_len;
            }
            for (int j = 0; j < count; ++j)
            {
                var name = ReadFileName (m_index.ToInt32 (name_pos));
                var entry = FormatCatalog.Instance.Create<OvaEntry> (name);

                entry.Offset = m_index.ToUInt32 (layout_pos) + m_base_offset;
                entry.UnpackedSize = m_index.ToUInt32 (layout_pos+4);
                int header_id = m_index.ToInt32 (layout_pos+8);
                entry.Header = headers[header_id];
                entry.Size = entry.UnpackedSize - (uint)entry.Header.Length;
                entry.IsPacked = true;
                if (!entry.CheckPlacement (m_max_offset))
                    throw new InvalidFormatException();

                m_dir.Add (entry);
                name_pos += 4;
                layout_pos += 0xC;
            }
        }

        string ReadFileName (int pos)
        {
            int i;
            for (i = pos; i+1 < m_filenames.Length; i += 2)
            {
                if (m_filenames[i] == 0 && m_filenames[i+1] == 0)
                    break;
            }
            return Encoding.Unicode.GetString (m_filenames, pos, i - pos);
        }

        byte[] ReadChunk ()
        {
            long start_pos = m_input.Position;
            int size = m_input.ReadInt32();
            int type = m_input.ReadInt32();
            int unpacked_size = m_input.ReadInt32();
            if (size <= 0 || unpacked_size <= 0)
                throw new InvalidFormatException();
            var data = new byte[unpacked_size];
            using (var decrypted = new XoredStream (m_input.AsStream, 0xFF, true))
            using (var lzss = new LzssStream (decrypted))
            {
                lzss.Read (data, 0, data.Length);
            }
            m_input.Position = start_pos + size;
            return data;
        }

        #region IDisposable Members
        bool _disposed = false;

        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize (this);
        }
        #endregion
    }

    internal sealed class ImageDecoder : IImageDecoder
    {
        Stream          m_input;
        ImageData       m_image;

        public Stream            Source => m_input;
        public ImageFormat SourceFormat => null;
        public PixelFormat       Format { get; private set; }
        public ImageMetaData       Info { get; private set; }
        public ImageData          Image => m_image ?? (m_image = GetImageData());

        public ImageDecoder (Stream input, DdsInfo info)
        {
            m_input = input;
            Info = info;
            Format = info.Flags.HasFlag (DdsPF.AlphaPixels) ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
        }

        private ImageData GetImageData ()
        {
            var pixels = new byte[Info.iWidth * Info.iHeight * 4];
            m_input.Read (pixels, 0, pixels.Length);
            return ImageData.Create (Info, Format, null, pixels);
        }

        #region IDisposable members
        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
            GC.SuppressFinalize (this);
        }
        #endregion
    }
}
