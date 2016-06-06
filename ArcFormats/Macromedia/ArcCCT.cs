//! \file       ArcCCT.cs
//! \date       Fri Jun 26 01:15:26 2015
//! \brief      Macromedia Director archive access implementation.
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
using System.Linq;
using System.Text;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Selen
{
    [Export(typeof(ArchiveFormat))]
    public class CctOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CCT"; } }
        public override string Description { get { return "Macromedia Director resource archive"; } }
        public override uint     Signature { get { return 0x52494658; } } // 'XFIR'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public CctOpener ()
        {
            Extensions = new string[] { "cct", "dcr" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint id = file.View.ReadUInt32 (8);
            if (id != 0x46474443 && id != 0x4647444D) // 'CDGF' || 'MDGF'
                return null;

            var reader = new CctReader (file);
            var dir = reader.ReadIndex();
            if (null != dir)
            {
                var arc = new ArcFile (file, this, dir);
                SetEntryTypes (arc);
                return arc;
            }
            return null;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var packed_entry = entry as PackedEntry;
            if (null == packed_entry || !packed_entry.IsPacked)
                return input;
            else
                return new ZLibStream (input, CompressionMode.Decompress);
        }

        private void SetEntryTypes (ArcFile arc)
        {
            foreach (var entry in arc.Dir.OrderBy (x => x.Offset))
            {
                if (entry.Name.EndsWith (".edim"))
                    entry.Type = DetectEdimType (arc, entry);
                else if (entry.Name.EndsWith (".bitd"))
                    entry.Type = "image";
            }
        }

        private string DetectEdimType (ArcFile arc, Entry entry)
        {
            using (var input = OpenEntry (arc, entry))
            {
                uint signature = (uint)input.ReadByte() << 24;
                signature |= (uint)input.ReadByte() << 16;
                signature |= (uint)input.ReadByte() << 8;
                signature |= (byte)input.ReadByte();
                if (0xffd8ffe0 == signature)
                    return "image";
                uint real_size = (entry as PackedEntry).UnpackedSize;
                if (signature > 0xffff || signature+4 > real_size)
                    return "";
                var header = new byte[signature+0x10];
                if (header.Length != input.Read (header, 0, header.Length))
                    return "";
                if (0xff == header[signature])
                    return "audio";
                return "";
            }
        }
    }

    internal class CctReader
    {
        ArcView         m_file;
        long            m_offset;

        public CctReader (ArcView file)
        {
            m_file = file;
            m_offset = 0x0C;
        }

        byte[] m_size_buffer = new byte[10];

        public List<Entry> ReadIndex ()
        {
            uint section_size = ReadSectionSize ("Fver");
            m_offset += section_size;
            section_size = ReadSectionSize ("Fcdr");
            /*
            int Mcdr_size;
            var Mcdr = ZlibUnpack (m_offset, section_size, out Mcdr_size);
            */
            m_offset += section_size;
            uint abmp_size = ReadSectionSize ("ABMP");
            int max_count = m_file.View.Read (m_offset, m_size_buffer, 0, Math.Min (10, abmp_size));
            int size_offset = 0;
            ReadValue (m_size_buffer, ref size_offset, max_count);
            max_count -= size_offset;

            int bmp_unpacked_size = (int)ReadValue (m_size_buffer, ref size_offset, max_count);
            m_offset += size_offset;
            abmp_size -= (uint)size_offset;
            int index_size;
            var index = ZlibUnpack (m_offset, abmp_size, out index_size, bmp_unpacked_size);
            m_offset += abmp_size;
            section_size = ReadSectionSize ("FGEI");
            if (0 != section_size)
                throw new NotSupportedException();

            int index_offset = 0;
            ReadValue (index, ref index_offset, index_size-index_offset);
            ReadValue (index, ref index_offset, index_size-index_offset);
            int entry_count = (int)ReadValue (index, ref index_offset, index_size-index_offset);
            if (entry_count <= 0 || entry_count > 0xfffff)
                return null;

            var type_buf = new char[4];
            var dir = new List<Entry> (entry_count);
            for (int i = 0; i < entry_count; ++i)
            {
                uint id     = ReadValue (index, ref index_offset, index_size-index_offset);
                uint offset = ReadValue (index, ref index_offset, index_size-index_offset);
                uint size   = ReadValue (index, ref index_offset, index_size-index_offset);
                uint unpacked_size = ReadValue (index, ref index_offset, index_size-index_offset);
                uint flag   = ReadValue (index, ref index_offset, index_size-index_offset);

                if (index_size-index_offset < 4)
                    return null;
                uint type_id = LittleEndian.ToUInt32 (index, index_offset);
                index_offset += 4;
                if (0 == type_id || uint.MaxValue == offset)
                    continue;

                Encoding.ASCII.GetChars (index, index_offset-4, 4, type_buf, 0);
                var entry = new PackedEntry
                {
                    Name = CreateName (id, type_buf),
                    Offset = (long)m_offset + offset,
                    Size = size,
                    UnpackedSize = unpacked_size,
                    IsPacked = 0 == flag,
                };
                if (entry.CheckPlacement (m_file.MaxOffset))
                {
                    dir.Add (entry);
                }
            }
            return dir;
        }

        string CreateName (uint id, char[] type_buf)
        {
            Array.Reverse (type_buf);
            int t = 3;
            while (t >= 0 && ' ' == type_buf[t])
                t--;
            if (t >= 0)
            {
                string ext = new string (type_buf, 0, t+1);
                return string.Format ("{0:X8}.{1}", id, ext.ToLowerInvariant());
            }
            else
                return id.ToString ("X8");
        }

        byte[] ZlibUnpack (long offset, uint size, out int actual_size, int unpacked_size_hint = 0)
        {
            using (var input = m_file.CreateStream (offset, size))
            using (var zstream = new ZLibStream (input, CompressionMode.Decompress))
            using (var mem = new MemoryStream (unpacked_size_hint))
            {
                zstream.CopyTo (mem);
                actual_size = (int)mem.Length;
                return mem.GetBuffer();
            }
        }

        uint ReadSectionSize (string id_str)
        {
            uint id = ConvertId (id_str);
            if (id != m_file.View.ReadUInt32 (m_offset))
                throw new InvalidFormatException();
            m_offset += 4;
            if (5 != m_file.View.Read (m_offset, m_size_buffer, 0, 5))
                throw new InvalidFormatException();
            int off_count = 0;
            uint size = ReadValue (m_size_buffer, ref off_count, 5);
            m_offset += off_count;
            return size;
        }

        static uint ReadValue (byte[] buffer, ref int offset, int length)
        {
            uint n = 0;
            for (int off_count = 0; off_count < length; ++off_count)
            {
                uint bits = buffer[offset++];
                n = (n << 7) | (bits & 0x7F);
                if (0 == (bits & 0x80))
                    return n;
            }
            throw new InvalidFormatException();
        }

        static uint ConvertId (string id)
        {
            if (id.Length != 4)
                throw new ArgumentException ("Invalid section id");
            uint n = 0;
            for (int i = 0; i < 4; ++i)
                n = (n << 8) | (byte)id[i];
            return n;
        }
    }
}
