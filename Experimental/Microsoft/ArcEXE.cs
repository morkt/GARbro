//! \file       ArcEXE.cs
//! \date       2023 Aug 24
//! \brief      Access portable executable (PE) resources.
//
// Copyright (C) 2023 by morkt
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
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;

namespace GameRes.Formats.Microsoft
{
    [Export(typeof(ArchiveFormat))]
    public class ExeOpener : ArchiveFormat
    {
        public override string         Tag { get => "EXE"; }
        public override string Description { get => "Windows executable resources"; }
        public override uint     Signature { get => 0; }
        public override bool  IsHierarchic { get => true; }
        public override bool      CanWrite { get => false; }

        public ExeOpener ()
        {
            Extensions = new[] { "exe",/* "dll"*/ };
        }

        static readonly Dictionary<string, string> RuntimeTypeMap = new Dictionary<string, string>() {
            { "#2",  "RT_BITMAP" },
            { "#10", "RT_RCDATA" },
        };

        static readonly Dictionary<string, string> ExtensionTypeMap = new Dictionary<string, string>() {
            { "PNG",  ".PNG" },
            { "WAVE", ".WAV" },
            { "MIDS", ".MID" },
            { "SCR",  ".BIN" },
            { "#2",   ".BMP" },
            { "#10",  ".BIN" },
        };

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "MZ") || VFS.IsVirtual)
                return null;
            var res = new ExeFile.ResourceAccessor (file.Name);
            try
            {
                var dir = new List<Entry>();
                foreach (var type in res.EnumTypes())
                {
                    string dir_name = type;
                    if (type.StartsWith ("#") && !RuntimeTypeMap.TryGetValue (type, out dir_name))
                        continue;
                    string ext;
                    if (!ExtensionTypeMap.TryGetValue (type, out ext))
                        ext = "";
                    foreach (var name in res.EnumNames (type))
                    {
                        string full_name = name;
                        if (name.StartsWith ("#"))
                            full_name = IdToString (name);
                        full_name = string.Join ("/", dir_name, full_name) + ext;
                        var entry = Create<ResourceEntry> (full_name);
                        entry.NativeName = name;
                        entry.NativeType = type;
                        entry.Offset = 0;
                        entry.Size = res.GetResourceSize (name, type);
                        dir.Add (entry);
                    }
                }
                if (0 == dir.Count)
                {
                    res.Dispose();
                    return null;
                }
                return new ResourcesArchive (file, this, dir, res);
            }
            catch
            {
                res.Dispose();
                throw;
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var rarc = (ResourcesArchive)arc;
            var rent = (ResourceEntry)entry;
            var data = rarc.Accessor.GetResource (rent.NativeName, rent.NativeType);
            if (null == data)
                return Stream.Null;
            return new BinMemoryStream (data, rent.Name);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var rent = (ResourceEntry)entry;
            if (rent.NativeType != "#2")
                return base.OpenImage (arc, entry);
            var rarc = (ResourcesArchive)arc;
            var bitmap = new byte[14 + entry.Size];
            int length = rarc.Accessor.ReadResource (rent.NativeName, rent.NativeType, bitmap, 14);

            length += 14;
            bitmap[0] = (byte)'B';
            bitmap[1] = (byte)'M';
            LittleEndian.Pack (length, bitmap, 2);
            int bits_length = bitmap.ToInt32 (0x22);
            int bits_pos = length - bits_length;
            if (bits_length == 0)
                bits_pos = bitmap.ToInt32 (14) + 22;
            LittleEndian.Pack (bits_pos, bitmap, 10);

            var bm = new BinMemoryStream (bitmap, 0, length, entry.Name);
            var info = ImageFormat.Bmp.ReadMetaData (bm);
            if (null == info)
            {
                bm.Dispose();
                throw new InvalidFormatException ("Invalid bitmap resource.");
            }
            bm.Position = 0;
            return new ImageFormatDecoder (bm, ImageFormat.Bmp, info);
        }

        internal static string IdToString (string id)
        {
            if (id.Length > 1 && id[0] == '#' && char.IsDigit (id[1]))
                id = id.Substring (1).PadLeft (5, '0');
            return id;
        }
    }

    internal class ResourceEntry : Entry
    {
        public string NativeName;
        public string NativeType;
    }

    internal class ResourcesArchive : ArcFile
    {
        public readonly ExeFile.ResourceAccessor Accessor;

        public ResourcesArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, ExeFile.ResourceAccessor acc)
            : base (arc, impl, dir)
        {
            Accessor = acc;
        }

        #region IDisposable Members
        bool _acc_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (_acc_disposed)
                return;
            if (disposing)
                Accessor.Dispose();
            _acc_disposed = true;
            base.Dispose (disposing);
        }
        #endregion
    }
}
