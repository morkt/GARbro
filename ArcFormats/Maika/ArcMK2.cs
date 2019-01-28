//! \file       ArcMK2.cs
//! \date       Thu Aug 04 05:11:20 2016
//! \brief      MAIKA resource archives.
//
// Copyright (C) 2016-2018 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Maika
{
    [Serializable]
    internal class ScrambleScheme
    {
        public uint                 ScrambledSize;
        public Tuple<byte, byte>[]  ScrambleMap;
    }

    internal class MkArchive : ArcFile
    {
        public readonly ScrambleScheme  Scheme;

        public MkArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, ScrambleScheme scheme)
            : base (arc, impl, dir)
        {
            Scheme = scheme;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class Mk2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/MK2"; } }
        public override string Description { get { return "MAIKA resource archive"; } }
        public override uint     Signature { get { return 0x2E324B4D; } } // 'MK2.0'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Mk2Opener ()
        {
            // 'MK2.0' 'BL2.0'. 'SL1.0', 'LS2.0', 'AR2.0'
            Signatures = new uint[] {
                0x2E324B4D, 0x2E324C42, 0x2E314C53, 0x2E32534C, 0x2E325241
            };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "0\0"))
                return null;
            int count = file.View.ReadInt32 (0x12);
            if (!IsSaneCount (count))
                return null;

            string arc_id = file.View.ReadString (0, 5);
            uint base_offset  = file.View.ReadUInt16 (8);
            uint index_offset = file.View.ReadUInt32 (0xE);
            if (index_offset >= file.MaxOffset)
                return null;
            uint index_size   = file.View.ReadUInt32 (0xA);
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;

            uint current_offset = index_offset;
            var dir = new List<Entry> (count);
            for (int i = 0; i < 512; ++i)
            {
                uint entry_offset = index_offset + file.View.ReadUInt32 (current_offset);
                int n = file.View.ReadUInt16 (current_offset+4);
                if (n > 0)
                {
                    for (int j = 0; j < n; ++j)
                    {
                        uint offset = file.View.ReadUInt32 (entry_offset) + base_offset;
                        uint size   = file.View.ReadUInt32 (entry_offset+4);
                        uint name_length = file.View.ReadByte (entry_offset+8);
                        if (0 == name_length)
                            return null;
                        var name = file.View.ReadString (entry_offset+9, name_length);
                        entry_offset += 9 + name_length;

                        var entry = FormatCatalog.Instance.Create<Entry> (name);
                        entry.Offset = offset;
                        entry.Size   = size;
                        if (!entry.CheckPlacement (index_offset))
                            return null;
                        dir.Add (entry);
                    }
                }
                else if (-1 == file.View.ReadInt32 (entry_offset))
                    break;
                current_offset += 6;
            }
            if (0 == dir.Count)
                return null;
            ScrambleScheme scheme;
            if (!KnownSchemes.TryGetValue (arc_id, out scheme))
                scheme = DefaultScheme;
            return new MkArchive (file, this, dir, scheme);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            ushort signature = arc.File.View.ReadUInt16 (entry.Offset);
            // C1/D1/E1/F1
            if (0x3146 != signature && 0x3143 != signature && 0x3144 != signature && 0x3145 != signature)
                return base.OpenEntry (arc, entry);
            var mkarc = arc as MkArchive;
            ScrambleScheme scheme = mkarc != null ? mkarc.Scheme : DefaultScheme;

            uint packed_size = arc.File.View.ReadUInt32 (entry.Offset+2);
            if (packed_size < scheme.ScrambledSize || packed_size > entry.Size-10)
                return base.OpenEntry (arc, entry);

            Stream input;
            // XXX scrambling might be applicable for 'E1' signatures only
            if (scheme.ScrambledSize > 0)
            {
                var prefix = arc.File.View.ReadBytes (entry.Offset+10, scheme.ScrambledSize);
                foreach (var pair in scheme.ScrambleMap)
                {
                    byte t = prefix[pair.Item1];
                    prefix[pair.Item1] = prefix[pair.Item2];
                    prefix[pair.Item2] = t;
                }
                input = arc.File.CreateStream (entry.Offset+10+scheme.ScrambledSize, packed_size-scheme.ScrambledSize);
                input = new PrefixStream (prefix, input);
            }
            else
            {
                input = arc.File.CreateStream (entry.Offset+10, packed_size);
            }
            input = new LzssStream (input);

            var header = new byte[5];
            input.Read (header, 0, 5);
            if (Binary.AsciiEqual (header, "BPR02"))
                return new PackedStream<Bpr02Decompressor> (input);
            if (Binary.AsciiEqual (header, "BPR01"))
                return new PackedStream<Bpr01Decompressor> (input);
            return new PrefixStream (header, input);
        }

        static readonly ScrambleScheme DefaultScheme = new ScrambleScheme {
            ScrambledSize = 14,
            ScrambleMap = new Tuple<byte,byte>[] {
                new Tuple<byte, byte> (7, 11),
                new Tuple<byte, byte> (9, 12)
            }
        };

        Dictionary<string, ScrambleScheme> KnownSchemes = new Dictionary<string, ScrambleScheme> {
            { "AR2.0", new ScrambleScheme {
                ScrambledSize = 15,
                ScrambleMap = new Tuple<byte,byte>[] {
                    new Tuple<byte, byte> (7, 13),
                    new Tuple<byte, byte> (9, 14)
                }
            } }
        };
    }

    internal abstract class BprDecompressor : Decompressor
    {
        readonly byte   m_rle_code;
        IBinaryStream   m_input;

        protected BprDecompressor (byte rle_code)
        {
            m_rle_code = rle_code;
        }

        public override void Initialize (Stream input)
        {
            m_input = new BinaryStream (input, "");
        }

        protected override IEnumerator<int> Unpack ()
        {
            for (;;)
            {
                int ctl = m_input.ReadByte();
                if (-1 == ctl || 0xFF == ctl)
                    yield break;
                int count = m_input.ReadInt32();
                if (m_rle_code == ctl)
                {
                    byte b = m_input.ReadUInt8();
                    while (count --> 0)
                    {
                        m_buffer[m_pos++] = b;
                        if (0 == --m_length)
                            yield return m_pos;
                    }
                }
                else
                {
                    while (count > 0)
                    {
                        int chunk = Math.Min (count, m_length);
                        int read = m_input.Read (m_buffer, m_pos, chunk);
                        count -= chunk;
                        m_pos += chunk;
                        m_length -= chunk;
                        if (0 == m_length)
                            yield return m_pos;
                    }
                }
            }
        }

        bool m_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                if (m_input != null)
                    m_input.Dispose();
                m_disposed = true;
            }
        }
    }

    internal class Bpr02Decompressor : BprDecompressor
    {
        public Bpr02Decompressor () : base (3) { }
    }

    internal class Bpr01Decompressor : BprDecompressor
    {
        public Bpr01Decompressor () : base (1) { }
    }
}
