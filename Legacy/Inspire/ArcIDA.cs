//! \file       ArcIDA.cs
//! \date       2018 Jan 14
//! \brief      Inspire resource archive.
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
using System.Text;
using GameRes.Compression;

// [000707][inspire] ambience

namespace GameRes.Formats.Inspire
{
    internal class IdaEntry : Entry
    {
        public uint Flags;
        public uint Key;
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "IDA"; } }
        public override string Description { get { return "Inspire resource archive"; } }
        public override uint     Signature { get { return 0x464158; } } // 'XAF'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadInt32 (4);
            if (version > 0x011400)
                return null;
            using (var index = file.CreateStream())
            {
                var dir = new List<Entry>();
                long index_pos = 8;
                for (;;)
                {
                    index.Position = index_pos;
                    uint entry_length = index.ReadUInt32();
                    if (0 == entry_length)
                        break;
                    uint offset = index.ReadUInt32();
                    uint size   = index.ReadUInt32();
                    index.Seek (8, SeekOrigin.Current);
                    uint flags  = index.ReadUInt32();
                    uint key    = index.ReadUInt32();
                    index.Seek (0x10, SeekOrigin.Current);
                    var name = DeserializeString (index);
                    index_pos += entry_length;

                    var entry = FormatCatalog.Instance.Create<IdaEntry> (name);
                    entry.Offset = offset;
                    entry.Size   = size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    entry.Flags = flags;
                    entry.Key = key;
                    dir.Add (entry);
                }
                if (0 == dir.Count)
                    return null;
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var ient = entry as IdaEntry;
            if (null == ient || 0 == ient.Flags)
                return base.OpenEntry (arc, entry);
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (0 != (ient.Flags & 0xB))
                input = DecryptEntry (input, ient);
            if (0 != (ient.Flags & 4))
                input = new PackedStream<RleDecompressor> (input);
            if (0 != (ient.Flags & 0x10))
                input = new ZLibStream (input, CompressionMode.Decompress);
            return input;
        }

        Stream DecryptEntry (Stream input, IdaEntry entry)
        {
            int input_size = (int)entry.Size;
            var data = new byte[input_size];
            using (input)
                input_size = input.Read (data, 0, input_size);
            byte key = (byte)entry.Key;
            for (int i = 0; i < input_size; ++i)
            {
                byte v = data[i];
                if (0 != (entry.Flags & 8))
                    v += key;
                if (0 != (entry.Flags & 2))
                    v ^= key;
                if (0 != (entry.Flags & 1))
                    v ^= 0xFF;
                data[i] = v;
                key = v;
            }
            return new BinMemoryStream (data, 0, input_size, entry.Name);
        }

        string DeserializeString (IBinaryStream input)
        {
            int length = DeserializeLength (input);
            if (0 == length)
                return "";
            if (length != -1)
                return input.ReadCString (length);
            length = DeserializeLength (input) * 2;
            var chars = input.ReadBytes (length);
            return Encoding.Unicode.GetString (chars, 0, length);
        }

        int DeserializeLength (IBinaryStream input)
        {
            int length = input.ReadUInt8();
            if (length < 0xFF)
                return length;
            length = input.ReadUInt16();
            if (0xFFFE == length)
                length = -1;
            else if (0xFFFF == length)
                length = input.ReadInt32();
            return length;
        }
    }

    internal class RleDecompressor : Decompressor
    {
        IBinaryStream   m_input;

        public override void Initialize (Stream input)
        {
            m_input = BinaryStream.FromStream (input, "");
        }

        protected override IEnumerator<int> Unpack ()
        {
            int output_size = m_input.ReadInt32();
            int processed = 0;
            while (processed < output_size)
            {
                int ctl = m_input.ReadByte();
                if (-1 == ctl)
                    yield break;
                int count = 0;
                if (0 == (ctl & 0x80))
                {
                    count = ctl & 0x3F;
                }
                else if (0 == (ctl & 3))
                {
                    count = m_input.ReadUInt8();
                }
                else if (1 == (ctl & 3))
                {
                    count = m_input.ReadUInt16();
                }
                else if (3 == (ctl & 3))
                {
                    count = m_input.ReadInt32();
                }
                processed += count;
                if (0 != (ctl & 0x40))
                {
                    byte v = m_input.ReadUInt8();
                    while (count --> 0)
                    {
                        m_buffer[m_pos++] = v;
                        if (0 == --m_length)
                            yield return m_pos;
                    }
                }
                else
                {
                    while (count > 0)
                    {
                        int avail = Math.Min (count, m_length);
                        int read = m_input.Read (m_buffer, m_pos, avail);
                        if (0 == read)
                            yield break;
                        count -= read;
                        m_pos += read;
                        m_length -= read;
                        if (0 == m_length)
                            yield return m_pos;
                    }
                }
            }
        }

        bool m_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                if (m_input != null)
                    m_input.Dispose();
                m_disposed = true;
                base.Dispose (disposing);
            }
        }
    }
}
