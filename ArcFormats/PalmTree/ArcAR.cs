//! \file       ArcAR.cs
//! \date       2018 Jan 22
//! \brief      PalmTree script engine resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Formats.PkWare;

namespace GameRes.Formats.PalmTree
{
    /// <summary>
    /// Ordinary PkWare ZIP archive with 'PK' signature changed to 'AR'
    /// </summary>
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ZipOpener
    {
        public override string         Tag { get { return "ARC/AR"; } }
        public override string Description { get { return "PalmTree script engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        static readonly byte[] ArDirSignature = { (byte)'A', (byte)'R', 5, 6 };

        public ArcOpener ()
        {
            Settings = null;
            Extensions = new string[] { "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (-1 == SearchForSignature (file, ArDirSignature))
                return null;
            var input = new ArPkStream (file.CreateStream());
            try
            {
                return OpenZipArchive (file, input);
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        public override ResourceScheme Scheme { get; set; }
    }

    /// <summary>
    /// Stream that changes all 'AR' signatures to 'PK' on the fly.
    /// </summary>
    /// <remarks>
    /// this is almost like reading ZIP file directory, maybe just write custom ZIP archive reader instead?
    /// </remarks>
    internal class ArPkStream : InputProxyStream
    {
        List<long>  m_ar_blocks;
        long        m_last_scan_pos;
        bool        m_scan_failed;

        public ArPkStream (Stream input) : base (input)
        {
            m_ar_blocks = new List<long>();
            m_last_scan_pos = 0;
            m_scan_failed = false;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            long pos = this.Position;
            if (pos + count > m_last_scan_pos && !m_scan_failed)
            {
                BuildDirectory (pos + count);
                this.Position = pos;
            }
            count = BaseStream.Read (buffer, offset, count);
            if (0 == count)
                return count;
            long buf_pos = pos;
            long buf_end = buf_pos + count;
            int index = m_ar_blocks.BinarySearch (buf_pos-1);
            if (index < 0)
                index = ~index;
            for (; index < m_ar_blocks.Count; ++index)
            {
                var ar_pos = m_ar_blocks[index];
                if (buf_end <= ar_pos)
                    break;
                if (buf_pos >= ar_pos+2)
                    continue;
                int signature_pos = (int)(ar_pos - pos);
                if (signature_pos >= 0)
                    buffer[offset+signature_pos] = (byte)'P';
                ++signature_pos;
                if (signature_pos >= 0 && signature_pos < count)
                    buffer[offset+signature_pos] = (byte)'K';
                buf_pos = ar_pos + 2;
            }
            return count;
        }

        byte[] pk_buffer = new byte[0x22];

        void BuildDirectory (long last_pos)
        {
            long pos = m_last_scan_pos;
            while (pos < last_pos)
            {
                this.Position = pos;
                int read = BaseStream.Read (pk_buffer, 0, 0x22);
                if (read < 4 || !pk_buffer.AsciiEqual ("AR"))
                {
                    m_scan_failed = true;
                    break;
                }
                m_ar_blocks.Add (pos);
                uint block_type = pk_buffer.ToUInt16 (2);
                if (0x0201 == block_type && read >= 0x22)
                {
                    uint name_length  = pk_buffer.ToUInt16 (0x1C);
                    uint extra_length = pk_buffer.ToUInt16 (0x1E);
                    uint cmt_length   = pk_buffer.ToUInt16 (0x20);
                    pos += 0x2EL + name_length + extra_length + cmt_length;
                }
                else if (0x0403 == block_type && read >= 0x1E)
                {
                    uint packed_size  = pk_buffer.ToUInt32 (0x12);
                    uint name_length  = pk_buffer.ToUInt16 (0x1A);
                    uint extra_length = pk_buffer.ToUInt16 (0x1C);
                    pos += 0x1EL + name_length + extra_length + packed_size;
                }
                else if (0x0605 == block_type && read >= 0x16)
                {
                    uint cmt_length = pk_buffer.ToUInt16 (0x14);
                    pos += 0x16L + cmt_length;
                }
                else
                {
                    pos += 4;
                    m_scan_failed = true;
                }
            }
            m_last_scan_pos = pos;
        }
    }
}
