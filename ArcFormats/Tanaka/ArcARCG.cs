//! \file       ArcARCG.cs
//! \date       Wed Feb 01 06:41:10 2017
//! \brief      Archive format by Tanaka Tatsuhiro.
//
// Copyright (C) 2017 by morkt
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
using System.Linq;

namespace GameRes.Formats.Will
{
    [Export(typeof(ArchiveFormat))]
    public class ArcGOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARCG"; } }
        public override string Description { get { return "Tanaka Tatsuhiro's engine resource archive"; } }
        public override uint     Signature { get { return 0x47435241; } } // 'ARCG'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public ArcGOpener ()
        {
            Extensions = new string[] { "arc", "bmx", "scb", "vpk" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (0x10000 != file.View.ReadUInt32 (4))
                return null;
            uint index_offset = file.View.ReadUInt32 (8);
            uint index_size   = file.View.ReadUInt32 (0xC);
            int dir_count = file.View.ReadUInt16 (0x10);
            int count = file.View.ReadInt32 (0x12);
            if (!IsSaneCount (count) || index_offset >= file.MaxOffset
                || index_size > file.View.Reserve (index_offset, index_size))
                return null;

            var dir = new List<Entry> (count);
            for (int j = 0; j < dir_count; ++j)
            {
                uint name_length = file.View.ReadByte (index_offset);
                var dir_name = file.View.ReadString (index_offset+1, name_length-1);
                index_offset += name_length; 
                uint dir_offset = file.View.ReadUInt32 (index_offset);
                int file_count = file.View.ReadInt32 (index_offset+4);
                if (dir_offset >= file.MaxOffset || file_count < 0 || file_count > count)
                    return null;
                index_offset += 8;
                for (int i = 0; i < file_count; ++i)
                {
                    name_length = file.View.ReadByte (dir_offset);
                    if (0 == name_length)
                        return null;
                    var file_name = file.View.ReadString (dir_offset+1, name_length-1);
                    file_name = file_name.Replace ('?', '？');
                    dir_offset += name_length;
                    file_name = Path.Combine (dir_name, file_name);
                    var entry = FormatCatalog.Instance.Create<Entry> (file_name);
                    entry.Offset = file.View.ReadUInt32 (dir_offset);
                    entry.Size   = file.View.ReadUInt32 (dir_offset+4);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir_offset += 8;
                    dir.Add (entry);
                }
            }
            foreach (var entry in dir.Where (e => string.IsNullOrEmpty (e.Type)))
            {
                uint signature = file.View.ReadUInt32 (entry.Offset);
                IResource res;
                if ((signature & 0xFFFF) == 0x4342) // 'BC'
                    res = BcFormat.Value;
                else
                    res = AutoEntry.DetectFileType (signature);
                if (res != null)
                    entry.Type = res.Type;
            }
            return new ArcFile (file, this, dir);
        }

        internal static Lazy<ImageFormat> BcFormat = new Lazy<ImageFormat> (() => ImageFormat.FindByTag ("BC"));
    }
}
