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
        public override bool      CanWrite { get { return false; } }

        public BinOpener ()
        {
            Extensions = new string[] { "bin" };
            Signatures = new uint[] { 0x58504341, 0x5F504341 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (3, "XPK01") && !file.View.AsciiEqual (3, "_PK.1"))
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
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
            if (!(entry.Size > 8 && arc.File.View.AsciiEqual (entry.Offset, "acp\0")))
                return base.OpenEntry (arc, entry);
            int unpacked_size = Binary.BigEndian (arc.File.View.ReadInt32 (entry.Offset+4));
            using (var input = arc.File.CreateStream (entry.Offset+8, entry.Size-8))
            using (var decoder = new LzwDecoder (input, unpacked_size))
            {
                decoder.Unpack();
                return new BinMemoryStream (decoder.Output, entry.Name);
            }
        }
    }

    internal sealed class LzwDecoder : IDisposable
    {
        private MsbBitStream    m_input;
        private byte[]          m_output;

        public byte[] Output { get { return m_output; } }

        public LzwDecoder (Stream input, int unpacked_size)
        {
            m_input = new MsbBitStream (input, true);
            m_output = new byte[unpacked_size];
        }

        public void Unpack ()
        {
            int dst = 0;
            var lzw_dict = new int[0x8900];
            int token_width = 9;
            int dict_pos = 0;
            while (dst < m_output.Length)
            {
                int token = m_input.GetBits (token_width);
                if (-1 == token)
                    throw new EndOfStreamException ("Invalid compressed stream");
                else if (0x100 == token) // end of input
                    break;
                else if (0x101 == token) // increase token width
                {
                    ++token_width;
                    if (token_width > 24)
                        throw new InvalidFormatException ("Invalid comressed stream");
                }
                else if (0x102 == token) // reset dictionary
                {
                    token_width = 9;
                    dict_pos = 0;
                }
                else
                {
                    if (dict_pos >= lzw_dict.Length)
                        throw new InvalidFormatException ("Invalid comressed stream");
                    lzw_dict[dict_pos++] = dst;
                    if (token < 0x100)
                    {
                        m_output[dst++] = (byte)token;
                    }
                    else
                    {
                        token -= 0x103;
                        if (token >= dict_pos)
                            throw new InvalidFormatException ("Invalid comressed stream");
                        int src = lzw_dict[token];
                        int count = Math.Min (m_output.Length-dst, lzw_dict[token+1] - src + 1);
                        if (count < 0)
                            throw new InvalidFormatException ("Invalid comressed stream");
                        Binary.CopyOverlapped (m_output, src, dst, count);
                        dst += count;
                    }
                }
            }
        }

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize (this);
        }
        #endregion
    }
}
