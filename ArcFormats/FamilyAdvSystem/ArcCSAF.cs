//! \file       ArcCSAF.cs
//! \date       2019 Jan 01
//! \brief      Family Adv System resource archive.
//
// Copyright (C) 2019 by morkt
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
using System.Security.Cryptography;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.FamilyAdvSystem
{

    [Export(typeof(ArchiveFormat))]
    public class CsafOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CSAF"; } }
        public override string Description { get { return "Family Adv System resource archive"; } }
        public override uint     Signature { get { return 0x46415343; } } // 'CSAF'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public CsafOpener ()
        {
            Extensions = new string[] { "" };
        }

        static readonly string DefaultKey = "江ノ島の南";
        static readonly byte[] DefaultIV = Encoding.ASCII.GetBytes ("FamilyAdvSystem ");

        public override ArcFile TryOpen (ArcView file)
        {
            uint flags = file.View.ReadUInt32 (4);
            if ((flags & 0x7FFFFFFF) != 0x10000)
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            bool is_encrypted = (flags >> 31) != 0;
            uint index_size = (uint)((count * 24 + 31) & -4096) + 0xFE0u;
            uint names_size = file.View.ReadUInt32 (12);
            var arc_md5 = file.View.ReadBytes (0x10, 0x10);
            var index = new byte[index_size + names_size];
            CsafEncryption enc = null;
            try
            {
                if (is_encrypted)
                {
                    file.View.Read (0x20, index, 0, index_size);
                    enc = new CsafEncryption (DefaultKey, DefaultIV);
                    using (var decryptor = enc.CreateDecryptor (0))
                    using (var enc_names = file.CreateStream (0x20 + index_size, names_size))
                    using (var dec_names = new InputCryptoStream (enc_names, decryptor))
                    {
                        dec_names.Read (index, (int)index_size, (int)names_size);
                    }
                }
                else
                {
                    file.View.Read (0x20, index, 0, index_size + names_size);
                }
                using (var md5 = MD5.Create())
                {
                    var hash = md5.ComputeHash (index);
                    if (!hash.SequenceEqual (arc_md5))
                        return null;
                    int index_pos = 0x10;
                    int name_pos = (int)index_size;
                    var dir = new List<Entry> (count);
                    for (int i = 0; i < count; ++i)
                    {
                        int j;
                        for (j = name_pos; j+1 < index.Length; j += 2)
                        {
                            if (index[j] == 0 && index[j+1] == 0)
                                break;
                        }
                        int name_length = j - name_pos;
                        var name = Encoding.Unicode.GetString (index, name_pos, name_length);
//                        hash = md5.ComputeHash (index, name_pos, name_length); // == [index_pos-0x10]
                        name_pos += name_length + 10;

                        var entry = Create<Entry> (name);
                        entry.Offset = (long)index.ToUInt32 (index_pos) << 12;
                        entry.Size = index.ToUInt32 (index_pos+4);
                        index_pos += 0x18;
                        if (!entry.CheckPlacement (file.MaxOffset))
                            return null;
                        dir.Add (entry);
                    }
                    if (!is_encrypted)
                        return new ArcFile (file, this, dir);
                    var arc = new CsafArchive (file, this, dir, enc);
                    enc = null;
                    return arc;
                }
            }
            finally
            {
                if (enc != null)
                    enc.Dispose();
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (0 == entry.Size)
                return Stream.Null;
            var carc = arc as CsafArchive;
            if (null == carc)
                return base.OpenEntry (arc, entry);
            var input = new CsafStream (carc);
            return new StreamRegion (input, entry.Offset, entry.Size);
        }
    }

    internal class CsafEncryption : IDisposable
    {
        Aes     m_aes;
        MD5     m_md5;
        byte[]  m_key;
        byte[]  m_iv;

        public CsafEncryption (string password, byte[] iv)
        {
            m_md5 = MD5.Create();
            m_aes = Aes.Create();
            m_aes.Mode = CipherMode.CBC;
            m_aes.Padding = PaddingMode.None;
            m_key = InitKey (password);
            m_iv = iv;
        }

        public ICryptoTransform CreateDecryptor (int block_num)
        {
            var block_key = GetBlockKey (block_num);
            return m_aes.CreateDecryptor (block_key, m_iv);
        }

        byte[] InitKey (string pass_phrase)
        {
            var key = new byte[32];
            if (!string.IsNullOrEmpty (pass_phrase))
            {
                var bytes = Encoding.Unicode.GetBytes (pass_phrase);
                var hash = m_md5.ComputeHash (bytes);
                Buffer.BlockCopy (hash, 0, key, 0, 16);
                hash = m_md5.ComputeHash (bytes, 1, bytes.Length - 2);
                Buffer.BlockCopy (hash, 0, key, 16, 16);
            }
            return key;
        }

        byte[] GetBlockKey (int block_num)
        {
            int offset = block_num / 8;
            int shift = block_num & 7;
            var key = new byte[32];
            var buf = new byte[16];
            for (int i = 0; i < 16; ++i)
            {
                buf[i] = Binary.RotByteL (m_key[(offset + i) & 0xF], shift);
            }
            var hash = m_md5.ComputeHash (buf, 0, 16);
            Buffer.BlockCopy (hash, 0, key, 0, 16);
            for (int i = 0; i < 16; ++i)
            {
                buf[i] = Binary.RotByteL (m_key[16 + ((offset + i) & 0xF)], shift);
            }
            hash = m_md5.ComputeHash (buf, 0, 16);
            Buffer.BlockCopy (hash, 0, key, 16, 16);
            return key;
        }

        #region IDisposable Members
        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_aes.Dispose();
                m_md5.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }

    internal class CsafArchive : ArcFile
    {
        public readonly CsafEncryption  Encryption;

        public CsafArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, CsafEncryption enc)
            : base (arc, impl, dir)
        {
            Encryption = enc;
        }

        #region IDisposable Members
        bool _csaf_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (_csaf_disposed)
                return;

            if (disposing)
                Encryption.Dispose();
            _csaf_disposed = true;
            base.Dispose (disposing);
        }
        #endregion
    }

    internal class CsafStream : Stream
    {
        readonly long   m_length;
        ArcView.Frame   m_view;
        CsafEncryption  m_encryption;
        long            m_position = 0;
        byte[]          m_block = new byte[0x1000];
        long            m_block_start = 0;
        int             m_block_length = 0;

        public override bool CanRead  { get { return true; } }
        public override bool CanSeek  { get { return true; } }
        public override bool CanWrite { get { return false; } }

        public CsafStream (CsafArchive arc)
        {
            m_length = arc.File.MaxOffset;
            m_view = arc.File.CreateFrame();
            m_encryption = arc.Encryption;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = 0;
            while (count > 0)
            {
                if (!(m_position >= m_block_start && m_position < m_block_start + m_block_length))
                {
                    if (!ReadBlock())
                        break;
                }
                int block_pos = (int)m_position & 0xFFF;
                int avail = Math.Min (count, m_block_length - block_pos);
                Buffer.BlockCopy (m_block, block_pos, buffer, offset, avail);
                m_position += avail;
                offset += avail;
                read += avail;
                count -= avail;
            }
            return read;
        }

        bool ReadBlock ()
        {
            if (m_position >= m_length)
                return false;
            m_block_start = m_position & ~0xFFFL;
            m_block_length = m_view.Read (m_block_start, m_block, 0, 0x1000);
            if (m_block_length != 0x1000)
                return false;
            using (var decryptor = m_encryption.CreateDecryptor ((int)(m_block_start >> 12)))
            using (var enc = new BinMemoryStream (m_block))
            using (var dec = new InputCryptoStream (enc, decryptor))
                dec.Read (m_block, 0, m_block_length);
            return true;
        }

        #region IO.Stream members
        public override long Length { get { return m_length; } }
        public override long Position
        {
            get { return m_position; }
            set { m_position = value; }
        }

        public override void Flush ()
        {
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            if (SeekOrigin.Begin == origin)
                Position = offset;
            else if (SeekOrigin.Current == origin)
                Position = m_position + offset;
            else
                Position = m_length + offset;

            return m_position;
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("CsafStream.SetLength method is not supported");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("CsafStream.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new NotSupportedException("CsafStream.WriteByte method is not supported");
        }
        #endregion

        #region IDisposable Members
        bool _disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    m_view.Dispose();
                }
                _disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }
}
