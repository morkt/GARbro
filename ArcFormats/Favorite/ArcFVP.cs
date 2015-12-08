//! \file       ArcFVP.cs
//! \date       Sat Feb 07 23:23:02 2015
//! \brief      Favorit View Point system engine archive implementation.
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

namespace GameRes.Formats.FVP
{
    [Export(typeof(ArchiveFormat))]
    public class BinOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BIN/ACPXPK"; } }
        public override string Description { get { return "Favorite View Point resource archive"; } }
        public override uint     Signature { get { return 0x58504341; } } // "ACPX"
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public BinOpener ()
        {
            Extensions = new string[] { "bin" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "PK01"))
                return null;
            int count = file.View.ReadInt32 (8);
            if (count <= 0 || count > 0xfffff)
                return null;
            long index_offset = 0x0c;
            uint index_size = (uint)(0x28 * count);
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset, 0x20);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x20);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x24);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x28;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var entry_offset = entry.Offset;
            var input = new ArcView.Frame (arc.File, entry_offset, entry.Size);
            try
            {
                if (entry.Size > 8 && input.AsciiEqual (entry_offset, "acp\0"))
                {
                    using (var decoder = new LzwDecoder (input))
                    {
                        decoder.Unpack();
                        return new MemoryStream (decoder.Output, false);
                    }
                }
                return new ArcView.ArcStream (input, entry_offset, entry.Size);
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }
    }

    internal sealed class LzwDecoder : IDisposable
    {
        private Stream  m_input;
        private byte[]  m_output;

        public byte[] Output { get { return m_output; } }
        
        uint m_dst_size;

        public LzwDecoder (ArcView.Frame input)
        {
            m_dst_size = Binary.BigEndian (input.ReadUInt32 (input.Offset+4));
            m_input = new ArcView.ArcStream (input, input.Offset+8, input.Reserved-8);
            m_output = new byte[m_dst_size];
        }

        public void Unpack ()
        {
            int dst = 0;
            int bits = 0;
            var buf = new int[0x8c00];
            uint dst_left = m_dst_size;
            uint L0 = 0x800000;
            uint edx = 0x102;
            for (;;)
            {
                uint eax = L0;
                bool hi = false;
                while (!hi)
                {
                    bits <<= 1;
                    if (0 == (bits&0xff))
                    {
                        bits = m_input.ReadByte();
                        if (-1 == bits)
                            throw new EndOfStreamException ("Invalid compressed stream");
                        bits = (bits << 1) | 1;
                    }
                    int next_bit = (bits >> 8) & 1;
                    hi = 0 != (eax & 0x80000000);
                    eax = (eax << 1) | (uint)next_bit;
                }
                if (0x100 == (eax & 0xff00) && 2 >= (eax & 0xff))
                {
                    if (2 == (eax & 0xff))
                    {
                        L0 = 0x800000;
                        edx = 0x102;
                        continue;
                    }
                    if (0 == (eax & 0xff))
                        break;
                    L0 >>= 1;
                    if (0 != (L0 & 0xff00))
                        throw new EndOfStreamException ("Invalid compressed stream");
                    continue;
                }
                ++edx;
                if (edx >= 0x8c00)
                    throw new EndOfStreamException ("Invalid compressed stream");
                buf[edx] = dst;
                if (0 == (eax & 0xff00))
                {
                    if (0 == dst_left--)
                        throw new EndOfStreamException ("Invalid compressed stream");
                    m_output[dst++] = (byte)(eax & 0xff);
                }
                else
                {
                    if (eax >= edx)
                        throw new EndOfStreamException ("Invalid compressed stream");
                    int src   = buf[eax];
                    int count = buf[eax+1] - src + 1;
                    if (dst_left < count)
                        throw new EndOfStreamException ("Invalid compressed stream");
                    dst_left -= (uint)count;
                    Binary.CopyOverlapped (m_output, src, dst, count);
                    dst += count;
                }
            }
        }

        #region IDisposable Members
        public void Dispose ()
        {
            if (null != m_input)
            {
                m_input.Dispose();
                m_input = null;
            }
            GC.SuppressFinalize (this);
        }
        #endregion
    }
}
