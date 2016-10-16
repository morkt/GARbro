//! \file       ArcPBX.cs
//! \date       Fri Nov 27 01:33:38 2015
//! \brief      Terios resource archive.
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

namespace GameRes.Formats.Terios
{
    [Export(typeof(ArchiveFormat))]
    public class PbxOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PBX"; } }
        public override string Description { get { return "\"Pandora.box\" resource archive"; } }
        public override uint     Signature { get { return 0x646E6150; } } // 'Pand'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "ora.box\0"))
                return null;
            uint next_offset = file.View.ReadUInt32 (0xC);
            if (next_offset > file.View.Reserve (0, next_offset))
                return null;
            uint index_offset = 0x10;
            int count = (int)(next_offset-0x10) / 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0xC);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = next_offset;
                next_offset = file.View.ReadUInt32 (index_offset+0xC);
                entry.Size = (uint)(next_offset - entry.Offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x10;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Size <= 0x10 || 0x6344764D != arc.File.View.ReadUInt32 (entry.Offset))
                return base.OpenEntry (arc, entry);
            try
            {
                int unpacked_size = arc.File.View.ReadInt32 (entry.Offset+8);
                using (var input = arc.File.CreateStream (entry.Offset+0x10, entry.Size-0x10))
                using (var reader = new PandoraCompression (input, unpacked_size))
                {
                    var data = reader.Unpack();
                    return new BinMemoryStream (data, entry.Name);
                }
            }
            catch
            {
                // in case of decompression error return compressed data
                return base.OpenEntry (arc, entry);
            }
        }
    }

    internal sealed class PandoraCompression : IDisposable
    {
        IBinaryStream   m_input;
        byte[]          m_output;

        public byte[] Data { get { return m_output; } }

        public PandoraCompression (IBinaryStream input, int unpacked_size)
        {
            m_input = input;
            m_output = new byte[unpacked_size];
        }

        public byte[] Unpack ()
        {
            int dst = 0;
            m_output[dst++] = m_input.ReadUInt8();
            while (dst < m_output.Length)
            {
                int ctl = m_input.ReadUInt8();
                int count;
                if (ctl >= 0x80)
                {
                    if (ctl >= 0xC0)
                    {
                        int offset = m_input.ReadUInt8();
                        offset += 0x101 + ((ctl & 0x3F) << 8);
                        offset = dst - offset;
                        if (offset < 0)
                            throw new InvalidFormatException();
                        m_output[dst++] = m_output[offset++];
                        m_output[dst++] = m_output[offset++];
                        m_output[dst++] = m_output[offset++];
                    }
                    else
                    {
                        if (ctl >= 0xB0)
                        {
                            count = Binary.BigEndian (m_input.ReadUInt16());
                            count += 0x813 + ((ctl & 7) << 16);
                            ctl &= 8;
                        }
                        else if (ctl >= 0xA0)
                        {
                            count = m_input.ReadByte() + ((ctl & 7) << 8) + 19;
                            ctl &= 8;
                        }
                        else
                        {
                            count = (ctl & 0xF) + 3;
                            ctl &= 0x10;
                        }
                        int offset;
                        if (ctl != 0)
                        {
                            offset = Binary.BigEndian (m_input.ReadUInt16()) + 0x101;
                        }
                        else
                        {
                            offset = m_input.ReadUInt8() + 1;
                        }
                        Binary.CopyOverlapped (m_output, dst - offset, dst, count);
                        dst += count;
                    }
                }
                else
                {
                    if (ctl >= 0x60)
                    {
                        count = Binary.BigEndian (m_input.ReadUInt16());
                        count += 0x2041 + ((ctl & 0x1F) << 16);
                    }
                    if (ctl >= 0x40)
                    {
                        count = m_input.ReadUInt8() + ((ctl & 0x1F) << 8) + 0x41;
                    }
                    else
                    {
                        count = ctl + 1;
                    }
                    count = m_input.Read (m_output, dst, count);
                    dst += count;
                }
            }
            return m_output;
        }

        #region IDisposable Members
        public void Dispose ()
        {
        }
        #endregion
    }
}
