//! \file       ArcKCAP.cs
//! \date       Tue Mar 10 04:47:25 2015
//! \brief      Selene resource archive implementation.
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Security.Cryptography;
using System.IO;
using GameRes.Formats.Strings;
using GameRes.Formats.Properties;

namespace GameRes.Formats.Selene
{
    public class SeleneArchive : ArcFile
    {
        public readonly byte[] KeyTable;

        public SeleneArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            KeyTable = key;
        }
    }

    internal class KcapEntry : Entry
    {
        public bool Encrypted;
    }

    public class KcapOptions : ResourceOptions
    {
        public string PassPhrase { get; set; }
    }

    [Serializable]
    public class KcapScheme : ResourceScheme
    {
        public Dictionary<string,string> KnownSchemes;
    }

    [Export(typeof(ArchiveFormat))]
    public class PackOpener : ArchiveFormat
    {
        public override string         Tag { get { return "KCAP"; } }
        public override string Description { get { return "Selene engine resource archive"; } }
        public override uint     Signature { get { return 0x5041434b; } } // 'KCAP'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        static private string DefaultPassPhrase = "Selene.Default.Password";

        public static Dictionary<string,string> KnownSchemes = new Dictionary<string,string>();

        public PackOpener ()
        {
            Extensions = new string[] { "pack" };
        }

        public override ResourceScheme Scheme
        {
            get { return new KcapScheme { KnownSchemes = KnownSchemes }; }
            set { KnownSchemes = ((KcapScheme)value).KnownSchemes; }
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (count <= 0 || count > 0xfffff)
                return null;
            uint index_size = (uint)count * 0x54u;
            if (index_size > file.View.Reserve (8, index_size))
                return null;
            long index_offset = 8;
            var dir = new List<Entry> (count);
            bool encrypted = false;
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset, 0x40);
                var entry = new KcapEntry
                {
                    Name = name,
                    Type = FormatCatalog.Instance.GetTypeFromName (name),
                    Offset = file.View.ReadUInt32 (index_offset+0x48),
                    Size   = file.View.ReadUInt32 (index_offset+0x4c),
                    Encrypted = 0 != file.View.ReadUInt32 (index_offset+0x50),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                encrypted = encrypted || entry.Encrypted;
                dir.Add (entry);
                index_offset += 0x54;
            }
            if (!encrypted)
                return new ArcFile (file, this, dir);
            var options = Query<KcapOptions> (arcStrings.ArcEncryptedNotice);
            var key = CreateKeyTable (options.PassPhrase);
            return new SeleneArchive (file, this, dir, key);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var kpa = arc as SeleneArchive;
            var kpe = entry as KcapEntry;
            if (null == kpa || null == kpe || !kpe.Encrypted)
                return input;
            return new InputCryptoStream (input, new KcapTransform(kpa.KeyTable));
        }

        public static string GetPassPhrase (string title)
        {
            string pass;
            if (string.IsNullOrEmpty (title) || !KnownSchemes.TryGetValue (title, out pass))
                return "";
            return pass;
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetKCAP();
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new KcapOptions {
                PassPhrase = Settings.Default.KCAPPassPhrase,
            };
        }

        static private byte[] CreateKeyTable (string pass) // (void *this, byte *a2)
        {
            if (pass.Length < 8)
                pass = DefaultPassPhrase;
            int pass_len = pass.Length;
            int hash = PasskeyHash (pass); // sub_100E0390
            var hash_table = new KeyTableGenerator (hash);
            byte[] table = new byte[0x10000];
            for (int i = 0; i < table.Length; ++i)
            {
                int key = hash_table.Permutate(); // sub_100E06D0
                table[i] = (byte)((byte)pass[i % pass_len] ^ (key >> 16));
            }
            return table;
        }

        static int PasskeyHash (string pass) // sub_100E0390(int a1, unsigned int a2)
        {
            int hash = -1; // eax@1
            for (int i = 0; i < pass.Length; ++i) // ecx@1
            {
                hash = (byte)pass[i] ^ hash; // eax@2
                for (int j = 0; j < 8; ++j)
                    hash = (int)(((uint)hash >> 1) ^ (0 != (hash & 1) ? 0xEDB88320u : 0u));
            }
            return ~hash;
        }

        internal class KeyTableGenerator
        {
            private int[]   m_table = new int[0x270];
            private int     m_pos;

            public KeyTableGenerator (int first = 0)
            {
                Init (first);
            }

            public void Init (int first)
            {
                m_table[0] = first;
                for (int i = 1; i < 0x270; ++i)
                    m_table[i] = i + 0x6C078965 * (m_table[i-1] ^ (m_table[i-1] >> 30));
                m_pos = 0x270;
            }

            private static int[] v14 = new int[2] { 0, -1727483681 }; // [sp+4h] [bp-4h]@1

            public int Permutate () // sub_100E06D0
            {
                if (m_pos >= 0x270)
                {
                    for (int i = 0; i < 0xe3; ++i)
                    {
                        int v5 = m_table[i] ^ m_table[i+1];
                        m_table[i] = m_table[i+0x18d] ^ v14[(m_table[i] ^ (byte)v5) & 1] ^ ((m_table[i] ^ v5 & 0x7FFFFFFF) >> 1);
                    }
                    for (int i = 0; i < 0x18c; ++i)
                    {
                        int v6 = i + 0xe3;
                        int v8 = m_table[v6] ^ m_table[v6+1];
                        m_table[v6] = m_table[i] ^ v14[(m_table[v6] ^ (byte)v8) & 1] ^ ((m_table[v6] ^ v8 & 0x7FFFFFFF) >> 1);
                    }
                    int v9 = m_table[0x26f] ^ (m_table[0] ^ m_table[0x26f]) & 0x7FFFFFFF;
                    m_table[0x26f] = (v9 >> 1) ^ m_table[0x18c] ^ v14[v9 & 1];
                    m_pos = 0;
                }
                int v11 = m_table[m_pos++]; // edx@7
                int v12 = (int)((((((((v11 >> 11) ^ v11) & 0xFF3A58AD) << 7) ^ (v11 >> 11) ^ v11) & 0xFFFFDF8C) << 15) ^ ((((v11 >> 11) ^ v11) & 0xFF3A58AD) << 7) ^ (v11 >> 11) ^ v11); // edx@7

                return v12 ^ (v12 >> 18);
            }
        }

        internal class KcapTransform : ICryptoTransform
        {
            private readonly byte[] KeyTable;

            public bool          CanReuseTransform { get { return true; } }
            public bool CanTransformMultipleBlocks { get { return false; } }
            public int              InputBlockSize { get { return KeyTable.Length; } }
            public int             OutputBlockSize { get { return KeyTable.Length; } }

            public KcapTransform (byte[] key_table)
            {
                KeyTable = key_table;
            }

            public int TransformBlock (byte[] inputBuffer, int inputOffset, int inputCount,
                                    byte[] outputBuffer, int outputOffset)
            {
                for (int i = 0; i < inputCount; ++i)
                {
                    outputBuffer[outputOffset++] = (byte)(inputBuffer[inputOffset+i]^KeyTable[i]);
                }
                return inputCount;
            }

            public byte[] TransformFinalBlock (byte[] inputBuffer, int inputOffset, int inputCount)
            {
                byte[] outputBuffer = new byte[inputCount];
                TransformBlock (inputBuffer, inputOffset, inputCount, outputBuffer, 0);
                return outputBuffer;
            }

            public void Dispose ()
            {
                System.GC.SuppressFinalize (this);
            }
        }
    }
}
