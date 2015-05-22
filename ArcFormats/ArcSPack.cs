//! \file       ArcSPack.cs
//! \date       Fri May 22 04:54:08 2015
//! \brief      'SPack' resource archives.
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
using System.Security.Cryptography;
using GameRes.Utility;

namespace GameRes.Formats.SPack
{
    internal class SPackEntry : PackedEntry
    {
        public byte     Method;
        public ushort   Crc;
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SPACK"; } }
        public override string Description { get { return "SPack resource archive"; } }
        public override uint     Signature { get { return 0x63615053; } } // 'SPac'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if ('k' != file.View.ReadInt16 (4))
                return null;
            int version = file.View.ReadInt16 (6);
            if (1 != version)
                return null;
            uint data_size = file.View.ReadUInt32 (8);
            int count = file.View.ReadInt32 (0x10);
            if (count <= 0 || count > 0xfffff)
                return null;
            uint index_size = (uint)(0x38 * count);
            long base_offset = 0x18;
            long index_offset = base_offset + data_size;
            if (index_offset >= file.MaxOffset || index_size > file.View.Reserve (index_offset, index_size))
                return null;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x20);
                index_offset += 0x20;
                var entry = new SPackEntry
                {
                    Name   = name,
                    Offset = base_offset + file.View.ReadUInt32 (index_offset),
                    UnpackedSize = file.View.ReadUInt32 (index_offset+4),
                    Size   = file.View.ReadUInt32 (index_offset+8),
                    Method = file.View.ReadByte (index_offset+12),
                    Crc    = file.View.ReadUInt16 (index_offset+14),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (name.EndsWith (".dat", StringComparison.InvariantCultureIgnoreCase))
                    entry.Type = "audio";
                else
                    entry.Type = FormatCatalog.Instance.GetTypeFromName (name);
                entry.IsPacked = entry.Method != 0;
                dir.Add (entry);
                index_offset += 0x18;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var packed_entry = entry as SPackEntry;
            if (null == packed_entry || !packed_entry.IsPacked)
                return input;
            if (1 == packed_entry.Method)
                return new CryptoStream (input, new Kogado.NotTransform(), CryptoStreamMode.Read);
            if (2 == packed_entry.Method)
            {
                using (var reader = new PackedReader (packed_entry, input))
                {
                    reader.Unpack();
                    return new MemoryStream (reader.Data);
                }
            }
            return input;
        }
    }

    internal class PackedReader : IDisposable
    {
        BinaryReader    m_input;
        uint            m_packed_size;
        byte[]          m_output;

        public byte[] Data { get { return m_output; } }

        public PackedReader (SPackEntry entry, Stream input)
        {
            m_input = new BinaryReader (input);
            m_packed_size = entry.Size;
            m_output = new byte[entry.UnpackedSize];
        }

        public byte[] Unpack ()
        {
            int dst = 0;
            uint src = 0;
            uint ctl = 0;
            uint mask = 0;

            while (dst < m_output.Length && src < m_packed_size)
            {
                if (0 == mask)
                {
                    ctl = m_input.ReadUInt32();
                    src += 4;
                    mask = 0x80000000;
                }
                if (0 != (ctl & mask))
                {
                    int copy_count, offset;

                    offset = m_input.ReadByte();
                    src++;
                    copy_count = offset >> 4;
                    offset &= 0x0f;
                    if (15 == copy_count)
                    {
                        copy_count = m_input.ReadUInt16();
                        src += 2;
                    }
                    else if (14 == copy_count)
                    {
                        copy_count = m_input.ReadByte();
                        src++;
                    }
                    else
                        copy_count++;

                    if (offset < 10)
                        offset++;
                    else
                    {
                        offset = ((offset - 10) << 8) | m_input.ReadByte();
                        src++;
                    }

                    if (dst + copy_count > m_output.Length)
                        copy_count = m_output.Length - dst;
                    Binary.CopyOverlapped (m_output, dst-offset, dst, copy_count);
                    dst += copy_count;
                }
                else
                {
                    m_output[dst++] = m_input.ReadByte();
                    src++;
                }
                mask >>= 1;
            }
            return m_output;
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
