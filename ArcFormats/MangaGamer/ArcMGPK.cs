//! \file       ArcMGPK.cs
//! \date       Mon Nov 03 20:03:36 2014
//! \brief      MGPK archive format.
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.Mg
{
    internal class MgArchive : ArcFile
    {
        public readonly byte[] Key;

        public MgArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    public class MgOptions : ResourceOptions
    {
        public byte[] Key;
    }

    [Serializable]
    public class MgScheme : ResourceScheme
    {
        public Dictionary<string, byte[]> KnownKeys;
    }

    [Export(typeof(ArchiveFormat))]
    public class MgpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MGPK"; } }
        public override string Description { get { return "MG resource archive"; } }
        public override uint     Signature { get { return 0x4b50474d; } } // MGPK
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public MgpkOpener ()
        {
            Extensions = new string[] { "pac" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadInt32 (4);
            int count = file.View.ReadInt32 (8);
            if (version < 1 || !IsSaneCount (count))
                return null;
            long cur_offset = 0x0C;
            var dir = new List<Entry> (count);
            bool has_encrypted = false;
            for (int i = 0; i < count; ++i)
            {
                uint name_length = file.View.ReadByte (cur_offset);
                string name = file.View.ReadString (cur_offset+1, name_length, Encoding.UTF8);
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (cur_offset+0x20);
                entry.Size = file.View.ReadUInt32 (cur_offset+0x24);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                has_encrypted = has_encrypted || name.HasAnyOfExtensions ("png", "txt");
                dir.Add (entry);
                cur_offset += 0x30;
            }
            if (has_encrypted && KnownKeys.Count > 0)
            {
                var key = QueryKey (file.Name);
                if (key != null)
                    return new MgArchive (file, this, dir, key);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var mgarc = arc as MgArchive;
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (null == mgarc || null == mgarc.Key)
                return input;
            using (input)
            {
                byte[] data = new byte[entry.Size];
                input.Read (data, 0, data.Length);
                DecryptData (data, mgarc.Key);
                if (entry.Name.HasExtension ("txt"))
                    return DecompressStream (data);
                return new BinMemoryStream (data, entry.Name);
            }
        }

        protected virtual void DecryptData (byte[] input, byte[] key)
        {
            key = (byte[])key.Clone();
            for (int i = 0; i < input.Length; i++)
            {
                input[i] ^= key[i % key.Length];
                key[i % key.Length] += 27;
            }
        }

        internal Stream DecompressStream (byte[] input)
        {
            byte[] output = new byte[input.Length * 2];
            int output_size = lzf_decompress (input, ref output);
            return new BinMemoryStream (output, 0, output_size);
        }

        private static int lzf_decompress (byte[] input, ref byte[] output)
        {
            int src = 0;
            int dst = 0;
            while (src < input.Length)
            {
                int count = input[src++];
                if (count < 32)
                {
                    ++count;
                    if (dst + count > output.Length)
                    {
                        Array.Resize (ref output, Math.Max (checked(output.Length * 2), dst + count));
                    }
                    Buffer.BlockCopy (input, src, output, dst, count);
                    src += count;
                    dst += count;
                }
                else
                {
                    int offset = (count & 31) << 8;
                    count >>= 5;
                    if (7 == count)
                    {
                        count += input[src++];
                    }
                    count += 2;
                    offset += input[src++] + 1;
                    if (offset > dst)
                        throw new InvalidFormatException();
                    if (dst + count > output.Length)
                    {
                        Array.Resize (ref output, Math.Max (checked(output.Length * 2), dst + count));
                    }
                    Binary.CopyOverlapped (output, dst-offset, dst, count);
                    dst += count;
                }
            }
            return dst;
        }

        internal byte[] QueryKey (string arc_name)
        {
            var options = Query<MgOptions> (arcStrings.ArcEncryptedNotice);
            return options.Key;
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new MgOptions { Key = GetKey (Properties.Settings.Default.MGPKTitle) };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetMGPK();
        }

        public static byte[] GetKey (string title)
        {
            byte[] key;
            if (string.IsNullOrEmpty (title) || !KnownKeys.TryGetValue (title, out key))
                return null;
            return key;
        }

        public static Dictionary<string, byte[]> KnownKeys { get { return DefaultScheme.KnownKeys; } }

        static MgScheme DefaultScheme = new MgScheme { KnownKeys = new Dictionary<string, byte[]>() };

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (MgScheme)value; }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class Mgpk0Opener : MgpkOpener
    {
        public override string         Tag { get { return "MGPK0"; } }
        public override string Description { get { return "MG resource archive"; } }
        public override uint     Signature { get { return 0x4B50474D; } } // MGPK
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadInt32 (4);
            int count = file.View.ReadInt32 (8);
            if (version != 0 || !IsSaneCount (count))
                return null;
            long cur_offset = 0x0C;
            var dir = new List<Entry> (count);
            bool has_encrypted = false;
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (cur_offset, 0x20, Encoding.UTF8);
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (cur_offset+0x20);
                entry.Size = file.View.ReadUInt32 (cur_offset+0x2C);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                has_encrypted = has_encrypted || name.HasAnyOfExtensions ("png", "txt");
                dir.Add (entry);
                cur_offset += 0x30;
            }
            if (has_encrypted)
            {
                var key = QueryKey (file.Name);
                if (key != null)
                    return new MgArchive (file, this, dir, key);
            }
            return new ArcFile (file, this, dir);
        }

        protected override void DecryptData (byte[] input, byte[] key)
        {
            for (int i = 0; i < input.Length; i++)
            {
                input[i] ^= key[i % key.Length];
            }
        }

        public override ResourceScheme Scheme { get; set; }
    }
}
