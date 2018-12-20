//! \file       ArcDET.cs
//! \date       Mon Nov 09 00:16:53 2015
//! \brief      μ-GameOperationSystem resource archive.
//
// Copyright (C) 2015-2018 by morkt
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.uGOS
{
    [Export(typeof(ArchiveFormat))]
    public class DetOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DET"; } }
        public override string Description { get { return "μ-GameOperationSystem resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".det"))
                return null;
            var name_file = Path.ChangeExtension (file.Name, "nme");
            if (!VFS.FileExists (name_file))
                return null;
            uint entry_size = 0x10;
            var index_file = Path.ChangeExtension (file.Name, "atm");
            if (!VFS.FileExists (index_file))
            {
                index_file = Path.ChangeExtension (index_file, "at2");
                if (!VFS.FileExists (index_file))
                    return null;
                entry_size = 0x14;
            }
            using (var nme = VFS.OpenView (name_file))
            using (var idx = VFS.OpenView (index_file))
            {
                var reader = new DetIndexReader (file, name_file: nme, index_file: idx);
                var dir = reader.ReadIndex (entry_size);
                if (null == dir && entry_size != 0x14)
                    dir = reader.ReadIndex (0x14);
                if (null == dir)
                    return null;
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new PackedStream<RleDecompressor> (input);
        }
    }

    internal sealed class DetIndexReader
    {
        ArcView     m_arc;
        ArcView     m_index;
        byte[]      m_names;
        List<Entry> m_dir;

        public DetIndexReader (ArcView arc_file, ArcView name_file, ArcView index_file)
        {
            m_arc = arc_file;
            m_names = name_file.View.ReadBytes (0, (uint)name_file.MaxOffset);
            m_index = index_file;
        }

        public List<Entry> ReadIndex (uint entry_size)
        {
            int count = (int)(m_index.MaxOffset / entry_size);
            if (!ArchiveFormat.IsSaneCount (count))
                return null;
            if (null == m_dir)
                m_dir = new List<Entry> (count);
            else
                m_dir.Clear();
            uint idx_offset = 0;
            for (int i = 0; i < count; ++i)
            {
                if (idx_offset + entry_size > m_index.MaxOffset)
                    return null;
                int name_offset = m_index.View.ReadInt32 (idx_offset);
                if (name_offset < 0 || name_offset >= m_names.Length)
                    return null;
                var name = Binary.GetCString (m_names, name_offset, m_names.Length - name_offset);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset = m_index.View.ReadUInt32 (idx_offset + 4);
                entry.Size = m_index.View.ReadUInt32 (idx_offset + 8);
                if (!entry.CheckPlacement (m_arc.MaxOffset))
                    return null;
                entry.IsPacked = true;
                if (entry_size >= 0x14)
                    entry.UnpackedSize = m_index.View.ReadUInt32 (idx_offset + 0x10);
                if (name.EndsWith (".bmp.txt", StringComparison.OrdinalIgnoreCase))
                    entry.Type = "image";
                m_dir.Add (entry);
                idx_offset += entry_size;
            }
            return m_dir;
        }
    }

    internal sealed class RleDecompressor : Decompressor
    {
        Stream          m_input;

        public override void Initialize (Stream input)
        {
            m_input = input;
        }

        protected override IEnumerator<int> Unpack ()
        {
            var frame = new byte[0x100];
            int frame_pos = 0;
            const int frame_mask = 0xFF;
            for (;;)
            {
                int ctl = m_input.ReadByte();
                if (-1 == ctl)
                    yield break;
                if (0xFF != ctl)
                {
                    m_buffer[m_pos++] = frame[frame_pos++ & frame_mask] = (byte)ctl;
                    if (0 == --m_length)
                        yield return m_pos;
                }
                else
                {
                    ctl = m_input.ReadByte();
                    if (-1 == ctl)
                        yield break;
                    if (0xFF == ctl)
                    {
                        m_buffer[m_pos++] = frame[frame_pos++ & frame_mask] = 0xFF;
                        if (0 == --m_length)
                            yield return m_pos;
                    }
                    else
                    {
                        int offset = frame_pos - ((ctl >> 2) + 1);
                        int count = (ctl & 3) + 3;
                        while (count --> 0)
                        {
                            byte v = frame[offset++ & frame_mask];
                            m_buffer[m_pos++] = frame[frame_pos++ & frame_mask] = v;
                            if (0 == --m_length)
                                yield return m_pos;
                        }
                    }
                }
            }
        }
    }
}
