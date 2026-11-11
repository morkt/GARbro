//! \file       ArcCG3.cs
//! \date       2023 Oct 10
//! \brief      Software House Parsley CG archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [050610][Software House Parsley] Desert Time Mugen no Meikyuu PE

namespace GameRes.Formats.Parsley
{
    [Export(typeof(ArchiveFormat))]
    public class DesertCgOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CG/DESERT"; } }
        public override string Description { get { return "Software House Parsley CG archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public DesertCgOpener ()
        {
            Extensions = new string[] { "" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!VFS.IsPathEqualsToFileName (file.Name, "CG"))
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            uint index_pos = 4;
            var filename_table = LookupFileNameTable (file, count);
            Func<int, string> get_entry_name;
            if (filename_table != null)
                get_entry_name = n => filename_table[n];
            else
                get_entry_name = n => string.Format ("CG#{0:D4}");
            long last_offset = count * 4 + 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++ i)
            {
                uint offset = file.View.ReadUInt32 (index_pos);
                if (0 == offset)
                    break;
                if (offset <= last_offset || offset >= file.MaxOffset)
                    return null;
                var entry = new Entry {
                    Name = get_entry_name (i),
                    Type = "image",
                    Offset = offset,
                };
                dir.Add (entry);
                last_offset = offset;
                index_pos += 4;
            }
            if (0 == dir.Count)
                return null;
            last_offset = file.MaxOffset;
            for (int i = dir.Count-1; i >= 0; --i)
            {
                dir[i].Size = (uint)(last_offset - dir[i].Offset);
                last_offset = dir[i].Offset;
            }
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            try
            {
                return new DesertCgDecoder (input);
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        internal static Dictionary<string, uint> FileNameTableMap = new Dictionary<string, uint> {
            { @"..\DTime.exe", 0x49E348 },
        };

        List<string> LookupFileNameTable (ArcView file, int count)
        {
            try
            {
                var dir_name = Path.GetDirectoryName (file.Name);
                foreach (var source in FileNameTableMap.Keys)
                {
                    var src_name = Path.Combine (dir_name, source);
                    if (File.Exists (src_name))
                    {
                        using (var src = new ArcView (src_name))
                        {
                            var exe = new ExeFile (src);
                            long offset = exe.GetAddressOffset (FileNameTableMap[source]);
                            if (offset >= src.MaxOffset || offset + 0x104 * count > src.MaxOffset)
                                return null;
                            var dir = new List<string> (count);
                            for (int i = 0; i < count; ++i)
                            {
                                var name = src.View.ReadString (offset, 0x104);
                                dir.Add (name);
                                offset += 0x104;
                            }
                            return dir;
                        }
                    }
                }
            }
            catch { } // ignore errors
            return null;
        }
    }

    internal class DesertCgDecoder : BinaryImageDecoder
    {
        public DesertCgDecoder (IBinaryStream input) : base (input, ReadMetaData (input))
        {
        }

        static ImageMetaData ReadMetaData (IBinaryStream input)
        {
            return new ImageMetaData {
                Width  = input.ReadUInt32(),
                Height = input.ReadUInt32(),
                BPP = 8,
            };
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 8;
            var palette = ImageFormat.ReadPalette (m_input.AsStream, 0x100, PaletteFormat.RgbX);
            int stride = (Info.iWidth + 3) & ~3;
            var pixels = m_input.ReadBytes (stride * Info.iHeight);
            return ImageData.Create (Info, PixelFormats.Indexed8, palette, pixels, stride);
        }
    }
}


