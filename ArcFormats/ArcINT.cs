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
using System.ComponentModel.Composition;
using System.Collections.Generic;
using System.Diagnostics;
using Simias.Encryption;
using System.Runtime.InteropServices;
using GameRes.Formats.Strings;
using GameRes.Formats.Properties;

namespace GameRes.Formats
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

    [Serializable()]
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
                IntOpener.KeyData keydata;
                if (IntOpener.KnownSchemes.TryGetValue (Scheme, out keydata))
                    return keydata.Key;
            }

            if (!string.IsNullOrEmpty (Password))
                return IntOpener.EncodePassPhrase (Password);

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
        public override string Tag { get { return "INT"; } }
        public override string Description { get { return arcStrings.INTDescription; } }
        public override uint Signature { get { return 0x0046494b; } }
        public override bool IsHierarchic { get { return false; } }
        public override bool CanCreate { get { return true; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint entry_count = file.View.ReadUInt32 (4);
            if (0 == entry_count || 0 != ((entry_count - 1) >> 0x14))
            {
                Trace.WriteLine (string.Format ("Invalid entry count ({0})", entry_count));
                return null;
            }
            if (file.View.AsciiEqual (8, "__key__.dat\x00"))
            {
                uint? key = QueryEncryptionInfo();
                if (null == key)
                    throw new UnknownEncryptionScheme();
                return OpenEncrypted (file, entry_count, key.Value);
            }

            long current_offset = 8;
            var dir = new List<Entry> ((int)entry_count);
            for (uint i = 0; i < entry_count; ++i)
            {
                string name = file.View.ReadString (current_offset, 0x40);
                var entry = FormatCatalog.Instance.CreateEntry (name);
                entry.Offset = file.View.ReadUInt32 (current_offset+0x40);
                entry.Size   = file.View.ReadUInt32 (current_offset+0x44);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                current_offset += 0x48;
            }
            return new ArcFile (file, this, dir);
        }

        private ArcFile OpenEncrypted (ArcView file, uint entry_count, uint main_key)
        {
            if (1 == entry_count)
                return null; // empty archive
            long current_offset = 8;
            var twister = new Twister();

            // [@@L1]   = 32-bit key
            // [@@L1+4] = 0 if key is available, -1 otherwise
            uint key_data = file.View.ReadUInt32 (current_offset+0x44);
            uint twist_key = twister.Twist (key_data);
            // [@@L0] = 32-bit twist key
            byte[] blowfish_key = BitConverter.GetBytes (twist_key);
            if (!BitConverter.IsLittleEndian)
                Array.Reverse (blowfish_key);

            var blowfish = new Blowfish (blowfish_key);
            var dir = new List<Entry> ((int)entry_count-1);
            byte[] name_info = new byte[0x40];
            for (uint i = 1; i < entry_count; ++i)
            {
                current_offset += 0x48;
                file.View.Read (current_offset, name_info, 0, 0x40);
                uint eax = file.View.ReadUInt32 (current_offset+0x40);
                uint edx = file.View.ReadUInt32 (current_offset+0x44);
                eax += i;
                blowfish.Decipher (ref eax, ref edx);
                uint key = twister.Twist (main_key + i);
                string name = DecipherName (name_info, key);

                var entry = FormatCatalog.Instance.CreateEntry (name);
                entry.Offset = eax;
                entry.Size   = edx;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new FrontwingArchive (file, this, dir, blowfish);
        }

        private Stream OpenEncryptedEntry (FrontwingArchive arc, Entry entry)
        {
            using (var view = arc.File.CreateViewAccessor (entry.Offset, entry.Size))
            {
                byte[] data = new byte[entry.Size];
                // below is supposedly faster version of
                //arc.File.View.Read (entry.Offset, data, 0, entry.Size);
                unsafe
                {
                    byte* ptr = view.GetPointer (entry.Offset);
                    try {
                        Marshal.Copy (new IntPtr(ptr), data, 0, data.Length);
                    } finally {
                        view.SafeMemoryMappedViewHandle.ReleasePointer();
                    }
                }
                arc.Encryption.Decipher (data, data.Length/8*8);
                return new MemoryStream (data, false);
            }
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
            key += (key >> 8) + (key >> 16) + (key >> 24);
            key &= 0xff;
            key %= 0x34;
            int count = 0;
            for (int i = 0; i < name.Length; ++i)
            {
                byte al = name[i];
                if (0 == al)
                    break;
                byte bl = (byte)key;
                ++count;
                uint edx = al;
                al |= 0x20;
                al -= 0x61;
                if (al < 0x1a)
                {
                    if (0 != (edx & 0x20))
                        al += 0x1a;
                    al = (byte)~al;
                    al += 0x34;
                    if (al >= bl)
                        al -= bl;
                    else
                        al = (byte)(al - bl + 0x34);
                    if (al >= 0x1a)
                        al += 6;
                    al += 0x41;
                    name[i] = al;
                }
                ++key;
                if (0x34 == key)
                    key = 0;
            }
            return Encodings.cp932.GetString (name, 0, count);
        }

        class Twister
        {
            const uint TwisterLength = 0x270;
            uint[]  m_twister = new uint[TwisterLength];
            uint    m_twister_pos = 0;

            public uint Twist (uint key)
            {
                Init (key);
                return Next();
            }

            public void Init (uint key)
            {
                uint edx = key;
                for (int i = 0; i < TwisterLength; ++i)
                {
                    uint ecx = edx * 0x10dcd + 1;
                    m_twister[i] = (edx & 0xffff0000) | (ecx >> 16);
                    edx *= 0x1C587629;
                    edx += 0x10dce;
                }
                m_twister_pos = 0;
            }

            public uint Next ()
            {
                uint ecx = m_twister[m_twister_pos];
                uint edx = m_twister_pos + 1;
                if (TwisterLength == edx)
                    edx = 0;
                uint edi = m_twister[edx];
                edi = ((edi ^ ecx) & 0x7FFFFFFF) ^ ecx;
                bool carry = 0 != (edi & 1);
                edi >>= 1;
                if (carry)
                    edi ^= 0x9908B0DF;
                ecx = m_twister_pos + 0x18d;
                if (ecx >= TwisterLength)
                    ecx -= TwisterLength;
                edi ^= m_twister[ecx];
                m_twister[m_twister_pos] = edi;
                m_twister_pos = edx;
                uint eax = edi ^ (edi >> 11);
                eax = ((eax & 0xFF3A58AD) << 7)  ^ eax;
                eax = ((eax & 0xFFFFDF8C) << 15) ^ eax;
                eax = (eax >> 18) ^ eax;
                return eax;
            }
        }

        public static uint EncodePassPhrase (string password)
        {
            byte[] pass_bytes = Encodings.cp932.GetBytes (password);
            uint key = 0xffffffff;
            foreach (var c in pass_bytes)
            {
                uint val = (uint)c << 24;
                key ^= val;
                for (int i = 0; i < 8; ++i)
                {
                    bool carry = 0 != (key & 0x80000000);
                    key <<= 1;
                    if (carry)
                        key ^= 0x4C11DB7;
                }
                key = ~key;
            }
            return key;
        }

        public struct KeyData
        {
            public uint     Key;
            public string   Passphrase;
        }

        public static readonly Dictionary<string, KeyData> KnownSchemes = new Dictionary<string, KeyData> {
            { "Grisaia no Kajitsu",               new KeyData { Key=0x1DAD9120, Passphrase="FW-6JD55162" }},
            { "Shukufuku no Campanella",          new KeyData { Key=0x4260E643, Passphrase="CAMPANELLA" }},
            { "Makai Tenshi Djibril -Episode 4-", new KeyData { Key=0xA5A166AA, Passphrase="FW_MAKAI-TENSHI_DJIBRIL4" }},
            { "Sengoku Tenshi Djibril (trial)",   new KeyData { Key=0xef870610, Passphrase="FW-8O9B6WDS" }},
        };

        public override ResourceOptions GetDefaultOptions ()
        {
            return new IntOptions {
                EncryptionInfo = Settings.Default.INTEncryption ?? new IntEncryptionInfo(),
            };
        }

        public override ResourceOptions GetOptions (object w)
        {
            var widget = w as GUI.WidgetINT;
            if (null != widget)
            {
                Settings.Default.INTEncryption = widget.Info;
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

        uint? QueryEncryptionInfo ()
        {
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
    }
}
