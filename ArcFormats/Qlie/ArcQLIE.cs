//! \file       ArcQLIE.cs
//! \date       Mon Jun 15 04:03:18 2015
//! \brief      QLIE engine archives implementation.
//
// Copyright (C) 2015-2017 by morkt
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
using GameRes.Formats.Strings;
using GameRes.Formats.Borland;

namespace GameRes.Formats.Qlie
{
    internal class QlieEntry : PackedEntry
    {
        public int  EncryptionMethod;
        public uint Hash;
        public byte[] RawName;

        public bool IsEncrypted { get { return EncryptionMethod != 0; } }

        /// <summary>
        /// Data from a separate key file "key.fkey" that comes with installed game.
        /// null if not used.
        /// </summary>
        public byte[] KeyFile;
    }

    internal class QlieArchive : ArcFile
    {
        public readonly IEncryption Encryption;

        public QlieArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, IEncryption enc)
            : base (arc, impl, dir)
        {
            Encryption = enc;
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
        public override bool      CanWrite { get { return false; } }

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
                || '.' != file.View.ReadByte (index_offset+0xC))
                return null;
            var pack_version = new Version (file.View.ReadByte (index_offset+0xB) - '0',
                                            file.View.ReadByte (index_offset+0xD) - '0');
            int count = file.View.ReadInt32 (index_offset+0x10);
            if (!IsSaneCount (count))
                return null;
            index_offset = file.View.ReadInt64 (index_offset+0x14);
            if (index_offset < 0 || index_offset >= file.MaxOffset)
                return null;

            byte[] arc_key = null;
            byte[] key_file = null;
            bool use_pack_keyfile = false;
            if (pack_version.Major >= 3)
            {
                key_file = FindKeyFile (file);
                use_pack_keyfile = key_file != null;
                // currently, user is prompted to choose encryption scheme only if there's 'key.fkey' file found.
                if (use_pack_keyfile && pack_version.Minor == 0)
                    arc_key = QueryEncryption (file);
//                use_pack_keyfile = null != arc_key;
            }
            var enc = QlieEncryption.Create (file, pack_version, arc_key);

            bool read_pack_keyfile = 3 == pack_version.Major && use_pack_keyfile;
            var name_buffer = new byte[0x100];
            var dir = new List<Entry> (count);
            using (var index = file.CreateStream (index_offset))
            {
                for (int i = 0; i < count; ++i)
                {
                    int name_length = index.ReadUInt16();
                    if (enc.IsUnicode)
                        name_length *= 2;
                    if (name_length > name_buffer.Length)
                        name_buffer = new byte[name_length];
                    if (name_length != index.Read (name_buffer, 0, name_length))
                        return null;
                    var name = enc.DecryptName (name_buffer, name_length);
                    var entry = FormatCatalog.Instance.Create<QlieEntry> (name);
                    if (use_pack_keyfile)
                        entry.RawName = name_buffer.Take (name_length).ToArray();

                    entry.Offset = index.ReadInt64();           // [+00]
                    entry.Size   = index.ReadUInt32();          // [+08]
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    entry.UnpackedSize = index.ReadUInt32();    // [+0C]
                    entry.IsPacked    = 0 != index.ReadInt32(); // [+10]
                    entry.EncryptionMethod = index.ReadInt32(); // [+14]
                    entry.Hash = index.ReadUInt32();            // [+18]
                    entry.KeyFile = key_file;
                    if (read_pack_keyfile && entry.Name.Contains ("pack_keyfile"))
                    {
                        // note that 'pack_keyfile' itself is encrypted using 'key.fkey' file contents.
                        key_file = ReadEntryBytes (file, entry, enc);
                        read_pack_keyfile = false;
                    }
                    dir.Add (entry);
                }
            }
            return new QlieArchive (file, this, dir, enc);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var qent = entry as QlieEntry;
            var qarc = arc as QlieArchive;
            if (null == qent || null == qarc || (!qent.IsEncrypted && !qent.IsPacked))
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var data = ReadEntryBytes (arc.File, qent, qarc.Encryption);
            return new BinMemoryStream (data, entry.Name);
        }

        private byte[] ReadEntryBytes (ArcView file, QlieEntry entry, IEncryption enc)
        {
            var data = file.View.ReadBytes (entry.Offset, entry.Size);
            if (entry.IsEncrypted)
            {
                enc.DecryptEntry (data, 0, data.Length, entry);
            }
            if (entry.IsPacked)
            {
                data = Decompress (data) ?? data;
            }
            return data;
        }

        internal static byte[] Decompress (byte[] input)
        {
            if (LittleEndian.ToUInt32 (input, 0) != 0xFF435031) // '1PC\xFF'
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

        public override ResourceOptions GetDefaultOptions ()
        {
            return new QlieOptions {
                GameKeyData = GetKeyData (Properties.Settings.Default.QLIEScheme)
            };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetQLIE();
        }

        byte[] QueryEncryption (ArcView file)
        {
            var title = FormatCatalog.Instance.LookupGame (file.Name, @"..\*.exe");
            byte[] key = null;
            if (!string.IsNullOrEmpty (title) && KnownKeys.ContainsKey (title))
                return KnownKeys[title];
            if (null == key)
                key = GuessKeyData (file.Name);
            if (null == key)
            {
                var options = Query<QlieOptions> (arcStrings.ArcEncryptedNotice);
                key = options.GameKeyData;
            }
            return key;
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
            var pattern = VFS.CombinePath (dir_name, @"..\*.exe");
            foreach (var exe_file in VFS.GetFiles (pattern))
            {
                using (var exe = new ExeFile.ResourceAccessor (exe_file.Name))
                {
                    var reskey = exe.GetResource ("RESKEY", "#10");
                    if (reskey != null)
                        return reskey;
                }
            }
            return null;
        }

        byte[] GuessKeyData (string arc_name)
        {
            if (VFS.IsVirtual)
                return null;
            // XXX add button to query dialog like with CatSystem?
            var pattern = VFS.CombinePath (VFS.GetDirectoryName (arc_name), @"..\*.exe");
            foreach (var file in VFS.GetFiles (pattern))
            {
                try
                {
                    var key = GetKeyDataFromExe (file.Name);
                    if (key != null)
                        return key;
                }
                catch { /* ignore errors */ }
            }
            return null;
        }

        public static byte[] GetKeyDataFromExe (string filename)
        {
            using (var exe = new ExeFile.ResourceAccessor (filename))
            {
                var tform = exe.GetResource ("TFORM1", "#10");
                if (null == tform || !tform.AsciiEqual (0, "TPF0"))
                    return null;
                using (var input = new BinMemoryStream (tform))
                {
                    var deserializer = new DelphiDeserializer (input);
                    var form = deserializer.Deserialize();
                    var image = form.Contents.FirstOrDefault (n => n.Name == "IconKeyImage");
                    if (null == image)
                        return null;
                    var icon = image.Props["Picture.Data"] as byte[];
                    if (null == icon || icon.Length < 0x106 || !icon.AsciiEqual (0, "\x05TIcon"))
                        return null;
                    return new CowArray<byte> (icon, 6, 0x100).ToArray();
                }
            }
        }
    }
}
