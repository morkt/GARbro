//! \file       ArcWARC1.0.cs
//! \date       2017 Dec 09
//! \brief      Forest archive format.
//
// Copyright (C) 2017 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Forest
{
    /// <summary>
    /// ShiinaRio WARC archives predecessor.
    /// </summary>
    [Export(typeof(ArchiveFormat))]
    public class War0Opener : ArchiveFormat
    {
        public override string         Tag { get { return "WAR/1.0"; } }
        public override string Description { get { return "Forest resource archive"; } }
        public override uint     Signature { get { return 0x43524157; } } // 'WARC'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, " 1.0"))
                return null;
            uint index_offset = file.View.ReadUInt32 (8);
            if (index_offset >= file.MaxOffset)
                return null;
            var index = file.View.ReadBytes (index_offset, 0xC000);
            int count = index.Length / 0x18;
            if (!IsSaneCount (count))
                return null;
            for (int i = 0; i < index.Length; i += 2)
            {
                index[i  ] ^= 0xFE;
                index[i+1] ^= 0xE5;
            }
            int pos = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = Binary.GetCString (index, pos, 0x10);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset = index.ToUInt32 (pos+0x10);
                entry.Size   = index.ToUInt32 (pos+0x14);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.UnpackedSize = entry.Size;
                dir.Add (entry);
                pos += 0x18;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent)
                return base.OpenEntry (arc, entry);
            if (!pent.IsPacked)
            {
                if (!arc.File.View.AsciiEqual (entry.Offset, "Ylz"))
                    return base.OpenEntry (arc, entry);
                pent.IsPacked = true;
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset+4);
            }
            var data = arc.File.View.ReadBytes (entry.Offset+8, entry.Size-8);
            var reader = new Ylz16Reader (data);
            data = reader.Unpack ((int)pent.UnpackedSize);
            return new BinMemoryStream (data, entry.Name);
        }
    }

    internal sealed class Ylz16Reader
    {
        byte[]      m_input;

        public Ylz16Reader (byte[] input)
        {
            m_input = input;
            DecryptInput();
        }

        void DecryptInput ()
        {
            for (int i = 0; i < m_input.Length; ++i)
                m_input[i] ^= 0xE6;
        }

        int GetCtlBit ()
        {
            int bit = m_ctl & 1;
            m_ctl >>= 1;
            if (--m_bit_count <= 0)
            {
                m_ctl = LittleEndian.ToUInt16 (m_input, m_src);
                m_src += 2;
                m_bit_count = 16;
            }
            return bit;
        }

        int     m_ctl;
        int     m_bit_count;
        int     m_src;

        public byte[] Unpack (int unpacked_size)
        {
            m_src = 0;
            m_bit_count = 0;
            GetCtlBit();
            var output = new byte[unpacked_size];
            int dst = 0;
            while (dst < unpacked_size)
            {
                if (GetCtlBit() != 0)
                {
                    output[dst++] = m_input[m_src++];
                }
                else
                {
                    int offset, count;
                    if (GetCtlBit() == 0)
                    {
                        count  = GetCtlBit() << 1;
                        count |= GetCtlBit();
                        count += 2;
                        offset = m_input[m_src++] | -0x100;
                    }
                    else
                    {
                        byte lo = m_input[m_src++];
                        byte hi = m_input[m_src++];
                        offset = lo | (hi & ~7) << 5 | -0x2000;
                        count = hi & 7;
                        if (0 == count)
                        {
                            count = m_input[m_src++];
                            if (0 == count)
                                break;
                            count += 9;
                        }
                        else
                        {
                            count += 2;
                        }
                    }
                    Binary.CopyOverlapped (output, dst + offset, dst, count);
                    dst += count;
                }
            }
            return output;
        }
    }
}
