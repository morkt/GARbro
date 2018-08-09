//! \file       MultiFileArchive.cs
//! \date       2018 Aug 09
//! \brief      Treat multiple files as a single archive by virtually concatenating them.
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
using System.IO;

namespace GameRes.Formats
{
    public class MultiFileArchive : ArcFile
    {
        IEnumerable<ArcView>  m_parts;

        public IEnumerable<ArcView> Parts
        {
            get
            {
                yield return File;
                if (m_parts != null)
                    foreach (var part in m_parts)
                        yield return part;
            }
        }

        public MultiFileArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, IEnumerable<ArcView> parts = null)
            : base (arc, impl, dir)
        {
            m_parts = parts;
        }

        public Stream OpenStream (Entry entry)
        {
            Stream input = null;
            try
            {
                long part_offset = 0;
                long entry_start = entry.Offset;
                long entry_end   = entry.Offset + GetEntrySize (entry);
                foreach (var part in Parts)
                {
                    long part_end_offset = part_offset + part.MaxOffset;
                    if (entry_start < part_end_offset)
                    {
                        uint part_size = (uint)Math.Min (entry_end - entry_start, part_end_offset - entry_start);
                        var entry_part = part.CreateStream (entry_start - part_offset, part_size);
                        if (input != null)
                            input = new ConcatStream (input, entry_part);
                        else
                            input = entry_part;
                        entry_start += part_size;
                        if (entry_start >= entry_end)
                            break;
                    }
                    part_offset = part_end_offset;
                }
                if (null == input)
                    return Stream.Null;
                return input;
            }
            catch
            {
                if (input != null)
                    input.Dispose();
                throw;
            }
        }

        protected virtual uint GetEntrySize (Entry entry)
        {
            return entry.Size;
        }
        
        bool m_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (m_disposed)
                return;

            if (disposing && m_parts != null)
            {
                foreach (var arc in m_parts)
                    arc.Dispose();
            }
            m_disposed = true;
            base.Dispose (disposing);
        }
    }
}
