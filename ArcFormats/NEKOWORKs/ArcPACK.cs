//! \file       ArcPACK.cs
//! \date       2026 Feb 01
//! \brief      NEKO WORKs Unity PACK resource archive.
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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using CommunityToolkit.HighPerformance;
using GameRes.Utility.Serialization;

namespace GameRes.Formats.NEKOWORKs
{
    [Export(typeof(ArchiveFormat))]
    public class NekoWorksPackOpener : ArchiveFormat
    {
        public override string Tag { get { return "PACK/EXFS"; } }
        public override string Description { get { return "NEKO WORKs resource archive"; } }
        public override uint Signature { get { return 0x53465845; } } // 'EXFS'
        public override bool IsHierarchic { get { return true; } }
        public override bool CanWrite { get { return false; } }

        [StructLayout(LayoutKind.Explicit, Size = 0x50)]
        private struct EXFSHeader
        {
            [FieldOffset(0x00)]
            public uint Signature;
            [FieldOffset(0x04)]
            public uint ReaderVersion;
            [FieldOffset(0x08)]
            public uint WriterVersion;
            [FieldOffset(0x0C)]
            public uint FileCount;
            [FieldOffset(0x10)]
            public long HeaderSize;
            [FieldOffset(0x18)]
            public long EntryTableSize;
            [FieldOffset(0x20)]
            public long PathTableSize;
            [FieldOffset(0x28)]
            public long ResourceTableOffset;

            [FieldOffset(0x30)]
            public long Reserve1;
            [FieldOffset(0x38)]
            public long Reserve2;
            [FieldOffset(0x40)]
            public long Reserve3;
            [FieldOffset(0x48)]
            public long Reserve4;

            public unsafe static int Size()
            {
                return Unsafe.SizeOf<EXFSHeader>();
            }
        }

        [StructLayout(LayoutKind.Explicit, Size = 0x20)]
        private struct EXFSFileEntry
        {
            [FieldOffset(0x00)]
            public long FilePathOffset;
            [FieldOffset(0x08)]
            public long FilePathSize;
            [FieldOffset(0x10)]
            public long FileOffset;
            [FieldOffset(0x18)]
            public long FileSize;

            public unsafe static int Size()
            {
                return Unsafe.SizeOf<EXFSFileEntry>();
            }
        }

        public override ArcFile TryOpen(ArcView file)
        {
            if (file.MaxOffset < EXFSHeader.Size())
            {
                return null;
            }

            using (ArcViewStream stream = file.CreateStream())
            {
                stream.ReadStruct(out EXFSHeader hdr);
                if (hdr.Signature != 0x53465845u)
                {
                    return null;
                }
                if (hdr.ReaderVersion == 0u)
                {
                    return null;
                }

                stream.Position = hdr.HeaderSize;
                byte[] entryTableBytes = new byte[hdr.EntryTableSize];
                byte[] pathTableBytes = new byte[hdr.PathTableSize];
                if (stream.Read(entryTableBytes) != entryTableBytes.Length)
                {
                    return null;
                }
                if (stream.Read(pathTableBytes) != pathTableBytes.Length)
                {
                    return null;
                }

                ReadOnlySpan<EXFSFileEntry> exfsEntries = MemoryMarshal.Cast<byte, EXFSFileEntry>(entryTableBytes);

                List<Entry> dir = new List<Entry>((int)hdr.FileCount);
                for (uint i = 0; i < hdr.FileCount; ++i)
                {
                    EXFSFileEntry fe = exfsEntries[(int)i];
                    string fn = Encoding.UTF8.GetString(pathTableBytes, (int)fe.FilePathOffset, (int)fe.FilePathSize);

                    Entry entry = Create<Entry>(fn);
                    entry.Offset = hdr.ResourceTableOffset + fe.FileOffset;
                    entry.Size = (uint)fe.FileSize;

                    if (!entry.CheckPlacement(file.MaxOffset))
                    { 
                        return null;
                    }

                    dir.Add(entry);
                }
                return new ArcFile(file, this, dir);
            }
        }
    }
}
