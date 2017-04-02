//! \file       ArcUF.cs
//! \date       Sun Apr 02 09:01:51 2017
//! \brief      Atelier Kaguya resource archive.
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

namespace GameRes.Formats.Kaguya
{
    [Export(typeof(ArchiveFormat))]
    public class UfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/UF01"; } }
        public override string Description { get { return "Atelier Kaguya resource archive"; } }
        public override uint     Signature { get { return 0x31304655; } } // 'UF01'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        const int MaxFileNameLength = 0x100;

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_offset = file.View.ReadUInt32 (4);
            if (index_offset >= file.MaxOffset - 4)
                return null;
            using (var index = file.CreateStream (index_offset+4))
            {
                long data_offset = 8;
                var name_buffer = new byte[MaxFileNameLength];
                var dir = new List<Entry>();
                while (index.PeekByte() != -1)
                {
                    int name_length = index.ReadInt32();
                    if (name_length <= 0 || name_length > name_buffer.Length)
                        return null;
                    if (name_length != index.Read (name_buffer, 0, name_length))
                        return null;
                    var name = DecryptString (name_buffer, name_length);
                    name = name.TrimStart ('\\', '/');
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                    int flags = index.ReadInt16();
                    data_offset += 4 + name_length + 6;
                    entry.Offset = data_offset;
                    entry.Size   = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    entry.IsPacked = 1 == flags;
                    dir.Add (entry);
                    data_offset += entry.Size;
                    if (entry.IsPacked)
                        data_offset += 4;
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            if (0 == pent.UnpackedSize)
            {
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset);
                if (0 == pent.UnpackedSize)
                    return Stream.Null;
            }
            using (var input = arc.File.CreateStream (entry.Offset+4, entry.Size))
            {
                var output = new byte[pent.UnpackedSize];
                LzUnpack (input, output);
                return new BinMemoryStream (output);
            }
        }

        string DecryptString (byte[] name, int length)
        {
            for (int i = 0; i < length; ++i)
                name[i] ^= 0xFF;
            return Encodings.cp932.GetString (name, 0, length);
        }

        void LzUnpack (Stream input, byte[] output)
        {
            var frame = new byte[0x1000];
            int frame_pos = 1;
            int dst = 0;
            using (var bits = new MsbBitStream (input))
            {
                while (dst < output.Length)
                {
                    if (0 != bits.GetNextBit())
                    {
                        byte b = (byte)bits.GetBits (8);
                        output[dst++] = b;
                        frame[frame_pos++ & 0xFFF] = b;
                    }
                    else
                    {
                        int offset = bits.GetBits (12);
                        int count = bits.GetBits (4) + 2;
                        for (int i = 0; i < count; ++i)
                        {
                            byte b = frame[(offset + i) & 0xFFF];
                            output[dst++] = b;
                            frame[frame_pos++ & 0xFFF] = b;
                        }
                    }
                }
            }
        }
    }
}
