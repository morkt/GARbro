//! \file       ArcAzurite.cs
//! \date       Fri Dec 30 04:09:23 2016
//! \brief      Silky's resource archive.
//
// Copyright (C) 2016 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Silky
{
    [Export(typeof(ArchiveFormat))]
    public class SilkyArcOpener : Ai6Opener
    {
        public override string         Tag { get { return "ARC/AZURITE"; } }
        public override string Description { get { return "Silky's resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".arc"))
                return null;
            uint index_size = file.View.ReadUInt32 (0);
            if (index_size < 10 || index_size >= file.MaxOffset-4)
                return null;
            if (0 == file.View.ReadByte (4))
                return null;

            var dir = new List<Entry>();
            using (var index = file.CreateStream (4, index_size))
            {
                var name_buffer = new byte[0x100];
                while (index.PeekByte() != -1)
                {
                    int name_length = index.ReadByte();
                    if (0 == name_length)
                        return null;
                    if (name_length != index.Read (name_buffer, 0, name_length))
                        return null;
                    DecryptName (name_buffer, name_length);
                    var name = Encodings.cp932.GetString (name_buffer, 0, name_length);
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                    entry.Size          = Binary.BigEndian (index.ReadUInt32());
                    entry.UnpackedSize  = Binary.BigEndian (index.ReadUInt32());
                    entry.Offset        = Binary.BigEndian (index.ReadUInt32());
                    if (entry.Offset < index_size+4 || !entry.CheckPlacement (file.MaxOffset))
                        return null;
                    entry.IsPacked = entry.Size != entry.UnpackedSize;
                    dir.Add (entry);
                }
            }
            return new ArcFile (file, this, dir);
        }

        static void DecryptName (byte[] buffer, int length)
        {
            byte key = (byte)length;
            for (int i = 0; i < length; ++i)
                buffer[i] += key--;
        }
    }
}
