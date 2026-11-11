//! \file       ArcMZP.cs
//! \date       2026-02-03
//! \brief      HUNEX General Game Engine multi-frame image container.
//
// Copyright (C) 2026 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.HuneX {
    internal class MzpMetaData : ImageMetaData {
        public uint TileWidth { get; set; }
        public uint TileHeight { get; set; }
        public uint TileXCount { get; set; }
        public uint TileYCount { get; set; }
        public uint Characteristics { get; set; }
        public uint Depth { get; set; }
        public uint TileCrop { get; set; }
        public BitmapPalette Palette { get; set; }
    }

    internal class MzpArchive : ArcFile {
        public MzpMetaData MetaData { get; set; }

        public MzpArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, MzpMetaData metadata) : base (arc, impl, dir) {
            MetaData = metadata;
        }
    }

    internal class MzpEntry : Entry {
        public int Index { get; set; }
    }

    [Export(typeof(ArchiveFormat))]
    public class MrgOpener : ArchiveFormat {
        public override string         Tag { get { return "MZP"; } }
        public override string Description { get { return "HuneX general game engine multi-frame image"; } }
        public override uint     Signature { get { return 0x6467726d; } } // "mrgd"
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public MrgOpener() {
            Extensions = new string[] { "mzp" };
        }

        public override ArcFile TryOpen(ArcView file) {
            if (!file.View.AsciiEqual(4, "00"))
                return null;

            int count = file.View.ReadInt16(6);
            if (!IsSaneCount(count))
                return null;

            var base_name = Path.GetFileNameWithoutExtension(file.Name);
            MzpMetaData metadata = null;

            var dir = new List<Entry>(count);
            uint offset = 8;
            for (int i = 0; i < count; i++) {
                uint section_offset = file.View.ReadUInt16(offset);
                uint file_offset = file.View.ReadUInt16(offset + 2);
                uint size_boundary = file.View.ReadUInt16(offset + 4);
                uint size = file.View.ReadUInt16(offset + 6);
                var entry = new MzpEntry {
                    Offset = 8 * (count + 1) + section_offset * 0x800 + file_offset,
                    Size = (size_boundary - 1) / 0x20 * 0x800 * 0x20 + size
                };
                if (!entry.CheckPlacement(file.MaxOffset))
                    return null;
                if (i == 0)
                    metadata = ReadInfo(file, entry);
                else {
                    entry.Name = string.Format("{0}#{1:D3}", base_name, i);
                    entry.Type = "image";
                    entry.Index = i;
                    dir.Add(entry);
                }
                offset += 8;
            }

            if (metadata == null)
                return null;
            return new MzpArchive(file, this, dir, metadata);
        }

        MzpMetaData ReadInfo(ArcView file, Entry entry) {
            if (entry.Size < 16)
                return null;

            MzpMetaData metadata = new MzpMetaData();
            metadata.Width = file.View.ReadUInt16(entry.Offset);
            metadata.Height = file.View.ReadUInt16(entry.Offset + 2);
            metadata.TileWidth = file.View.ReadUInt16(entry.Offset + 4);
            metadata.TileHeight = file.View.ReadUInt16(entry.Offset + 6);
            metadata.TileXCount = file.View.ReadUInt16(entry.Offset + 8);
            metadata.TileYCount = file.View.ReadUInt16(entry.Offset + 10);
            ushort type = file.View.ReadUInt16(entry.Offset + 12);
            metadata.Characteristics = type;
            byte depth = file.View.ReadByte(entry.Offset + 14);
            metadata.Depth = depth;
            metadata.TileCrop = file.View.ReadByte(entry.Offset + 15);

            if (type == 1 && (depth & 0xf) == 0)
                metadata.BPP = 4;
            else if (type == 1 && (depth & 0xf) == 1)
                metadata.BPP = 8;
            else if (type == 8 && depth == 0x14)
                metadata.BPP = 24;
            else if ((type == 0xb && depth == 0x14) || (type == 0xc && depth == 0x11))
                metadata.BPP = 32;
            else
                throw new NotImplementedException("[MZP] Unsupported BPP type");

            if (type == 1) {
                int palette_size = (depth & 0xf) == 1 ? 256 : 16;
                var raw_palette = new byte[0x400];
                file.View.Read(entry.Offset + 16, raw_palette, 0, (uint)palette_size * 4);
                if (depth == 0x11 || depth == 0x91) {
                    for (int i = 0; i < palette_size; i += 32) {
                        var block = new byte[32];
                        Buffer.BlockCopy(raw_palette, (i + 8) * 4, block, 0, block.Length);
                        Buffer.BlockCopy(raw_palette, (i + 16) * 4, raw_palette, (i + 8) * 4, block.Length);
                        Buffer.BlockCopy(block, 0, raw_palette, (i + 16) * 4, block.Length);
                    }
                }
                for (int i = palette_size; i < 256; i++)
                    raw_palette[i * 4 + 3] = 0xFF;
                metadata.Palette = MzxImageReader.GetPaletteFromRaw(raw_palette);
            }
            else {
                metadata.Palette = null;
            }

            return metadata;
        }

        public override IImageDecoder OpenImage(ArcFile arc, Entry entry) {
            var marc = arc as MzpArchive;

            byte[] buffer;
            if (arc.File.View.AsciiEqual(entry.Offset, "MZX0")) {
                buffer = arc.File.View.ReadBytes(entry.Offset + 4, entry.Size - 4);
                var decoder = new MzxDecoder(buffer);
                buffer = decoder.Unpack();
            }
            else
                buffer = arc.File.View.ReadBytes(entry.Offset, entry.Size);

            return new MzxImageReader(new BinMemoryStream(buffer), marc.MetaData);
        }
    }

    internal class MzxImageReader : IImageDecoder {
        IBinaryStream m_input;
        byte[]        m_output;
        MzpMetaData   m_info;
        uint          m_height;
        uint          m_width;

        public byte[]           Data { get { return m_output; } }
        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }

        public Stream Source { get { m_input.Position = 0; return m_input.AsStream; } }

        public ImageFormat SourceFormat { get { return null; } }

        public ImageMetaData Info {
            get {
                return new ImageMetaData {
                    Height = m_height,
                    Width = m_width,
                    BPP = Format.BitsPerPixel
                };
            }
        }

        public ImageData Image {
            get {
                if (null == m_output)
                    Unpack();
                return ImageData.Create(Info, Format, Palette, Data);
            }
        }

        public MzxImageReader(IBinaryStream input, MzpMetaData info) {
            m_input = input;
            m_info = info;
            m_height = m_info.TileHeight;
            m_width = m_info.TileWidth;
            Palette = m_info.Palette;
            switch (m_info.BPP) {
                case 4: // Format = PixelFormats.Indexed4; break;
                case 8: Format = PixelFormats.Indexed8; break;
                case 24: Format = PixelFormats.Bgr24; break;
                case 32: Format = PixelFormats.Bgra32; break;
                default: throw new InvalidFormatException();
            }
        }

        public void Unpack() {
            if (m_info.Characteristics == 0xC) {
                UnpackHep();
                return;
            }
            uint tile_size = m_height * m_width;
            m_output = new byte[tile_size * (m_info.BPP + 4) / 8];

            uint index = 0;
            switch (m_info.BPP) {
                case 4:
                    byte[] temp4 = new byte[(tile_size + 1) / 2];
                    m_input.Read(temp4, 0, temp4.Length);
                    for (int i = 0; i < temp4.Length; i++) {
                        m_output[index++] = (byte)(temp4[i] & 0x0F);
                        if (index < tile_size)
                            m_output[index++] = (byte)(temp4[i] >> 4);
                    }
                    break;

                case 8:
                    m_input.Read(m_output, 0, m_output.Length);
                    break;

                case 24:
                case 32:
                    byte[] rgb565 = new byte[tile_size * 2];
                    m_input.Read(rgb565, 0, rgb565.Length);
                    byte[] offsets = new byte[tile_size];
                    m_input.Read(offsets, 0, offsets.Length);
                    byte[] alphas = null;
                    if (m_info.BPP == 32) {
                        alphas = new byte[tile_size];
                        m_input.Read(alphas, 0, alphas.Length);
                    }
                    for (int i = 0; i < tile_size; i++) {
                        ushort pq = BitConverter.ToUInt16(rgb565, i * 2);
                        byte offset_byte = offsets[i];
                        byte r = (byte)(((pq & 0xF800) >> 8) | ((offset_byte >> 5) & 7));
                        byte g = (byte)(((pq & 0x07E0) >> 3) | ((offset_byte >> 3) & 3));
                        byte b = (byte)(((pq & 0x001F) << 3) | (offset_byte & 7));
                        m_output[index++] = b;
                        m_output[index++] = g;
                        m_output[index++] = r;
                        if (alphas != null)
                            m_output[index++] = alphas[i];
                    }
                    break;
            }
        }

        void UnpackHep() {
            if (m_input.ReadUInt32() != 0x00504548) // 'HEP\0'
                throw new InvalidFormatException();
            m_input.ReadBytes(0x10);
            m_width = m_input.ReadUInt32();
            m_height = m_input.ReadUInt32();
            m_input.ReadUInt32();
            Format = PixelFormats.Indexed8;
            m_output = new byte[m_height * m_width];
            m_input.Read(m_output, 0, m_output.Length);

            var raw_palette = new byte[0x400];
            m_input.Read(raw_palette, 0, raw_palette.Length);
            Palette = GetPaletteFromRaw(raw_palette);
        }

        public static BitmapPalette GetPaletteFromRaw(byte[] raw_palette) {
            var colors = new Color[raw_palette.Length / 4];
            for (int i = 0; i < raw_palette.Length; i += 4) {
                byte r = raw_palette[i];
                byte g = raw_palette[i + 1];
                byte b = raw_palette[i + 2];
                byte a = raw_palette[i + 3];

                if ((a & 0x80) == 0)
                    a = (byte)(((a << 1) | (a >> 6)) & 0xFF);
                else
                    a = 0xFF;

                colors[i / 4] = Color.FromArgb(a, r, g, b);
            }
            return new BitmapPalette(colors);
        }

        #region IDisposable Members
        bool m_disposed = false;
        public void Dispose() {
            if (!m_disposed) {
                m_input.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }
}
