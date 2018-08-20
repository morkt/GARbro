//! \file       ArcSDT.cs
//! \date       2018 Aug 07
//! \brief      Uma audio archive.
//
// Copyright (C) 2018 by morkt
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

namespace GameRes.Formats.Uma
{
    internal class SdtEntry : PackedEntry
    {
        public uint HeaderSize;
    }

    [Export(typeof(ArchiveFormat))]
    public class SdtOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SDT/UMA"; } }
        public override string Description { get { return "Uma audio archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".sdt"))
                return null;
            int signature = file.View.ReadInt32 (0);
            if (signature != 0 && signature != 1)
                return null;
            using (var input = file.CreateStream())
            {
                var dir = new List<Entry>();
                while (input.PeekByte() != -1)
                {
                    int is_packed = input.ReadInt32();
                    if (is_packed != 0 && is_packed != 1)
                        return null;
                    uint size = input.ReadUInt32();
                    var name = input.ReadCString();
                    if (string.IsNullOrWhiteSpace (name))
                        return null;
                    var entry = FormatCatalog.Instance.Create<SdtEntry> (name);
                    entry.HeaderSize = input.ReadUInt32();
                    entry.Size = entry.HeaderSize + size;
                    entry.UnpackedSize = size;
                    entry.Offset = input.Position;
                    entry.IsPacked = is_packed != 0;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    entry.ChangeType (AudioFormat.Wav);
                    dir.Add (entry);
                    input.Seek (entry.Size, SeekOrigin.Current);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var snd_ent = (SdtEntry)entry;
            var header = arc.File.View.ReadBytes (entry.Offset, snd_ent.HeaderSize);
            using (var mem = new MemoryStream ((int)snd_ent.HeaderSize + 0x18))
            using (var fmt = new BinaryWriter (mem))
            {
                uint total_size = snd_ent.Size + 0x18;
                fmt.Write (AudioFormat.Wav.Signature);
                fmt.Write (total_size);
                fmt.Write (0x45564157); // 'WAVE'
                fmt.Write (0x20746d66); // 'fmt '
                fmt.Write (header.Length);
                fmt.Write (header, 0, header.Length);
                fmt.Write (0x61746164); // 'data'
                fmt.Write (snd_ent.UnpackedSize);
                fmt.Flush();
                header = mem.ToArray();
            }
            Stream input = arc.File.CreateStream (entry.Offset+snd_ent.HeaderSize, snd_ent.UnpackedSize);
            if (snd_ent.IsPacked)
                input = new LzssStream (input);
            return new PrefixStream (header, input);
        }
    }
}
