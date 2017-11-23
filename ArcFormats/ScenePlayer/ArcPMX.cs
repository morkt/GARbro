//! \file       ArcPMX.cs
//! \date       2017 Nov 23
//! \brief      ScenePlayer scripts archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.ScenePlayer
{
    [Export(typeof(ArchiveFormat))]
    public class PmxOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PMX"; } }
        public override string Description { get { return "ScenePlayer scripts archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".pmx")
                || file.View.ReadByte (0) != (0x78^0x21))
                return null;

            Stream input = file.CreateStream();
            input = new XoredStream (input, 0x21);
            input = new ZLibStream (input, CompressionMode.Decompress);
            input = new SeekableStream (input);
            bool index_complete = false;
            try
            {
                using (var index = new BinaryStream (input, file.Name, true))
                {
                    int count = index.ReadInt32();
                    if (!IsSaneCount (count))
                        return null;
                    var dir = new List<Entry> (count);
                    uint data_offset = 4 + (uint)count * 0x24;
                    for (int i = 0; i < count; ++i)
                    {
                        var name = index.ReadCString (0x20);
                        // IsPathRooted throws on invalid filename characters
                        if (string.IsNullOrWhiteSpace (name) || Path.IsPathRooted (name))
                            return null;
                        uint size = index.ReadUInt32();
                        var entry = new Entry {
                            Name = name,
                            Type = "script",
                            Offset = data_offset,
                            Size =  size,
                        };
                        dir.Add (entry);
                        data_offset += size;
                    }
                    index_complete = true;
                    return new PmxArchive (file, this, dir, input);
                }
            }
            finally
            {
                if (!index_complete)
                    input.Dispose();
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var parc = (PmxArchive)arc;
            return new StreamRegion (parc.BaseStream, entry.Offset, entry.Size, true);
        }
    }

    internal class PmxArchive : ArcFile
    {
        public readonly Stream BaseStream;

        public PmxArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, Stream base_stream)
            : base (arc, impl, dir)
        {
            BaseStream = base_stream;
        }

        bool m_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                    BaseStream.Dispose();
                m_disposed = true;
            }
        }
    }
}
