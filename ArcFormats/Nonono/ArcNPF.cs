//! \file       ArcNPF.cs
//! \date       2019 Feb 02
//! \brief      NGS engine resource archive.
//
// Copyright (C) 2019 by morkt
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

namespace GameRes.Formats.Nonono
{
    internal class NpfEntry : Entry
    {
        public int  Seed;
    }

    [Export(typeof(ArchiveFormat))]
    public class NpfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "NPF"; } }
        public override string Description { get { return "NGS engine resource archive"; } }
        public override uint     Signature { get { return 0x4B434150; } } // 'PACK'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        const int DefaultSeed = 0x46415420; // 'FAT '

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadInt32 (4) != 4 || file.View.ReadInt32 (8) != 1)
                return null;
            var header = file.View.ReadBytes (12, 20);
            var rnd = new RandomGenerator (DefaultSeed);
            Decrypt (header, 0, header.Length, rnd);
            if (!header.AsciiEqual ("FAT "))
                return null;
            int count = header.ToInt32 (8);
            if (!IsSaneCount (count))
                return null;
            rnd.SRand (count);
            var index = file.View.ReadBytes (0x20, 20 * (uint)count);
            Decrypt (index, 0, index.Length, rnd);
            int pos = 0;
            int name_pos = 0x20 + 20 * count;
            var dir = new List<Entry> (count);
            var name_buffer = new byte[0x100];
            for (int i = 0; i < count; ++i)
            {
                int name_length = index.ToInt32 (pos+12);
                if (name_length <= 0 || name_length > name_buffer.Length)
                    return null;
                file.View.Read (name_pos, name_buffer, 0, (uint)name_length);
                int seed = index.ToInt32 (pos+8);
                rnd.SRand (seed);
                Decrypt (name_buffer, 0, name_length, rnd);
                var name = Encodings.cp932.GetString (name_buffer, 0, name_length);
                var entry = Create<NpfEntry> (name);
                entry.Offset = index.ToUInt32 (pos+4);
                entry.Size   = index.ToUInt32 (pos+16);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.Seed = seed;
                dir.Add (entry);
                pos += 20;
                name_pos += name_length;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var nent = entry as NpfEntry;
            if (null == nent)
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            var rnd = new RandomGenerator (nent.Seed);
            Decrypt (data, 0, data.Length, rnd);
            return new BinMemoryStream (data, entry.Name);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.OpenBinaryEntry (entry);
            if (input.Length <= 8 || input.Signature != 0x58474D49) // 'IMGX'
                return ImageFormatDecoder.Create (input);
            return new ImgXDecoder (input);
        }

        internal void Decrypt (byte[] data, int pos, int count, RandomGenerator rnd)
        {
            for (int i = 0; i < count; ++i)
                data[pos + i] ^= (byte)rnd.Rand();
        }
    }

    internal class RandomGenerator
    {
        int      m_seed;

        const int DefaultSeed = 0x67895;

        public RandomGenerator (int seed = DefaultSeed)
        {
            SRand (seed);
        }

        public void SRand (int seed)
        {
            m_seed = seed;
            for (int i = 0; i < 32; ++i)
            {
                Rand();
            }
        }

        public int Rand ()
        {
            m_seed ^= 0x65AC9365;
            m_seed ^= (((m_seed >> 1) ^ m_seed) >> 3)
                    ^ (((m_seed << 1) ^ m_seed) << 3);
            return m_seed;
        }
    }
}
