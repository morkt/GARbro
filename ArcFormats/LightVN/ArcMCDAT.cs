//! \file       ArcMCDAT.cs
//! \date       2026-5-27
//! \brief      LightVN Engine mcdat resource archive
//
// Copyright (C) 2026 by morkt
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
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace GameRes.Formats.LightVN
{
    internal class McdatArchive : ArcFile
    {
        private readonly Dictionary<string, string> mMap;
        private readonly string mRoot;
        private readonly byte[] mKey;

        public McdatArchive(ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, 
                            Dictionary<string, string> map, string root, byte[] key)
                : base(arc, impl, dir)
        {
            this.mMap = map;
            this.mRoot = root;
            this.mKey = key;
        }

        public string GetFilePath(string name)
        {
            if (this.mMap.TryGetValue(name, out string relativePath))
            {
                return Path.Combine(this.mRoot, relativePath);
            }
            else
            {
                return string.Empty;
            }
        }

        public void RestoreSize()
        {
            foreach(Entry e in this.Dir)
            {
                string path = this.GetFilePath(e.Name);
                if(!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    FileInfo fi = new FileInfo(path);
                    e.Size = (uint)fi.Length;
                }
            }
        }

        public void Decrypt(byte[] data)
        {
            McdatArchive.Decrypt(data, this.mKey, 100);
        }

        public static void Decrypt(byte[] data, byte[] key, int length)
        {
            int dataLen = data.Length;

            int decLen;
            if (length < 0)
            {
                decLen = dataLen;
            }
            else
            {
                decLen = Math.Min(dataLen, length);
            }

            for (int i = 1; i < decLen; ++i)
            {
                byte k = key[i % key.Length];
                data[dataLen - i] ^= k;
            }

            for (int i = 0; i < decLen; ++i)
            {
                byte k = key[i % key.Length];
                data[i] ^= k;
            }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class McdatOpener : ArchiveFormat
    {
        public override string Tag => "MCDAT/LightVN";
        public override string Description => "LightVN Engine resource archive";
        public override uint Signature => 0u;
        public override bool IsHierarchic => true;
        public override bool CanWrite => false;

        public McdatOpener()
        {
            Extensions = new string[] { "mcdat" };
        }

        private static readonly string smIndexRelativePath = "\\Data\\_\\0.mcdat";
        public static readonly byte[] DefaultKey = new byte[]
        {
            0x64, 0x36, 0x63, 0x35, 0x66, 0x4B, 0x49, 0x33, 0x47, 0x67, 0x42, 0x57, 0x70, 0x5A, 0x46, 0x33,
            0x54, 0x7A, 0x36, 0x69, 0x61, 0x33, 0x6B, 0x46, 0x30,
        };


        public override ArcFile TryOpen(ArcView file)
        {
            if (!file.Name.EndsWith(smIndexRelativePath))
            {
                return null;
            }

            string root = file.Name.Remove(file.Name.Length - smIndexRelativePath.Length);
            byte[] key = this.QueryKey();

            byte[] index = file.View.ReadBytes(0L, (uint)file.MaxOffset);
            McdatArchive.Decrypt(index, key, -1);

            Dictionary<string, string> map = null;
            try
            {
                string json = Encoding.UTF8.GetString(index);
                map = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            }
            catch
            {
                return null;
            }

            if (map == null)
            {
                return null;
            }

            List<Entry> entries = new List<Entry>(map.Count);
            foreach (string name in map.Keys)
            {
                Entry entry = Create<Entry>(name);
                entry.Offset = 0L;
                entry.Size = 0u;
                entries.Add(entry);
            }

            McdatArchive mcdatArc = new McdatArchive(file, this, entries, map, root, key);
            mcdatArc.RestoreSize();
            return mcdatArc;
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            McdatArchive mcdatArc = (McdatArchive)arc;

            string path = mcdatArc.GetFilePath(entry.Name);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                byte[] data = File.ReadAllBytes(path);
                mcdatArc.Decrypt(data);
                return new MemoryStream(data, false);
            }
            return Stream.Null;
        }

        private byte[] QueryKey()
        {
            return DefaultKey;
        }
    }
}
