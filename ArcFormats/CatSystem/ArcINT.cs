//! \file       ArcINT.cs
//! \date       Fri Jul 11 09:32:36 2014
//! \brief      Frontwing games archive.
//
// Copyright (C) 2014 by morkt
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
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GameRes.Formats.Strings;
using GameRes.Cryptography;
using GameRes.Utility;

namespace GameRes.Formats.CatSystem
{
    public class FrontwingArchive : ArcFile
    {
        public readonly Blowfish Encryption;

        public FrontwingArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, Blowfish cipher)
            : base (arc, impl, dir)
        {
            Encryption = cipher;
        }
    }

    [Serializable]
    public struct KeyData
    {
        public uint     Key;
        public string   Passphrase;

        public KeyData (string password)
        {
            Passphrase = password;
            Key = EncodePassPhrase (password);
        }

        public static uint EncodePassPhrase (string password)
        {
            byte[] pass_bytes = Encodings.cp932.GetBytes (password);
            uint key = 0xffffffff;
            foreach (var c in pass_bytes)
            {
                key = ~Crc32Normal.Table[(key >> 24) ^ c] ^ (key << 8);
            }
            return key;
        }
    }

    [Serializable]
    public class IntScheme : ResourceScheme
    {
        public Dictionary<string, KeyData> KnownKeys;
    }

    [Serializable]
    public class IntEncryptionInfo
    {
        public uint?    Key      { get; set; }
        public string   Scheme   { get; set; }
        public string   Password { get; set; }

        public uint? GetKey ()
        {
            if (null != Key && Key.HasValue)
                return Key;

            if (!string.IsNullOrEmpty (Scheme))
            {
                KeyData keydata;
                if (IntOpener.KnownSchemes.TryGetValue (Scheme, out keydata))
                    return keydata.Key;
            }

            if (!string.IsNullOrEmpty (Password))
                return KeyData.EncodePassPhrase (Password);

            return null;
        }
    }

    public class IntOptions : ResourceOptions
    {
        public IntEncryptionInfo EncryptionInfo { get; set; }
    }

    [Export(typeof(ArchiveFormat))]
    public class IntOpener : ArchiveFormat
    {
        public override string         Tag { get { return "INT"; } }
        public override string Description { get { return arcStrings.INTDescription; } }
        public override uint     Signature { get { return 0x0046494b; } } // 'KIF'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return true; } }

        static readonly byte[] NameSizes = { 0x20, 0x40 };

        public override ArcFile TryOpen (ArcView file)
        {
            int entry_count = file.View.ReadInt32 (4);
            if (!IsSaneCount (entry_count))
                return null;
            if (file.View.AsciiEqual (8, "__key__.dat\x00"))
            {
                uint? key = QueryEncryptionInfo (file.Name);
                if (null == key)
                    throw new UnknownEncryptionScheme();
                return OpenEncrypted (file, entry_count, key.Value);
            }

            var dir = new List<Entry> (entry_count);
            foreach (var name_length in NameSizes)
            {
                try
                {
                    long current_offset = 8;
                    for (int i = 0; i < entry_count; ++i)
                    {
                        string name = file.View.ReadString (current_offset, name_length);
                        if (0 == name.Length)
                        {
                            dir.Clear();
                            break;
                        }
                        var entry = FormatCatalog.Instance.Create<Entry> (name);
                        current_offset += name_length;
                        entry.Offset = file.View.ReadUInt32 (current_offset);
                        entry.Size   = file.View.ReadUInt32 (current_offset+4);
                        if (entry.Offset <= current_offset || !entry.CheckPlacement (file.MaxOffset))
                        {
                            dir.Clear();
                            break;
                        }
                        dir.Add (entry);
                        current_offset += 8;
                    }
                    if (dir.Count > 0)
                        return new ArcFile (file, this, dir);
                }
                catch { /* ignore parse errors */ }
            }
            return null;
        }

        private ArcFile OpenEncrypted (ArcView file, int entry_count, uint main_key)
        {
            if (1 == entry_count)
                return null; // empty archive
            long current_offset = 8;

            uint seed = file.View.ReadUInt32 (current_offset+0x44);
            var twister = new MersenneTwister (seed);
            byte[] blowfish_key = BitConverter.GetBytes (twister.Rand());
            if (!BitConverter.IsLittleEndian)
                Array.Reverse (blowfish_key);

            var blowfish = new Blowfish (blowfish_key);
            var dir = new List<Entry> (entry_count-1);
            byte[] name_buffer = new byte[0x40];
            for (int i = 1; i < entry_count; ++i)
            {
                current_offset += 0x48;
                file.View.Read (current_offset, name_buffer, 0, 0x40);
                uint offset = file.View.ReadUInt32 (current_offset+0x40) + (uint)i;
                uint size   = file.View.ReadUInt32 (current_offset+0x44);
                blowfish.Decipher (ref offset, ref size);
                twister.SRand (main_key + (uint)i);
                uint name_key = twister.Rand();
                string name = DecipherName (name_buffer, name_key);

                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = offset;
                entry.Size   = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new FrontwingArchive (file, this, dir, blowfish);
        }

        private Stream OpenEncryptedEntry (FrontwingArchive arc, Entry entry)
        {
            byte[] data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            arc.Encryption.Decipher (data, data.Length/8*8);
            return new BinMemoryStream (data, entry.Name);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (arc is FrontwingArchive)
                return OpenEncryptedEntry (arc as FrontwingArchive, entry);
            else
                return base.OpenEntry (arc, entry);
        }

        public string DecipherName (byte[] name, uint key)
        {
            string alphabet = "zyxwvutsrqponmlkjihgfedcbaZYXWVUTSRQPONMLKJIHGFEDCBA";
            int k = (byte)((key >> 24) + (key >> 16) + (key >> 8) + key);
            int i;
            for (i = 0; i < name.Length && name[i] != 0; ++i)
            {
                int j = alphabet.IndexOf ((char)name[i]);
                if (j != -1)
                {
                    j -= k % 0x34;
                    if (j < 0) j += 0x34;
                    name[i] = (byte)alphabet[0x33-j];
                }
                ++k;
            }
            return Encodings.cp932.GetString (name, 0, i);
        }

        public static Dictionary<string, KeyData> KnownSchemes { get { return DefaultScheme.KnownKeys; } }

        static IntScheme DefaultScheme = new IntScheme { KnownKeys = new Dictionary<string, KeyData>() };

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (IntScheme)value; }
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new IntOptions {
                EncryptionInfo = Properties.Settings.Default.INTEncryption ?? new IntEncryptionInfo(),
            };
        }

        public override ResourceOptions GetOptions (object w)
        {
            var widget = w as GUI.WidgetINT;
            if (null != widget)
            {
                Properties.Settings.Default.INTEncryption = widget.Info;
                return new IntOptions { EncryptionInfo = widget.Info };
            }
            return this.GetDefaultOptions();
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetINT ();
        }

        public override object GetCreationWidget ()
        {
            return new GUI.CreateINTWidget();
        }

        uint? QueryEncryptionInfo (string arc_name)
        {
            var title = FormatCatalog.Instance.LookupGame (arc_name);
            if (!string.IsNullOrEmpty (title) && KnownSchemes.ContainsKey (title))
                return KnownSchemes[title].Key;
            var options = Query<IntOptions> (arcStrings.INTNotice);
            return options.EncryptionInfo.GetKey();
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            int file_count = list.Count();
            if (null != callback)
                callback (file_count+2, null, null);
            int callback_count = 0;
            using (var writer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                writer.Write (Signature);
                writer.Write (file_count);
                long dir_offset = output.Position;

                var encoding = Encodings.cp932.WithFatalFallback();
                byte[] name_buf = new byte[0x40];
                int previous_size = 0;

                if (null != callback)
                    callback (callback_count++, null, arcStrings.MsgWritingIndex);

                // first, write names only
                foreach (var entry in list)
                {
                    string name = Path.GetFileName (entry.Name);
                    try
                    {
                        int size = encoding.GetBytes (name, 0, name.Length, name_buf, 0);
                        for (int i = size; i < previous_size; ++i)
                            name_buf[i] = 0;
                        previous_size = size;
                    }
                    catch (EncoderFallbackException X)
                    {
                        throw new InvalidFileName (entry.Name, arcStrings.MsgIllegalCharacters, X);
                    }
                    catch (ArgumentException X)
                    {
                        throw new InvalidFileName (entry.Name, arcStrings.MsgFileNameTooLong, X);
                    }
                    writer.Write (name_buf);
                    writer.BaseStream.Seek (8, SeekOrigin.Current);
                }

                // now, write files and remember offset/sizes
                long current_offset = output.Position;
                foreach (var entry in list)
                {
                    if (null != callback)
                        callback (callback_count++, entry, arcStrings.MsgAddingFile);

                    entry.Offset = current_offset;
                    using (var input = File.OpenRead (entry.Name))
                    {
                        var size = input.Length;
                        if (size > uint.MaxValue || current_offset + size > uint.MaxValue)
                            throw new FileSizeException();
                        current_offset += (uint)size;
                        entry.Size = (uint)size;
                        input.CopyTo (output);
                    }
                }

                if (null != callback)
                    callback (callback_count++, null, arcStrings.MsgUpdatingIndex);

                // at last, go back to directory and write offset/sizes
                dir_offset += 0x40;
                foreach (var entry in list)
                {
                    writer.BaseStream.Position = dir_offset;
                    writer.Write ((uint)entry.Offset);
                    writer.Write (entry.Size);
                    dir_offset += 0x48;
                }
            }
        }

        /// <summary>
        /// Parse certain executable resources for encryption passphrase.
        /// Returns null if no passphrase found.
        /// </summary>
        public static string GetPassFromExe (string filename)
        {
            using (var exe = new ExeFile.ResourceAccessor (filename))
            {
                var code = exe.GetResource ("DATA", "V_CODE2");
                if (null == code || code.Length < 8)
                    return null;
                var key = exe.GetResource ("KEY", "KEY_CODE");
                if (null != key)
                {
                    for (int i = 0; i < key.Length; ++i)
                        key[i] ^= 0xCD;
                }
                else
                {
                    key = Encoding.ASCII.GetBytes ("windmill");
                }
                var blowfish = new Blowfish (key);
                blowfish.Decipher (code, code.Length/8*8);
                int length = Array.IndexOf<byte> (code, 0);
                if (-1 == length)
                    length = code.Length;
                return Encodings.cp932.GetString (code, 0, length);
            }
        }
    }
}
