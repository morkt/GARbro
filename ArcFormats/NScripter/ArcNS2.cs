//! \file       ArcNS2.cs
//! \date       Sat Jul 30 13:00:13 2016
//! \brief      NS2 resorce archives.
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
using System.Security.Cryptography;
using System.Text;
using GameRes.Formats.Properties;
using GameRes.Formats.Strings;

namespace GameRes.Formats.NScripter
{
    [Export(typeof(ArchiveFormat))]
    public class Ns2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "NS2"; } }
        public override string Description { get { return arcStrings.NSADescription; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public static Dictionary<string, string> KnownKeys = new Dictionary<string, string>();

        public override ResourceScheme Scheme
        {
            get { return new NsaScheme { KnownKeys = KnownKeys }; }
            set { KnownKeys = ((NsaScheme)value).KnownKeys; }
        }

        public override ArcFile TryOpen (ArcView file)
        {
            List<Entry> dir = null;
            uint data_offset = file.View.ReadUInt32 (0);
            if (data_offset > 4 && data_offset < file.MaxOffset)
            {
                try
                {
                    using (var input = file.CreateStream())
                    {
                        dir = ReadIndex (input);
                        if (null != dir)
                            return new ArcFile (file, this, dir);
                    }
                }
                catch { /* ignore parse errors */ }
            }
            if (!file.Name.HasExtension (".ns2"))
                return null;

            var password = QueryPassword();
            if (string.IsNullOrEmpty (password))
                return null;
            var key = Encoding.ASCII.GetBytes (password);

            using (var input = OpenEncryptedStream (file, key))
            {
                dir = ReadIndex (input);
                if (null == dir)
                    return null;
                return new NsaEncryptedArchive (file, this, dir, key);
            }
        }

        protected List<Entry> ReadIndex (Stream file)
        {
            using (var input = new BinaryReader (file, Encodings.cp932, true))
            {
                uint base_offset = input.ReadUInt32();
                if (base_offset <= 4 || base_offset >= file.Length)
                    return null;

                var name_buffer = new char[0x100];
                long current_offset = base_offset;
                var dir = new List<Entry>();
                while (file.Position < base_offset)
                {
                    if (input.ReadChar() != '"')
                        break;
                    char c;
                    int i = 0;
                    while ((c = input.ReadChar()) != '"')
                    {
                        if (name_buffer.Length == i)
                            return null;
                        name_buffer[i++] = c;
                    }
                    if (0 == i)
                        return null;
                    var name = new string (name_buffer, 0, i);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = current_offset;
                    entry.Size   = input.ReadUInt32();
                    if (!entry.CheckPlacement (file.Length))
                        return null;
                    current_offset += entry.Size;
                    dir.Add (entry);
                }
                return dir.Count > 0 ? dir : null;
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var nsa_arc = arc as NsaEncryptedArchive;
            if (null == nsa_arc)
            {
                return arc.File.CreateStream (entry.Offset, entry.Size);
            }
            var encrypted = OpenEncryptedStream (arc.File, nsa_arc.Key);
            return new StreamRegion (encrypted, entry.Offset, entry.Size);
        }

        Stream OpenEncryptedStream (ArcView file, byte[] key)
        {
            if (key.Length < 96)
                return new EncryptedViewStream (file, key);
            else
                return new Ns2Stream (file, key);
        }

        private string QueryPassword ()
        {
            var options = Query<NsaOptions> (arcStrings.ArcEncryptedNotice);
            return options.Password;
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new NsaOptions { Password = Settings.Default.NSAPassword };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            var w = widget as GUI.WidgetNSA;
            if (null != w)
                Settings.Default.NSAPassword = w.Password.Text;
            return GetDefaultOptions();
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetNSA (KnownKeys);
        }
    }

    internal class Ns2Stream : ViewStreamBase
    {
        byte[]          m_key;

        readonly Cryptography.MD5 MD5 = new Cryptography.MD5();

        const int BlockSize   = 32;

        public Ns2Stream (ArcView mmap, byte[] key) : base (mmap)
        {
            m_key = key;
        }

        byte[] m_seed = new byte[64];

        protected override void DecryptBlock ()
        {
            var temp = new byte[32];
            var hash = new byte[16];
            for (int src = 0; src < m_current_block_length; src += BlockSize)
            {
                int src2 = src + 16;
                int key1 = 0; // within m_key
                int key2 = 48;

                Buffer.BlockCopy (m_current_block, src2, m_seed, 0,  16);
                Buffer.BlockCopy (m_key,           key1, m_seed, 16, 48);

                MD5.Initialize();
                MD5.Update (m_seed, 0, m_seed.Length);
                Buffer.BlockCopy (MD5.State, 0, hash, 0, 16);

                for (int j = 0; j < 16; ++j)
                {
                    temp[j] = m_seed[j] = (byte)(hash[j] ^ m_current_block[src + j]);
                }

                Buffer.BlockCopy (m_key, key2, m_seed, 16, 48);

                MD5.Initialize();
                MD5.Update (m_seed, 0, m_seed.Length);
                Buffer.BlockCopy (MD5.State, 0, hash, 0, 16);
                
                for (int j = 0; j < 16; ++j)
                {
                    temp[16 + j] = m_seed[j] = (byte)(hash[j] ^ m_current_block[src2 + j]);
                }

                Buffer.BlockCopy (m_key, key1, m_seed, 16, 48);

                MD5.Initialize();
                MD5.Update (m_seed, 0, m_seed.Length);
                Buffer.BlockCopy (MD5.State, 0, hash, 0, 16);

                Buffer.BlockCopy (temp, 16, m_current_block, src, 16);
                for (int j = 0; j < 16; ++j)
                {
                    m_current_block[src2 + j] = (byte)(hash[j] ^ temp[j]);
                }
            }
        }
    }
}
