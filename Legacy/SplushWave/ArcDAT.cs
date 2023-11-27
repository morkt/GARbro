//! \file       ArcDAT.cs
//! \date       2023 Aug 14
//! \brief      Splush Wave resource archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [030817][Splush Wave] Knock Out -Taisengata Datsui Mahjong-

namespace GameRes.Formats.SplushWave
{
    internal class FlkEntry : Entry
    {
        public byte Flags;
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/FLK"; } }
        public override string Description { get { return "Splush Wave resource archive"; } }
        public override uint     Signature { get { return 0x4B4C46; } } // 'FLK'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint arc_size = file.View.ReadUInt32 (0x14);
            if (arc_size != file.MaxOffset)
                return null;
            int count = file.View.ReadInt32 (0x18);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0x20;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new FlkEntry {
                    Name = string.Format ("{0}#{1:D4}", base_name, i),
                    Offset = file.View.ReadUInt32 (index_offset),
                    Size = file.View.ReadUInt32 (index_offset+4),
                    Flags = file.View.ReadByte (index_offset+0xF),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x10;
            }
            foreach (var entry in dir)
            {
                var type = file.View.ReadUInt32 (entry.Offset);
                if (0x475753 == type || 0x475753 == (type >> 8))
                    entry.Type = "image";
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var fent = (FlkEntry)entry;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if ((fent.Flags & 1) != 0)
            {
                using (input)
                {
                    return LzssUnpack (input);
                }
            }
            return input;
        }

        internal Stream LzssUnpack (IBinaryStream input)
        {
            var frame = new byte[0x400];
            var output = new byte[0x200000];
            int dst = 0;
            int frame_pos = 0x3BE;
            int ctl = 0;
            while (input.PeekByte() != -1)
            {
                ctl >>= 1;
                if (0 == (ctl & 0x100))
                {
                    ctl = input.ReadByte() | 0xFF00;
                }
                if (0 == (ctl & 1))
                {
                    int next = input.ReadByte();
                    if (-1 == next)
                        break;
                    output[dst++] = frame[frame_pos++ & 0x3FF] = (byte)next;
                }
                else
                {
                    int lo = input.ReadByte();
                    int hi = input.ReadByte();
                    if (lo == -1 || hi == -1)
                        break;
                    int offset = lo + ((hi & 0xC0) << 2);
                    int count = (hi & 0x3F) + 3;
                    while (count --> 0)
                    {
                        output[dst++] = frame[frame_pos++ & 0x3FF] = frame[offset++ & 0x3FF];
                    }
                }
            }
            return new BinMemoryStream (output, 0, dst);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var fent = (FlkEntry)entry;
            var input = BinaryStream.FromStream (OpenEntry (arc, fent), fent.Name);
            if ((fent.Flags & 0x10) == 0)
                return ImageFormatDecoder.Create (input);
            try
            {
                var info = Swg.ReadMetaData (input) as SwgMetaData;
                if (null == info)
                {
                    input.Position = 0;
                    return new ImageFormatDecoder(input);
                }
                return new Swg1ImageDecoder (input, info);
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        static readonly ResourceInstance<SwgFormat> s_swg = new ResourceInstance<SwgFormat> ("SWG");

        internal static SwgFormat Swg { get => s_swg.Value; }
    }

    internal sealed class Swg1ImageDecoder : BinaryImageDecoder
    {
        SwgMetaData     m_info;

        public Swg1ImageDecoder (IBinaryStream input, SwgMetaData info) : base (input, info)
        {
            SourceFormat = DatOpener.Swg;
            m_info = info;
        }

        static readonly byte[] PlaneMap = { 3, 2, 1, 0 };

        protected override ImageData GetImageData ()
        {
            m_input.Position = m_info.DataOffset;
            int stride = 4 * m_info.iWidth;
            int plane_size = m_info.iWidth * m_info.iHeight;
            var output = new byte[stride * m_info.iHeight];
            ushort[] ctl_buf = new ushort[m_info.iHeight];
            for (int c = 0; c < 4; ++c)
            {
                int compress_method = ReadU16BE();
                if (0 == compress_method)
                {
                    int dst = PlaneMap[c] + stride * (m_info.iHeight - 1);
                    for (int y = 0; y < m_info.iHeight; ++y)
                    {
                        for (int x = 0; x < stride; x += 4)
                        {
                            output[dst+x] = m_input.ReadUInt8();
                        }
                        dst -= stride;
                    }
                    continue;
                }
                if (compress_method != 1)
                    throw new InvalidFormatException();
                for (int y = 0; y < m_info.iHeight; ++y)
                {
                    ctl_buf[y] = ReadU16BE();
                }
                int row = PlaneMap[c];
                for (int y = 0; y < m_info.iHeight; ++y)
                {
                    int dst = row;
                    int row_size = ctl_buf[y];
                    SwgFormat.DecompressRow (m_input, row_size, output, dst, 4);
                    row += stride;
                }
            }
            return ImageData.Create (m_info, PixelFormats.Bgra32, null, output, stride);
        }

        ushort ReadU16BE ()
        {
            ushort le = m_input.ReadUInt16();
            return (ushort)(le >> 8 | le << 8);
        }
    }
}
