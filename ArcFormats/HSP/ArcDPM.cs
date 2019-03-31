//! \file       ArcDPM.cs
//! \date       Sun Jan 22 22:17:21 2017
//! \brief      Hot Soup Processor engine resource archive.
//
// Copyright (C) 2017 by morkt
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

namespace GameRes.Formats.HSP
{
    [Export(typeof(ArchiveFormat))]
    public class DpmOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DPM"; } }
        public override string Description { get { return "Hot Soup Processor resource archive"; } }
        public override uint     Signature { get { return 0x584D5044; } } // 'DPMX'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public DpmOpener ()
        {
            Extensions = new string[] { "dpm", "bin" };
            Signatures = new uint[] { 0x584D5044, 0 };
        }

        static readonly uint DefaultKey = 0xAC52AE58; // 0x24B70413

        public override ArcFile TryOpen (ArcView file)
        {
            uint signature = file.View.ReadUInt32 (0);
            bool is_inside_exe = false;
            long base_offset = 0;
            uint arc_key = 0;
            if (0x5A4D == (signature & 0xFFFF)) // 'MZ'
            {
                var exe = new ExeFile (file);
                if (exe.Overlay.Size <= 4 || !file.View.AsciiEqual (exe.Overlay.Offset, "DPMX"))
                    return null;
                base_offset = exe.Overlay.Offset;
                arc_key = FindExeKey (exe, base_offset);
                is_inside_exe = true;
            }
            else if (0x584D5044 != signature)
                return null;
            int count = file.View.ReadInt32 (base_offset+8);
            if (!IsSaneCount (count))
                return null;
            long index_offset = base_offset + 0x10 + file.View.ReadUInt32 (base_offset+0xC);
            uint data_size = (uint)(file.MaxOffset - (index_offset + 32 * count));
            base_offset += file.View.ReadUInt32 (base_offset+4);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x10);
                index_offset += 0x14;
                var entry = Create<DpmEntry> (name);
                entry.Key = file.View.ReadUInt32 (index_offset);
                entry.Offset = file.View.ReadUInt32 (index_offset+4) + base_offset;
                entry.Size   = file.View.ReadUInt32 (index_offset+8);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0xC;
            }
            if (is_inside_exe)
                return new DpmArchive (file, this, dir, arc_key, data_size);
            else
                return new DpmArchive (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var dent = entry as DpmEntry;
            var darc = arc as DpmArchive;
            if (null == dent || null == darc || 0 == dent.Key)
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            darc.DecryptEntry2 (data, dent.Key);
            return new BinMemoryStream (data, entry.Name);
        }

        static uint FindExeKey (ExeFile exe, long dpm_offset)
        {
            uint base_offset = (uint)(dpm_offset - 0x10000);
            var offset_str = base_offset.ToString() + '\0';
            var offset_bytes = Encoding.ASCII.GetBytes (offset_str);
            long key_pos = -1;
            if (exe.ContainsSection (".rdata"))
                key_pos = exe.FindString (exe.Sections[".rdata"], offset_bytes);
            if (-1 == key_pos && exe.ContainsSection (".data"))
                key_pos = exe.FindString (exe.Sections[".data"], offset_bytes);
            if (-1 == key_pos)
                return DefaultKey;
            return exe.View.ReadUInt32 (key_pos+0x17);
        }

        DpmxScheme DefaultScheme = new DpmxScheme { KnownKeys = new Dictionary<string, uint>() };

        public IDictionary<string, uint> KnownKeys { get { return DefaultScheme.KnownKeys; } }

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (DpmxScheme)value; }
        }
    }

    internal class DpmEntry : Entry
    {
        public uint Key;
    }

    internal class DpmArchive : ArcFile
    {
        readonly byte Seed1;
        readonly byte Seed2;

        public DpmArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir)
            : base (arc, impl, dir)
        {
            Seed1 = 0xAA;
            Seed2 = 0x55;
        }

        public DpmArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, uint arc_key, uint dpm_size)
            : base (arc, impl, dir)
        {
            Seed1 = (byte)((((arc_key >> 16) & 0xFF) * (arc_key & 0xFF) / 3) ^ dpm_size);
            Seed2 = (byte)((((arc_key >> 8)  & 0xFF) * ((arc_key >> 24) & 0xFF) / 5) ^ dpm_size ^ 0xAA);
        }

        internal void DecryptEntry (byte[] data, uint entry_key)
        {
            byte s1 = 0xA5; // FIXME these constants seem to vary across games
            byte s2 = 0x5A; // but this engine is rare, can't confirm for sure.
            s1 = (byte)(Seed1 + ((entry_key >> 16) ^  (entry_key       + s1)));
            s2 = (byte)(Seed2 + ((entry_key >> 24) ^ ((entry_key >> 8) + s2)));
            byte val = 0;
            for (int i = 0; i < data.Length; ++i)
            {
                val += (byte)(s1 ^ (data[i] - s2));
                data[i] = val;
            }
        }

        internal void DecryptEntry2 (byte[] data, uint entry_key)
        {
            byte s1 = 0x5A;
            byte s2 = 0xA5;
            s1 = (byte)(Seed1 + ((entry_key >> 16) ^  (entry_key       + s1)));
            s2 = (byte)(Seed2 + ((entry_key >> 24) ^ ((entry_key >> 8) + s2)));
            byte val = 0;
            for (int i = 0; i < data.Length; ++i)
            {
                val += (byte)((s1 ^ data[i]) - s2);
                data[i] = val;
            }
        }
    }

    [Serializable]
    public class DpmxScheme : ResourceScheme
    {
        public IDictionary<string, uint>    KnownKeys;
    }
}
