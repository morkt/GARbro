//! \file       ArcDAT.cs
//! \date       2026-9-13
//! \brief      NekoNyan Unity Engine Resource Archive
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
using System.Runtime.InteropServices;
using System.Text;
using CommunityToolkit.HighPerformance;

namespace GameRes.Formats.NekoNyan
{
    internal class NekoNyanDATArchiveV1 : ArcFile
    {
        public readonly ArchiveCryptoBase Crypto;
        public NekoNyanDATArchiveV1(ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, ArchiveCryptoBase crypto)
            :base (arc, impl, dir)
        {
            this.Crypto = crypto;
        }
    }

    internal class NekoNyanDATEntryV1 : Entry
    {
        public uint Key { get; set; }

        public bool CheckValidRange(long maxOffset)
        {
            return this.Offset <= maxOffset && this.Size <= maxOffset && this.Offset <= maxOffset - this.Size;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class NekoNyanDATOpenerV1 : ArchiveFormat
    {
        public override string Tag => "DAT/NekoNyan";
        public override string Description => "NekoNyan Unity Engine Resource Archive";
        public override uint Signature => 0u;
        public override bool IsHierarchic => true;
        public override bool CanWrite => false;

        public NekoNyanDATOpenerV1()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen(ArcView file)
        {
            ArchiveCryptoBase crypto = QueryTitle(file.Name);
            if (crypto is null)
            {
                return null;
            }
            if (!crypto.Load(file, out List<ArchiveCryptoBase.FileEntry> entries))
            {
                return null;
            }

            List<Entry> dir = new List<Entry>(entries.Count);
            foreach(ArchiveCryptoBase.FileEntry fe in entries)
            {
                NekoNyanDATEntryV1 nnde = Create<NekoNyanDATEntryV1>(fe.FileName);
                nnde.Offset = fe.Offset;
                nnde.Size = fe.Size;
                nnde.Key = fe.Key;

                if (!nnde.CheckValidRange(file.MaxOffset))
                {
                    return null;
                }

                dir.Add(nnde);
            }

            return new NekoNyanDATArchiveV1(file, this, dir, crypto);
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            Stream stream = base.OpenEntry(arc, entry);
            if(!(arc is NekoNyanDATArchiveV1 nnarc) || !(entry is NekoNyanDATEntryV1 e))
            {
                return stream;
            }

            byte[] data = new byte[stream.Length];
            stream.Read(data, 0, data.Length);

            nnarc.Crypto.DecryptData(data, e.Key);

            return new MemoryStream(data, false);
        }

        private static ArchiveCryptoBase QueryTitle(string path)
        {
            string dir = Path.GetDirectoryName(path);
            while (!string.IsNullOrEmpty(dir) && Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly).Length == 0)
            {
                dir = Path.GetDirectoryName(dir);
            }
            if (string.IsNullOrEmpty(dir))
            {
                return null;
            }

            string up = Path.Combine(dir, "UnityPlayer.dll");
            string uch32 = Path.Combine(dir, "UnityCrashHandler32.exe");
            string uch64 = Path.Combine(dir, "UnityCrashHandler64.exe");
            if (!File.Exists(up) || !(File.Exists(uch32) || File.Exists(uch64)))
            {
                return null;
            }

            return ArchiveCryptoBase.CreateFactory(dir);
        }
    }

    internal abstract class ArchiveCryptoBase
    {
        public struct FileEntry
        {
            public string FileName;
            public uint Offset;
            public uint Size;
            public uint Key;
        }

        private static readonly Dictionary<string, ArchiveCryptoBase> smTitles = new Dictionary<string, ArchiveCryptoBase>()
        {
            { "Aokana.exe", new ArchiveCryptoV10() },
            { "AokanaEXTRA1.exe", new ArchiveCryptoV10() },
            { "Kinkoi.exe", new ArchiveCryptoV10() },
            { "AokanaEXTRA2.exe", new ArchiveCryptoV11() },
            { "Clover Days Plus.exe", new ArchiveCryptoV12()},
            { "KoiChoco.exe", new ArchiveCryptoV13() },
        };

        public static ArchiveCryptoBase CreateFactory(string root)
        {
            foreach(KeyValuePair<string, ArchiveCryptoBase> pair in ArchiveCryptoBase.smTitles)
            {
                string path = Path.Combine(root, pair.Key);
                if (File.Exists(path))
                {
                    return pair.Value;
                }
            }
            return null;
        }

        public bool Load(ArcView file, out List<FileEntry> entries)
        {
            using (ArcViewStream stream = file.CreateStream())
            {
                return this.Initialize(stream, out entries);
            }
        }

        public void DecryptData(byte[] data, uint key)
        {
            this.Decrypt(data, key);
        }

        protected abstract bool Initialize(Stream stream, out List<FileEntry> entries);

        protected abstract List<FileEntry> ParseFileEntry(ReadOnlySpan<byte> rawEntryData, ReadOnlySpan<byte> rawFileNamesData, int fileCount);

        protected abstract void KeyGenerator(Span<byte> tablePtr, uint key);

        protected abstract void Decrypt(Span<byte> data, uint key);
    }

    internal class ArchiveCryptoV10 : ArchiveCryptoBase
    {
        protected override bool Initialize(Stream stream, out List<FileEntry> entries)
        {
            entries = null;

            byte[] rawPkgInfo = new byte[1024];
            if (stream.Read(rawPkgInfo, 0, rawPkgInfo.Length) != rawPkgInfo.Length)
            {
                return false;
            }

            int fileCount = 0;
            uint rawFileEntryKey = 0u;
            uint rawFileNamesKey = 0u;
            {
                Span<int> rawPkgInfoPack4 = MemoryMarshal.Cast<byte, int>(rawPkgInfo);
                for (int i = 4; i < 255; ++i)
                {
                    fileCount += rawPkgInfoPack4[i];
                }
                rawFileEntryKey = (uint)rawPkgInfoPack4[53];
                rawFileNamesKey = (uint)rawPkgInfoPack4[23];
            }

            byte[] rawEntryData = new byte[16 * fileCount];
            if (stream.Read(rawEntryData, 0, rawEntryData.Length) != rawEntryData.Length)
            {
                return false;
            }
            this.Decrypt(rawEntryData, rawFileEntryKey);

            byte[] rawFileNamesData = new byte[BitConverter.ToInt32(rawEntryData, 12) - (1024 + rawEntryData.Length)];
            if (stream.Read(rawFileNamesData, 0, rawFileNamesData.Length) != rawFileNamesData.Length)
            {
                return false;
            }
            this.Decrypt(rawFileNamesData, rawFileNamesKey);

            entries = this.ParseFileEntry(rawEntryData, rawFileNamesData, fileCount);
            return true;
        }

        protected override List<FileEntry> ParseFileEntry(ReadOnlySpan<byte> rawEntryData, ReadOnlySpan<byte> rawFileNamesData, int fileCount)
        {
            List<FileEntry> entries = new List<FileEntry>(fileCount);

            ReadOnlySpan<uint> rawEntryDataPack4 = MemoryMarshal.Cast<byte, uint>(rawEntryData);
            for (int i = 0; i < fileCount; ++i)
            {
                int pos = 4 * i;
                FileEntry entry = new FileEntry()
                {
                    Size = rawEntryDataPack4[pos + 0],
                    Key = rawEntryDataPack4[pos + 2],
                    Offset = rawEntryDataPack4[pos + 3]
                };

                int fileNameOffset = (int)rawEntryDataPack4[pos + 1];
                int fileNameLen = rawFileNamesData.Slice(fileNameOffset).IndexOf((byte)0x00);

                entry.FileName = Encoding.UTF8.GetString(rawFileNamesData.Slice(fileNameOffset, fileNameLen).ToArray()).ToLower();

                entries.Add(entry);
            }

            return entries;
        }

        protected override void KeyGenerator(Span<byte> tablePtr, uint key)
        {
            uint k1 = key * 0x00001CDF + 0x0000A74C;
            uint k2 = k1 << 0x11 ^ k1;

            for (int i = 0; i < 256; ++i)
            {
                k1 = k1 - key + k2;
                k2 = k1 + 0x38;
                k1 *= k2 & 0xEF;
                tablePtr[i] = (byte)k1;
                k1 >>= 1;
            }
        }

        protected override void Decrypt(Span<byte> data, uint key)
        {
            Span<byte> table = stackalloc byte[256];
            this.KeyGenerator(table, key);
            for (int i = 0; i < data.Length; ++i)
            {
                byte temp = data[i];
                temp ^= table[i % 253];
                temp += 0x03;
                temp += table[i % 89];
                temp ^= 0x99;
                data[i] = temp;
            }
        }
    }

    internal class ArchiveCryptoV11 : ArchiveCryptoV10
    {
        protected override bool Initialize(Stream stream, out List<FileEntry> entries)
        {
            entries = null;

            byte[] rawPkgInfo = new byte[1024];
            if (stream.Read(rawPkgInfo, 0, rawPkgInfo.Length) != rawPkgInfo.Length)
            {
                return false;
            }

            int fileCount = 0;
            uint rawFileEntryKey = 0u;
            uint rawFileNamesKey = 0u;
            {
                Span<int> rawPkgInfoPack4 = MemoryMarshal.Cast<byte, int>(rawPkgInfo);
                for (int i = 3; i < 255; ++i)
                {
                    fileCount += rawPkgInfoPack4[i];
                }
                rawFileEntryKey = (uint)rawPkgInfoPack4[53];
                rawFileNamesKey = (uint)rawPkgInfoPack4[23];
            }

            byte[] rawEntryData = new byte[16 * fileCount];
            if (stream.Read(rawEntryData, 0, rawEntryData.Length) != rawEntryData.Length)
            {
                return false;
            }
            this.Decrypt(rawEntryData, rawFileEntryKey);

            byte[] rawFileNamesData = new byte[BitConverter.ToInt32(rawEntryData, 12) - (1024 + rawEntryData.Length)];
            if (stream.Read(rawFileNamesData, 0, rawFileNamesData.Length) != rawFileNamesData.Length)
            {
                return false;
            }
            this.Decrypt(rawFileNamesData, rawFileNamesKey);

            entries = this.ParseFileEntry(rawEntryData, rawFileNamesData, fileCount);
            return true;
        }

        protected override void KeyGenerator(Span<byte> tablePtr, uint key)
        {
            uint k1 = key * 0x0000131C + 0x0000A740;
            uint k2 = k1 << 0x07 ^ k1;

            for (int i = 0; i < 256; ++i)
            {
                k1 = k1 - key + k2;
                k2 = k1 + 0x9C;
                k1 *= k2 & 0xCE;
                tablePtr[i] = (byte)k1;
                k1 >>= 3;
            }
        }

        protected override void Decrypt(Span<byte> data, uint key)
        {
            Span<byte> table = stackalloc byte[256];
            this.KeyGenerator(table, key);
            for (int i = 0; i < data.Length; ++i)
            {
                byte temp = data[i];
                temp ^= table[i % 179];
                temp += 0x03;
                temp += table[i % 89];
                temp ^= 0x77;
                data[i] = temp;
            }
        }
    }

    internal class ArchiveCryptoV12 : ArchiveCryptoV10
    {
        protected override void Decrypt(Span<byte> data, uint key)
        {
            Span<byte> table = stackalloc byte[256];
            this.KeyGenerator(table, key);
            for (int i = 0; i < data.Length; ++i)
            {
                byte temp = data[i];
                temp ^= table[i % 253];
                temp += table[i % 59];
                temp ^= 0x99;
                data[i] = temp;
            }
        }
    }

    internal class ArchiveCryptoV13 : ArchiveCryptoV11
    {
        protected override void KeyGenerator(Span<byte> tablePtr, uint key)
        {
            uint k1 = key * 0x00001704u + 0x0000A140u;
            uint k2 = k1 << 0x07 ^ k1;

            for (int i = 0; i < 256; ++i)
            {
                k1 = k1 - key + k2;
                k2 = k1 + 0x155u;
                k1 *= k2 & 0xDCu;
                tablePtr[i] = (byte)k1;
                k1 >>= 2;
            }
        }

        protected override void Decrypt(Span<byte> data, uint key)
        {
            Span<byte> table = stackalloc byte[256];
            this.KeyGenerator(table, key);
            for (int i = 0; i < data.Length; ++i)
            {
                byte temp = data[i];
                temp ^= table[i % 235];
                temp += 0x1F;
                temp += table[i % 87];
                temp ^= 0xA5;
                data[i] = temp;
            }
        }
    }
}
