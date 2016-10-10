//! \file       ArcABMP.cs
//! \date       Wed Oct 28 02:51:46 2015
//! \brief      QLIE mulit-frame images.
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

using GameRes.Utility;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GameRes.Formats.Qlie
{
    [Export(typeof(ArchiveFormat))]
    public class AbmpOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ABMP/QLIE"; } }
        public override string Description { get { return "QLIE engine multi-frame archive"; } }
        public override uint     Signature { get { return 0x706D6261; } } // 'abmp'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public AbmpOpener ()
        {
            Extensions = new string[] { "b" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadByte (4) * 10 + file.View.ReadByte (5) - '0' * 11;
            if (file.View.ReadByte (6) != 0 || version < 10 || version > 12)
                return null;
            string base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint offset = 0x10;
            var dir = new List<Entry>();
            int n = 0;
            var type_buf = new byte[0x10];
            while (offset < file.MaxOffset)
            {
                if (0x10 != file.View.Read (offset, type_buf, 0, 0x10))
                    break;
                offset += 0x10;
                if (Binary.AsciiEqual (type_buf, "abdata"))
                {
                    var entry = new Entry {
                        Name = string.Format ("{0}#{1}.dat", base_name, n++),
                        Offset = offset + 4,
                        Size = file.View.ReadUInt32 (offset),
                    };
                    dir.Add (entry);
                    offset += 4 + entry.Size;
                }
                else if (Binary.AsciiEqual (type_buf, "abimage10\0")
                         || Binary.AsciiEqual (type_buf, "absound10\0"))
                {
                    int count = file.View.ReadByte (offset++);
                    for (int i = 0; i < count && offset < file.MaxOffset; ++i)
                    {
                        file.View.Read (offset, type_buf, 0, 0x10);
                        var tag = Binary.GetCString (type_buf, 0, 0x10, Encoding.ASCII);
                        uint name_length = file.View.ReadUInt16 (offset+0x10);
                        var name = file.View.ReadString (offset+0x12, name_length);
                        offset += 0x12 + name_length;

                        if (tag != "abimgdat10" && tag != "absnddat10")
                        {
                            offset += 2u + file.View.ReadUInt16 (offset);
                            if ("abimgdat13" == tag)
                                offset += 0x0C;
                            else if ("abimgdat14" == tag)
                                offset += 0x4C;
                        }
                        ++offset;
                        var size = file.View.ReadUInt32 (offset);
                        offset += 4;
                        if (0 != size)
                        {
                            if (string.IsNullOrEmpty (name))
                                name = string.Format ("{0}#{1}", base_name, n++);
                            else
                                name = s_InvalidChars.Replace (name, "_");
                            var entry = new Entry {
                                Name = name,
                                Type = tag.StartsWith ("abimg") ? "image" : tag.StartsWith ("absnd") ? "audio" : "",
                                Offset = offset,
                                Size = size,
                            };
                            if (entry.CheckPlacement (file.MaxOffset))
                            {
                                DetectFileType (file, entry);
                                dir.Add (entry);
                            }
                        }
                        offset += size;
                    }
                }
                else
                {
                    var entry = new Entry {
                        Name = string.Format ("{0}#{1}#{2}", base_name, n++, GetTypeName (type_buf)),
                        Offset = offset,
                        Size = (uint)(file.MaxOffset - offset),
                    };
                    dir.Add (entry);
                    offset += entry.Size;
                }
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (0xFF435031 != arc.File.View.ReadUInt32 (entry.Offset))
                return base.OpenEntry (arc, entry);
            var packed = new byte[entry.Size];
            arc.File.View.Read (entry.Offset, packed, 0, entry.Size);
            var unpacked = PackOpener.Decompress (packed);
            if (null == unpacked)
                return new MemoryStream (packed);
            return new MemoryStream (unpacked);
        }

        static protected void DetectFileType (ArcView file, Entry entry)
        {
            uint signature = file.View.ReadUInt32 (entry.Offset);
            if (signature != 0)
            {
                string ext = null;
                if (0x4D42 == (signature & 0xFFFF))
                {
                    ext = "bmp";
                }
                else
                {
                    var res = FormatCatalog.Instance.LookupSignature (signature).FirstOrDefault();
                    if (null != res)
                        ext = res.Extensions.FirstOrDefault();
                }
                if (!string.IsNullOrEmpty (ext))
                    entry.Name = Path.ChangeExtension (entry.Name, ext);
            }
        }

        static string GetTypeName (byte[] type_buf)
        {
            int n = 0;
            while (n < type_buf.Length)
            {
                if (0 == type_buf[n])
                    break;
                if (type_buf[n] < 0x20 || type_buf[n] > 0x7E)
                    return "unknown";
            }
            if (0 == n)
                return "";
            return Encoding.ASCII.GetString (type_buf, 0, n).Trim();
        }

        static readonly Regex s_InvalidChars = new Regex (@"[:/\\*?]");
    }

    [Export(typeof(ArchiveFormat))]
    public class Abmp7Opener : AbmpOpener
    {
        public override string         Tag { get { return "ABMP7"; } }
        public override string Description { get { return "QLIE engine multi-frame image archive"; } }
        public override uint     Signature { get { return 0x504D4241; } } // 'ABMP'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadUInt16 (4) != '7')
                return null;
            string base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint offset = 0xC;
            var dir = new List<Entry>();
            uint size = file.View.ReadUInt32 (offset);
            offset += 4;
            var entry = new Entry {
                Name = string.Format ("{0}#0.dat", base_name),
                Offset = offset,
                Size = size,
            };
            if (!entry.CheckPlacement (file.MaxOffset))
                return null;
            dir.Add (entry);
            offset += size;
            int n = 1;
            while (offset < file.MaxOffset)
            {
                size = file.View.ReadUInt32 (offset);
                if (0 == size)
                    break;
                offset += 4;
                entry = new Entry {
                    Name = string.Format ("{0}#{1}", base_name, n++),
                    Type = "image",
                    Offset = offset,
                    Size = size,
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    break;
                DetectFileType (file, entry);
                dir.Add (entry);
                offset += size;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
