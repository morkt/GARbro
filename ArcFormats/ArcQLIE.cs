//! \file       ArcQLIE.cs
//! \date       Mon Jun 15 04:03:18 2015
//! \brief      QLIE engine archives implementation.
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

using System.IO;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System;
using GameRes.Utility;

namespace GameRes.Formats.Qlie
{
    internal class QlieEntry : PackedEntry
    {
        public bool IsEncrypted;
        public uint Hash;
        public uint Key;
    }

    [Export(typeof(ArchiveFormat))]
    public class PackOpener : ArchiveFormat
    {
        public override string         Tag { get { return "QLIE/PACK"; } }
        public override string Description { get { return "QLIE engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public PackOpener ()
        {
            Extensions = new string [] { "pack" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset <= 0x1c)
                return null;
            long index_offset = file.MaxOffset - 0x1c;
            if (!file.View.AsciiEqual (index_offset, "FilePackVer")
                || !file.View.AsciiEqual (index_offset+0xC, ".0"))
                return null;
            int pack_version = file.View.ReadByte (index_offset+0xB) - '0';
            if (pack_version != 2)
                throw new NotSupportedException ("Not supported QLIE archive version");
            int count = file.View.ReadInt32 (index_offset+0x10);
            if (count <= 0 || count > 0xfffff)
                return null;
            index_offset = file.View.ReadInt64 (index_offset+0x14);
            if (index_offset < 0 || index_offset >= file.MaxOffset)
                return null;

            int pack_key = 0xC4;
            var name_buffer = new byte[0x100];
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int name_length = file.View.ReadUInt16 (index_offset);
                if (name_length > name_buffer.Length)
                    name_buffer = new byte[name_length];
                if (name_length != file.View.Read (index_offset+2, name_buffer, 0, (uint)name_length))
                    return null;

                int key = name_length + (pack_key ^ 0x3e);
                for (int k = 0; k < name_length; ++k)
                    name_buffer[k] ^= (byte)(((k + 1) ^ key) + k + 1);

                string name = Encodings.cp932.GetString (name_buffer, 0, name_length);
                var entry = new QlieEntry { Name = name };
                entry.Type = FormatCatalog.Instance.GetTypeFromName (name);

                index_offset += 2 + name_length;
                entry.Offset = file.View.ReadInt64 (index_offset);
                entry.Size   = file.View.ReadUInt32 (index_offset+8);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.UnpackedSize = file.View.ReadUInt32 (index_offset+12);
                entry.IsPacked = 0 != file.View.ReadInt32 (index_offset+0x10);
                entry.IsEncrypted = 0 != file.View.ReadInt32 (index_offset+0x14);
                entry.Hash = file.View.ReadUInt32 (index_offset+0x18);
                if (3 == pack_version)
                    entry.Key = (uint)pack_key;
                else
                    entry.Key = 0;
                dir.Add (entry);
                index_offset += 0x1c;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var qent = entry as QlieEntry;
            if (null == qent || (!qent.IsEncrypted && !qent.IsPacked))
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var data = new byte[entry.Size];
            if (entry.Size != arc.File.View.Read (entry.Offset, data, 0, entry.Size))
                return arc.File.CreateStream (entry.Offset, entry.Size);

            if (qent.IsEncrypted)
                Decrypt (data, 0, data.Length, qent.Key);
            if (qent.IsPacked)
            {
                data = Decompress (data);
                if (null == data) // probably wrong decryption
                    return arc.File.CreateStream (entry.Offset, entry.Size);
            }
            return new MemoryStream (data);
        }

        private void Decrypt (byte[] buffer, int offset, int length, uint key)
        {
            if (offset < 0)
                throw new ArgumentOutOfRangeException ("offset");
            if (length > buffer.Length || offset > buffer.Length - length)
                throw new ArgumentOutOfRangeException ("length");

            var table = new uint[2,2];
            var xor   = new uint[2];

            for (int i = 0; i < 2; ++i)
            {
                table[0,i] = 0xA73C5F9Du;
                table[1,i] = 0xCE24F523u;
                xor[i] = ((uint)length + key) ^ 0xFEC9753Eu;
            }
            unsafe
            {
                fixed (byte* raw = buffer)
                {
                    uint* encoded = (uint*)(raw + offset);
                    for (int i = 0; i < length / 8; ++i)
                    {
                        for (int k = 0; k < 2; ++k)
                        {
                            table[0,k] = (table[0,k] + table[1,k]) ^ xor[k];
                            *encoded ^= table[0,k];
                            xor[k] = *encoded++;
                        }
                    }
                }
            }
        }

        private byte[] Decompress (byte[] input)
        {
            if (LittleEndian.ToUInt32 (input, 0) != 0xFF435031)
                return null;

            bool is_16bit = 0 != (input[4] & 1);

            var node = new byte[2,256];
            var child_node = new byte[256];

            int output_length = LittleEndian.ToInt32 (input, 8);
            var output = new byte[output_length];

            int src = 12;
            int dst = 0;
            while (src < input.Length)
            {
                int i, k, count, index;

                for (i = 0; i < 256; i++)
                    node[0,i] = (byte)i;

                for (i = 0; i < 256; )
                {
                    count = input[src++];

                    if (count > 127)
                    {
                        int step = count - 127;
                        i += step;
                        count = 0;
                    }

                    if (i > 255)
                        break;

                    count++;
                    for (k = 0; k < count; k++)
                    {
                        node[0,i] = input[src++];
                        if (node[0,i] != i)
                            node[1,i] = input[src++];
                        i++;
                    }
                }

                if (is_16bit)
                {
                    count = LittleEndian.ToUInt16 (input, src);
                    src += 2;
                }
                else
                {
                    count = LittleEndian.ToInt32 (input, src);
                    src += 4;
                }

                k = 0;
                for (;;)
                {
                    if (k > 0)
                        index = child_node[--k];
                    else
                    {
                        if (0 == count)
                            break;
                        count--;
                        index = input[src++];
                    }

                    if (node[0,index] == index)
                        output[dst++] = (byte)index;
                    else
                    {
                        child_node[k++] = node[1,index];
                        child_node[k++] = node[0,index];
                    }
                }
            }
            if (dst != output.Length)
                return null;

            return output;
        }
    }
}
