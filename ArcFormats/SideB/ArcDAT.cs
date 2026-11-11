//! \file       ArcDAT.cs
//! \date       2026-08-21
//! \brief      Side-B resource archive.
//
// Copyright (C) 2026 by morkt
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
using System.Text;
using System.Text.RegularExpressions;
using GameRes.Compression;

namespace GameRes.Formats.AttacheCase
{
    internal class AtcArchive : ArcFile
    {
        public readonly byte[] Key;
        public readonly byte[] IV;
        public readonly uint DataOffset;

        public AtcArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key, byte[] iv, uint data_offset)
            : base (arc, impl, dir)
        {
            Key = key;
            IV = iv;
            DataOffset = data_offset;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/SIDEB"; } }
        public override string Description { get { return "'Martopia' resource archive"; } }
        public override uint     Signature { get { return 0x00030006; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        static readonly byte[] DefaultKey = Encoding.ASCII.GetBytes ("OTG");

        public override ArcFile TryOpen (ArcView file)
        {
            uint header_size = file.View.ReadUInt32 (0x1C);
            uint data_offset = 0x60 + header_size + header_size % 32;
            if (data_offset >= file.MaxOffset)
                return null;

            var key = new byte[32];
            Buffer.BlockCopy (DefaultKey, 0, key, 0, Math.Min (DefaultKey.Length, key.Length));
            var iv = file.View.ReadBytes (0x20, 32);

            var dir = new List<Entry> ();
            using (var aes = Rijndael.Create())
            {
                aes.BlockSize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.Zeros;
                aes.Key = key;
                aes.IV = iv;

                using (var enc = file.CreateStream (0x40))
                using (var dec = new InputCryptoStream (enc, aes.CreateDecryptor()))
                {
                    var info = new byte[header_size];
                    dec.Read (info, 0, info.Length);
                    string info_str = Encoding.UTF8.GetString (info);
                    var matches = Regex.Matches (info_str, @"^U_\d+:([^\t]*)\t(\d*)\t", RegexOptions.Multiline);
                    uint offset = 0;
                    foreach (Match match in matches)
                    {
                        string name = match.Groups[1].Value;
                        uint size = uint.Parse (match.Groups[2].Value);
                        var entry = Create<Entry> (name);
                        entry.Offset = offset;
                        entry.Size = size;
                        offset += size;
                        dir.Add (entry);
                    }
                }
            }

            if (dir.Count == 0)
                return null;
            iv = file.View.ReadBytes (data_offset - 32, 32);
            return new AtcArchive (file, this, dir, key, iv, data_offset);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var aarc = arc as AtcArchive;
            using (var aes = Rijndael.Create())
            {
                aes.BlockSize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.Zeros;
                aes.Key = aarc.Key;
                aes.IV = aarc.IV;

                Stream input = aarc.File.CreateStream (aarc.DataOffset);
                input = new InputCryptoStream (input, aes.CreateDecryptor());
                input = new ZLibStream (input, CompressionMode.Decompress);
                StreamSkip (input, (int)entry.Offset);
                return new LimitStream (input, entry.Size);
            }
        }

        void StreamSkip (Stream input, int bytesToSkip)
        {
            byte[] buffer = new byte[4096];
            int totalRead = 0;
 
            while (totalRead < bytesToSkip)
            {
                int toRead = Math.Min (buffer.Length, bytesToSkip - totalRead);
                int read = input.Read (buffer, 0, toRead);

                if (read == 0) break; // EOS reached early
                totalRead += read;
            }
        }
    }
}
