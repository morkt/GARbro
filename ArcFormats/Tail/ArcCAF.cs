//! \file       ArcCAF.cs
//! \date       2017 Nov 30
//! \brief      Tail resource archive.
//
// Copyright (C) 2017-2018 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Tail
{
    [Export(typeof(ArchiveFormat))]
    public class CafOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CAF"; } }
        public override string Description { get { return "Tail resource archive"; } }
        public override uint     Signature { get { return 0x30464143; } } // 'CAF0'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (0xC);
            uint index_size = file.View.ReadUInt32 (0x10);
            uint names_offset = file.View.ReadUInt32 (0x14);
            uint names_size = file.View.ReadUInt32 (0x18);
            var names = file.View.ReadBytes (names_offset, names_size);
            if (names.Length != names_size)
                return null;
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            var dir_map = new Dictionary<int, string>();
            long data_offset = names_offset + names_size;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int dir_name_offset = file.View.ReadInt32 (index_offset+4);
                int name_offset = file.View.ReadInt32 (index_offset+8);
                var name = Binary.GetCString (names, name_offset);
                if (dir_name_offset >= 0)
                {
                    string dir_name;
                    if (!dir_map.TryGetValue (dir_name_offset, out dir_name))
                    {
                        dir_name = Binary.GetCString (names, dir_name_offset);
                        dir_map[dir_name_offset] = dir_name;
                    }
                    name = Path.Combine (dir_name, name);
                }
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0xC) + data_offset;
                entry.Size   = file.View.ReadUInt32 (index_offset+0x10);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x14;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            bool is_pren = arc.File.View.AsciiEqual (entry.Offset, "PREN");
            bool is_cfp0 = !is_pren && arc.File.View.AsciiEqual (entry.Offset, "CFP0");
            if (!is_pren && !is_cfp0)
                return base.OpenEntry (arc, entry);
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
            {
                var data = is_pren ? UnpackPren (input) : UnpackCfp0 (input);
                return new BinMemoryStream (data, entry.Name);
            }
        }

        byte[] UnpackPren (IBinaryStream input)
        {
            input.Position = 8;
            int unpacked_size = input.ReadInt32();
            byte rle_code = input.ReadUInt8();
            input.Seek (3, SeekOrigin.Current);
            var output = new byte[unpacked_size];
            int dst = 0;
            while (dst < output.Length)
            {
                int v = input.ReadByte();
                if (-1 == v)
                    break;
                if (rle_code == v)
                {
                    byte count = input.ReadUInt8();
                    byte x = rle_code;
                    if (count > 2)
                        x = input.ReadUInt8();

                    while (count --> 0)
                        output[dst++] = x;
                }
                else
                {
                    output[dst++] = (byte)v;
                }
            }
            return output;
        }

        byte[] UnpackCfp0 (IBinaryStream input)
        {
            input.Position = 8;
            int unpacked_size = input.ReadInt32();
            var output = new byte[unpacked_size];
            int dst = 0;
            while (dst < output.Length)
            {
                int cmd = input.ReadByte();
                int count = 0;
                switch (cmd)
                {
                case 0:
                    count = input.ReadUInt8();
                    input.Read (output, dst, count);
                    break;
                case 1:
                    count = input.ReadInt32();
                    input.Read (output, dst, count);
                    break;
                case 2:
                    {
                        count = input.ReadUInt8();
                        byte v = input.ReadUInt8();
                        for (int i = 0; i < count; ++i)
                            output[dst+i] = v;
                        break;
                    }
                case 3:
                    {
                        count = input.ReadInt32();
                        byte v = input.ReadUInt8();
                        for (int i = 0; i < count; ++i)
                            output[dst+i] = v;
                        break;
                    }
                case 6:
                    int offset = input.ReadUInt16();
                    count = input.ReadUInt16();
                    Binary.CopyOverlapped (output, dst-offset, dst, count);
                    break;

                case 15:
                case -1:
                    return output;
                }
                dst += count;
            }
            return output;
        }
    }
}
