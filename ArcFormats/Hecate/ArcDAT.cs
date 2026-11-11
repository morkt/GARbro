//! \file       ArcDAT.cs
//! \date       2026-09-24
//! \brief      Hecate resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Hecate
{
    internal class DatArchive : ArcFile
    {
        public readonly byte[] Key;
        public readonly byte[] IV;

        public DatArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key, byte[] iv)
            : base (arc, impl, dir)
        {
            Key = key;
            IV = iv;
        }
    }

    internal class DatEntry : Entry
    {
        public uint EncryptedSize;
        public uint RemainingSize;
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/HECATE"; } }
        public override string Description { get { return "Hecate resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static readonly byte[] DefaultKey = Encoding.UTF8.GetBytes ("プッチンプリン食べたいなー");

        static readonly ISet<string> ImageArchives = new HashSet<string> {
            "bgimage", "ev", "fgimage", "image"
        };
        static readonly ISet<string> AudioArchives = new HashSet<string> { "bgm", "se", "voice" };

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".dat"))
                return null;
            var toc_name = Path.ChangeExtension (file.Name, "lib");
            if (!VFS.FileExists (toc_name))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name).ToLowerInvariant();
            bool is_image = ImageArchives.Contains (base_name);
            bool is_audio = AudioArchives.Contains (base_name);
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = ResizeKey (DefaultKey, 32);
                aes.IV = ResizeKey (DefaultKey, 16);

                using (var enc = VFS.OpenStream (toc_name))
                using (var dec = new InputCryptoStream (enc, aes.CreateDecryptor()))
                using (var reader = new BinaryReader (dec))
                {
                    int count = (int)reader.ReadInt64();
                    if (!IsSaneCount (count))
                        return null;

                    var dir = new List<Entry> (count);
                    for (int i = 0; i < count; i++)
                    {
                        var name_buf = reader.ReadBytes (0x200);
                        var name = Binary.GetCString (name_buf, 0, Encoding.UTF8);
                        var entry = Create<DatEntry> (name);
                        entry.Size = (uint)reader.ReadInt64();
                        entry.EncryptedSize = (uint)reader.ReadInt64();
                        entry.RemainingSize = (uint)reader.ReadInt64();
                        entry.Offset = reader.ReadInt64();
                        reader.ReadInt64();
                        if (!entry.CheckPlacement (file.MaxOffset))
                            return null;
                        if (base_name == "script")
                        {
                            entry.Type = "script";
                            entry.Name += ".ks";
                        }
                        else if (is_image)
                            entry.Type = "image";
                        else if (is_audio)
                            entry.Type = "audio";
                        dir.Add (entry);
                    }

                    return new DatArchive (file, this, dir, aes.Key, aes.IV);
                }
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var darc = arc as DatArchive;
            var dent = entry as DatEntry;
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = darc.Key;
                aes.IV = darc.IV;
                
                var buffer = new byte[dent.Size];

                using (var enc = arc.File.CreateStream (dent.Offset, dent.EncryptedSize + dent.RemainingSize))
                using (var lim = new LimitStream (enc, dent.EncryptedSize))
                using (var dec = new InputCryptoStream (lim, aes.CreateDecryptor()))
                using (var mem = new MemoryStream())
                {
                    dec.CopyTo (mem);
                    mem.Position = 0;
                    mem.Read (buffer, 0, (int)mem.Length);
                    enc.Read (buffer, (int)mem.Length, (int)dent.RemainingSize);
                    return new BinMemoryStream (buffer);
                }
            }
        }

        byte[] ResizeKey (byte[] key, int size)
        {
            var output = new byte[size];
            for (int i = 0; i < key.Length; i++)
            {
                output[i % output.Length] ^= key[i];
            }
            return output;
        }
    }
}
