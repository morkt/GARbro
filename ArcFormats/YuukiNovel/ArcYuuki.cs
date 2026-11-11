//! \file       ArcYuuki.cs
//! \date       2026-09-17
//! \brief      Yuuki! Novel embedded resource archive.
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

namespace GameRes.Formats.YuukiNovel
{
    [Export(typeof(ArchiveFormat))]
    public class YuukiOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PackageFile2"; } }
        public override string Description { get { return "Yuuki! Novel resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public YuukiOpener ()
        {
            Extensions = new[] { "exe" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (0x5A4D != file.View.ReadUInt16 (0))
                return null;
            var exe = new ExeFile (file);
            uint base_offset = (uint)exe.Overlay.Offset;
            if (!file.View.AsciiEqual (base_offset, "\x0cPackageFile2")) // 0xC is length
                return null;

            int count = file.View.ReadUInt16 (base_offset + 0xD);
            if (!IsSaneCount (count))
                return null;
 
            uint index_offset = base_offset + 0xF;
            uint data_offset = index_offset + (uint)count * 0x108;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; i++)
            {
                uint name_length = file.View.ReadByte (index_offset);
                var name = file.View.ReadString (index_offset + 1, name_length);
                if (string.IsNullOrEmpty (name))
                    return null;
                var entry = Create<Entry> (name);
                index_offset += 0x100;
                entry.Offset = file.View.ReadUInt32 (index_offset) + data_offset;
                entry.Size = file.View.ReadUInt32 (index_offset + 4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (entry.Name != "autorun.yne")
                return input;
            foreach (var scheme in KnownSchemes)
            {
                using (var stream = scheme.CreateStream (input))
                {
                    var sign = new byte[20];
                    stream.Read (sign, 0, sign.Length);
                    input.Position = 0;
                    if (sign.AsciiEqual (1, "YuukiNovel WorkFile"))
                        return scheme.CreateStream (input);
                }
            }
            return input;
        }

        static readonly IYuukiScheme[] KnownSchemes = { new YuukiSchemeV1(), new YuukiSchemeV2() };
    }

    internal interface IYuukiScheme
    {
        Stream CreateStream (ArcViewStream input);
    }

    internal class YuukiSchemeV1 : IYuukiScheme
    {
        public Stream CreateStream (ArcViewStream input)
        {
            var key = new byte[] { 0x1F, 0x53, 0x58, 0x60, 0x86, 0x27, 0x11, 0xD5, 0x8B, 0xCA, 0x00, 0x33, 0x54, 0xC1, 0x08, 0x01 };
            return new ByteStringEncryptedStream (input, key, true);
        }
    }

    internal class YuukiSchemeV2 : IYuukiScheme
    {
        public Stream CreateStream (ArcViewStream input)
        {
            uint seed = input.ReadUInt32();
            return new EncryptedStream (input, seed, true);
        }
    }

    internal class EncryptedStream : InputProxyStream
    {
        uint m_seed;

        public EncryptedStream (Stream input, uint seed, bool leave_open = false)
            : base (input, leave_open)
        {
            m_seed = seed;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = BaseStream.Read (buffer, offset, count);
            for (int i = 0; i < read; ++i)
            {
                buffer[offset+i] ^= Rand();
            }
            return read;
        }

        public override int ReadByte ()
        {
            int b = BaseStream.ReadByte();
            if (-1 != b)
                b ^= Rand();
            return b;
        }

        byte Rand ()
        {
            m_seed = m_seed * 0x6C078965 + 1;
            return (byte)(m_seed >> 0x18);
        }
    }
}
