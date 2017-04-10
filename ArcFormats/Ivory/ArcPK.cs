//! \file       ArcPK.cs
//! \date       Mon Sep 12 01:39:49 2016
//! \brief      'fPK' resource archive.
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
using GameRes.Utility;

namespace GameRes.Formats.Ivory
{
    internal class PkEntry : Entry
    {
        public int  NameOffset;
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PK/IVORY"; } }
        public override string Description { get { return "Ivory resource archive"; } }
        public override uint     Signature { get { return 0x204B5066; } } // 'fPK '
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset != file.View.ReadUInt32 (4))
                return null;

            long base_offset = 0;
            List<Entry> dir = null;
            bool got_names = false;
            long offset = 8;
            while (offset < file.MaxOffset)
            {
                var id = new AsciiString (file.View.ReadBytes (offset, 4));
                uint section_size = file.View.ReadUInt32 (offset+4);
                if (0 == section_size)
                    return null;
                uint header_size = file.View.ReadUInt32 (offset+8);
                if ("cLST" == id)
                {
                    int count = file.View.ReadInt32 (offset+0x10);
                    if (!IsSaneCount (count))
                        return null;
                    uint key = file.View.ReadUInt32 (offset+0x14);
                    var clst = file.View.ReadBytes (offset+header_size, section_size - header_size);
                    Decrypt (clst, key);
                    dir = new List<Entry> (count);
                    int index_offset = 0;
                    for (int i = 0; i < count; ++i)
                    {
                        var entry = new PkEntry {
                            NameOffset  = LittleEndian.ToInt32 (clst, index_offset),
                            Offset      = LittleEndian.ToUInt32 (clst, index_offset+4),
                            Size        = LittleEndian.ToUInt32 (clst, index_offset+8),
                        };
                        dir.Add (entry);
                        index_offset += 12;
                    }
                }
                else if ("cNAM" == id)
                {
                    if (null == dir)
                        return null;
                    uint key = file.View.ReadUInt32 (offset+0x10);
                    var cnam = file.View.ReadBytes (offset+header_size, section_size - header_size);
                    Decrypt (cnam, key);
                    foreach (PkEntry entry in dir)
                    {
                        var name = Binary.GetCString (cnam, entry.NameOffset);
                        entry.Name = name;
                        if (name.HasExtension (".px"))
                            entry.Type = "audio";
                        else
                            entry.Type = FormatCatalog.Instance.GetTypeFromName (name);
                    }
                    got_names = true;
                }
                else if ("cDAT" == id)
                {
                    base_offset = offset + header_size;
                }
                offset += section_size;
            }
            if (null == dir || !got_names || 0 == base_offset)
                return null;

            foreach (var entry in dir)
            {
                entry.Offset += base_offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
            }
            return new ArcFile (file, this, dir);
        }

        internal static void Decrypt (byte[] data, uint seed)
        {
            int length = data.Length / 4;
            if (0 == length)
                return;
            var ctl = new ushort[32];
            var key = new uint[32];

            for (int i = 0; i < 32; ++i)
            {
                uint code = 0;
                uint k = seed;
                for (int j = 0; j < 16; ++j)
                {
                    code = (k ^ (k >> 1)) << 15 | (code & 0xFFFF) >> 1;
                    k >>= 2;
                }		
                key[i] = seed;
                ctl[i] = (ushort)code;
                seed = Binary.RotL (seed, 1);
            }
            unsafe
            {
                fixed (byte* data8 = data)
                {
                    uint* data32 = (uint*)data8;
                    for (int i = 0; i < length; ++i)
                    {
                        uint s = *data32;
                        ushort code = ctl[i & 0x1F];
                        uint d = 0;
                        uint v3 = 3;
                        uint v2 = 2;
                        uint v1 = 1;
                        for (int j = 0; j < 16; ++j)
                        {
                            if (0 != (code & 1))
                            {
                                d |= (s & v1) << 1 | (s >> 1) & (v2 >> 1);
                            }
                            else
                            {
                                d |= s & v3;
                            }
                            code >>= 1;
                            v3 <<= 2;
                            v2 <<= 2;
                            v1 <<= 2;
                        }
                        *data32++ = d ^ key[i & 0x1F];
                    }
                }
            }
        }
    }
}
