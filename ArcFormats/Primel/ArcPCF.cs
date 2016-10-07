//! \file       ArcPCF.cs
//! \date       Fri Sep 30 10:37:28 2016
//! \brief      Primel the Adventure System resource archive.
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
using System.Security.Cryptography;
using GameRes.Utility;

namespace GameRes.Formats.Primel
{
    internal class PcfEntry : PackedEntry
    {
        public uint                 Flags;
        public IEnumerable<byte>    Key;
    }

    [Export(typeof(ArchiveFormat))]
    public class PcfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PCF"; } }
        public override string Description { get { return "Primel ADV System resource archive"; } }
        public override uint     Signature { get { return 0x6B636150; } } // 'Pack'
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "Code"))
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            long data_size = file.View.ReadInt64 (0x10);
            long index_offset = file.View.ReadInt64 (0x28);
            if (data_size >= file.MaxOffset || index_offset >= file.MaxOffset)
                return null;
            uint index_size = file.View.ReadUInt32 (0x30);
            uint flags = file.View.ReadUInt32 (0x38);
            var key = file.View.ReadBytes (0x58, 8);
            long base_offset = file.MaxOffset - data_size;

            using (var stream = file.CreateStream (base_offset + index_offset, index_size))
            using (var index = ReadFile (stream, key, flags))
            {
                var buffer = new byte[0x80];
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    if (buffer.Length != index.Read (buffer, 0, buffer.Length))
                        break;
                    var name = Binary.GetCString (buffer, 0, 0x50);
                    var entry = FormatCatalog.Instance.Create<PcfEntry> (name);
                    entry.Offset = LittleEndian.ToInt64 (buffer, 0x50) + base_offset;
                    entry.UnpackedSize = LittleEndian.ToUInt32 (buffer, 0x58);
                    entry.Size   = LittleEndian.ToUInt32 (buffer, 0x60);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    entry.Flags  = LittleEndian.ToUInt32 (buffer, 0x68);
                    entry.Key    = new ArraySegment<byte> (buffer, 0x78, 8).ToArray();
                    entry.IsPacked = entry.UnpackedSize != entry.Size;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PcfEntry;
            if (null == pent)
                return base.OpenEntry (arc, entry);
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            try
            {
                input = ReadFile (input, pent.Key, pent.Flags);
                if (pent.IsPacked)
                    input = new LimitStream (input, pent.UnpackedSize);
                return input;
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        Stream ReadFile (Stream input, IEnumerable<byte> key, uint flags)
        {
            var key1 = GenerateKey (key);
            var iv   = GenerateKey (key1);

            ICryptoTransform decryptor;
            switch (flags & 0xF0000)
            {
            case 0x10000:
                decryptor = new Primel1Encyption (key1, iv);
                break;
            case 0x20000:
                decryptor = new Primel2Encyption (key1, iv);
                break;
            case 0x30000:
                decryptor = new Primel3Encyption (key1, iv);
                break;
            case 0x80000: // RC6
                decryptor = new GameRes.Cryptography.RC6 (key1, iv);
                break;

            case 0xA0000: // AES
                using (var aes = Rijndael.Create())
                {
                    aes.Mode = CipherMode.CFB;
                    aes.Padding = PaddingMode.Zeros;
                    decryptor = aes.CreateDecryptor (key1, iv);
                }
                break;

            default: // not encrypted
                return input;
            }
            input = new CryptoStream (input, decryptor, CryptoStreamMode.Read);
            try
            {
                if (0 != (flags & 0xFF))
                {
                    input = new RangePackedStream (input);
                }
                switch (flags & 0xF00)
                {
                case 0x400:
                    input = new RlePackedStream (input);
                    input = new MtfPackedStream (input);
                    break;
                case 0x700:
                    input = new LzssPackedStream (input);
                    break;
                }
                return input;
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        byte[] GenerateKey (IEnumerable<byte> seed)
        {
            var sha = new Primel.SHA256();
            var hash = sha.ComputeHash (seed.ToArray());
            var key = new byte[0x10];
            for (int i = 0; i < hash.Length; ++i)
            {
                key[i & 0xF] ^= hash[i];
            }
            return key;
        }
    }
}
