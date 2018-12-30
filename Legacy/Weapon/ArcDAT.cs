//! \file       ArcDAT.cs
//! \date       2018 Jan 04
//! \brief      Weapon event CG archives.
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
using System.Drawing;
using System.IO;
using System.Windows.Media;

// [000331][WEAPON] Seido Techou

namespace GameRes.Formats.Weapon
{
    internal class CgEntry : Entry
    {
        public uint     Width;
        public uint     Height;
    }

#if DEBUG
    [Export(typeof(ArchiveFormat))]
#endif
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/WEAPON"; } }
        public override string Description { get { return "Weapon resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        const uint DefaultWidth = 800;

        public override ArcFile TryOpen (ArcView file)
        {
            var arc_name = Path.GetFileName (file.Name);
            Size[] dim_table;
            if (!KnownFileTables.TryGetValue (arc_name, out dim_table))
                return null;
            uint offset = 0;
            var base_name = Path.GetFileNameWithoutExtension (arc_name);
            var dir = new List<Entry> (dim_table.Length);
            for (int i = 0; i < dim_table.Length; ++i)
            {
                var name = string.Format ("{0}#{1:D4}", base_name, i);
                uint width = (uint)dim_table[i].Width;
                uint height = (uint)dim_table[i].Height;
                var entry = new CgEntry {
                    Name = name,
                    Type = "image",
                    Offset = offset,
                    Size = height * width * 2,
                    Width = width,
                    Height = height,
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                offset += entry.Size;
            }
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var cgent = (CgEntry)entry;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var info = new ImageMetaData { Width = cgent.Width, Height = cgent.Height, BPP = 16 };
            return new CgDecoder (input, info);
        }

        static readonly Dictionary<string, Size[]> KnownFileTables = new Dictionary<string, Size[]> (StringComparer.OrdinalIgnoreCase) {
            { "eventcg.dat",
                new Size[] {
                    new Size (800, 600), new Size (800, 600), new Size (800, 600), new Size (800, 600),
                    new Size (800, 600), new Size (800, 1200), new Size (800, 600), new Size (800, 600),
                    new Size (800, 600), new Size (800, 600), new Size (800, 600), new Size (800, 600),
                    new Size (800, 1200), new Size (800, 600), new Size (800, 600), new Size (800, 600),
                    new Size (800, 1200), new Size (800, 600), new Size (800, 600), new Size (800, 600),
                    new Size (800, 600), new Size (800, 600), new Size (800, 600), new Size (800, 600),
                    new Size (800, 600), new Size (800, 600), new Size (800, 600), new Size (800, 600),
                    new Size (800, 600), new Size (800, 600), new Size (800, 600), new Size (800, 600),
                    new Size (800, 600), new Size (800, 600), new Size (800, 600), new Size (800, 600),
                    new Size (800, 600), new Size (800, 600), new Size (800, 600), new Size (800, 600),
                    new Size (800, 600), new Size (800, 600), new Size (800, 600), new Size (800, 600),
                    new Size (800, 600), new Size (800, 600), new Size (800, 600), new Size (800, 600),
                    new Size (800, 600), new Size (800, 600), new Size (800, 600), new Size (800, 600),
                    new Size (800, 600), new Size (800, 600), new Size (800, 600), new Size (800, 600),
                    new Size (800, 600), new Size (800, 600), new Size (800, 1200), new Size (800, 600),
                    new Size (800, 600), new Size (800, 600), new Size (800, 1200), new Size (800, 600),
                    new Size (800, 600), new Size (800, 600), new Size (800, 600), new Size (800, 600),
                    new Size (800, 600),
                }
            },
            { "buy.dat",
                new Size[] {
                    new Size (800, 900), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160), new Size (128, 160), new Size (128, 160), new Size (128, 160),
                    new Size (128, 160),
                }
            },
            { "heyacg.dat",
                new Size[] {
                    new Size (236, 174), new Size (236, 174), new Size (236, 174), new Size (236, 174),
                    new Size (236, 174), new Size (236, 174), new Size (236, 174), new Size (236, 174),
                    new Size (236, 174), new Size (236, 174), new Size (236, 174), new Size (236, 174),
                    new Size (236, 174), new Size (236, 174),
                }
            },
            { "kigaecg.dat",
                new Size[] {
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                    new Size (435, 600), new Size (435, 600), new Size (435, 600), new Size (435, 600),
                }
            },
            { "chibicg.dat",
                new Size[] {
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                    new Size (64, 64), new Size (64, 64), new Size (64, 64), new Size (64, 64),
                }
            },
            { "omake.dat",
                new Size[] {
                    new Size (800, 600),
                    new Size (800, 300),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96), new Size (384, 96), new Size (384, 96), new Size (384, 96),
                    new Size (384, 96),
                }
            },
            { "result.dat",
                new Size[] {
                    new Size (528, 600), new Size (528, 600),
                    new Size (272, 600), new Size (272, 600), new Size (272, 600), new Size (272, 600),
                }
            },
            { "title.dat",
                new Size[] {
                    new Size (800, 1076),
                    new Size (800, 600),
                }
            },
        };
    }

    internal class CgDecoder : BinaryImageDecoder
    {
        public CgDecoder (IBinaryStream input, ImageMetaData info) : base (input, info)
        {
        }

        protected override ImageData GetImageData ()
        {
            int stride = (int)Info.Width * 2;
            var pixels = m_input.ReadBytes (stride * (int)Info.Height);
            for (int i = 0; i < pixels.Length; i += 2)
            {
                int hi = pixels[i] << 2 | (pixels[i+1] & 3);
                int lo = pixels[i+1] >> 2 | (pixels[i] & ~0x1F);
                pixels[i] = (byte)lo;
                pixels[i+1] = (byte)hi;
            }
            return ImageData.Create (Info, PixelFormats.Bgr555, null, pixels, stride);
        }
    }
}
