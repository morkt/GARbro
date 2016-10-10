//! \file       ArcGPC.cs
//! \date       Tue Mar 22 01:38:13 2016
//! \brief      Super Neko X engine resource archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using GameRes.Utility;

namespace GameRes.Formats.SuperNekoX
{
    [Export(typeof(ArchiveFormat))]
    public class GpcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GPC7"; } }
        public override string Description { get { return "Super NekoX engine resource archive"; } }
        public override uint     Signature { get { return 0x37637047; } } // 'Gpc7'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public GpcOpener ()
        {
            Extensions = new string[] { "gpc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            int index_offset = 8;
            long data_offset = count * 4 + 8;
            uint next_offset = file.View.ReadUInt32 (index_offset);
            for (int i = 0; i < count; ++i)
            {
                index_offset += 4;
                var entry = new PackedEntry { Offset = next_offset };
                next_offset = i + 1 < count ? file.View.ReadUInt32 (index_offset) : (uint)file.MaxOffset;
                entry.Size = next_offset - (uint)entry.Offset;
                if (entry.Offset < data_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.Name = string.Format ("{0}#{1:D4}", base_name, i);
                dir.Add (entry);
            }
            DetectFileTypes (file, dir);
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (null != pent && pent.IsPacked)
            {
                Stream unpacked;
                using (input)
                {
                    var data = new byte[pent.UnpackedSize];
                    UnpackEntry (input, data);
                    unpacked = new MemoryStream (data);
                }
                input = unpacked;
            }
            if (input.Length > 4 && input.Length < 0x10000)
            {
                using (var reader = new ArcView.Reader (input))
                {
                    int unpacked_size = reader.ReadUInt16();
                    int packed_size = reader.ReadUInt16();
                    if (packed_size == input.Length-4)
                    {
                        using (input)
                        {
                            var data = new byte[unpacked_size];
                            UnpackLz77 (input, data);
                            return new MemoryStream (data);
                        }
                    }
                    input.Position = 0;
                }
            }
            return input;
        }

        void DetectFileTypes (ArcView file, List<Entry> dir)
        {
            using (var input = file.CreateStream())
            using (var reader = new ArcView.Reader (input))
            {
                var buffer = new byte[0x10];
                foreach (PackedEntry entry in dir)
                {
                    input.Position = entry.Offset;
                    uint packed_size = reader.ReadUInt32();
                    entry.UnpackedSize = reader.ReadUInt32();
                    entry.Offset += 8;
                    if (0 == packed_size)
                    {
                        entry.Size = entry.UnpackedSize;
                    }
                    else
                    {
                        entry.IsPacked = true;
                        entry.Size = packed_size;
                    }
                    if (entry.Size < 0x10)
                        continue;
                    uint signature;
                    if (entry.IsPacked)
                    {
                        UnpackEntry (input, buffer);
                        signature = LittleEndian.ToUInt32 (buffer, 0);
                    }
                    else
                        signature = reader.ReadUInt32();
                    IResource res;
                    if (0x020000 == signature || 0x0A0000 == signature)
                        res = ImageFormat.Tga;
                    else
                        res = AutoEntry.DetectFileType (signature);
                    if (null != res)
                    {
                        entry.Type = res.Type;
                        var ext = res.Extensions.FirstOrDefault();
                        if (!string.IsNullOrEmpty (ext))
                            entry.Name = Path.ChangeExtension (entry.Name, ext);
                    }
                }
            }
        }

        void UnpackEntry (Stream input, byte[] output)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                int ctl = input.ReadByte();
                if (-1 == ctl)
                    break;
                int count, offset;
                if (ctl >= 0x20)
                {
                    if (ctl >= 0x80)
                    {
                        count = (ctl >> 5) & 3;
                        offset = (ctl & 0x1F) << 8;
                        offset |= input.ReadByte();
                    }
                    else if ((ctl & 0x60) == 0x20)
                    {
                        offset = (ctl >> 2) & 7;
                        count = ctl & 3;
                    }
                    else if ((ctl & 0x60) == 0x40)
                    {
                        offset = (ctl & 0x1F) << 8;
                        offset |= input.ReadByte();
                        count = input.ReadByte() + 4;
                    }
                    else
                    {
                        offset = (ctl & 0x1F) << 8 | input.ReadByte();
                        count  = input.ReadByte() << 24;
                        count |= input.ReadByte() << 16;
                        count |= input.ReadByte() << 8;
                        count |= input.ReadByte();
                    }
                    count = Math.Min (count + 3, output.Length-dst);
                    Binary.CopyOverlapped (output, dst-offset-1, dst, count);
                }
                else
                {
                    if (ctl < 0x1D)
                    {
                        count = ctl + 1;
                    }
                    else if (0x1D == ctl)
                    {
                        count = input.ReadByte() + 0x1E;
                    }
                    else if (0x1E == ctl)
                    {
                        count  = input.ReadByte() << 8;
                        count |= input.ReadByte();
                        count += 286;
                    }
                    else
                    {
                        count  = input.ReadByte() << 24;
                        count |= input.ReadByte() << 16;
                        count |= input.ReadByte() << 8;
                        count |= input.ReadByte();
                    }
                    count = Math.Min (count, output.Length-dst);
                    input.Read (output, dst, count);
                }
                dst += count;
            }
        }

        void UnpackLz77 (Stream input, byte[] output)
        {
            int dst = 0;
            int mask = 0;
            int bits = 0;
            while (dst < output.Length)
            {
                mask >>= 1;
                if (0 == mask)
                {
                    bits = input.ReadByte();
                    if (-1 == bits)
                        break;
                    mask = 0x80;
                }
                if (0 != (bits & mask))
                {
                    int count = input.ReadByte();
                    int offset = input.ReadByte() << 4 | count >> 4;
                    count = Math.Min ((count & 0xf) + 3, output.Length - dst);
                    Binary.CopyOverlapped (output, dst-offset-1, dst, count);
                    dst += count;
                }
                else
                {
                    output[dst++] = (byte)input.ReadByte();
                }
            }
        }
    }
}
