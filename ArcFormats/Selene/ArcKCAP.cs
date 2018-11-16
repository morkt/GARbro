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
using GameRes.Utility;

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

        public PackOpener ()
        {
            Extensions = new string[] { "pack" };
        }

        static KcapScheme DefaultScheme = new KcapScheme { KnownSchemes = new Dictionary<string,string>() };

        public static Dictionary<string,string> KnownSchemes { get { return DefaultScheme.KnownSchemes; } }

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (KcapScheme)value; }
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
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
                var entry = Create<KcapEntry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x48);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x4C);
                entry.Encrypted = 0 != file.View.ReadUInt32 (index_offset+0x50);
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
                PassPhrase = Properties.Settings.Default.KCAPPassPhrase,
            };
        }

        static private byte[] CreateKeyTable (string pass)
        {
            if (pass.Length < 8)
                pass = DefaultPassPhrase;
            int pass_len = pass.Length;
            uint seed = PasskeyHash (pass);
            var rng = new KeyTableGenerator ((int)seed);
            byte[] table = new byte[0x10000];
            for (int i = 0; i < table.Length; ++i)
            {
                int key = rng.Rand();
                table[i] = (byte)(pass[i % pass_len] ^ (key >> 16));
            }
            return table;
        }

        static uint PasskeyHash (string pass)
        {
            var bytes = Encodings.cp932.GetBytes (pass);
            return Crc32.Compute (bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Mersenne Twister with signed integer arithmetics, resulting in a different sequence.
        /// </summary>
        internal class KeyTableGenerator
        {
            const int   StateLength     = 624;
            const int   StateM          = 397;
            const int   MatrixA         = -1727483681;
            const int   TemperingMaskB  = -1658038656;
            const int   TemperingMaskC  = -272236544;

            private int[]   m_table = new int[StateLength];
            private int     m_pos;

            public KeyTableGenerator (int seed = 0)
            {
                SRand (seed);
            }

            public void SRand (int seed)
            {
                m_table[0] = seed;
                for (int i = 1; i < StateLength; ++i)
                    m_table[i] = i + 0x6C078965 * (m_table[i-1] ^ (m_table[i-1] >> 30));
                m_pos = StateLength;
            }

            private static int[] mag01 = new int[2] { 0, MatrixA };

            public int Rand ()
            {
                if (m_pos >= StateLength)
                {
                    int i;
                    for (i = 0; i < StateLength - StateM; ++i)
                    {
                        int x = m_table[i] ^ m_table[i+1];
                        m_table[i] = m_table[i + StateM] ^ mag01[(m_table[i] ^ x) & 1]
                                   ^ ((m_table[i] ^ x & 0x7FFFFFFF) >> 1);
                    }
                    for (; i < StateLength - 1; ++i)
                    {
                        int x = m_table[i] ^ m_table[i + 1];
                        m_table[i] = m_table[i + StateM - StateLength] ^ mag01[(m_table[i] ^ x) & 1]
                                   ^ ((m_table[i] ^ x & 0x7FFFFFFF) >> 1);
                    }
                    int z = m_table[StateLength - 1] ^ (m_table[0] ^ m_table[StateLength-1]) & 0x7FFFFFFF;
                    m_table[StateLength - 1] = m_table[StateM-1] ^ (z >> 1) ^ mag01[z & 1];
                    m_pos = 0;
                }
                int y = m_table[m_pos++];
                y ^= y >> 11;
                y ^= (y << 7)  & TemperingMaskB;
                y ^= (y << 15) & TemperingMaskC;
                y ^= y >> 18;
                return y;
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
