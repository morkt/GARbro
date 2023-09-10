//! \file       ArcX.cs
//! \date       2023 Sep 02
//! \brief      Rare archive format.
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

// [990528][Rare] Seisen Ren'ai Yuugi

namespace GameRes.Formats.Rare
{
    [Export(typeof(ArchiveFormat))]
    public class XOpener : ArchiveFormat
    {
        public override string         Tag => "X/RARE";
        public override string Description => "Rare resource archive";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        static readonly Dictionary<string, Tuple<uint, int>> KnownExeMap = new Dictionary<string, Tuple<uint, int>> {
            { "seisen.exe", Tuple.Create (0x3A9A0u, 715) },
        };

        public override ArcFile TryOpen (ArcView file)
        {
            if (!VFS.IsPathEqualsToFileName (file.Name, "PP.X"))
                return null;
            string full_exe_name = null;
            Tuple<uint, int> index_pos = null;
            foreach (var exe_name in KnownExeMap.Keys)
            {
                full_exe_name = VFS.ChangeFileName (file.Name, exe_name);
                if (VFS.FileExists (full_exe_name))
                {
                    index_pos = KnownExeMap[exe_name];
                    break;
                }
            }
            if (null == index_pos)
                return null;
            uint index_offset = index_pos.Item1;
            int count = index_pos.Item2;
            using (var index = VFS.OpenView (full_exe_name))
            {
                index.View.Reserve (index_offset, (uint)count * 12u);
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var entry = new PackedEntry {
                        Name = string.Format ("PP#{0:D5}.BMP", i),
                        Type = "image",
                        Offset = index.View.ReadUInt32 (index_offset),
                        Size   = index.View.ReadUInt32 (index_offset+4),
                        UnpackedSize = index.View.ReadUInt32 (index_offset+8),
                        IsPacked = true,
                    };
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    index_offset += 12;
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = (PackedEntry)entry;
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
            {
                var output = new byte[pent.UnpackedSize];
                Decompress (input, output);
                return new BinMemoryStream (output, entry.Name);
            }
        }

        internal static void Decompress (IBinaryStream input, byte[] output)
        {
            var frame = new byte[0x400];
            int frame_pos = 1;
            int dst = 0;
            using (var bits = new MsbBitStream (input.AsStream, true))
            {
                while (dst < output.Length)
                {
                    int ctl = bits.GetNextBit();
                    if (-1 == ctl)
                        break;
                    if (ctl != 0)
                    {
                        int v = bits.GetBits (8);
                        output[dst++] = frame[frame_pos++ & 0x3FF] = (byte)v;
                    }
                    else
                    {
                        int offset = bits.GetBits (10);
                        int count = bits.GetBits (5) + 2;
                        while (count --> 0)
                        {
                            byte v = frame[offset++ & 0x3FF];
                            output[dst++] = frame[frame_pos++ & 0x3FF] = v;
                        }
                    }
                }
            }
        }
    }
}
