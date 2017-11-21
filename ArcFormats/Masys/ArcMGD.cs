//! \file       ArcMGD.cs
//! \date       Sun May 10 18:11:01 2015
//! \brief      MEGU archives implementation.
//
// Copyright (C) 2015 by morkt
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

// MEGU
// Masys Enhanced Game Unit

namespace GameRes.Formats.Megu
{
    [Export(typeof(ArchiveFormat))]
    public class MgdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MGD"; } }
        public override string Description { get { return "Masys resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        internal static readonly string Key = "Powerd by Masys";

        public override ArcFile TryOpen (ArcView file)
        {
            uint signature = file.View.ReadUInt32 (0);
            if (0x44474d != (signature & 0xffffff)) // 'MGD'
                return null;
            int count = file.View.ReadInt16 (0x20);
            if (count <= 0)
                return null;
            int flag = file.View.ReadUInt16 (3);
            var dir = new List<Entry> (count);
            int index_offset = 0x22;
            byte[] name_buf = new byte[16];
            for (uint i = 0; i < count; ++i)
            {
                int name_size = file.View.ReadByte (index_offset+1);
                if (0 == name_size)
                    return null;
                if (name_size > name_buf.Length)
                    Array.Resize (ref name_buf, name_size);
                file.View.Read (index_offset+2, name_buf, 0, (uint)name_size);
                if (100 == flag)
                    Decrypt (name_buf, 0, name_size);
                string name = Encodings.cp932.GetString (name_buf, 0, name_size);
                index_offset += 2 + name_size;

                uint offset = file.View.ReadUInt32 (index_offset+4);
                var entry = AutoEntry.Create (file, offset, name);
                entry.Size = file.View.ReadUInt32 (index_offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }

        internal static void Decrypt (byte[] buffer, int offset, int length)
        {
            for (int i = 0; i < length; ++i)
            {
                buffer[offset+i] ^= (byte)Key[i%0xf];
            }
        }
    }
}
