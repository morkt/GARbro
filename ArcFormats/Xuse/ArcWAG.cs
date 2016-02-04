//! \file       ArcWAG.cs
//! \date       Tue Aug 11 08:28:28 2015
//! \brief      Xuse/Eternal resource archive.
//
// Copyright (C) 2015-2016 by morkt
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
using System.Text.RegularExpressions;
using GameRes.Utility;

namespace GameRes.Formats.Xuse
{
    internal class WagArchive : ArcFile
    {
        public readonly byte[] Key;

        public WagArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class WagOpener : ArchiveFormat
    {
        public override string         Tag { get { return "WAG"; } }
        public override string Description { get { return "Xuse/Eternal resource archive"; } }
        public override uint     Signature { get { return 0x40474157; } } // 'WAG@'
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public WagOpener ()
        {
            Extensions = new string[] { "wag", "4ag" };
            Signatures = new uint[] { 0x40474157, 0x34464147 }; // 'GAF4'
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (0x0300 != file.View.ReadUInt16 (4))
                return null;
            int count = file.View.ReadInt32 (0x46);
            if (!IsSaneCount (count))
                return null;

            byte[] title = file.View.ReadBytes (6, 0x40);
            if (0x40 != title.Length)
                return null;
            int title_length = Array.IndexOf<byte> (title, 0);
            if (-1 == title_length)
                title_length = title.Length;
            string arc_filename = Path.GetFileName (file.Name).ToLowerInvariant();
            byte[] bin_filename = Encodings.cp932.GetBytes (arc_filename);

            byte[] name_key = GenerateKey (bin_filename);

            uint key_sum = (uint)name_key.Select (x => (int)x).Sum();
            uint index_offset = 0x200 + key_sum;
            for (int i = 0; i < name_key.Length; ++i)
            {
                index_offset ^= name_key[i];
                index_offset = Binary.RotR (index_offset, 1);
            }
            for (int i = 0; i < name_key.Length; ++i)
            {
                index_offset ^= name_key[i];
                index_offset = Binary.RotR (index_offset, 1);
            }
            index_offset %= 0x401;

            index_offset += 0x4A;
            byte[] index = file.View.ReadBytes (index_offset, (uint)(4*count));
            if (index.Length != 4*count)
                return null;

            byte[] index_key = new byte[index.Length];
            for (int i = 0; i < index_key.Length; ++i)
            {
                int v = name_key[(i+1) % name_key.Length] ^ (name_key[i % name_key.Length] + (i & 0xFF));
                index_key[i] = (byte)(count + v);
            }
            Decrypt (index_offset, index_key, index);

            var dir = new List<Entry> (count);
            int current_offset = 0;
            uint next_offset = LittleEndian.ToUInt32 (index, current_offset);
            byte[] data_key = GenerateKey (title, title_length);
            string base_filename = Path.GetFileNameWithoutExtension (arc_filename);
            byte[] chunk_buf = new byte[8];
            byte[] filename_buf = new byte[0x40];
            for (int i = 0; i < count; ++i)
            {
                current_offset += 4;
                uint entry_offset = next_offset;
                if (entry_offset >= file.MaxOffset)
                    return null;
                if (i + 1 == count)
                    next_offset = (uint)file.MaxOffset;
                else
                    next_offset = LittleEndian.ToUInt32 (index, current_offset);
                uint entry_size = next_offset - entry_offset;
                if (8 != file.View.Read (entry_offset, chunk_buf, 0, 8))
                    return null;

                Decrypt (entry_offset, data_key, chunk_buf);
                if (!Binary.AsciiEqual (chunk_buf, "DSET"))
                    return null;
                uint chunk_offset = entry_offset + 10;
                int chunk_count = LittleEndian.ToInt32 (chunk_buf, 4);
                string filename = null;
                string type = null;
                for (int chunk = 0; chunk < chunk_count; ++chunk)
                {
                    if (8 != file.View.Read (chunk_offset, chunk_buf, 0, 8))
                        return null;
                    Decrypt (chunk_offset, data_key, chunk_buf);
                    int chunk_size = LittleEndian.ToInt32 (chunk_buf, 4);
                    if (chunk_size <= 0)
                        return null;
                    if (Binary.AsciiEqual (chunk_buf, "PICT"))
                    {
                        if (null == type)
                        {
                            type = "image";
                            entry_offset = chunk_offset + 0x10;
                            entry_size = (uint)chunk_size - 6;
                        }
                    }
                    else if (null == filename && Binary.AsciiEqual (chunk_buf, "FTAG"))
                    {
                        if (chunk_size > filename_buf.Length)
                            filename_buf = new byte[chunk_size];
                        if (chunk_size != file.View.Read (chunk_offset+10, filename_buf, 0, (uint)chunk_size))
                            return null;
                        Decrypt (chunk_offset+10, data_key, filename_buf, 0, chunk_size-2);
                        filename = Encodings.cp932.GetString (filename_buf, 0, chunk_size-2);
                    }
                    chunk_offset += 10 + (uint)chunk_size;
                }
                Entry entry;
                if (!string.IsNullOrEmpty (filename))
                {
                    filename = DriveRe.Replace (filename, "");
                    entry = FormatCatalog.Instance.Create<Entry> (filename);
                }
                else
                {
                    entry = new Entry {
                        Name = string.Format ("{0}#{1:D4}", base_filename, i),
                        Type = type ?? ""
                    };
                }
                entry.Offset = entry_offset;
                entry.Size = entry_size;
                dir.Add (entry);
            }
            return new WagArchive (file, this, dir, data_key);
        }

        static readonly Regex DriveRe = new Regex (@"^(?:.+:)?\\+");

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var warc = arc as WagArchive;
            if (null == warc)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var data = new byte[entry.Size];
            arc.File.View.Read (entry.Offset, data, 0, entry.Size);
            Decrypt ((uint)entry.Offset, warc.Key, data);
            return new MemoryStream (data);
        }

        private byte[] GenerateKey (byte[] keyword)
        {
            return GenerateKey (keyword, keyword.Length);
        }

        private byte[] GenerateKey (byte[] keyword, int length)
        {
            int hash = 0;
            for (int i = 0; i < length; ++i)
                hash = (((sbyte)keyword[i] + i) ^ hash) + length;

            int key_length = (hash & 0xFF) + 0x40;

            for (int i = 0; i < length; ++i)
                hash += (sbyte)keyword[i];

            byte[] key = new byte[key_length--];
            key[1] = (byte)(hash >> 8);
            hash &= 0xF;
            key[0] = (byte)hash;
            key[2] = 0x46;
            key[3] = 0x88;

            for (int i = 4; i < key_length; ++i)
            {
                hash += (((sbyte)keyword[i % length] ^ hash) + i) & 0xFF;
                key[i] = (byte)hash;
            }
            return key;
        }

        private void Decrypt (uint offset, byte[] key, byte[] index)
        {
            Decrypt (offset, key, index, 0, index.Length);
        }

        private void Decrypt (uint offset, byte[] key, byte[] index, int pos, int length)
        {
            uint key_last = (uint)key.Length-1;
            for (uint i = 0; i < length; ++i)
                index[pos+i] ^= key[(offset + i) % key_last];
        }
    }
}
