//! \file       ArcPCF.cs
//! \date       Fri Sep 30 10:37:28 2016
//! \brief      Primel the Adventure System resource archive.
//
// Copyright (C) 2016-2017 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Primel
{
    internal class PcfEntry : PackedEntry
    {
        public uint     Flags;
        public byte[]   Key;
    }

    internal class PcfArchive : ArcFile
    {
        public readonly PrimelScheme    Scheme;

        public PcfArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, PrimelScheme scheme)
            : base (arc, impl, dir)
        {
            Scheme = scheme;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PcfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PCF"; } }
        public override string Description { get { return "Primel ADV System resource archive"; } }
        public override uint     Signature { get { return 0x6B636150; } } // 'Pack'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "Code"))
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            var reader = new PcfIndexReader (file, count);
            var dir = reader.Read();
            if (null == dir)
                return null;
            return new PcfArchive (file, this, dir, reader.Scheme);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var parc = arc as PcfArchive;
            var pent = entry as PcfEntry;
            if (null == pent || null == parc)
                return base.OpenEntry (arc, entry);
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            try
            {
                input = parc.Scheme.TransformStream (input, pent.Key, pent.Flags);
                if (pent.IsPacked)
                    input = new LimitStream (input, pent.UnpackedSize);
                return input;
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }
    }

    internal sealed class PcfIndexReader
    {
        ArcView         m_file;
        int             m_count;
        long            m_base_offset;
        List<Entry>     m_dir;

        public PrimelScheme Scheme { get; set; }

        public PcfIndexReader (ArcView file, int count)
        {
            m_file = file;
            m_count = count;
            m_dir = new List<Entry> (m_count);
        }

        static readonly PrimelScheme[] KnownSchemes = {
            new PrimelScheme(), new PrimelSchemeV2()
        };

        public List<Entry> Read ()
        {
            long data_size = m_file.View.ReadInt64 (0x10);
            long index_offset = m_file.View.ReadInt64 (0x28);
            if (data_size >= m_file.MaxOffset || index_offset >= m_file.MaxOffset)
                return null;
            uint index_size = m_file.View.ReadUInt32 (0x30);
            uint flags = m_file.View.ReadUInt32 (0x38);
            var key = m_file.View.ReadBytes (0x58, 8);
            m_base_offset = m_file.MaxOffset - data_size;
            foreach (var scheme in KnownSchemes)
            {
                m_dir.Clear();
                try
                {
                    using (var stream = m_file.CreateStream (m_base_offset + index_offset, index_size))
                    using (var index = scheme.TransformStream (stream, key, flags))
                    {
                        if (ReadIndex (index))
                        {
                            this.Scheme = scheme;
                            return m_dir;
                        }
                    }
                }
                catch { /* invalid scheme, retry */ }
            }
            return null;
        }

        byte[] m_buffer = new byte[0x80];

        bool ReadIndex (Stream index)
        {
            for (int i = 0; i < m_count; ++i)
            {
                if (m_buffer.Length != index.Read (m_buffer, 0, m_buffer.Length))
                    break;
                var name = Binary.GetCString (m_buffer, 0, 0x50);
                var entry = FormatCatalog.Instance.Create<PcfEntry> (name);
                entry.Offset = LittleEndian.ToInt64 (m_buffer, 0x50) + m_base_offset;
                entry.UnpackedSize = LittleEndian.ToUInt32 (m_buffer, 0x58);
                entry.Size   = LittleEndian.ToUInt32 (m_buffer, 0x60);
                if (!entry.CheckPlacement (m_file.MaxOffset))
                    return false;
                entry.Flags  = LittleEndian.ToUInt32 (m_buffer, 0x68);
                entry.Key    = new ArraySegment<byte> (m_buffer, 0x78, 8).ToArray();
                entry.IsPacked = entry.UnpackedSize != entry.Size;
                m_dir.Add (entry);
            }
            return m_dir.Count > 0;
        }
    }

    internal class PrimelScheme
    {
        public Stream TransformStream (Stream input, byte[] key, uint flags)
        {
            var key1 = GenerateKey (key);
            var iv   = GenerateKey (key1);

            ICryptoTransform decryptor;
            switch (flags & 0xF0000)
            {
            case 0x10000:
                decryptor = new Primel1Encyption (key1, iv);
                break;
            case 0x20000:
                decryptor = new Primel2Encyption (key1, iv);
                break;
            case 0x30000:
                decryptor = new Primel3Encyption (key1, iv);
                break;
            case 0x80000: // RC6
                decryptor = new GameRes.Cryptography.RC6 (key1, iv);
                break;

            case 0xA0000: // AES
                using (var aes = Rijndael.Create())
                {
                    aes.Mode = CipherMode.CFB;
                    aes.Padding = PaddingMode.Zeros;
                    decryptor = aes.CreateDecryptor (key1, iv);
                }
                break;

            default: // not encrypted
                return input;
            }
            input = new InputCryptoStream (input, decryptor);
            try
            {
                if (0 != (flags & 0xFF))
                {
                    input = new RangePackedStream (input);
                }
                switch (flags & 0xF00)
                {
                case 0x400:
                    input = new RlePackedStream (input);
                    input = new MtfPackedStream (input);
                    break;
                case 0x700:
                    input = new LzssPackedStream (input);
                    break;
                }
                return input;
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        byte[] GenerateKey (byte[] seed)
        {
            var hash = ComputeHash (seed);
            var key = new byte[0x10];
            for (int i = 0; i < hash.Length; ++i)
            {
                key[i & 0xF] ^= hash[i];
            }
            return key;
        }

        protected virtual byte[] ComputeHash (byte[] seed)
        {
            var sha = new Primel.SHA256();
            return sha.ComputeHash (seed);
        }
    }

    internal class PrimelSchemeV2 : PrimelScheme
    {
        protected override byte[] ComputeHash (byte[] seed)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return sha.ComputeHash (seed);
        }
    }
}
