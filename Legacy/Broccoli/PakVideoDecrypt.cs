//! \file       PakVideoDecrypt.cs
//! \date       2026-09-30
//! \brief      Ikusabune encrypted MPEG video (gadat*.pak).
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
// The first 0x10000 bytes of gadat*.pak are XOR-encrypted with a keystream seeded
// from the file name, so the generic MPEG opener cannot read them.  Only those
// first bytes are encrypted; the remainder is a plain MPEG stream.
//

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text.RegularExpressions;

namespace GameRes.Formats.Broccoli
{
    internal sealed class PakVideoDecryptArchive : ArcFile
    {
        public PakVideoDecryptArchive (ArcView file, ArchiveFormat impl, Entry entry)
            : base (file, impl, new List<Entry> { entry })
        {
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PakVideoDecryptOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MPG/BROCCOLI"; } }
        public override string Description { get { return "Ikusabune encrypted MPEG video"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        // Only gadat1xx.pak (100-199) are encrypted; other gadat/data files are not.
        static readonly Regex VideoName = new Regex (@"^gadat1\d\d\.pak$", RegexOptions.IgnoreCase);

        public PakVideoDecryptOpener ()
        {
            Extensions = new string[] { "pak" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var name = Path.GetFileName (file.Name);
            if (!VideoName.IsMatch (name))
                return null;
            var entry = new Entry {
                Name = Path.GetFileNameWithoutExtension (name) + ".mpg",
                Offset = 0,
                Size = (uint)file.MaxOffset,
            };
            return new PakVideoDecryptArchive (file, this, entry);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            int head_size = (int)Math.Min (arc.File.MaxOffset, 0x10000);
            var head = arc.File.View.ReadBytes (0, (uint)head_size);
            if (head.Length != head_size)
                throw new EndOfStreamException ();
            var cipher = new PakVideoCipher (PakVideoCipher.MakeFileId (Path.GetFileName (arc.File.Name)));
            cipher.Decrypt (head);
            Stream tail = arc.File.MaxOffset > head_size ? arc.File.CreateStream (head_size) : Stream.Null;
            return new PrefixStream (head, tail);
        }
    }

    /// <summary>
    /// Keystream generator matching the original game: a Mersenne Twister seeded
    /// from the file name, stepped through a linear congruential generator.
    /// </summary>
    internal sealed class PakVideoCipher
    {
        const int  StateLength = 624;
        const int  StateM      = 397;
        const uint MatrixA     = 0x9908B0DF;
        const uint SignMask    = 0x80000000;
        const uint LowerMask   = 0x7FFFFFFF;

        readonly uint[] m_mt = new uint[StateLength];
        int m_index;

        public PakVideoCipher (uint seed)
        {
            unchecked
            {
                m_mt[0] = 1103515245u * seed + 12345u;
                for (int i = 1; i < StateLength; ++i)
                    m_mt[i] = 1812433253u * (m_mt[i-1] ^ (m_mt[i-1] >> 30)) + (uint)i;
            }
            m_index = 0;
        }

        uint NextWord ()
        {
            if (m_index >= StateLength)
            {
                int kk;
                for (kk = 0; kk < StateLength - StateM; ++kk)
                {
                    uint y = (m_mt[kk] & SignMask) | (m_mt[kk+1] & LowerMask);
                    m_mt[kk] = m_mt[kk+StateM] ^ (y >> 1) ^ (0 != (y & 1) ? MatrixA : 0);
                }
                for (; kk < StateLength - 1; ++kk)
                {
                    uint y = (m_mt[kk] & SignMask) | (m_mt[kk+1] & LowerMask);
                    m_mt[kk] = m_mt[kk+StateM-StateLength] ^ (y >> 1) ^ (0 != (y & 1) ? MatrixA : 0);
                }
                uint z = (m_mt[StateLength-1] & SignMask) | (m_mt[0] & LowerMask);
                m_mt[StateLength-1] = m_mt[StateM-1] ^ (z >> 1) ^ (0 != (z & 1) ? MatrixA : 0);
                m_index = 0;
            }
            uint v = m_mt[m_index++];
            v ^= v >> 11;
            v ^= (v << 7)  & 0x9D2C5680;
            v ^= (v << 15) & 0xEFC60000;
            return v ^ (v >> 18);
        }

        /// <summary>
        /// Decrypt the first min(length, 0x10000) bytes in place, four bytes at a time.
        /// </summary>
        public void Decrypt (byte[] data)
        {
            int limit = Math.Min (data.Length, 0x10000) & ~3;
            if (0 == limit)
                return;
            unchecked
            {
                uint v = NextWord ();
                for (int i = 0; i < limit; i += 4)
                {
                    v = 0 != (v & 1) ? 1103515245u * v + 12345u : NextWord ();
                    data[i  ] ^= (byte)v;
                    data[i+1] ^= (byte)(v >> 8);
                    data[i+2] ^= (byte)(v >> 16);
                    data[i+3] ^= (byte)(v >> 24);
                }
            }
        }

        public static uint MakeFileId (string name)
        {
            unchecked
            {
                uint v2 = 0, v3 = 0;
                foreach (var ch in name.ToLowerInvariant ())
                {
                    uint v = ch;
                    v3 += v;
                    v2 = v + (v2 << 8);
                    if (0 != (v2 & 0xFF800000))
                        v2 %= 0xFFF9D7;
                }
                return v2 | (v3 << 24);
            }
        }
    }
}
