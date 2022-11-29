//! \file       ArcSX.cs
//! \date       2022 Apr 29
//! \brief      SakanaGL resource archive implementation.
//
// Copyright (C) 2022 by morkt
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

using GameRes.Utility;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;

namespace GameRes.Formats.Sakana
{
    internal class SxEntry : PackedEntry
    {
        public ushort   Flags;

        public bool IsEncrypted  { get { return 0 == (Flags & 0x10); } }
    }

    [Export(typeof(ArchiveFormat))]
    public class SxOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SXSTORAGE"; } }
        public override string Description { get { return "SakanaGL engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        const uint DefaultKey = 0x2E76034B;
        
        public override ArcFile TryOpen (ArcView file)
        {
            var base_name = Path.GetFileName (file.Name);
            var sx_name = base_name.Substring (0, 4) + "(00).sx";
            sx_name = VFS.ChangeFileName (file.Name, sx_name);
            if (!VFS.FileExists (sx_name) || file.Name.Equals (sx_name, StringComparison.InvariantCultureIgnoreCase))
                return null;
            byte[] index_data;
            using (var sx = VFS.OpenView (sx_name))
            {
                if (sx.MaxOffset <= 0x10)
                    return null;
                if (!sx.View.AsciiEqual (0, "SSXXDEFL"))
                    return null;
                int key = Binary.BigEndian (sx.View.ReadInt32 (8));
                int length = (int)(sx.MaxOffset - 0x10);
                var index_packed = sx.View.ReadBytes (0x10, (uint)length);

                long lkey = (long)key + length;
                lkey = key ^ (961 * lkey - 124789) ^ DefaultKey;
                uint key_lo = (uint)lkey;
                uint key_hi = (uint)(lkey >> 32) ^ 0x2E6;
                DecryptData (index_packed, key_lo, key_hi);

                index_data = UnpackZstd (index_packed);
            }
            using (var index = new BinMemoryStream (index_data))
            {
                var reader = new SxIndexDeserializer (index, file.MaxOffset);
                var dir = reader.Deserialize();
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var sx_entry = entry as SxEntry;
            if (null == sx_entry || (!sx_entry.IsEncrypted && !sx_entry.IsPacked))
                return base.OpenEntry (arc, entry);
            var input = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            if (sx_entry.IsEncrypted)
            {
                uint key_lo = (uint)(entry.Offset >> 4) ^ (entry.Size << 16) ^ DefaultKey;
                uint key_hi = (entry.Size >> 16) ^ 0x2E6;
                DecryptData (input, key_lo, key_hi);
            }
            if (sx_entry.IsPacked)
                input = UnpackZstd (input);
            return new BinMemoryStream (input, entry.Name);
        }

        internal static byte[] UnpackZstd (byte[] data)
        {
            int unpacked_size = BigEndian.ToInt32 (data, 0);
            using (var dec = new ZstdNet.Decompressor())
            {
                var packed = new ArraySegment<byte> (data, 4, data.Length - 4);
                return dec.Unwrap (packed, unpacked_size);
            }
        }

        internal static void DecryptData (byte[] data, uint key_lo, uint key_hi)
        {
            if (data.Length < 4)
                return;
            key_lo ^= 0x159A55E5;
            key_hi ^= 0x075BCD15;
            uint v1 = key_hi ^ (key_hi << 11) ^ ((key_hi ^ (key_hi << 11)) >> 8) ^ 0x549139A;
            uint v2 = v1 ^ key_lo ^ (key_lo << 11) ^ ((key_lo ^ (key_lo << 11) ^ (v1 >> 11)) >> 8);
            uint v3 = v2 ^ (v2 >> 19) ^ 0x8E415C26;
            uint v4 = v3 ^ (v3 >> 19) ^ 0x4D9D5BB8;
            int count = data.Length / 4;
            unsafe
            {
                fixed (byte* data_raw = data)
                {
                    uint* data32 = (uint*)&data_raw[0];
                    for (int i = 0; i < count; ++i)
                    {
                        uint t1 = v4 ^ v1 ^ (v1 << 11) ^ ((v1 ^ (v1 << 11) ^ (v4 >> 11)) >> 8);
                        uint t2 = v2 ^ (v2 << 11);
                        v2 = v4;
                        v4 = t1 ^ t2 ^ ((t2 ^ (t1 >> 11)) >> 8);
                        data32[i] ^= (t1 >> 4) ^ (v4 << 12);
                        v1 = v3;
                        v3 = t1;
                    }
                }
            }
        }
    }

    internal class SxIndexDeserializer
    {
        IBinaryStream   m_index;
        long            m_max_offset;
        string[]        m_name_list;
        List<Entry>     m_dir;

        public SxIndexDeserializer (IBinaryStream index, long max_offset)
        {
            m_index = index;
            m_max_offset = max_offset;
        }

        public List<Entry> Deserialize ()
        {
            m_index.Position = 8;
            int count = Binary.BigEndian (m_index.ReadInt32());
            m_name_list = new string[count];
            for (int i = 0; i < count; ++i)
            {
                int length = m_index.ReadUInt8();
                m_name_list[i] = m_index.ReadCString (length, Encoding.UTF8);
            }

            count = Binary.BigEndian (m_index.ReadInt32());
            m_dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                m_index.ReadUInt16();
                ushort flags = Binary.BigEndian (m_index.ReadUInt16());
                uint offset  = Binary.BigEndian (m_index.ReadUInt32());
                uint size    = Binary.BigEndian (m_index.ReadUInt32());
                var entry = new SxEntry {
                    Flags  = flags,
                    Offset = (long)offset << 4,
                    Size   = size,
                    IsPacked = 0 != (flags & 0x03),
                };
                if (!entry.CheckPlacement (m_max_offset))
                    return null;
                m_dir.Add (entry);
            }

            count = Binary.BigEndian (m_index.ReadUInt16());
            for (int i = 0; i < count; ++i)
            {
                m_index.ReadUInt32();
                m_index.ReadUInt32();
                m_index.ReadUInt32();
                Binary.BigEndian (m_index.ReadUInt32()); // archive body length
                m_index.ReadUInt64();
                m_index.Seek (16, SeekOrigin.Current); // MD5 sum
            }

            count = Binary.BigEndian (m_index.ReadUInt16());
            if (count > 0)
                m_index.Seek (count * 24, SeekOrigin.Current);
            DeserializeTree();
            return m_dir;
        }

        void DeserializeTree (string path = "")
        {
            int count = Binary.BigEndian (m_index.ReadUInt16());
            int name_index = Binary.BigEndian (m_index.ReadInt32());
            int file_index = Binary.BigEndian (m_index.ReadInt32());
            var name = Path.Combine (path, m_name_list[name_index]);
            if (-1 == file_index)
            {
                for (int i = 0; i < count; ++i)
                {
                    DeserializeTree (name);
                }
            }
            else
            {
                m_dir[file_index].Name = name;
                m_dir[file_index].Type = FormatCatalog.Instance.GetTypeFromName (name);
            }
        }
    }
}
