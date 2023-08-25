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

    internal class NpfArchive : ArcFile
    {
        public readonly IRandomGenerator KeyGenerator;

        public NpfArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, IRandomGenerator rnd)
            : base (arc, impl, dir)
        {
            KeyGenerator = rnd;
        }
    }

    internal interface IRandomGenerator
    {
        void SRand (int seed);

        int Rand ();
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

            foreach (var rnd in GetGenerators())
            {
                var dir = ReadIndex (file, rnd);
                if (dir != null)
                {
                    return new NpfArchive (file, this, dir, rnd);
                }
            }
            return null;
        }

        internal IEnumerable<IRandomGenerator> GetGenerators ()
        {
            yield return new RandomGenerator1 (DefaultSeed);
            yield return new RandomGenerator2 (DefaultSeed);
        }

        List<Entry> ReadIndex (ArcView file, IRandomGenerator rnd)
        {
            var header = file.View.ReadBytes (12, 20);
//            rnd.SRand (DefaultSeed); // generator already seeded by GetGenerators()
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
            return dir;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var narc = (NpfArchive)arc;
            var nent = (NpfEntry)entry;
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            var rnd = narc.KeyGenerator;
            rnd.SRand (nent.Seed);
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

        internal void Decrypt (byte[] data, int pos, int count, IRandomGenerator rnd)
        {
            for (int i = 0; i < count; ++i)
                data[pos + i] ^= (byte)rnd.Rand();
        }
    }

    internal class RandomGenerator1 : IRandomGenerator
    {
        int      m_seed;

        const int DefaultSeed = 0x67895;

        public RandomGenerator1 (int seed = DefaultSeed)
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

    internal class RandomGenerator2 : IRandomGenerator
    {
        int     m_seed1;
        int     m_seed2;

        const int DefaultSeed = 0x67895;

        public RandomGenerator2 (int seed = DefaultSeed)
        {
            SRand (seed);
        }

        public void SRand (int seed)
        {
            m_seed1 = seed;
            m_seed2 = ((seed >> 12) ^ (seed << 18)) - 0x579E2B8D;
        }

        public int Rand ()
        {
            int n = m_seed2 + ((m_seed1 >> 10) ^ (m_seed1 << 14));
            m_seed2 = n - 0x15633649 + ((m_seed2 >> 12) ^ (m_seed2 << 18));
            return m_seed2;
        }
    }
}
