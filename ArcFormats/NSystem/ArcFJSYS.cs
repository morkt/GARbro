//! \file       ArcFJSYS.cs
//! \date       Thu Nov 24 14:00:07 2016
//! \brief      NSystem engine resource archive.
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
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.NSystem
{
    internal class MsdArchive : ArcFile
    {
        public readonly string Key;

        public MsdArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, string key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class FjsysOpener : ArchiveFormat
    {
        public override string         Tag { get { return "FJSYS"; } }
        public override string Description { get { return "NSystem engine resource archive"; } }
        public override uint     Signature { get { return 0x59534A46; } } // 'FJSY'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public FjsysOpener ()
        {
            Extensions = new string[] { "" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadByte (4) != 'S')
                return null;
            uint names_size = file.View.ReadUInt32 (0xC);
            int count = file.View.ReadInt32 (0x10);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 0x54;
            uint index_size = (uint)count * 0x10;
            var names = file.View.ReadBytes (index_offset + index_size, names_size);

            var dir = new List<Entry> (count);
            bool has_scripts = false;
            for (int i = 0; i < count; ++i)
            {
                var name_offset = file.View.ReadInt32 (index_offset);
                var name = Binary.GetCString (names, name_offset);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Size   = file.View.ReadUInt32 (index_offset+4);
                entry.Offset = file.View.ReadInt64 (index_offset+8);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                has_scripts = has_scripts || name.HasExtension (".msd");
                dir.Add (entry);
                index_offset += 0x10;
            }
            if (has_scripts)
            {
                var password = QueryPassword (file.Name);
                if (!string.IsNullOrEmpty (password))
                    return new MsdArchive (file, this, dir, password);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var msarc = arc as MsdArchive;
            if (null == msarc || string.IsNullOrEmpty (msarc.Key)
                || !entry.Name.HasExtension (".msd")
                || arc.File.View.AsciiEqual (entry.Offset, "MSCENARIO FILE  "))
                return base.OpenEntry (arc, entry);
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new InputCryptoStream (input, new MsdTransform (msarc.Key));
        }

        string QueryPassword (string arc_name)
        {
            var title = FormatCatalog.Instance.LookupGame (arc_name);
            if (!string.IsNullOrEmpty (title) && KnownPasswords.ContainsKey (title))
                return KnownPasswords[title];
            var options = Query<FjsysOptions> (arcStrings.FJSYSNotice);
            return options.MsdPassword;
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new FjsysOptions { MsdPassword = Properties.Settings.Default.FJSYSPassword };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            if (widget is GUI.WidgetMSD)
                Properties.Settings.Default.FJSYSPassword = ((GUI.WidgetMSD)widget).Password.Text;
            return GetDefaultOptions();
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetMSD();
        }

        public static Dictionary<string, string> KnownPasswords = new Dictionary<string, string>();

        public override ResourceScheme Scheme
        {
            get { return new FjsysScheme { MsdPasswords = KnownPasswords }; }
            set { KnownPasswords = ((FjsysScheme)value).MsdPasswords; }
        }
    }

    internal sealed class MsdTransform : ICryptoTransform
    {
        const int BlockSize = 0x20;
        string          m_key;
        MD5             m_md5;
        StringBuilder   m_hash_str;
        int             m_block_num;

        public bool          CanReuseTransform { get { return false; } }
        public bool CanTransformMultipleBlocks { get { return true; } }
        public int              InputBlockSize { get { return BlockSize; } }
        public int             OutputBlockSize { get { return BlockSize; } }

        public MsdTransform (string key)
        {
            m_key = key;
            m_md5 = MD5.Create();
            m_hash_str = new StringBuilder (BlockSize);
            m_block_num = 0;
        }

        public int TransformBlock (byte[] inputBuffer, int inputOffset, int inputCount,
                                   byte[] outputBuffer, int outputOffset)
        {
            int block_count = inputCount / BlockSize;
            for (int i = 0; i < block_count; ++i)
            {
                DoTransform (inputBuffer, inputOffset, BlockSize, outputBuffer, outputOffset);
                inputOffset += BlockSize;
                outputOffset += BlockSize;
            }
            return inputCount;
        }

        public byte[] TransformFinalBlock (byte[] inputBuffer, int inputOffset, int inputCount)
        {
            byte[] outputBuffer = new byte[inputCount];
            DoTransform (inputBuffer, inputOffset, inputCount, outputBuffer, 0);
            return outputBuffer;
        }

        void DoTransform (byte[] input, int src, int count, byte[] output, int dst)
        {
            string chunk_key_str = m_key + m_block_num.ToString();
            ++m_block_num;
            var chunk_key = Encodings.cp932.GetBytes (chunk_key_str);
            var hash = m_md5.ComputeHash (chunk_key);
            m_hash_str.Clear();
            for (int j = 0; j < hash.Length; ++j)
                m_hash_str.AppendFormat ("{0:x2}", hash[j]);
            count = Math.Min (m_hash_str.Length, count);
            for (int k = 0; k < count; ++k)
                output[dst++] = (byte)(input[src++] ^ m_hash_str[k]);
        }

        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_md5.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize (this);
        }
    }

    [Serializable]
    public class FjsysScheme : ResourceScheme
    {
        public Dictionary<string, string> MsdPasswords;
    }

    public class FjsysOptions : ResourceOptions
    {
        public string MsdPassword;
    }
}
