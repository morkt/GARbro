//! \file       ArcDAT.cs
//! \date       Sat Feb 25 02:30:54 2017
//! \brief      Valkyria resource archive.
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
using System.Linq;
using GameRes.Utility;

namespace GameRes.Formats.Valkyria
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/VALKYRIA"; } }
        public override string Description { get { return "Valkyria resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_size = file.View.ReadUInt32 (0);
            if (0 == index_size || 1 == index_size)
                return TryOpenV1 (file);
            if (index_size >= file.MaxOffset)
                return null;
            int count = (int)index_size / 0x10C;
            if (index_size != (uint)count * 0x10Cu || !IsSaneCount (count))
                return null;
            uint index_offset = 4;
            long base_offset = index_offset + index_size;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x104);
                index_offset += 0x104;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = base_offset + file.View.ReadUInt32 (index_offset);
                entry.Size   = file.View.ReadUInt32 (index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                index_offset += 8;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        private ArcFile TryOpenV1 (ArcView file)
        {
            var index_encrypted = file.View.ReadUInt32 (0) == 1;
            var index_size = file.View.ReadUInt32 (4);
            if (0 == index_size || index_size >= file.MaxOffset)
                return null;
            int count = (int)index_size / 0x10C;
            if (index_size != (uint)count * 0x10Cu || !IsSaneCount (count))
                return null;
            var dir_path = Path.GetDirectoryName (file.Name);
            if (null == dir_path)
                return null;
            var arc_key = ReadArcKey (dir_path);
            if (null == arc_key)
                return null;
            uint index_offset = 0;
            long base_offset = 8 + index_size;
            var index = file.View.ReadBytes (8, index_size);
            if (index_encrypted)
            {
                var scheme = new EncryptionScheme
                {
                    OddKey1 = 0x627e907b,
                    OddKey2 = 7,
                    EvenKey1 = 0xe1da85e3,
                    EvenKey2 = 3
                };
                DecryptIndex (index, scheme, (uint)index.Length);
            }
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name_buffer = new byte[0x104];
                Buffer.BlockCopy (index, (int)index_offset, name_buffer, 0, 0x104);
                var name = Binary.GetCString (name_buffer, 0);
                index_offset += 0x104;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                var buffer = new byte[8];
                Buffer.BlockCopy (index, (int)index_offset, buffer, 0, 8);
                entry.Offset = LittleEndian.ToUInt32 (buffer, 0);
                entry.Size = LittleEndian.ToUInt32 (buffer, 4);
                if (!index_encrypted)
                {
                    var key = GetEntryKey (arc_key, name_buffer);
                    entry.Offset ^= key;
                    entry.Size ^= key;
                }
                entry.Offset += base_offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                index_offset += 8;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        private static byte[] ReadArcKey (string dir_path)
        {
            var file_path = Path.Combine (dir_path, "system.dat");
            if (!File.Exists (file_path))
                return null;
            var info = new byte[260];
            using (var fs = File.OpenRead (file_path))
            {
                fs.Position = 0x10E;
                fs.Read (info, 0, 260);
                fs.Close ();
            }
            var key = new byte[4];
            var len = info.TakeWhile (x => x != 0).Count ();
            for (int i = len, j = 0; i != 0; i--)
            {
                key[j] += info[i];
                if (++j == 4)
                    j = 0;
            }
            return key;
        }

        private static uint GetEntryKey (byte[] arc_key, byte[] name)
        {
            var key = new byte[4];
            var len = name.TakeWhile (x => x != 0).Count ();
            for (int i = len, j = 0; i != 0; i--)
            {
                key[j] += name[i];
                if (++j == 4)
                    j = 0;
            }
            key[0] += arc_key[3];
            key[1] += arc_key[2];
            key[2] += arc_key[1];
            key[3] += arc_key[0];
            return BitConverter.ToUInt32 (key, 0);
        }

        public static void DecryptIndex (byte[] index, EncryptionScheme scheme, uint length)
        {
            var branch = false;
            for (int i = 0; i <= index.Length - 4; i++)
            {
                unsafe
                {
                    fixed (byte* index_ptr = index)
                    {
                        uint* ptr = (uint*)(index_ptr + i);
                        var key1 = branch ? scheme.OddKey1 : scheme.EvenKey1;
                        var key2 = branch ? scheme.OddKey2 : scheme.EvenKey2;
                        *ptr = Binary.RotR (*ptr - key1, key2) ^ length;
                        branch = !branch;
                    }
                }
            }
        }
    }

    [Serializable]
    public class EncryptionScheme
    {
        public uint OddKey1;
        public int  OddKey2;
        public uint EvenKey1;
        public int  EvenKey2;
    }
}
