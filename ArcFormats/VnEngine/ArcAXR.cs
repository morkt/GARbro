//! \file       ArcAXR.cs
//! \date       Fri Jul 01 13:43:07 2016
//! \brief      GEM/vnengine resource archive.
//
// Copyright (C) 2016 by morkt
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

namespace GameRes.Formats.VnEngine
{
    internal class AxrArchive : ArcFile
    {
        public readonly byte[]  KeyTable = new byte[0x400];

        public AxrArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, uint key)
            : base (arc, impl, dir)
        {
            AxrOpener.Decrypt (KeyTable, key, 0x400);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class AxrOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AXR"; } }
        public override string Description { get { return "GEM/vnengine resource archive"; } }
        public override uint     Signature { get { return 0x65525841; } } // 'AXRe'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            var header = file.View.ReadBytes (0, 0x10);
            byte checksum = header[4];
            for (int i = 1; i < 8; ++i)
            {
                checksum ^= Binary.RotByteR (header[4+i], i);
            }
            uint t = MutateKey (MutateKey (LittleEndian.ToUInt32 (header, 8) ^ LittleEndian.ToUInt32 (header, 0)));
            uint key = LittleEndian.ToUInt32 (header, 4);
            uint index_size = t ^ key;
            uint stored_checksum = LittleEndian.ToUInt32 (header, 12) ^ MutateKey (t);
            if (index_size < 8 || checksum != stored_checksum)
                return null;
            var index = new byte[(index_size + 4) & ~3u];
            if (index_size != file.View.Read (0x10, index, 0, index_size))
                return null;
            Decrypt (index, index_size, index.Length);
            index[index_size] = 0;
            var dir = new List<Entry>();
            int current_offset = 0;
            while (current_offset+8 < (int)index_size)
            {
                uint offset = LittleEndian.ToUInt32 (index, current_offset);
                uint size   = LittleEndian.ToUInt32 (index, current_offset+4);
                current_offset += 8;
                int name_end = Array.IndexOf<byte> (index, 0, current_offset);
                int name_length = name_end - current_offset;
                if (0 == name_length)
                    break;
                var name = Encodings.cp932.GetString (index, current_offset, name_length);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = offset;
                entry.Size   = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                current_offset += (name_length + 4) & -4;
            }
            if (0 == dir.Count)
                return null;
            return new AxrArchive (file, this, dir, key);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var axr = arc as AxrArchive;
            if (null == axr)
                return input;
            return new AxrEncryptedStream (input, axr.KeyTable);
        }

        static uint MutateKey (uint key)
        {
            key ^= (key & 0xFFF) << 17;
            return ~(key ^ (key << 18 | key >> 15));
        }

        internal static void Decrypt (byte[] data, uint key, int count)
        {
            if (count > data.Length)
                throw new ArgumentException ("count");
            count /= 4;
            if (0 == count)
                return;
            unsafe
            {
                fixed (byte* data8 = data)
                {
                    uint* data32 = (uint*)data8;
                    for (int i = 0; i < count; ++i)
                    {
                        key = MutateKey (key);
                        uint v = *data32 ^ key;
                        *data32++ = v;
                        key += v;
                    }
                }
            }
        }
    }

    internal class AxrEncryptedStream : ProxyStream
    {
        byte[]  m_key;

        public AxrEncryptedStream (Stream input, byte[] key, bool leave_open = false)
            : base (input, leave_open)
        {
            m_key = key;
        }

        public override bool CanWrite { get { return false; } }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int start = (int)Position;
            int read = BaseStream.Read (buffer, offset, count);
            for (int i = 0; i < read; ++i)
            {
                buffer[offset+i] ^= m_key[start++ & 0x3FF];
            }
            return read;
        }

        public override int ReadByte ()
        {
            int start = (int)Position & 0x3FF;
            int b = BaseStream.ReadByte();
            if (-1 != b)
                b ^= m_key[start];
            return b;
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("AxrEncryptedStream.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new NotSupportedException ("AxrEncryptedStream.WriteByte method is not supported");
        }
    }
}
