//! \file       ArcFPK.cs
//! \date       Mon May 25 10:01:24 2015
//! \brief      FPK resource archives implementation.
//
// Copyright (C) 2015 by morkt
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
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            List<Entry> dir = null;
            try
            {
                dir = ReadIndex (file, count, 0x10);
            }
            catch { /* read failed, try another filename length */ }
            if (null == dir)
                dir = ReadIndex (file, count, 0x18);
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
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset+8, (uint)name_size);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset);
                entry.Size   = file.View.ReadUInt32 (index_offset+4);
                if (entry.Offset < index_size || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8 + name_size;
            }
            return dir;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (entry.Size <= 8)
                return input;
            var sign = FormatCatalog.ReadSignature (input);
            if (0x32434c5a != sign) // 'ZLC2'
            {
                input.Position = 0;
                return input;
            }
            using (input)
            using (var reader = new Zlc2Reader (input, (int)entry.Size))
            {
                reader.Unpack();
                return new MemoryStream (reader.Data);
            }
        }
    }

    internal class Zlc2Reader : IDisposable
    {
        BinaryReader    m_input;
        byte[]          m_output;
        int             m_size;

        public byte[] Data { get { return m_output; } }

        public Zlc2Reader (Stream input, int input_length)
        {
            input.Position = 4;
            m_input = new ArcView.Reader (input);
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
                int ctl = m_input.ReadByte();
                remaining--;
                for (int mask = 0x80; mask != 0 && remaining > 0 && dst < m_output.Length; mask >>= 1)
                {
                    if (0 != (ctl & mask))
                    {
                        if (remaining < 2)
                            return;

                        int offset = m_input.ReadByte();
                        int count  = m_input.ReadByte();
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
                        m_output[dst++] = m_input.ReadByte();
                    }
                }
            }
        }

        #region IDisposable Members
        bool disposed = false;

        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    m_input.Dispose();
                }
                disposed = true;
            }
        }
        #endregion
    }
}
