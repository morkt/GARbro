//! \file       ArcPBZ.cs
//! \date       Thu Jun 23 19:12:32 2016
//! \brief      PVNS engine (CMVS precursor) resource archive.
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
using System.Linq;
using GameRes.Utility;

namespace GameRes.Formats.Pvns
{
    internal class PbzArchive : ArcFile
    {
        public readonly byte[] ArcKey;
        public readonly byte[] ScriptKey;

        public PbzArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, PbzKeys scheme)
            : base (arc, impl, dir)
        {
            ArcKey = scheme.ArcKey;
            ScriptKey = scheme.ScriptKey;
        }
    }

    [Serializable]
    public class PbzKeys
    {
        public byte[]   ArcKey;
        public byte[]   ScriptKey;
    }

    [Serializable]
    public class PbzScheme : ResourceScheme
    {
        public Dictionary<string, PbzKeys> KnownSchemes;
    }

    [Export(typeof(ArchiveFormat))]
    public class PbzOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PBZ"; } }
        public override string Description { get { return "PVNS engine resource archive"; } }
        public override uint     Signature { get { return 0x315A4250; } } // 'PBZ1'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public static Dictionary<string, PbzKeys> KnownSchemes = new Dictionary<string, PbzKeys>();

        public override ResourceScheme Scheme
        {
            get { return new PbzScheme { KnownSchemes = KnownSchemes }; }
            set { KnownSchemes = ((PbzScheme)value).KnownSchemes; }
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (0 == KnownSchemes.Count)
                return null;
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint index_size = file.View.ReadUInt32 (8);
            var index = file.View.ReadBytes (0x10, index_size);
            if (index.Length != index_size)
                return null;

            var scheme = KnownSchemes.Values.First(); // XXX
            Decrypt (index, scheme.ArcKey);

            long base_offset = 0x10 + index_size;
            int index_pos = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int entry_size = LittleEndian.ToInt32 (index, index_pos);
                if (entry_size <= 0x18)
                    return null;
                var name = Binary.GetCString (index, index_pos+0x18, entry_size - 0x18);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Size   = LittleEndian.ToUInt32 (index, index_pos+4);
                entry.Offset = base_offset + LittleEndian.ToUInt32 (index, index_pos+8);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_pos += entry_size;
            }
            return new PbzArchive (file, this, dir, scheme);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var parc = arc as PbzArchive;
            if (null == parc)
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            Decrypt (data, parc.ArcKey);
            if (entry.Name.EndsWith (".scr", StringComparison.InvariantCultureIgnoreCase)
                && Binary.AsciiEqual (data, "PSRA") && data.Length > 20)
            {
                data = DecryptScript (data, parc.ScriptKey);
            }
            return new MemoryStream (data);
        }

        static void Decrypt (byte[] data, byte[] key)
        {
            if (0 == key.Length)
                return;
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] = (byte)((data[i] ^ key[i % key.Length]) + 0x80);
            }
        }

        byte[] DecryptScript (byte[] data, byte[] key)
        {
            int input_size = data.Length - 0x14;
            for (int i = 4; i < 0x14; ++i)
            {
                data[i] = (byte)((data[i] ^ 0xF5) - 0x10);
            }
            if (0 != (input_size & -4))
            {
                unsafe
                {
                    fixed (byte* data8 = &data[0x14])
                    {
                        uint m = 0x817543;
                        uint* data32 = (uint*)data8;
                        for (int length = (((input_size & -4) - 1) >> 2) + 1; length > 0; --length)
                        {
                            *data32 ^= m;
                            m += 0x1352467u;
                            ++data32;
                        }
                    }
                }
            }
            uint checksum = LittleEndian.ToUInt32 (data, 4);
            for (int i = 0x14; i < data.Length; ++i)
            {
                checksum -= data[i];
            }
            if (checksum != 0)
                throw new InvalidEncryptionScheme();

            for (int i = 0x14; i < data.Length; ++i)
            {
                data[i] = Binary.RotByteR (data[i], 3);
            }
            int src = 0x14;
            for (int count = ((input_size - 2) >> 1) + 1; count > 0; --count)
            {
                byte t = data[src];
                data[src] = data[src+1];
                data[src+1] = t;
                src += 2;
            }
            int k = 0;
            for (int i = 0x14; i < data.Length; ++i)
            {
                data[i] = (byte)((data[i] ^ key[k++]) + 0x35);
                k %= key.Length;
            }
            int unpacked_size = LittleEndian.ToInt32 (data, 0x10);
            var output = new byte[unpacked_size];
            src = 0x14 + LittleEndian.ToInt32 (data, 8);
            int dst = 0;
            int bit_mask = 0x80;
            int bits = 0x14;
            while (dst < output.Length)
            {
                if (0 == bit_mask)
                {
                    bit_mask = 0x80;
                    ++bits;
                }
                if (0 != (bit_mask & data[bits]))
                {
                    int u = LittleEndian.ToUInt16 (data, src);
                    src += 2;
                    int offset = (u >> 5) + 1;
                    int count = (u & 0x1F) + 3;
                    Binary.CopyOverlapped (output, dst - offset, dst, count);
                    dst += count;
                }
                else
                {
                    output[dst++] = data[src++];
                }
                bit_mask >>= 1;
            }
            for (int i = 0; i < output.Length; ++i)
            {
                output[i] ^= 0x7E;
            }
            return output;
        }
    }
}
