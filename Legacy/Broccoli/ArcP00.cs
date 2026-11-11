//! \file       ArcP00.cs
//! \date       2026-01-08
//! \brief      Broccoli resource archive format.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.IO.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Broccoli {
    internal class P00Entry : Entry {
        public string FileName;
    }

    internal static class IkusabuneHelper
    {
        public static uint ReadSignature (Stream input)
        {
            uint v = (uint)input.ReadByte ();
            v |= (uint)input.ReadByte () << 8;
            v |= (uint)input.ReadByte () << 16;
            v |= (uint)input.ReadByte () << 24;
            return v;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class P00Opener : ArchiveFormat {
        public override string         Tag { get { return "P00"; } }
        public override string Description { get { return "Ikusabune multipart archive format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public P00Opener() {
            Extensions = new string[] { "pak", "p00", "p01", "p02", "p03", "p04", "p05", "p06", "p07", "p08", "p09" };
        }

        public override ArcFile TryOpen(ArcView file) {
            var index_file_name = Path.ChangeExtension(file.Name, "pak");
            if (!VFS.FileExists(index_file_name))
                return null;
            using (var index_file = VFS.OpenView(index_file_name)) {
                if (!index_file.View.AsciiEqual(0, "IPF "))
                    return null;

                int count = index_file.View.ReadInt32(4);
                if (!IsSaneCount(count))
                    return null;

                long index_offset = 8;
                var dir = new List<Entry>(count);
                for (int i = 0; i < count; i++) {
                    uint hash = index_file.View.ReadUInt32(index_offset);
                    string name = hash.ToString("X8");
                    var entry = Create<P00Entry>(name);
                    entry.Offset = index_file.View.ReadUInt32(index_offset + 4);
                    entry.Size   = index_file.View.ReadUInt32(index_offset + 8);
                    var data_file_name = Path.ChangeExtension(file.Name, string.Format("p{0:00}", entry.Offset >> 28));
                    if (!VFS.FileExists(data_file_name))
                        return null;
                    entry.FileName = data_file_name;
                    dir.Add(entry);
                    index_offset += 12;
                }
                DetectEntryTypes (dir);
                return new ArcFile(file, this, dir);
            }
        }

        void DetectEntryTypes (IList<Entry> dir)
        {
            var parts = new Dictionary<string, ArcView> ();
            try
            {
                foreach (P00Entry entry in dir)
                {
                    var res = DetectEntryType (parts, entry);
                    if (null != res)
                        entry.ChangeType (res);
                }
            }
            finally
            {
                foreach (var part in parts.Values)
                    part.Dispose ();
            }
        }

        IResource DetectEntryType (Dictionary<string, ArcView> parts, P00Entry entry)
        {
            if (entry.Size < 0x0A)
                return null;
            ArcView part;
            if (!parts.TryGetValue (entry.FileName, out part))
            {
                part = VFS.OpenView (entry.FileName);
                parts[entry.FileName] = part;
            }
            try
            {
                uint offset = (uint)entry.Offset & 0xFFFFFFF;
                uint signature = part.View.ReadUInt32 (offset);
                if (0x305A == (signature & 0xFFFF)) // 'Z0' compressed block
                {
                    using (var input = part.CreateStream (offset+0x0A, (uint)(entry.Size-0x0A)))
                    using (var z = new DeflateStream (input, CompressionMode.Decompress))
                        return AutoEntry.DetectFileType (IkusabuneHelper.ReadSignature (z));
                }
                return AutoEntry.DetectFileType (signature);
            }
            catch
            {
                return null;
            }
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry) {
            var pent = entry as P00Entry;
            using (var data_file = new ArcView(pent.FileName)) {
                var input = data_file.CreateStream(pent.Offset & 0xFFFFFFF, pent.Size);
                if (input.ReadUInt16() == 0x305A) { // 'Z0'
                    input.Seek(10, SeekOrigin.Begin);
                    return new DeflateStream(input, CompressionMode.Decompress);
                }
                else {
                    input.Seek(0, SeekOrigin.Begin);
                    return input;
                }
            }
        }
    }

    internal class IpfbEntry : Entry
    {
        public string FileName;
    }

    [Export(typeof(ArchiveFormat))]
    public class IpfbOpener : ArchiveFormat
    {
        public override string         Tag { get { return "IPFB"; } }
        public override string Description { get { return "Ikusabune multipart archive format (IPFB)"; } }
        public override uint     Signature { get { return 0x42465049; } } // 'IPFB'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public IpfbOpener ()
        {
            Extensions = new string[] { "pak", "p00", "p01", "p02", "p03", "p04", "p05", "p06", "p07",
                                        "p08", "p09" };
            // Also tried for any file, so opening a pXX data part can locate the index via its sibling .pak.
            Signatures = new uint[] { 0x42465049, 0 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            // The index may be opened directly, or one of the pXX data parts may be opened instead.
            var index = file;
            bool borrowed = false;
            if (!file.View.AsciiEqual (0, "IPFB"))
            {
                var index_name = Path.ChangeExtension (file.Name, "pak");
                if (!VFS.FileExists (index_name))
                    return null;
                index = VFS.OpenView (index_name);
                borrowed = true;
            }
            try
            {
                if (!index.View.AsciiEqual (0, "IPFB"))
                    return null;

                int count = (int)Binary.BigEndian (index.View.ReadUInt32 (4));
                if (!IsSaneCount (count))
                    return null;

                long index_offset = 0x10;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; i++)
                {
                    uint hash = Binary.BigEndian (index.View.ReadUInt32 (index_offset));
                    if (0 == hash)
                        break;
                    uint offset = Binary.BigEndian (index.View.ReadUInt32 (index_offset + 4));
                    var entry = new IpfbEntry {
                        Name = hash.ToString ("X8"),
                        Offset = offset,
                        Size = Binary.BigEndian (index.View.ReadUInt32 (index_offset + 8)),
                    };
                    var data_file_name = Path.ChangeExtension (file.Name, string.Format ("p{0:00}", offset >> 28));
                    if (!VFS.FileExists (data_file_name))
                        return null;
                    entry.FileName = data_file_name;
                    dir.Add (entry);
                    index_offset += 12;
                }
                DetectEntryTypes (dir);
                return new ArcFile (file, this, dir);
            }
            finally
            {
                if (borrowed)
                    index.Dispose ();
            }
        }

        void DetectEntryTypes (IList<Entry> dir)
        {
            var parts = new Dictionary<string, ArcView> ();
            try
            {
                foreach (IpfbEntry entry in dir)
                {
                    var res = DetectEntryType (parts, entry);
                    if (null != res)
                        entry.ChangeType (res);
                }
            }
            finally
            {
                foreach (var part in parts.Values)
                    part.Dispose ();
            }
        }

        IResource DetectEntryType (Dictionary<string, ArcView> parts, IpfbEntry entry)
        {
            if (entry.Size < 0x0C)
                return null;
            ArcView part;
            if (!parts.TryGetValue (entry.FileName, out part))
            {
                part = VFS.OpenView (entry.FileName);
                parts[entry.FileName] = part;
            }
            try
            {
                uint offset = (uint)entry.Offset & 0xFFFFFFF;
                uint signature = part.View.ReadUInt32 (offset);
                if (0x315A == (signature & 0xFFFF)) // 'Z1' zlib block
                {
                    using (var input = part.CreateStream (offset+0x0C, (uint)(entry.Size-0x0C)))
                    using (var z = new DeflateStream (input, CompressionMode.Decompress))
                        return AutoEntry.DetectFileType (IkusabuneHelper.ReadSignature (z));
                }
                return AutoEntry.DetectFileType (signature);
            }
            catch
            {
                return null;
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var ient = entry as IpfbEntry;
            var input = new BinaryStream (new StreamRegion (VFS.OpenStream (ient.FileName),
                                          ient.Offset & 0xFFFFFFF, ient.Size), entry.Name);
            try
            {
                if (input.Length != ient.Size)
                    throw new InvalidFormatException ();
                if (input.Length >= 2 && input.ReadUInt16 () == 0x315A) // 'Z1'
                {
                    if (input.Length < 12)
                        throw new InvalidFormatException ();
                    input.Position = 12;
                    return new DeflateStream (input, CompressionMode.Decompress);
                }
                input.Position = 0;
                return input;
            }
            catch
            {
                input.Dispose ();
                throw;
            }
        }
    }
}
