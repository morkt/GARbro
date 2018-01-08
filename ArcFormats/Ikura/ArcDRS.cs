//! \file       ArcDRS.cs
//! \date       Thu Aug 21 06:11:09 2014
//! \brief      Digital Romance System archive implementation.
//
// Copyright (C) 2014-2016 by morkt
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Utility;
using GameRes.Formats.Strings;

namespace GameRes.Formats.Ikura
{
    [Export(typeof(ArchiveFormat))]
    public class DrsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DRS"; } }
        public override string Description { get { return "Digital Romance System resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public DrsOpener ()
        {
            Extensions = Enumerable.Empty<string>(); // DRS archives have no extensions
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset > uint.MaxValue)
                return null;
            int dir_size = file.View.ReadUInt16 (0);
            if (dir_size < 0x20 || 0 != (dir_size & 0xf) || dir_size + 2 >= file.MaxOffset)
                return null;
            byte first = file.View.ReadByte (2);
            if (0 == first)
                return null;
            file.View.Reserve (0, (uint)dir_size + 2);
            int dir_offset = 2;

            uint next_offset = file.View.ReadUInt32 (dir_offset+12);
            if (next_offset > file.MaxOffset || next_offset < dir_size+2)
                return null;
            var encoding = Encodings.cp932.WithFatalFallback();
            byte[] name_raw = new byte[12];

            int count = dir_size / 0x10 - 1;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (dir_offset, name_raw, 0, 12);
                int name_length = name_raw.Length;
                while (name_length > 0 && 0 == name_raw[name_length-1])
                    --name_length;
                if (0 == name_length)
                    return null;
                uint offset = next_offset;
                dir_offset += 0x10;
                next_offset = file.View.ReadUInt32 (dir_offset+12);
                if (next_offset > file.MaxOffset || next_offset < offset)
                    return null;
                string name = encoding.GetString (name_raw, 0, name_length).ToLowerInvariant();
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = offset;
                entry.Size = next_offset - offset;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }

    [Serializable]
    public class IsfScheme : ResourceScheme
    {
        public Dictionary<string, byte[]> KnownSecrets;
    }

    internal class IsfOptions : ResourceOptions
    {
        public byte[] Secret;
    }

    internal class IsfArchive : ArcFile
    {
        public byte[] Secret;

        public IsfArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] secret = null)
            : base (arc, impl, dir)
        {
            Secret = secret;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class MpxOpener : ArchiveFormat
    {
        public override string         Tag { get { return "IKURA/GDL"; } }
        public override string Description { get { return "IKURA GDL resource archive"; } }
        public override uint     Signature { get { return 0x4d324d53; } } // 'SM2M'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public MpxOpener ()
        {
            Extensions = Enumerable.Empty<string>(); // DRS archives have no extensions
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "PX10") || file.MaxOffset > uint.MaxValue)
                return null;
            int count = file.View.ReadInt32 (8);
            if (count <= 0 || count > 0xfffff)
                return null;
            uint index_size = file.View.ReadUInt32 (12);
            if (index_size > file.MaxOffset)
                return null;
            var encoding = Encodings.cp932.WithFatalFallback();
            byte[] name_raw = new byte[12];

            long dir_offset = 0x20;
            var dir = new List<Entry> (count);
            bool has_scripts = false;
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (dir_offset, name_raw, 0, 12);
                int name_length = name_raw.Length;
                while (name_length > 0 && 0 == name_raw[name_length-1])
                    --name_length;
                if (0 == name_length)
                    return null;
                string name = encoding.GetString (name_raw, 0, name_length).ToLowerInvariant();
                Entry entry;
                if (name.EndsWith (".isf") || name.EndsWith (".snr"))
                {
                    entry = new Entry { Name = name, Type = "script" };
                    has_scripts = true;
                }
                else if (name.EndsWith (".bin"))
                    entry = new ImageEntry { Name = name };
                else
                    entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (dir_offset+12);
                entry.Size   = file.View.ReadUInt32 (dir_offset+16);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                dir_offset += 0x14;
            }
            if (has_scripts)
                return new IsfArchive (file, this, dir);
            else
                return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var isf = arc as IsfArchive;
            if (null == isf || entry.Type != "script" || entry.Size <= 0x10)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            bool encoded = arc.File.View.AsciiEqual (entry.Offset+entry.Size-0x10, "SECRETFILTER100a");
            uint entry_size = entry.Size;
            if (encoded)
            {
                entry_size -= 0x10;
                if (null == isf.Secret)
                    isf.Secret = QuerySecret();
                if (null == isf.Secret || 0 == isf.Secret.Length)
                    return arc.File.CreateStream (entry.Offset, entry.Size);
            }
            var data = new byte[entry_size];
            arc.File.View.Read (entry.Offset, data, 0, entry_size);
            if (encoded)
            {
                var decoder = new IsfDecoder (isf.Secret);
                decoder.Decode (data);
            }
            int signature = LittleEndian.ToUInt16 (data, 4);
            if (0x9795 == signature)
            {
                ApplyTransformation (data, 8, x => x >> 2 | x << 6);
            }
            else if (0xd197 == signature)
            {
                ApplyTransformation (data, 8, x => ~x);
            }
            else if (0xce89 == signature && 0 != data[6])
            {
                byte key = data[6];
                ApplyTransformation (data, 8, x => x ^ key);
            }
            return new BinMemoryStream (data, entry.Name);
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new IsfOptions {
                Secret = GetSecret (Properties.Settings.Default.ISFScheme) ?? new byte[0]
            };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetISF();
        }

        private byte[] QuerySecret ()
        {
            var options = Query<IsfOptions> (arcStrings.ArcEncryptedNotice);
            return options.Secret;
        }

        private static byte[] GetSecret (string scheme)
        {
            byte[] secret;
            if (KnownSecrets.TryGetValue (scheme, out secret))
                return secret;
            return null;
        }

        private static void ApplyTransformation (byte[] data, int offset, Func<byte, int> method)
        {
            for (int i = offset; i < data.Length; ++i)
                data[i] = (byte)method (data[i]);
        }

        public static Dictionary<string, byte[]> KnownSecrets = new Dictionary<string, byte[]>();

        public override ResourceScheme Scheme
        {
            get { return new IsfScheme { KnownSecrets = KnownSecrets }; }
            set { KnownSecrets = ((IsfScheme)value).KnownSecrets; }
        }
    }

    internal class IsfDecoder
    {
        byte[]      m_secret;

        public IsfDecoder (byte[] secret)
        {
            m_secret = secret;
        }

        public void Decode (byte[] data)
        {
            var key_string = CreateKeyString();
            int n = 0;
            for (int i = 0; i < data.Length; )
            {
                DecodePrepare (n++, key_string);
                for (int j = 0; j < key_string.Length && i < data.Length; )
                {
                    data[i++] ^= key_string[j++];
                }
            }
        }

        private byte[] CreateKeyString ()
        {
            byte[] len_str = new byte[2];
            for (int i = 0; i < 2; i++)
                len_str[i] = EncodeHex ((byte)(Chr2HexCode (m_secret[0x500 + i]) - Chr2HexCode (m_secret[0x100 + i])));

            byte[] key_string = new byte[Str2Hex (len_str)];
            for (int i = 0; i < key_string.Length; i++)
                key_string[i] = EncodeHex ((byte)(Chr2HexCode (m_secret[0x510 + i]) - Chr2HexCode (m_secret[0x110 + i])));
            return key_string;
        }

        private void DecodePrepare (int index, byte[] key_string)
        {
            int p = (index & 0x3f) * 16; // index within SecretTable
            for (int i = 0; i < key_string.Length; i++)
                key_string[i] = EncodeHex ((byte)(Chr2HexCode (key_string[i]) + Chr2HexCode (m_secret[p+i])));
        }

        private static byte EncodeHex (byte symbol)
        {
            if (symbol < 0x80)
                return HexEncodeMap[symbol % 36];
            symbol = (byte)(-(sbyte)symbol % 36);
            if (0 == symbol)
                return HexEncodeMap[0];
            return HexEncodeMap[36 - symbol];
        }

        private static byte Chr2HexCode (byte chr)
        {
            return HexTable[Chr2Hex (chr)];
        }

        private static byte Chr2Hex (byte chr)
        {
            byte code;
            if (chr >= '0' && chr <= '9')
                code = (byte)(chr - '0');
            else if (chr >= 'a' && chr <= 'z')
                code = (byte)(chr - 'a' + 10);
            else if (chr >= 'A' && chr <= 'Z')
                code = (byte)(chr - 'A' + 10);
            else
                code = 0;
            return code;
        }

        private static int Str2Hex (byte[] shex)
        {
            int idec = 0;
            for (int i = 0; i < shex.Length; ++i)
            {
                int mid = Chr2Hex (shex[i]);
                mid <<= ((shex.Length - i - 1) << 2);
                idec |= mid;
            }
            return idec;
        }

        static readonly byte[] HexEncodeMap = Encoding.ASCII.GetBytes("G5FXIL094MPRKWCJ3OEBVA7HQ2SU8Y6TZ1ND");
        static readonly byte[] HexTable = new byte[] {
            0x06, 0x21, 0x19, 0x10, 0x08, 0x01, 0x1E, 0x16, 0x1C, 0x07, 0x15, 0x13, 0x0E, 0x23, 0x12, 0x02,
            0x00, 0x17, 0x04, 0x0F, 0x0C, 0x05, 0x09, 0x22, 0x11, 0x0A, 0x18, 0x0B, 0x1A, 0x1F, 0x1B, 0x14,
            0x0D, 0x03, 0x1D, 0x20,
        };
    }
}
