//! \file       ArcPK.cs
//! \date       Mon Sep 12 01:39:49 2016
//! \brief      'fPK' resource archive.
//
// Copyright (C) 2016-2017 by morkt
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

        public PakOpener ()
        {
            Signatures = new uint[] { 0x204B5066, 0x324B5066 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = '2' == file.View.ReadByte (3) ? 2 : 1;
            long base_offset = 0;
            List<Entry> dir = null;
            byte[] names = null;
            using (var stream = file.CreateStream())
            {
                Func<IBinaryStream, long> read_long;
                if (2 == version)
                    read_long = s => s.ReadInt64();
                else
                    read_long = s => s.ReadUInt32();

                stream.Position = 4;
                if (file.MaxOffset != read_long (stream))
                    return null;
                for (;;)
                {
                    long section_start = stream.Position;
                    var id_bytes = stream.ReadBytes (4);
                    if (0 == id_bytes.Length)
                        break;
                    var id = new AsciiString (id_bytes);
                    var section_size = read_long (stream);
                    var header_size = read_long (stream);
                    if (section_size < 4 || header_size > section_size)
                        return null;
                    var content_pos = section_start + header_size;
                    if ("cLST" == id)
                    {
                        stream.ReadUInt32();
                        int count = stream.ReadInt32();
                        if (!IsSaneCount (count))
                            return null;
                        uint key = stream.ReadUInt32();
                        stream.Position = content_pos;
                        var clst = stream.ReadBytes ((int)(section_size - header_size));
                        Decrypt (clst, key);
                        dir = new List<Entry> (count);
                        using (var index = new BinMemoryStream (clst))
                        {
                            for (int i = 0; i < count; ++i)
                            {
                                var entry = new PkEntry {
                                    NameOffset  = (int)read_long (index),
                                    Offset      = (long)read_long (index),
                                    Size        = (uint)read_long (index),
                                };
                                dir.Add (entry);
                            }
                        }
                    }
                    else if ("cNAM" == id)
                    {
                        if (null == dir)
                            return null;
                        stream.ReadUInt32();
                        uint key = stream.ReadUInt32();
                        stream.Position = content_pos;
                        names = stream.ReadBytes ((int)(section_size - header_size));
                        Decrypt (names, key);
                    }
                    else if ("cDAT" == id)
                    {
                        base_offset = content_pos;
                    }
                    stream.Position = section_start + section_size;
                }
            }
            if (null == dir || null == names || 0 == base_offset)
                return null;

            foreach (PkEntry entry in dir)
            {
                entry.Offset += base_offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                var name = Binary.GetCString (names, entry.NameOffset);
                entry.Name = name;
                if (name.HasExtension (".px"))
                    entry.Type = "audio";
                else
                    entry.Type = FormatCatalog.Instance.GetTypeFromName (name);
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
