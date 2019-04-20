//! \file       ArcNEKO.cs
//! \date       Fri Mar 13 02:27:53 2015
//! \brief      Nekopack archive format implementation.
//
// Copyright (C) 2015-2016 by morkt
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
using System.Diagnostics;
using System.IO;
using System.Linq;
using GameRes.Utility;

namespace GameRes.Formats.Neko
{
    /// <summary>
    /// Interface that represents archive format and encryption details.
    /// </summary>
    internal interface INekoFormat
    {
        /// <summary>
        /// Decrypt chunk of data using specified key.
        /// </summary>
        void Decrypt (uint key, byte[] input, int offset, int length);
        /// <summary>
        /// Get a hash corresponding to a string stored in a supplied byte array.
        /// </summary>
        uint HashFromName (byte[] str, int offset, int length);

        /// <summary>
        /// Read a directory record from archive index.
        /// </summary>
        DirRecord ReadDir (IBinaryStream input);
        /// <summary>
        /// Returns offset of an entry that immediately follows specified one.
        /// </summary>
        long NextOffset (Entry entry);
    }

    internal struct DirRecord
    {
        public uint Hash;
        public int  FileCount;
    }

    internal sealed class IndexReader : IDisposable
    {
        IBinaryStream   m_input;
        int             m_index_size;
        long            m_max_offset;
        INekoFormat     m_format;

        public IndexReader (ArcView file, INekoFormat enc, byte[] index, int index_size)
        {
            m_input = new BinMemoryStream (index, 0, index_size, file.Name);
            m_index_size = index_size;
            m_max_offset = file.MaxOffset;
            m_format = enc;
        }

        public List<Entry> Parse (long current_offset)
        {
            var names_map = GetNamesMap (KnownDirNames);
            var files_map = GetNamesMap (KnownFileNames);

            var dir = new List<Entry>();
            while (m_input.Position < m_index_size)
            {
                var dir_info = m_format.ReadDir (m_input);
                string dir_name;
                if (!names_map.TryGetValue (dir_info.Hash, out dir_name))
                    dir_name = dir_info.Hash.ToString ("X8");
                dir.Capacity = dir.Count + dir_info.FileCount;
                for (int i = 0; i < dir_info.FileCount; ++i)
                {
                    uint name_hash = m_input.ReadUInt32();
                    uint size = m_input.ReadUInt32();
                    string file_name;
                    string type = "";
                    if (!files_map.TryGetValue (name_hash, out file_name))
                        file_name = name_hash.ToString ("X8");
                    else
                        type = FormatCatalog.Instance.GetTypeFromName (file_name);
                    var entry = new Entry
                    {
                        Name = string.Format ("{0}/{1}", dir_name, file_name),
                        Type = type,
                        Offset = current_offset,
                        Size = size,
                    };
                    if (!entry.CheckPlacement (m_max_offset))
                        return null;
                    dir.Add (entry);
                    current_offset = m_format.NextOffset (entry);
                }
            }
            return dir.Count > 0 ? dir : null;
        }

        static string[]  KnownDirNames { get { return s_known_dir_names; } }
        static string[] KnownFileNames { get { return s_known_file_names.Value; } }

        static string[] s_known_dir_names = {
            "image/actor", "image/back", "image/mask", "image/visual", "image/actor/big",
            "image/face", "image/actor/b", "image/actor/bb", "image/actor/s", "image/actor/ss",
            "sound/bgm", "sound/env", "sound/se", "voice", "script", "system", "count",
        };

        static Lazy<string[]> s_known_file_names = new Lazy<string[]> (ReadNekoPackLst);

        IDictionary<uint, string> GetNamesMap (string[] known_names)
        {
            var map = new Dictionary<uint, string> (known_names.Length);
            var buffer = new byte[0x100];
            foreach (var name in known_names)
            {
                int length = Encodings.cp932.GetBytes (name, 0, name.Length, buffer, 0);
                uint hash = m_format.HashFromName (buffer, 0, length);
                if (!map.ContainsKey (hash))
                    map[hash] = name;
                else if (!map[hash].Equals (name, StringComparison.InvariantCultureIgnoreCase))
                    Trace.WriteLine (string.Format ("{0}: hash collision with {1} [{2:X8}]", name, map[hash], hash),
                                     "[NEKOPACK]");
            }
            return map;
        }

        public void DetectTypes (IEnumerable<Entry> dir, Func<Entry, uint> get_signature)
        {
            foreach (var entry in dir.Where (e => string.IsNullOrEmpty (e.Type)))
            {
                if (entry.Name.HasAnyOfExtensions ("txt", "nut"))
                {
                    entry.Type = "script";
                    continue;
                }
                uint signature = get_signature (entry);
                var res = AutoEntry.DetectFileType (signature);
                if (res != null)
                    entry.ChangeType (res);
                else if (entry.Name.StartsWith ("script/"))
                    entry.Type = "script";
            }
        }

        static string[] ReadNekoPackLst ()
        {
            try
            {
                var names = new List<string>();
                FormatCatalog.Instance.ReadFileList ("nekopack.lst", name => names.Add (name));
                return names.ToArray();
            }
            catch (Exception X)
            {
                Trace.WriteLine (X.Message, "[NEKOPACK]");
                return new string[0];
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
        }
        #endregion
    }

    internal class NekoArchive : ArcFile
    {
        public readonly INekoFormat Decoder;

        public NekoArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, INekoFormat decoder)
            : base (arc, impl, dir)
        {
            Decoder = decoder;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class Pak1Opener : ArchiveFormat
    {
        public override string         Tag { get { return "NEKOPACK/1"; } }
        public override string Description { get { return "NekoPack resource archive"; } }
        public override uint     Signature { get { return 0x4f4b454e; } } // "NEKO"
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public Pak1Opener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "PACK"))
                return null;
            int length = file.View.ReadInt32 (0x14);
            if (length < 0x10 || length >= file.MaxOffset)
                return null;
            uint seed = file.View.ReadUInt32 (8);
            var dec = new NekoEncryption32bit (seed);
            byte[] index = ReadBlock (file.View, dec, 0x10, out length);

            using (var reader = new IndexReader (file, dec, index, length))
            {
                var dir = reader.Parse (0x18+length);
                if (null == dir)
                    return null;
                byte[] buffer = new byte[0x10];
                reader.DetectTypes (dir, entry => {
                    file.View.Read (entry.Offset, buffer, 0, 0x10);
                    uint hash = LittleEndian.ToUInt32 (buffer, 0);
                    if (0 != hash)
                        dec.Decrypt (hash, buffer, 8, 8);
                    return LittleEndian.ToUInt32 (buffer, 8);
                });
                return new NekoArchive (file, this, dir, dec);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pak = arc as NekoArchive;
            if (null == pak)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            int length;
            var data = ReadBlock (arc.File.View, pak.Decoder, entry.Offset, out length);
            return new BinMemoryStream (data, 0, length, entry.Name);
        }

        static uint HashFromString (uint seed, byte[] str, int offset, int length)
        {
            // 0x00000000, "NEKOPACK" -> 0xAC0BF0B1
            // 0xAC0BF0B1, "RBS0011"  -> 0x9B75ADA7
            uint result = seed;
            for (int i = 0; i < length; ++i)
            {
                byte c = str[offset+i];
                result += c + result * 37;
            }
            return result;
        }

        static uint CalcParity (uint a1, uint a2)
        {
            uint v1 = (a2 ^ ((a2 ^ ((a2 ^ ((a2 ^ a1) + 1566083941u)) - 899497514u)) - 1894007588u)) + 1812433253u;
            int v2 = (int)(((a2 ^ ((a2 ^ a1) + 1566083941u)) - 899497514u) >> 27);
            return v1 << v2 | v1 >> (32-v2);
        }

        static byte[] ReadBlock (ArcView.Frame view, INekoFormat enc, long offset, out int length)
        {
            uint hash = view.ReadUInt32 (offset);
            length = view.ReadInt32 (offset+4);

            int aligned_size = (length+7) & ~7;
            byte[] buffer = new byte[aligned_size];
            length = view.Read (offset+8, buffer, 0, (uint)length);
            if (0 != hash)
            {
                enc.Decrypt (hash, buffer, 0, aligned_size);
            }
            return buffer;
        }
    }

    internal class NekoEncryption32bit : INekoFormat
    {
        readonly uint m_seed;

        public NekoEncryption32bit (uint seed)
        {
            m_seed = seed;
        }

        public void Decrypt (uint hash, byte[] buf, int offset, int length)
        {
            if (offset < 0 || offset > buf.Length)
                throw new ArgumentException ("offset");
            int count = Math.Min (length, buf.Length-offset) / 8;
            if (0 == count)
                return;
            ulong key = KeyFromHash (hash);
            unsafe
            {
                fixed (byte* data = buf)
                {
                    ulong* first = (ulong*)(data + offset);
                    ulong* last = first + count;
                    while (first != last)
                    {
                        ulong v = *first ^ key;
                        key = MMX.PAddW (key, v);
                        *first++ = v;
                    }
                }
            }
        }

        public uint HashFromName (byte[] str, int offset, int length)
        {
            // 0x9B75ADA7, "script"    -> 0x0DDB021E
            // 0x9B75ADA7, "start.bin" -> 0xCB8FB53B
            uint hash = m_seed;
            for (int i = 0; i < length; ++i)
            {
                byte c = str[offset+i];
                hash = 81 * (ShiftMap[c] ^ hash);
            }
            return hash;
        }

        public DirRecord ReadDir (IBinaryStream input)
        {
            return new DirRecord {
                Hash      = input.ReadUInt32(),
                FileCount = input.ReadInt32()
            };
        }

        public long NextOffset (Entry entry)
        {
            return entry.Offset + entry.Size + 8;
        }

        public static ulong KeyFromHash (uint hash)
        {
            uint v2 = hash ^ (hash + 1566083941u);
            uint v3 = v2 ^ (hash - 899497514u);
            ulong result = v3 ^ (v2 - 1894007588u);
            return result | (result ^ (v3 + 1812433253u)) << 32;
        }

        static readonly byte[] ShiftMap = new byte[]
        {
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x38, 0x2F, 0x33, 0x3C, 0x40, 0x3B, 0x2A, 0x2E, 0x31, 0x30, 0x26, 0x44, 0x35, 0x28, 0x3E, 0x12,
            0x02, 0x22, 0x06, 0x20, 0x1A, 0x1C, 0x0F, 0x11, 0x18, 0x17, 0x42, 0x2B, 0x3A, 0x37, 0x34, 0x0C,
            0x41, 0x08, 0x1D, 0x07, 0x15, 0x21, 0x05, 0x1E, 0x0A, 0x14, 0x0E, 0x10, 0x09, 0x27, 0x1F, 0x0B,
            0x23, 0x16, 0x0D, 0x01, 0x25, 0x04, 0x1B, 0x03, 0x13, 0x24, 0x19, 0x2D, 0x12, 0x29, 0x32, 0x3F,
            0x3D, 0x08, 0x1D, 0x07, 0x15, 0x21, 0x05, 0x1E, 0x0A, 0x14, 0x0E, 0x10, 0x09, 0x27, 0x1F, 0x0B,
            0x23, 0x16, 0x0D, 0x01, 0x25, 0x04, 0x1B, 0x03, 0x13, 0x24, 0x19, 0x2C, 0x39, 0x43, 0x36, 0x00,
            0x4B, 0xA9, 0xA7, 0xAF, 0x50, 0x52, 0x91, 0x9F, 0x47, 0x6B, 0x96, 0xAB, 0x87, 0xB5, 0x9B, 0xBB,
            0x99, 0xA4, 0xBF, 0x5C, 0xC6, 0x9C, 0xC2, 0xC4, 0xB6, 0x4F, 0xB8, 0xC1, 0x85, 0xA8, 0x51, 0x7E,
            0x5F, 0x82, 0x73, 0xC7, 0x90, 0x4E, 0x45, 0xA5, 0x7A, 0x63, 0x70, 0xB3, 0x79, 0x83, 0x60, 0x55,
            0x5B, 0x5E, 0x68, 0xBA, 0x53, 0xA1, 0x67, 0x97, 0xAC, 0x71, 0x81, 0x59, 0x64, 0x7C, 0x9D, 0xBD,
            0x9D, 0xBD, 0x95, 0xA0, 0xB2, 0xC0, 0x6F, 0x6A, 0x54, 0xB9, 0x6D, 0x88, 0x77, 0x48, 0x5D, 0x72,
            0x49, 0x93, 0x57, 0x65, 0xBE, 0x4A, 0x80, 0xA2, 0x5A, 0x98, 0xA6, 0x62, 0x7F, 0x84, 0x75, 0xBC,
            0xAD, 0xB1, 0x6E, 0x76, 0x8B, 0x9E, 0x8C, 0x61, 0x69, 0x8D, 0xB4, 0x78, 0xAA, 0xAE, 0x8F, 0xC3,
            0x58, 0xC5, 0x74, 0xB7, 0x8E, 0x7D, 0x89, 0x8A, 0x56, 0x4D, 0x86, 0x94, 0x9A, 0x4C, 0x92, 0xB0,
        };
    }

    [Export(typeof(ArchiveFormat))]
    public class Pak2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "NEKOPACK/2"; } }
        public override string Description { get { return "NekoPack resource archive"; } }
        public override uint     Signature { get { return 0x4F4B454E; } } // "NEKO"
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public Pak2Opener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "PACK"))
                return null;

            uint init_key = file.View.ReadUInt32 (0xC);
            var xdec = new NekoXCode (init_key);
            uint seed = file.View.ReadUInt32 (0x10);
            var buffer = file.View.ReadBytes (0x14, 8);
            xdec.Decrypt (seed, buffer, 0, 8);

            uint index_size = LittleEndian.ToUInt32 (buffer, 0);
            if (index_size < 0x14 || index_size != LittleEndian.ToUInt32 (buffer, 4))
                return null;
            var index = new byte[(index_size + 7u) & ~7u];
            if (file.View.Read (0x1C, index, 0, index_size) < index_size)
                return null;
            xdec.Decrypt (seed, index, 0, index.Length);

            using (var reader = new IndexReader (file, xdec, index, (int)index_size))
            {
                var dir = reader.Parse (0x1C+index.Length);
                if (null == dir)
                    return null;
                reader.DetectTypes (dir, entry => {
                    uint key = file.View.ReadUInt32 (entry.Offset);
                    file.View.Read (entry.Offset+12, buffer, 0, 8);
                    xdec.Decrypt (key, buffer, 0, 8);
                    return LittleEndian.ToUInt32 (buffer, 0);
                });
                return new NekoArchive (file, this, dir, xdec);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var narc = arc as NekoArchive;
            if (null == narc || entry.Size <= 12)
                return base.OpenEntry (arc, entry);
            uint key = arc.File.View.ReadUInt32 (entry.Offset);
            var data = new byte[entry.Size];
            arc.File.View.Read (entry.Offset+4, data, 0, 8);
            narc.Decoder.Decrypt (key, data, 0, 8);
            int size = LittleEndian.ToInt32 (data, 0);
            if (size != LittleEndian.ToInt32 (data, 4))
            {
                Trace.WriteLine ("entry decryption failed", "[NEKOPACK]");
                return base.OpenEntry (arc, entry);
            }
            int aligned_size = (size + 7) & ~7;
            if (aligned_size > data.Length)
                data = new byte[aligned_size];
            arc.File.View.Read (entry.Offset+12, data, 0, (uint)size);
            narc.Decoder.Decrypt (key, data, 0, aligned_size);
            return new BinMemoryStream (data, 0, size, entry.Name);
        }
    }

    internal class NekoXCode : INekoFormat
    {
        uint            m_seed;
        uint[]          m_random;
        SimdProgram     m_program;

        public NekoXCode (uint init_key)
        {
            m_seed = init_key;
            m_random = InitTable (init_key);
            m_program = new SimdProgram (init_key);
        }

        public void Decrypt (uint key, byte[] input, int offset, int length)
        {
            for (int i = 1; i < 7; ++i)
            {
                uint src = key % 0x28 * 2;
                m_program.mm[i] = m_random[src] | (ulong)m_random[src+1] << 32;
                key /= 0x28;
            }
            m_program.Execute (input, offset, length);
        }

        public uint HashFromName (byte[] str, int offset, int length)
        {
            uint hash = m_seed;
            for (int i = 0; i < length; ++i)
            {
                hash = 0x100002A * (ShiftMap[str[offset+i] & 0xFF] ^ hash);
            }
            return hash;
        }

        public DirRecord ReadDir (IBinaryStream input)
        {
            uint hash = input.ReadUInt32();
            int count = input.ReadInt32();
            if (count != input.ReadInt32())
                throw new InvalidFormatException();
            return new DirRecord { Hash = hash, FileCount = count };
        }

        public long NextOffset (Entry entry)
        {
            return entry.Offset + entry.Size;
        }

        static readonly byte[] ShiftMap = {
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xc9, 0xca, 0x00, 0xcb, 0xcc, 0xcd, 0xce, 0xcf, 0xd0, 0xd1, 0x00, 0xd2, 0xd3, 0x27, 0x25, 0xc8,
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x00, 0xd4, 0x00, 0xd5, 0x00, 0x00,
            0xd6, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19,
            0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20, 0x21, 0x22, 0x23, 0x24, 0xd7, 0xc8, 0xd8, 0xd9, 0x26,
            0xda, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19,
            0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20, 0x21, 0x22, 0x23, 0x24, 0xdb, 0x00, 0xdc, 0xdd, 0x00,
            0x29, 0x2a, 0x2b, 0x2c, 0x2d, 0x2e, 0x2f, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38,
            0x39, 0x3a, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f, 0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
            0x49, 0x4a, 0x4b, 0x4c, 0x4d, 0x4e, 0x4f, 0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58,
            0x59, 0x5a, 0x5b, 0x5c, 0x5d, 0x5e, 0x5f, 0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
            0x69, 0x6a, 0x6b, 0x6c, 0x6d, 0x6e, 0x6f, 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78,
            0x79, 0x7a, 0x7b, 0x7c, 0x7d, 0x7e, 0x7f, 0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88,
            0x89, 0x8a, 0x8b, 0x8c, 0x8d, 0x8e, 0x8f, 0x90, 0x91, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98,
            0x99, 0x9a, 0x9b, 0x9c, 0x9d, 0x9e, 0x9f, 0xa0, 0xa1, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7, 0xa8,
        };

        class SimdProgram
        {
            public ulong[] mm = new ulong[7];

            Action[]    m_transform = new Action[4];
            Action[]    m_shuffle = new Action[6];

            Action<int>[]   TransformList;
            Action[]        ShuffleList;

            public SimdProgram (uint key)
            {
                TransformList = new Action<int>[] {
                    pxor, paddb, paddw, paddd, psubb, psubw, psubd,
                    pxor, psubb, psubw, psubd, paddb, paddw, paddd,
                };
                ShuffleList = new Action[] {
                    paddq_1_2, paddq_2_3, paddq_3_4, paddq_4_5, paddq_5_6, paddq_6_1,
                };

                GenerateProgram (key);
            }

            void pxor (int i) { mm[0] ^= mm[i]; }
            void paddb (int i) { mm[0] = MMX.PAddB (mm[0], mm[i]); }
            void paddw (int i) { mm[0] = MMX.PAddW (mm[0], mm[i]); }
            void paddd (int i) { mm[0] = MMX.PAddD (mm[0], mm[i]); }
            void psubb (int i) { mm[0] = MMX.PSubB (mm[0], mm[i]); }
            void psubw (int i) { mm[0] = MMX.PSubW (mm[0], mm[i]); }
            void psubd (int i) { mm[0] = MMX.PSubD (mm[0], mm[i]); }

            void paddq_1_2 () { mm[1] += mm[2]; }
            void paddq_2_3 () { mm[2] += mm[3]; }
            void paddq_3_4 () { mm[3] += mm[4]; }
            void paddq_4_5 () { mm[4] += mm[5]; }
            void paddq_5_6 () { mm[5] += mm[6]; }
            void paddq_6_1 () { mm[6] += mm[1]; }

            void GenerateProgram (uint key)
            {
                int t1 = 7 + (int)(key >> 28);
                int cmd_base = (int)key & 0xffff;
                int arg_base = (int)(key >> 16) & 0xfff;
                for (int i = 3; i >= 0; --i)
                {
                    int cmd = ((cmd_base >> (4 * i)) + t1) % TransformList.Length;
                    int arg = (arg_base >> (3 * i)) % 6 + 1;
                    m_transform[3-i] = () => TransformList[cmd] (arg);
                }
                for (uint i = 0; i < 6; ++i)
                {
                    m_shuffle[i] = ShuffleList[(i + key) % (uint)ShuffleList.Length];
                }
            }

            public unsafe void Execute (byte[] input, int offset, int length)
            {
                if (offset < 0 || offset > input.Length)
                    throw new ArgumentException ("offset");
                int count = Math.Min (length, input.Length-offset) / 8;
                if (0 == count)
                    return;
                fixed (byte* data = &input[offset])
                {
                    ulong* data64 = (ulong*)data;
                    for (;;)
                    {
                        mm[0] = *data64;
                        foreach (var cmd in m_transform)
                            cmd();
                        *data64++ = mm[0];
                        if (1 == count--)
                            break;
                        foreach (var cmd in m_shuffle)
                            cmd();
                    }
                }
            }
        }

        static uint[] InitTable (uint key)
        {
            uint a = 0;
            uint b = 0;
            do
            {
                a <<= 1;
                b ^= 1;
                a = ((a | b) << (int)(key & 1)) | b;
                key >>= 1;
            }
            while (0 == (a & 0x80000000));
            key = a << 1;
            a = key + Binary.BigEndian (key);
            byte count = (byte)key;
            do
            {
                b = key ^ a;
                a = (b << 4) ^ (b >> 4) ^ (b << 3) ^ (b >> 3) ^ b;
            }
            while (--count != 0);

            var table = new uint[154];
            for (int i = 0; i < table.Length; ++i)
            {
                b = key ^ a;
                a = (b << 4) ^ (b >> 4) ^ (b << 3) ^ (b >> 3) ^ b;
                table[i] = a;
            }
            return table;
        }
    }
}
