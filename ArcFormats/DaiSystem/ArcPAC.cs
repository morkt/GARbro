//! \file       ArcPAC.cs
//! \date       2018 Jan 11
//! \brief      DAI system resource archive.
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
using System.Diagnostics;
using System.IO;
using System.Linq;
using GameRes.Utility;

namespace GameRes.Formats.DaiSystem
{
    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/DAI"; } }
        public override string Description { get { return "DAI system resource archive"; } }
        public override uint     Signature { get { return 0x5F494144; } } // 'DAI_SYSTEM_01000'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "SYSTEM_01000"))
                return null;
            int count = Binary.BigEndian (file.View.ReadUInt16 (0x10));
            if (!IsSaneCount (count))
                return null;
            uint index_size = Binary.BigEndian (file.View.ReadUInt32 (0x12));
            var index = file.View.ReadBytes (0x16, index_size);
            for (int i = 0; i < index.Length; ++i)
                index[i] -= (byte)(i + 0x28);

            int index_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int name_end = Array.IndexOf<byte> (index, (byte)',', index_offset);
                if (-1 == name_end)
                    return null;
                var name = Encodings.cp932.GetString (index, index_offset, name_end - index_offset);
                index_offset = name_end + 1;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = BigEndian.ToUInt32 (index, index_offset);
                index_offset += 5;
                dir.Add (entry);
            }
            for (int i = 1; i < dir.Count; ++i)
            {
                var entry = dir[i-1];
                entry.Size = (uint)(dir[i].Offset - entry.Offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
            }
            dir[dir.Count-1].Size = (uint)(file.MaxOffset - dir[dir.Count-1].Offset);
            DetectFileTypes (file, dir);
            return new ArcFile (file, this, dir);
        }

        void DetectFileTypes (ArcView file, List<Entry> dir)
        {
            foreach (var entry in dir.Where (e => string.IsNullOrEmpty (e.Type)))
            {
                uint signature = file.View.ReadUInt32 (entry.Offset);
                if (0x05304148 == signature)
                {
                    entry.Type = "image";
                    continue;
                }
                else if (0x304148 == (signature & 0xFFFFFF))
                {
                    uint encryption = file.View.ReadUInt32 (entry.Offset+8);
                    long offset = entry.Offset + 0x10 + (signature >> 24);
                    if (0 != encryption)
                    {
                        if (4 == encryption)
                        {
                            uint bits = Binary.BigEndian (file.View.ReadUInt32 (offset + 8));
                            if (bits > entry.Size)
                                continue;
                            if (file.View.AsciiEqual (offset+12+bits, "BM"))
                            {
                                entry.ChangeType (ImageFormat.Bmp);
                            }
                            else if (file.View.AsciiEqual (entry.Offset+entry.Size-0x12, "TRUEVISION"))
                            {
                                entry.ChangeType (ImageFormat.Tga);
                            }
                        }
                        continue;
                    }
                    signature = file.View.ReadUInt32 (offset);
                }
                entry.ChangeType (AutoEntry.DetectFileType (signature));
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!arc.File.View.AsciiEqual (entry.Offset, "HA0"))
                return base.OpenEntry (arc, entry);
            byte header_length = arc.File.View.ReadByte (entry.Offset+3);
            uint ha0_header_length = 0x10u + header_length;
            if (ha0_header_length >= entry.Size)
                return base.OpenEntry (arc, entry);
            uint unpacked_size = Binary.BigEndian (arc.File.View.ReadUInt32 (entry.Offset+4));
            uint pattern = arc.File.View.ReadUInt32 (entry.Offset+8);
            var header = arc.File.View.ReadBytes (entry.Offset+0x10, header_length);
            var input = arc.File.View.ReadBytes (entry.Offset + ha0_header_length, entry.Size - ha0_header_length);
            while (pattern != 0)
            {
                uint code = pattern >> 24;
                switch (code)
                {
                case 0: break;
                case 2: input = Decrypt2 (input); break;
                case 3: input = Decrypt3 (input); break;
                case 4: input = Unpack4 (input); break;
                default:
                    Trace.WriteLine (string.Format ("Unknown encryption method ({0})", code), "[DAI_SYSTEM]");
                    return base.OpenEntry (arc, entry);
                }
                pattern <<= 8;
            }
            return new BinMemoryStream (input, entry.Name);
        }

        byte[] Decrypt2 (byte[] input)
        {
            var output = new byte[input.Length];
            int src = 0;
            for (int i = 0; i < 3; ++i)
            {
                for (int dst = i; dst < output.Length; dst += 3)
                    output[dst] = input[src++];
            }
            return output;
        }

        byte[] Decrypt3 (byte[] input)
        {
            for (int i = 1; i < input.Length; ++i)
            {
                input[i] += input[i-1];
            }
            return input;
        }

        byte[] Unpack4 (byte[] input)
        {
            int unpacked_size = BigEndian.ToInt32 (input, 0);
            var output = new byte[unpacked_size];
            int ctl_bits = BigEndian.ToInt32 (input, 4);
            int ctl_bytes = BigEndian.ToInt32 (input, 8);
            int ctl = 12;
            int src = 12 + ctl_bytes;
            int dst = 0;
            int bits = 2;
            while (dst < output.Length)
            {
                bits >>= 1;
                if (1 == bits)
                {
                    bits = input[ctl++] | 0x100;
                }
                if (0 == (bits & 1))
                {
                    output[dst++] = input[src++];
                }
                else
                {
                    int offset = input[src++];
                    int count  = input[src++];
                    Binary.CopyOverlapped (output, dst-offset, dst, count);
                    dst += count;
                }
            }
            return output;
        }
    }
}
