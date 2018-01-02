//! \file       ArcBK.cs
//! \date       2018 Jan 02
//! \brief      MAIKA resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Maika
{
    [Export(typeof(ArchiveFormat))]
    public class BkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/BK"; } }
        public override string Description { get { return "Maika resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_offset = file.View.ReadUInt32 (0);
            uint index_size = file.View.ReadUInt32 (4);
            if (index_offset+index_size != file.MaxOffset)
                return null;
            using (var input = file.CreateStream (index_offset, index_size))
            using (var packed = new PackedStream<LzBitsDecompressor> (input))
            using (var index = new BinaryStream (packed, file.Name))
            {
                int count = index.ReadInt32();
                if (!IsSaneCount (count))
                    return null;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    uint offset         = index.ReadUInt32();
                    uint size           = index.ReadUInt32();
                    uint unpacked_size  = index.ReadUInt32();
                    var name = index.ReadCString (0x104);
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                    entry.Offset        = offset;
                    entry.Size          = size;
                    entry.UnpackedSize  = unpacked_size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    if (name.HasExtension (".gpt"))
                        entry.Type = "image";
                    entry.IsPacked = entry.Size != entry.UnpackedSize;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return input;
            input = new PackedStream<LzBitsDecompressor> (input);
            input = new LimitStream (input, pent.UnpackedSize);
            if (entry.Name.HasExtension (".gpa"))
                input = new XoredStream (input, 0xFF);
            return input;
        }
    }

    internal class LzBitsDecompressor : Decompressor
    {
        MsbBitStream        m_input;

        public override void Initialize (Stream input)
        {
            m_input = new MsbBitStream (input, true);
        }

        protected override IEnumerator<int> Unpack ()
        {
            var frame = new byte[0x400];
            int frame_pos = 1;
            for (;;)
            {
                int bit = m_input.GetNextBit();
                if (-1 == bit)
                    yield break;
                if (bit != 0)
                {
                    int v = m_input.GetBits (8);
                    if (-1 == v)
                        yield break;
                    m_buffer[m_pos++] = frame[frame_pos++ & 0x3FF] = (byte)v;
                    if (0 == --m_length)
                        yield return m_pos;
                }
                else
                {
                    int offset = m_input.GetBits (10);
                    if (-1 == offset)
                        yield break;
                    int count = m_input.GetBits (5);
                    if (-1 == count)
                        yield break;
                    count += 2;
                    while (count-- > 0)
                    {
                        byte v = frame[offset++ & 0x3FF];
                        m_buffer[m_pos++] = frame[frame_pos++ & 0x3FF] = v;
                        if (0 == --m_length)
                            yield return m_pos;
                    }
                }
            }
        }

        #region IDisposable Members
        bool m_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (disposing && !m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }
}
