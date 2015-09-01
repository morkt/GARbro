//! \file       ArcEAGLS.cs
//! \date       Fri May 15 02:52:04 2015
//! \brief      EAGLS system resource archives.
//
// Copyright (C) 2015 by morkt
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

namespace GameRes.Formats.Eagls
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/EAGLS"; } }
        public override string Description { get { return "EAGLS engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public PakOpener ()
        {
            Extensions = new string[] { "pak" };
        }

        static readonly string IndexKey = "1qaz2wsx3edc4rfv5tgb6yhn7ujm8ik,9ol.0p;/-@:^[]";
        static readonly string Key = "EAGLS_SYSTEM";

        public override ArcFile TryOpen (ArcView file)
        {
            string idx_name = Path.ChangeExtension (file.Name, ".idx");
            if (!VFS.FileExists (idx_name))
                return null;
            var idx_entry = VFS.FindFile (idx_name);
            if (idx_entry.Size > 0xfffff || idx_entry.Size < 10000)
                return null;

            byte[] index;
            using (var idx = VFS.OpenView (idx_entry))
                index = DecryptIndex (idx);
            int index_offset = 0;
            int entry_size = index.Length / 10000;
            bool long_offsets = 40 == entry_size;
            int name_size = long_offsets ? 0x18 : 0x14;
            long first_offset = LittleEndian.ToUInt32 (index, name_size);
            var dir = new List<Entry>();
            while (index_offset < index.Length)
            {
                if (0 == index[index_offset])
                    break;
                var name = Binary.GetCString (index, index_offset, name_size);
                index_offset += name_size;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                if (name.EndsWith (".dat", StringComparison.InvariantCultureIgnoreCase))
                    entry.Type = "script";
                if (long_offsets)
                {
                    entry.Offset = LittleEndian.ToInt64 (index, index_offset) - first_offset;
                    entry.Size   = LittleEndian.ToUInt32 (index, index_offset+8);
                    index_offset += 0x10;
                }
                else
                {
                    entry.Offset = LittleEndian.ToUInt32 (index, index_offset) - first_offset;
                    entry.Size   = LittleEndian.ToUInt32 (index, index_offset+4);
                    index_offset += 8;
                }
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Name.EndsWith (".gr", StringComparison.InvariantCultureIgnoreCase))
                return DecryptGr (arc, entry);
            if (entry.Name.EndsWith (".dat", StringComparison.InvariantCultureIgnoreCase))
                return DecryptDat (arc, entry);
            return arc.File.CreateStream (entry.Offset, entry.Size);
        }

        Stream DecryptGr (ArcFile arc, Entry entry)
        {
            var input = new byte[entry.Size];
            arc.File.View.Read (entry.Offset, input, 0, entry.Size);
            int seed = 0x75bd924 ^ input[input.Length-1];
            int limit = Math.Min (input.Length-1, 0x174b);
            for (int i = 0; i < limit; ++i)
            {
                seed = LRand (seed);
                int index = (int)(seed * 4.656612875245797e-10 * 256);
                input[i] ^= (byte)Key[index % Key.Length];
            }
            return new MemoryStream (input);
        }

        Stream DecryptDat (ArcFile arc, Entry entry)
        {
            byte[] input = new byte[entry.Size];
            arc.File.View.Read (entry.Offset, input, 0, entry.Size);
            int text_offset = 3600;
            int text_length = (int)(entry.Size - text_offset - 2);
            int seed = (sbyte)input[input.Length-1];
            for (int i = 0; i < text_length; i += 2)
            {
                seed = seed * 0x343FD + 0x269EC3;
                int index = (int)(((uint)seed >> 16) & 0x7fff);
                input[text_offset + i] ^= (byte)Key[index % Key.Length];
            }
            return new MemoryStream (input);
        }

        byte[] DecryptIndex (ArcView idx)
        {
            int idx_size = (int)idx.MaxOffset-4;
            byte[] output = new byte[idx_size];
            using (var view = idx.CreateViewAccessor (0, (uint)idx.MaxOffset))
            unsafe
            {
                uint seed = view.ReadUInt32 (idx_size);
                byte* ptr = view.GetPointer (0);
                try
                {
                    for (int i = 0; i < idx_size; ++i)
                    {
                        seed = seed * 0x343FD + 0x269EC3;
                        int index = (int)(seed >> 16) & 0x7FFF;
                        output[i] = (byte)(ptr[i] ^ IndexKey[index % IndexKey.Length]);
                    }
                    return output;
                }
                finally
                {
                    view.SafeMemoryMappedViewHandle.ReleasePointer();
                }
            }
        }

        int LRand (int seed)
        {
            const int A = 48271;
            const int Q = 44488;
            const int R = 3399;
            const int M = 2147483647;
            seed = A * (seed % Q) - R * (seed / Q);
            if (seed < 0)
                seed += M;
            return seed;
        }
    }
}
