//! \file       ArcQLIE.cs
//! \date       Mon Jun 15 04:03:18 2015
//! \brief      QLIE engine archives implementation.
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
using System.IO;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using GameRes.Utility;
using GameRes.Formats.Properties;
using GameRes.Formats.Strings;

namespace GameRes.Formats.Qlie
{
    internal class QlieEntry : PackedEntry
    {
        public bool IsEncrypted;
        public uint Hash;
        public byte[] RawName;

        /// <summary>
        /// Data from a separate key file "key.fkey" that comes with installed game.
        /// null if not used.
        /// </summary>
        public byte[] KeyFile;
    }

    internal class QlieArchive : ArcFile
    {
        /// <summary>
        /// Hash generated from the key data contained within archive index.
        /// </summary>
        public uint Hash;

        /// <summary>
        /// Internal game data used to decrypt encrypted entries.
        /// null if not used.
        /// </summary>
        public byte[] GameKeyData;

        public QlieArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir,
                            uint hash, byte[] key_data)
            : base (arc, impl, dir)
        {
            Hash = hash;
            GameKeyData = key_data;
        }
    }

    internal class QlieOptions : ResourceOptions
    {
        public byte[] GameKeyData;
    }

    [Serializable]
    public class QlieScheme : ResourceScheme
    {
        public Dictionary<string, byte[]> KnownKeys;
    }

    [Export(typeof(ArchiveFormat))]
    public class PackOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PACK/QLIE"; } }
        public override string Description { get { return "QLIE engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public PackOpener ()
        {
            Extensions = new string [] { "pack" };
        }

        /// <summary>
        /// Possible locations of the 'key.fkey' file relative to an archive being accessed.
        /// </summary>
        static readonly string[] KeyLocations = { ".", "..", @"..\DLL", "DLL" };

        public static Dictionary<string, byte[]> KnownKeys = new Dictionary<string, byte[]>();

        public override ResourceScheme Scheme
        {
            get { return new QlieScheme { KnownKeys = KnownKeys }; }
            set { KnownKeys = ((QlieScheme)value).KnownKeys; }
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset <= 0x1c)
                return null;
            long index_offset = file.MaxOffset - 0x1c;
            if (!file.View.AsciiEqual (index_offset, "FilePackVer")
                || !file.View.AsciiEqual (index_offset+0xC, ".0"))
                return null;
            int pack_version = file.View.ReadByte (index_offset+0xB) - '0';
            if (pack_version < 1 || pack_version > 3)
                throw new NotSupportedException ("Not supported QLIE archive version");
            int count = file.View.ReadInt32 (index_offset+0x10);
            if (!IsSaneCount (count))
                return null;
            index_offset = file.View.ReadInt64 (index_offset+0x14);
            if (index_offset < 0 || index_offset >= file.MaxOffset)
                return null;

            byte[] arc_key = null;
            byte[] key_file = null;
            uint name_key = 0xC4; // default name encryption key for versions 1 and 2
            if (3 == pack_version)
            {
                key_file = FindKeyFile (file);
                // currently, user is prompted to choose encryption scheme only if there's 'key.fkey' file found.
                if (key_file != null)
                    arc_key = QueryEncryption();
                if (null == arc_key)
                    key_file = null;

                var key_data = new byte[0x100];
                file.View.Read (file.MaxOffset-0x41C, key_data, 0, 0x100);
                name_key = GenerateKey (key_data) & 0x0FFFFFFFu;
            }

            var name_buffer = new byte[0x100];
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int name_length = file.View.ReadUInt16 (index_offset);
                if (name_length > name_buffer.Length)
                    name_buffer = new byte[name_length];
                if (name_length != file.View.Read (index_offset+2, name_buffer, 0, (uint)name_length))
                    return null;

                int key = name_length + ((int)name_key ^ 0x3e);
                for (int k = 0; k < name_length; ++k)
                    name_buffer[k] ^= (byte)(((k + 1) ^ key) + k + 1);

                string name = Encodings.cp932.GetString (name_buffer, 0, name_length);
                var entry = FormatCatalog.Instance.Create<QlieEntry> (name);
                if (key_file != null)
                    entry.RawName = name_buffer.Take (name_length).ToArray();

                index_offset += 2 + name_length;
                entry.Offset = file.View.ReadInt64 (index_offset);
                entry.Size   = file.View.ReadUInt32 (index_offset+8);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.UnpackedSize = file.View.ReadUInt32 (index_offset+12);
                entry.IsPacked = 0 != file.View.ReadInt32 (index_offset+0x10);
                entry.IsEncrypted = 0 != file.View.ReadInt32 (index_offset+0x14);
                entry.Hash = file.View.ReadUInt32 (index_offset+0x18);
                entry.KeyFile = key_file;
                if (3 == pack_version && null != arc_key && entry.Name.Contains ("pack_keyfile"))
                {
                    // note that 'pack_keyfile' itself is encrypted using 'key.fkey' file contents.
                    key_file = ReadEntryBytes (file, entry, name_key, arc_key);
                }
                dir.Add (entry);
                index_offset += 0x1c;
            }
            if (pack_version < 3)
                name_key = 0;
            return new QlieArchive (file, this, dir, name_key, arc_key);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var qent = entry as QlieEntry;
            var qarc = arc as QlieArchive;
            if (null == qent || null == qarc || (!qent.IsEncrypted && !qent.IsPacked))
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var data = ReadEntryBytes (arc.File, qent, qarc.Hash, qarc.GameKeyData);
            return new MemoryStream (data);
        }

        private byte[] ReadEntryBytes (ArcView file, QlieEntry entry, uint hash, byte[] game_key)
        {
            var data = new byte[entry.Size];
            file.View.Read (entry.Offset, data, 0, entry.Size);
            if (entry.IsEncrypted)
            {
                if (entry.KeyFile != null)
                    DecryptV3 (data, 0, data.Length, entry.RawName, hash, entry.KeyFile, game_key);
                else
                    Decrypt (data, 0, data.Length, hash);
            }
            if (entry.IsPacked)
            {
                var unpacked = Decompress (data);
                if (null != unpacked)
                    data = unpacked;
            }
            return data;
        }

        private void Decrypt (byte[] buffer, int offset, int length, uint key)
        {
            if (offset < 0)
                throw new ArgumentOutOfRangeException ("offset");
            if (length > buffer.Length || offset > buffer.Length - length)
                throw new ArgumentOutOfRangeException ("length");

            ulong hash = 0xA73C5F9DA73C5F9Dul;
            ulong xor = ((uint)length + key) ^ 0xFEC9753Eu;
            xor |= xor << 32;
            unsafe
            {
                fixed (byte* raw = buffer)
                {
                    ulong* encoded = (ulong*)(raw + offset);
                    for (int i = 0; i < length / 8; ++i)
                    {
                        hash = MMX.PAddD (hash, 0xCE24F523CE24F523ul) ^ xor;
                        xor = *encoded ^ hash;
                        *encoded++ = xor;
                    }
                }
            }
        }

        private void DecryptV3 (byte[] data, int offset, int length, byte[] file_name,
                                uint arc_hash, byte[] key_file, byte[] game_key)
        {
            // play it safe with 'unsafe' sections
            if (offset < 0)
                throw new ArgumentOutOfRangeException ("offset");
            if (length > data.Length || offset > data.Length - length)
                throw new ArgumentOutOfRangeException ("length");

            if (length < 8)
                return;

            uint hash = 0x85F532;
            uint seed = 0x33F641;

            for (uint i = 0; i < file_name.Length; i++)
            {
                hash += (i & 0xFF) * file_name[i];
                seed ^= hash;
            }

            seed += arc_hash ^ (7 * ((uint)data.Length & 0xFFFFFF) + (uint)data.Length
                                + hash + (hash ^ (uint)data.Length ^ 0x8F32DCu));
            seed = 9 * (seed & 0xFFFFFF);

            if (game_key != null)
                seed ^= 0x453A;

            var mt = new QlieMersenneTwister (seed);
            if (key_file != null)
                mt.XorState (key_file);
            if (game_key != null)
                mt.XorState (game_key);

            // game code fills dword[41] table, but only the first 16 qwords are used
            ulong[] table = new ulong[16];
            for (int i = 0; i < table.Length; ++i)
                table[i] = mt.Rand64();

            // compensate for 9 discarded dwords
            for (int i = 0; i < 9; ++i)
                mt.Rand();

            ulong hash64 = mt.Rand64();
            uint t = mt.Rand() & 0xF;
            unsafe
            {
                fixed (byte* raw_data = &data[offset])
                {
                    ulong* data64 = (ulong*)raw_data;
                    int qword_length = length / 8;
                    for (int i = 0; i < qword_length; ++i)
                    {
                        hash64 = MMX.PAddD (hash64 ^ table[t], table[t]);

                        ulong d = data64[i] ^ hash64;
                        data64[i] = d;

                        hash64 = MMX.PAddB (hash64, d) ^ d;
                        hash64 = MMX.PAddW (MMX.PSllD (hash64, 1), d);

                        t++;
                        t &= 0xF;
                    }
                }
            }
        }

        internal static byte[] Decompress (byte[] input)
        {
            if (LittleEndian.ToUInt32 (input, 0) != 0xFF435031)
                return null;

            bool is_16bit = 0 != (input[4] & 1);

            var node = new byte[2,256];
            var child_node = new byte[256];

            int output_length = LittleEndian.ToInt32 (input, 8);
            var output = new byte[output_length];

            int src = 12;
            int dst = 0;
            while (src < input.Length)
            {
                int i, k, count, index;

                for (i = 0; i < 256; i++)
                    node[0,i] = (byte)i;

                for (i = 0; i < 256; )
                {
                    count = input[src++];

                    if (count > 127)
                    {
                        int step = count - 127;
                        i += step;
                        count = 0;
                    }

                    if (i > 255)
                        break;

                    count++;
                    for (k = 0; k < count; k++)
                    {
                        node[0,i] = input[src++];
                        if (node[0,i] != i)
                            node[1,i] = input[src++];
                        i++;
                    }
                }

                if (is_16bit)
                {
                    count = LittleEndian.ToUInt16 (input, src);
                    src += 2;
                }
                else
                {
                    count = LittleEndian.ToInt32 (input, src);
                    src += 4;
                }

                k = 0;
                for (;;)
                {
                    if (k > 0)
                        index = child_node[--k];
                    else
                    {
                        if (0 == count)
                            break;
                        count--;
                        index = input[src++];
                    }

                    if (node[0,index] == index)
                        output[dst++] = (byte)index;
                    else
                    {
                        child_node[k++] = node[1,index];
                        child_node[k++] = node[0,index];
                    }
                }
            }
            if (dst != output.Length)
                return null;

            return output;
        }

        uint GenerateKey (byte[] key_data)
        {
            ulong hash = 0;
            ulong key  = 0;
            unsafe
            {
                fixed (byte* data = key_data)
                {
                    ulong* data64 = (ulong*)data;
                    for (int i = key_data.Length / 8; i > 0; --i)
                    {
                        hash = MMX.PAddW (hash, 0x0307030703070307);
                        key  = MMX.PAddW (key, *data64++ ^ hash);
                    }
                }
            }
            return (uint)(key ^ (key >> 32));
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new QlieOptions {
                GameKeyData = GetKeyData (Settings.Default.QLIEScheme)
            };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetQLIE();
        }

        byte[] QueryEncryption ()
        {
            var options = Query<QlieOptions> (arcStrings.ArcEncryptedNotice);
            return options.GameKeyData;
        }

        static byte[] GetKeyData (string scheme)
        {
            byte[] key;
            if (KnownKeys.TryGetValue (scheme, out key))
                return key;
            return null;
        }

        /// <summary>
        /// Look for 'key.fkey' file within nearby directories specified by KeyLocations.
        /// </summary>
        static byte[] FindKeyFile (ArcView arc_file)
        {
            // QLIE archives with key could be opened at the physical file system level only
            if (VFS.IsVirtual)
                return null;
            var dir_name = Path.GetDirectoryName (arc_file.Name);
            foreach (var path in KeyLocations)
            {
                var name = Path.Combine (dir_name, path, "key.fkey");
                if (File.Exists (name))
                {
                    Trace.WriteLine ("reading key from "+name, "[QLIE]");
                    return File.ReadAllBytes (name);
                }
            }
            return null;
        }
    }
}
