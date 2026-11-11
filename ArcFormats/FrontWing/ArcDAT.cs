//! \file       ArcDAT.cs
//! \date       2025-12-13
//! \brief      FrontWing resource archive.
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
using GameRes.Utility;

namespace GameRes.Formats.FrontWing
{
    internal class TimeLeapArchive : ArcFile
    {
        private bool m_encrypted;

        public bool IsEncrypted { get { return m_encrypted; } }

        public TimeLeapArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, bool encrypted)
            : base (arc, impl, dir)
        {
            m_encrypted = encrypted;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/TIMELEAP"; } }
        public override string Description { get { return "'Time Leap' resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset < 4)
                return null;
            int count = file.View.ReadInt32 (file.MaxOffset - 4);
            if (!IsSaneCount (count))
                return null;

            int index_size = 0x50 * count;
            if (file.MaxOffset < index_size + 4)
                return null;
            var index = file.View.ReadBytes (file.MaxOffset - 4 - index_size, (uint)index_size);
            bool encrypted = !index.IsAsciiVisible (0, 0x40, true);
            if (encrypted)
                NibbleSwap (index);

            int index_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; i++)
            {
                var name = Binary.GetCString (index, index_offset, 0x40);
                var entry = Create<Entry> (name);
                entry.Offset = LittleEndian.ToUInt32 (index, index_offset + 0x40);
                entry.Size   = LittleEndian.ToUInt32 (index, index_offset + 0x48);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                index_offset += 0x50;
                dir.Add (entry);
            }
            return new TimeLeapArchive (file, this, dir, encrypted);
        }

        static readonly byte[] keyTable = {
            0xff, 0xff, 0xff, 0x01,
            0x9c, 0xaa, 0xa5, 0x00,
            0x30, 0xff, 0x77, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        };

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var tarc = arc as TimeLeapArchive;
            var data = tarc.File.View.ReadBytes (entry.Offset, entry.Size);
            if (tarc.IsEncrypted)
            {
                for (int i = 1; i < data.Length; i += 4)
                {
                    data[i] = (byte)(-data[i] & 0xff);
                }
                for (int i = 0; i < data.Length; i += 3)
                {
                    data[i] ^= keyTable[i / 5 % 5 + i % 6];
                }
                NibbleSwap (data, 2, 6);
            }
            return new BinMemoryStream (data);
        }

        void NibbleSwap (byte[] buffer, int start = 0, int step = 1)
        {
            for (int i = start; i < buffer.Length; i += step)
            {
                buffer[i] = (byte)(((buffer[i] & 0xf0) >> 4) + ((buffer[i] & 0xf) << 4));
            }
        }
    }
}
