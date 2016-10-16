//! \file       ArcValkyrieComplex.cs
//! \date       Sat Apr 23 14:58:33 2016
//! \brief      Valkyrie Complex resource archives.
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

namespace GameRes.Formats.Circus
{
    internal class VcPakFile : ArcFile
    {
        public readonly byte Key;

        public VcPakFile (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class VcPakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/VC"; } }
        public override string Description { get { return "Valkyrie Complex resource archive"; } }
        public override uint     Signature { get { return 0x62827582; } } // 'ＶＣ'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        static readonly byte[] TrialSignature  = Encodings.cp932.GetBytes ("ＶＣ体験版");
        static readonly byte[] RetailSignature = Encodings.cp932.GetBytes ("ＶＣ製品版");

        public VcPakOpener ()
        {
            Extensions = new string[] { "pak" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var signature = file.View.ReadBytes (0, 10);
            bool is_trial = signature.SequenceEqual (TrialSignature);
            if (!is_trial && !signature.SequenceEqual (RetailSignature))
                return null;
            byte key = (byte)(is_trial ? 0x38 : 0x58);
            uint ukey = (uint)key << 8 | key;
            ukey |= ukey << 16;
            int count = (int)(file.View.ReadUInt32 (0x18) ^ ukey);
            if (!IsSaneCount (count))
                return null;
            uint index_size = file.View.ReadUInt32 (0x1C) ^ ukey;
            if (index_size >= file.MaxOffset)
                return null;
            var index = file.View.ReadBytes (0x20, index_size);
            for (int i = 0; i < index.Length; ++i)
                index[i] ^= key;

            int index_pos = 4;
            int names_pos = count * 0x10;
            var dir = new List<Entry> (count);
            for (uint i = 0; i < count; ++i)
            {
                int name_length = LittleEndian.ToInt32 (index, index_pos);
                var name = Encodings.cp932.GetString (index, names_pos, name_length);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = LittleEndian.ToUInt32 (index, index_pos+4);
                entry.Size   = LittleEndian.ToUInt32 (index, index_pos+8);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (name.EndsWith (".cps", StringComparison.InvariantCultureIgnoreCase))
                    entry.Type = "image";
                dir.Add (entry);
                index_pos += 0x10;
                names_pos += name_length + 1;
            }
            return new VcPakFile (file, this, dir, (byte)(is_trial ? 0x25 : 0x24));
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var vcarc = arc as VcPakFile;
            if (null == vcarc)
                return base.OpenEntry (arc, entry);
            var data = vcarc.File.View.ReadBytes (entry.Offset, entry.Size);
            for (int i = 0; i < data.Length; ++i)
                data[i] ^= vcarc.Key;
            if (entry.Name.EndsWith (".cs", StringComparison.InvariantCultureIgnoreCase))
            {
                for (int i = 0; i < data.Length; ++i)
                    --data[i];
            }
            else if (entry.Name.EndsWith (".cps", StringComparison.InvariantCultureIgnoreCase))
            {
                data = UnpackCps (data);
            }
            return new BinMemoryStream (data, entry.Name);
        }

        byte[] UnpackCps (byte[] input)
        {
            uint unpacked_size = (LittleEndian.ToUInt32 (input, 0) ^ 0xA415FCF) & 0xFFFFFFF;
            int type = input[3] >> 4;
            if (type > 3)
                return input;
            DecryptCps (input);
            var output = new byte[unpacked_size];
            switch (type)
            {
            case 0: Buffer.BlockCopy (input, 4, output, 0, output.Length); break;
            case 1: UnpackV1 (input, output); break;
            case 2: UnpackV2 (input, output); break;
            case 3: UnpackV3 (input, output); break;
            }
            return output;
        }

        static void DecryptCps (byte[] input)
        {
            int length = input.Length;
            if (length < 0x308)
                return;

            input[4] ^= input[length - 1];
            byte key = 0xFF;

            length -= 8;
            int a = 8 + length - key;
            int b = 8 + length / 0x200 + key;
            byte t = input[a];
            input[a] = input[b];
            input[b] = t;

            a = 8 + key + 2 * (length / 0x200);
            b = 8 + length - 2 * key;
            t = input[a];
            input[a] = input[b];
            input[b] = t;
        }

        static void UnpackV1 (byte[] input, byte[] output)
        {
            int dst = 0;
            using (var mem = new MemoryStream (input, 4, input.Length-4))
            using (var bits = new ReverseBitStream (mem))
            {
                while (dst < output.Length)
                {
                    int count = bits.GetBits (4);
                    if (0 != bits.GetNextBit())
                    {
                        for (int i = 0; i <= count; ++i)
                            output[dst++] = (byte)bits.GetBits (8);
                    }
                    else
                    {
                        byte b = (byte)bits.GetBits (8);
                        for (int i = 0; i < count; ++i)
                            output[dst++] = b;
                    }
                }
            }
        }

        static void UnpackV2 (byte[] input, byte[] output)
        {
            output[output.Length-1] = input[36];
            using (var mem = new MemoryStream (input, 38, input.Length-38))
            using (var bits = new ReverseBitStream (mem))
            {
                for (int dst = 0; dst < output.Length; dst += 2)
                {
                    if (0 != bits.GetNextBit())
                    {
                        output[dst+1] = (byte)bits.GetBits (8);
                        output[dst]   = (byte)bits.GetBits (8);
                    }
                    else
                    {
                        int j;
                        for (j = 0; j < 30; j += 2)
                        {
                            if (0 != bits.GetNextBit())
                                break;
                        }
                        output[dst]   = input[4 + j];
                        output[dst+1] = input[5 + j];
                    }
                }
            }
        }

        static void UnpackV3 (byte[] input, byte[] output)
        {
            int src = 4;
            int dst = 0;
            if (output.Length <= 128)
            {
                Buffer.BlockCopy (input, src, output, dst, output.Length);
                return;
            }
            Buffer.BlockCopy (input, src, output, dst, 128);
            src += 128;
            dst += 128;
            using (var mem = new MemoryStream (input, src, input.Length-src))
            using (var bits = new ReverseBitStream (mem))
            {
                while (dst < output.Length)
                {
                    if (0 != bits.GetNextBit())
                    {
                        int offset = bits.GetBits (7) + 1;
                        int count  = bits.GetBits (4) + 2;
                        Binary.CopyOverlapped (output, dst - offset, dst, count);
                        dst += count;
                    }
                    else
                    {
                        output[dst++] = (byte)bits.GetBits (8);
                    }
                }
            }
        }
    }

    internal class ReverseBitStream : BitStream, IBitStream
    {
        public ReverseBitStream (Stream file, bool leave_open = false)
            : base (file, leave_open)
        {
        }

        public int GetBits (int count)
        {
            while (m_cached_bits < count)
            {
                int b = m_input.ReadByte();
                if (-1 == b)
                    return -1;
                m_bits |= b << m_cached_bits;
                m_cached_bits += 8;
            }
            int value = 0;
            m_cached_bits -= count;
            while (count --> 0)
            {
                value |= (m_bits & 1) << count;
                m_bits >>= 1;
            }
            return value;
        }

        public int GetNextBit ()
        {
            return GetBits (1);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class VcPacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/VC"; } }
        public override string Description { get { return "Valkyrie Complex resource archive"; } }
        public override uint     Signature { get { return 1; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public VcPacOpener ()
        {
            Extensions = new string[] { "pac" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint base_offset = file.View.ReadUInt32 (8);
            uint file_size = file.View.ReadUInt32 (0xC);
            if (base_offset >= file.MaxOffset || file_size != file.MaxOffset)
                return null;

            uint index_offset = 0x20;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x20);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                index_offset += 0x20;
                entry.Size   = file.View.ReadUInt32 (index_offset);
                entry.Offset = file.View.ReadUInt32 (index_offset+4) + base_offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                index_offset += 0x18;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
