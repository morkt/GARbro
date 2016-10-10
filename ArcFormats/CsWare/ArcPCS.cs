//! \file       ArcPCS.cs
//! \date       Mon Jan 25 01:36:53 2016
//! \brief      C's ware resource archive.
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
using System.Security.Cryptography;
using GameRes.Cryptography;
using GameRes.Utility;

namespace GameRes.Formats.CsWare
{
    internal class PcsEntry : Entry
    {
        public byte Key;
        public uint NameHash;   // used in archives version 6
    }

    internal class PcsArchive : ArcFile
    {
        public readonly int Version;

        public PcsArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, int version)
            : base (arc, impl, dir)
        {
            Version = version;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PcsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PCS"; } }
        public override string Description { get { return "C's ware resource archive"; } }
        public override uint     Signature { get { return 0x53434350; } } // 'PCCS'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadUInt16 (4);
            if (version < 1 || version > 6)
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            uint data_offset = file.View.ReadUInt32 (12);
            uint index_size = data_offset - 0x10;
            var index = file.View.ReadBytes (0x10, index_size);
            if (6 == version)
            {
                index = ShuffleBlocks (index, file.View.ReadUInt16 (6));
            }
            int index_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int name_length;
                if (version > 1)
                {
                    name_length = LittleEndian.ToInt32 (index, index_offset);
                    if (0 == name_length)
                        break;
                    if (name_length > index_size)
                        return null;
                    index_offset += 5 + name_length;
                }
                name_length = LittleEndian.ToInt32 (index, index_offset);
                if (0 == name_length)
                    break;
                if (name_length > index_size)
                    return null;
                index_offset += 5;
                byte checksum;
                var name = DecryptName (index, index_offset, name_length, out checksum);
                Entry entry;
                if (version >= 4)
                {
                    var pcs_entry = FormatCatalog.Instance.Create<PcsEntry> (name);
                    entry = pcs_entry;
                    pcs_entry.Key = checksum;
                    if (6 == version)
                        pcs_entry.NameHash = ComputeHash (index, index_offset, name_length-1);
                    index_offset += name_length;
                    int c = -1 - checksum;
                    for (int j = 0; j < 4; ++j)
                    {
                        byte key = (byte)((checksum + (17 << j)) & 0x33);
                        index[index_offset+j]   = (byte)(c + key - index[index_offset+j]);
                        index[index_offset+j+4] = (byte)(c + key - index[index_offset+j+4]);
                    }
                }
                else
                {
                    index_offset += name_length;
                    entry = FormatCatalog.Instance.Create<Entry> (name);
                }
                if (index_offset > data_offset)
                    return null;
                entry.Offset = LittleEndian.ToUInt32 (index, index_offset) + data_offset;
                entry.Size   = LittleEndian.ToUInt32 (index, index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x10;
            }
            if (0 == dir.Count)
                return null;
            return new PcsArchive (file, this, dir, version);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PcsEntry;
            var parc = arc as PcsArchive;
            if (null == pent || null == parc)
                return base.OpenEntry (arc, entry);
            uint header_size = Math.Min (entry.Size, 512u);
            var header = arc.File.View.ReadBytes (entry.Offset, header_size);
            if (6 == parc.Version)
            {
                header = ShuffleBlocks (header, pent.NameHash);
            }
            for (int i = 0; i < header.Length; ++i)
            {
                header[i] = (byte)(pent.Key - header[i] - 1);
            }
            if (header_size == entry.Size)
                return new MemoryStream (header);
            var rest = arc.File.CreateStream (entry.Offset+512, entry.Size-512);
            return new PrefixStream (header, rest);
        }

        string DecryptName (byte[] name_buffer, int offset, int length, out byte checksum)
        {
            int count;
            checksum = 0;
            for (count = 0; count < length; ++count)
            {
                if (0 == name_buffer[offset+count])
                    break;
                name_buffer[offset+count] = Binary.RotByteL (name_buffer[offset+count], 4);
                checksum += name_buffer[offset+count];
            }
            return Encodings.cp932.GetString (name_buffer, offset, count);
        }

        byte[] ShuffleBlocks (byte[] input, uint key)
        {
            int block_size = input.Length >> 5;
            var output = new byte[input.Length];
            var twister = new MersenneTwister (key);
            int copied_sections = 0;
            for (int i = 0; i < 0x20; ++i)
            {
                int j = (int)(twister.Rand() & 0x1F);
                while (0 != (copied_sections & (1 << j)))
                    j = (j + 1) & 0x1F;
                copied_sections |= 1 << j;
                Buffer.BlockCopy (input, j * block_size, output, i * block_size, block_size); 
            }
            int shuffled = block_size << 5;
            if (shuffled != input.Length)
                Buffer.BlockCopy (input, shuffled, output, shuffled, input.Length-shuffled);
            return output;
        }

        static readonly Lazy<HashAlgorithm> SHA1 = new Lazy<HashAlgorithm> (() => System.Security.Cryptography.SHA1.Create());

        uint ComputeHash (byte[] data, int offset, int length)
        {
            var hash = SHA1.Value.ComputeHash (data, offset, length);
            return BigEndian.ToUInt32 (hash, 0);
        }
    }
}
