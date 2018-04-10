//! \file       ArcPAK.cs
//! \date       2018 Apr 10
//! \brief      AbogadoPowers resource archive.
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

namespace GameRes.Formats.Abogado
{
    internal class Fs8Archive : ArcFile
    {
        public Fs8Archive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir)
            : base (arc, impl, dir)
        {
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/ABOGADO"; } }
        public override string Description { get { return "AbogadoPowers resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt16 (0);
            if (!IsSaneCount (count))
                return null;
            int encryption = file.View.ReadInt16 (2);
            if (encryption < 0 || encryption > 1)
                return null;

            uint index_offset = 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x40);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x40);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x44);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x48;
            }
            if (encryption != 0)
                return new Fs8Archive (file, this, dir);
            else
                return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (!(arc is Fs8Archive))
                return input;
            var enc_offset = entry.Offset + entry.Size;
            int key = arc.File.View.ReadInt32 (enc_offset);
            if (key < 0 || key >= DefaultKeys.Value.Length)
                return input;
            using (input)
            {
                var data = input.ReadBytes ((int)entry.Size);
                FsDecrypt (data, DefaultKeys.Value[key]);
                return new BinMemoryStream (data, entry.Name);
            }
        }

        void FsDecrypt (byte[] data, byte[] key)
        {
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] = key[data[i]];
            }
        }

        static readonly Lazy<byte[][]> DefaultKeys = new Lazy<byte[][]> (LoadKeys);

        static byte[][] LoadKeys ()
        {
            using (var input = EmbeddedResource.Open ("keytable.dat", typeof (PakOpener)))
            {
                if (null == input)
                    return Array.Empty<byte[]>();
                var keys = new List<byte[]> (128);
                for (int i = 0; i < 128; ++i)
                {
                    var k = new byte[256];
                    if (input.Read (k, 0, 256) != 256)
                        break;
                    keys.Add (k);
                }
                return keys.ToArray();
            }
        }
    }
}
