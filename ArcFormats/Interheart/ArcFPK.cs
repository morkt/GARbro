//! \file       ArcFPK.cs
//! \date       Mon May 25 10:01:24 2015
//! \brief      FPK resource archives implementation.
//
// Copyright (C) 2015-2016 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.CandySoft
{
    [Export(typeof(ArchiveFormat))]
    public class FpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "FPK"; } }
        public override string Description { get { return "Interheart/Candy Soft resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public FpkOpener ()
        {
            Signatures = new uint[] { 0, 1 };
            ContainedFormats = new[] { "BMP", "KG", "OGG", "SCR", "TXT" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset < 0x10)
                return null;
            int count = file.View.ReadInt32 (0);
            List<Entry> dir = null;
            if (count < 0)
            {
                count &= 0x7FFFFFFF;
                if (!IsSaneCount (count))
                    return null;
                dir = ReadEncryptedIndex (file, count);
            }
            else
            {
                if (!IsSaneCount (count))
                    return null;
                try
                {
                    dir = ReadIndex (file, count, 0x10);
                }
                catch { /* read failed, try another filename length */ }
                if (null == dir)
                    dir = ReadIndex (file, count, 0x18);
            }
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }

        private List<Entry> ReadIndex (ArcView file, int count, int name_size)
        {
            long index_offset = 4;
            uint index_size = (uint)((8 + name_size) * count);
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            uint data_offset = 4 + index_size;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset+8, (uint)name_size);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset);
                entry.Size   = file.View.ReadUInt32 (index_offset+4);
                if (entry.Offset < data_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8 + name_size;
            }
            return dir;
        }

        private List<Entry> ReadEncryptedIndex (ArcView file, int count)
        {
            uint index_offset = file.View.ReadUInt32 (file.MaxOffset-4);
            if (index_offset < 4 || index_offset >= file.MaxOffset-8)
                return null;
            var key = file.View.ReadBytes (file.MaxOffset-8, 4);
            int name_size = 0x18;
            int index_size = count * (12 + name_size);
            var index = file.View.ReadBytes (index_offset, (uint)index_size);
            if (index.Length != index_size)
                return null;
            for (int i = 0; i < index.Length; ++i)
                index[i] ^= key[i & 3];

            int index_pos = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                string name = Binary.GetCString (index, index_pos+8, name_size);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                var entry = Create<Entry> (name);
                entry.Offset = LittleEndian.ToUInt32 (index, index_pos);
                entry.Size   = LittleEndian.ToUInt32 (index, index_pos+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_pos += 12 + name_size;
            }
            return dir;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (entry.Size <= 8)
                return input;
            var sign = input.Signature;
            if (0x32434c5a != sign) // 'ZLC2'
                return input;

            using (input)
            using (var reader = new Zlc2Reader (input, (int)entry.Size))
            {
                reader.Unpack();
                return new BinMemoryStream (reader.Data, entry.Name);
            }
        }
    }

    internal class Zlc2Reader : IDisposable
    {
        IBinaryStream   m_input;
        byte[]          m_output;
        int             m_size;

        public byte[] Data { get { return m_output; } }

        public Zlc2Reader (IBinaryStream input, int input_length)
        {
            input.Position = 4;
            m_input = input;
            uint output_length = m_input.ReadUInt32();
            m_output = new byte[output_length];
            m_size = input_length - 8;
        }

        public void Unpack ()
        {
            int remaining = m_size;
            int dst = 0;
            while (remaining > 0 && dst < m_output.Length)
            {
                int ctl = m_input.ReadUInt8();
                remaining--;
                for (int mask = 0x80; mask != 0 && remaining > 0 && dst < m_output.Length; mask >>= 1)
                {
                    if (0 != (ctl & mask))
                    {
                        if (remaining < 2)
                            return;

                        int offset = m_input.ReadUInt8();
                        int count  = m_input.ReadUInt8();
                        remaining -= 2;
                        offset |= (count & 0xF0) << 4;
                        count   = (count & 0x0F) + 3;

                        if (0 == offset)
                            offset = 4096;
                        if (dst + count > m_output.Length)
                            count = m_output.Length - dst;

                        Binary.CopyOverlapped (m_output, dst-offset, dst, count);
                        dst += count;
                    }
                    else
                    {
                        m_output[dst++] = m_input.ReadUInt8();
                        remaining--;
                    }
                }
            }
        }

        #region IDisposable Members
        public void Dispose ()
        {
        }
        #endregion
    }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "SPT")]
    [ExportMetadata("Target", "SCR")]
    public class SptFormat : ResourceAlias { }
}
