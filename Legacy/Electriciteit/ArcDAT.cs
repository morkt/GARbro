//! \file       ArcDAT.cs
//! \date       2018 Jun 08
//! \brief      Electriciteit resource archive.
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

// [010511][e-Erekiteru] Silence ~Seinaru Yoru no Kane no Naka de...~

namespace GameRes.Formats.Electriciteit
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/electr"; } }
        public override string Description { get { return "Electriciteit resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".dat"))
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            bool is_bitmap = Path.GetFileNameWithoutExtension (file.Name).StartsWith ("b");
            uint index_offset = 4;
            uint first_offset = index_offset + (uint)count * 0x2C;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x10);
                if (!IsValidEntryName (name))
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x24);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x28);
                if (entry.Offset < first_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (is_bitmap)
                    entry.Type = "image";
                dir.Add (entry);
                index_offset += 0x2C;
            }
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            if (arc.File.View.ReadUInt16 (entry.Offset) != 0xB2BD) // ~'BM'
                return base.OpenImage (arc, entry);
            Stream input = arc.File.CreateStream (entry.Offset+2, entry.Size-2);
            input = new PrefixStream (BitmapHeader, input);
            var bitmap = new BinaryStream (input, entry.Name);
            try
            {
                return new ImageFormatDecoder (bitmap);
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        static readonly byte[] BitmapHeader = new byte[] { (byte)'B', (byte)'M' };
    }
}
