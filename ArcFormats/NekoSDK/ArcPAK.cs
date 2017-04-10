//! \file       ArcPAK.cs
//! \date       Thu Sep 29 15:17:42 2016
//! \brief      NekoSDK NEKOPACK resource archive.
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.NekoSDK
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "NEKOPACK/4"; } }
        public override string Description { get { return "NekoSDK engine resource archive"; } }
        public override uint     Signature { get { return 0x4F4B454E; } } // 'NEKOPACK'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public PakOpener ()
        {
            Extensions = new string[] { "pak" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "NEKOPACK4"))
                return null;
            int version = file.View.ReadByte (9);
            uint index_offset, index_end;
            if ('A' == version)
            {
                index_offset = 0xE;
                index_end = index_offset + file.View.ReadUInt32 (0xA);
            }
            else if ('S' == version)
            {
                index_offset = 0xA;
                index_end = index_offset + file.View.ReadUInt32 (0xA) + 12;
            }
            else
            {
                return null;
            }
            var dir = new List<Entry>();
            var name_buffer = new byte[0x100];
            while (index_offset < index_end)
            {
                uint name_length = file.View.ReadUInt32 (index_offset);
                index_offset += 4;
                if (0 == name_length)
                    break;
                if (name_length > name_buffer.Length)
                    return null;
                file.View.Read (index_offset, name_buffer, 0, name_length);
                index_offset += name_length;
                int key = 0;
                for (uint i = 0; i < name_length; ++i)
                    key += (sbyte)name_buffer[i];
                var name = Binary.GetCString (name_buffer, 0, (int)name_length);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset)   ^ (uint)key;
                entry.Size   = file.View.ReadUInt32 (index_offset+4) ^ (uint)key;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (name.HasExtension (".alp"))
                    entry.Type = "";
                dir.Add (entry);
                index_offset += 8;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            uint key = entry.Size / 8 + 0x22;
            var header = new byte[Math.Min (4, entry.Size)];
            arc.File.View.Read (entry.Offset, header, 0, (uint)header.Length);
            for (int i = 0; i < header.Length; ++i)
            {
                header[i] ^= (byte)key;
                key <<= 3;
            }
            var pent = entry as PackedEntry;
            if (null != pent && !pent.IsPacked && entry.Size >= 8)
            {
                pent.IsPacked = true;
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset+entry.Size-4);
            }
            Stream input;
            if (header.Length == entry.Size)
            {
                input = new MemoryStream (header);
            }
            else
            {
                input = arc.File.CreateStream (entry.Offset+4, entry.Size-8);
                input = new PrefixStream (header, input);
            }
            return new ZLibStream (input, CompressionMode.Decompress);
        }
    }
}
