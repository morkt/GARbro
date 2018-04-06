//! \file       ArcGM.cs
//! \date       2018 Apr 05
//! \brief      Eve resource archive.
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
using GameRes.Compression;

// [021206][Eve] Mitsugetsu ~Secret Moon~

namespace GameRes.Formats.Eve
{
    [Export(typeof(ArchiveFormat))]
    public class GmDatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/GM"; } }
        public override string Description { get { return "Eve resource archive"; } }
        public override uint     Signature { get { return 0x2E314D47; } } // 'GM1.0'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadByte (4) != '0')
                return null;
            using (var index = file.CreateStream())
            {
                var signature = index.ReadCString();
                index.Position = (((int)index.Position + 4) >> 2) << 2;
                uint data_offset = index.ReadUInt16();
                uint index_size = index.ReadUInt32();
                uint index_offset = index.ReadUInt32();
                int count = index.ReadInt32();
                if (!IsSaneCount (count))
                    return null;
                int key_length = index.ReadUInt16();
                int flags = index.ReadUInt16();
                index.Position = 20;
                var key = index.ReadBytes (key_length);
                index.Position = index_offset + 0xC00;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    uint offset = index.ReadUInt32() + data_offset;
                    uint size   = index.ReadUInt32();
                    int name_len = index.ReadUInt8();
                    var name = index.ReadCString (name_len);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = offset;
                    entry.Size   = size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var header = arc.File.View.ReadBytes (entry.Offset, 25);
            if (header[0] < 'B' || header[0] > 'E' || header[1] != '1')
                return arc.File.CreateStream (entry.Offset, entry.Size);
            if ('E' == header[0])
            {
                byte t = header[17];
                header[17] = header[23];
                header[23] = t;
                t = header[19];
                header[19] = header[24];
                header[24] = t;
            }
            int unpacked_size = header.ToInt32 (6);
            var data = new byte[unpacked_size];
            Stream input = arc.File.CreateStream (entry.Offset+header.Length, entry.Size-(uint)header.Length);
            input = new PrefixStream (header, input);
            input.Position = 10;
            using (input = new LzssStream (input))
                input.Read (data, 0, unpacked_size);
            if (data.AsciiEqual ("BPR01"))
                return new PackedStream<BprDecompressor> (Stream.Null, new BprDecompressor (data));
            else
                return new BinMemoryStream (data, entry.Name);
        }

        void DecryptIndex (byte[] index, int length, byte[] key)
        {
            for (int i = 0; i < length; ++i)
            {
                index[i] ^= key[i % key.Length];
            }
        }
    }

    internal class BprDecompressor : Decompressor
    {
        IBinaryStream   m_input;

        public BprDecompressor ()
        {
        }

        public BprDecompressor (byte[] data)
        {
            m_input = new BinMemoryStream (data, 5, data.Length-5);
        }

        public override void Initialize (Stream input)
        {
        }

        protected override IEnumerator<int> Unpack ()
        {
            for (;;)
            {
                int ctl = m_input.ReadByte();
                if (-1 == ctl || 0xFF == ctl)
                    yield break;
                int count = m_input.ReadInt32();
                if (1 == ctl)
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
                        int chunk = Math.Min (count, m_length);
                        chunk = m_input.Read (m_buffer, m_pos, chunk);
                        if (0 == chunk)
                            yield break;
                        m_pos += chunk;
                        m_length -= chunk;
                        count -= chunk;
                        if (0 == m_length)
                            yield return m_pos;
                    }
                }
            }
        }

        #region IDisposable Members
        bool m_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing && m_input != null)
                    m_input.Dispose();
                m_disposed = true;
                base.Dispose();
            }
        }
        #endregion
    }
}
