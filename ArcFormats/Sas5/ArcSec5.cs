//! \file       ArcSec5.cs
//! \date       Fri Oct 23 18:10:06 2015
//! \brief      Sas5 engine resource index file.
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
using System.IO;
using GameRes.Utility;
using System.Text;

namespace GameRes.Formats.Sas5
{
    [Export(typeof(ArchiveFormat))]
    public class Sec5Opener : ArchiveFormat
    {
        public override string         Tag { get { return "SEC5"; } }
        public override string Description { get { return "SAS5 engine resource index file"; } }
        public override uint     Signature { get { return 0x35434553; } } // 'SEC5'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint offset = 8;
            var dir = new List<Entry>();
            while (offset < file.MaxOffset)
            {
                string name = file.View.ReadString (offset, 4, Encoding.ASCII);
                if ("ENDS" == name)
                    break;
                uint section_size = file.View.ReadUInt32 (offset+4);
                offset += 8;
                var entry = new Entry {
                    Name = name,
                    Offset = offset,
                    Size = section_size,
                };
                dir.Add (entry);
                offset += section_size;
            }
            if (dir.Count > 0)
                return new ArcFile (file, this, dir);
            return null;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if ("CODE" != entry.Name)
                return arc.File.CreateStream (entry.Offset, entry.Size);

            var code = new byte[entry.Size];
            arc.File.View.Read (entry.Offset, code, 0, entry.Size);
            DecryptCodeSection (code);
            return new BinMemoryStream (code, entry.Name);
        }

        protected static void DecryptCodeSection (byte[] code)
        {
            byte key = 0;
            for (int i = 0; i < code.Length; ++i)
            {
                int x = code[i] + 18;
                code[i] ^= key;
                key += (byte)x;
            }
        }

        static internal Dictionary<string, Dictionary<int, Entry>> CurrentIndex;

        static internal Dictionary<int, Entry> LookupIndex (string filename)
        {
            if (null == CurrentIndex)
                CurrentIndex = FindSec5Resr (filename);
            if (null == CurrentIndex)
                return null;
            Dictionary<int, Entry> arc_map = null;
            CurrentIndex.TryGetValue (Path.GetFileName (filename), out arc_map);
            return arc_map;
        }

        static internal Dictionary<string, Dictionary<int, Entry>> FindSec5Resr (string arc_name)
        {
            string dir_name = Path.GetDirectoryName (arc_name);
            var match = Directory.GetFiles (dir_name, "*.sec5");
            if (0 == match.Length)
            {
                string parent = Path.GetDirectoryName (dir_name);
                if (!string.IsNullOrEmpty (parent))
                    match = Directory.GetFiles (parent, "*.sec5");
            }
            if (0 == match.Length)
                return null;
            foreach (var m in match)
            {
                using (var sec5 = new ArcView (m))
                {
                    if (!sec5.View.AsciiEqual (0, "SEC5"))
                        continue;
                    uint offset = 8;
                    while (offset < sec5.MaxOffset)
                    {
                        string id = sec5.View.ReadString (offset, 4, Encoding.ASCII);
                        if ("ENDS" == id)
                            break;
                        uint section_size = sec5.View.ReadUInt32 (offset+4);
                        offset += 8;
                        if ("RESR" == id)
                        {
                            using (var resr = sec5.CreateStream (offset, section_size))
                                return ReadResrSection (resr);
                        }
                        if ("RES2" == id)
                        {
                            using (var res2 = sec5.CreateStream (offset, section_size))
                                return ReadRes2Section (res2);
                        }
                        offset += section_size;
                    }
                }
            }
            return null;
        }

        static internal Dictionary<string, Dictionary<int, Entry>> ReadResrSection (Stream input)
        {
            using (var resr = new BinaryReader (input, Encodings.cp932, true))
            {
                int count = resr.ReadInt32();
                if (0 == count)
                    return null;
                var map = new Dictionary<string, Dictionary<int, Entry>> (StringComparer.InvariantCultureIgnoreCase);
                for (int i = 0; i < count; ++i)
                {
                    string name = resr.BaseStream.ReadCString();
                    string type = resr.BaseStream.ReadCString();
                    string arc_type = resr.BaseStream.ReadCString();
                    int res_length = resr.ReadInt32();
                    var next_pos = resr.BaseStream.Position + res_length;
                    if (arc_type == "file-war" || arc_type == "file-iar")
                    {
                        string arc_name = resr.BaseStream.ReadCString();
                        int id = resr.ReadInt32();
                        var base_arc_name = Path.GetFileName (arc_name);
                        if (!map.ContainsKey (base_arc_name))
                            map[base_arc_name] = new Dictionary<int, Entry>();
                        var entry = new Entry
                        {
                            Name = name,
                            Type = type,
                        };
                        map[base_arc_name][id] = entry;
                    }
                    resr.BaseStream.Position = next_pos;
                }
                return map.Count > 0 ? map : null;
            }
        }

        static internal Dictionary<string, Dictionary<int, Entry>> ReadRes2Section (Stream input)
        {
            using (var resr = new Res2Reader (input))
                return resr.Read();
        }

        static internal Entry GetEntryBySectionName (ArcView file, string section_name)
        {
            if (file == null)
                return null;
            uint offset = 8;
            while (offset < file.MaxOffset)
            {
                string name = file.View.ReadString (offset, 4, Encoding.ASCII);
                if ("ENDS" == name)
                    break;
                uint section_size = file.View.ReadUInt32 (offset+4);
                offset += 8;
                if (section_name == name)
                {
                    var entry = new Entry {
                        Name = name,
                        Offset = offset,
                        Size = section_size,
                    };
                    return entry;
                }
                offset += section_size;
            }
            return null;
        }
    }

    internal class Sep5Archive : ArcFile
    {
        private ArcView m_sec5;

        public ArcView Sec5File { get { return m_sec5; } }

        public Sep5Archive (ArcView arc, ArcView sec5, ArchiveFormat impl, ICollection<Entry> dir)
            : base (arc, impl, dir)
        {
            m_sec5 = sec5;
        }
    }

    internal class Sep5Entry : PackedEntry
    {
        public byte PType;
    }

    [Export(typeof(ArchiveFormat))]
    public class Sep5Opener : Sec5Opener
    {
        public override string         Tag { get { return "SEP5"; } }
        public override string Description { get { return "SAS5 engine patched resource index file"; } }
        public override uint     Signature { get { return 0x35504553; } } // 'SEP5'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint offset = 8;
            var dir = new List<Entry>();
            ArcView sec5 = null;

            while (offset < file.MaxOffset)
            {
                string name = file.View.ReadString (offset, 4, Encoding.ASCII);
                if ("ENDS" == name)
                    break;
                uint section_size = file.View.ReadUInt32 (offset + 4);
                offset += 8;
                if ("OLDF" == name)
                    sec5 = new ArcView (file.View.ReadString (offset + 0x18, section_size - 0x18, Encoding.ASCII));
                else
                {
                    byte patch_type = file.View.ReadByte (offset);
                    uint size;
                    switch (patch_type)
                    {
                        case 0:
                            if (section_size != 1) return null;
                            var ent = GetEntryBySectionName (sec5, name);
                            if (ent == null) return null;
                            size = ent.Size;
                            break;
                        case 1:
                            size = section_size - 1;
                            break;
                        case 2:
                            if ("CODE" == name)
                            {
                                var tmp = new byte[0x1D];
                                file.View.Read (offset + 1, tmp, 0, 0x1D);
                                DecryptCodeSection (tmp);
                                size = BitConverter.ToUInt32 (tmp, 0x19);
                            }
                            else
                                size = file.View.ReadUInt32 (offset + 0x1A);
                            break;
                        default:
                            return null;
                    }
                    var entry = new Sep5Entry {
                        Name = name,
                        Offset = offset + 1,
                        Size = section_size - 1,
                        UnpackedSize = size,
                        PType = patch_type,
                    };
                    dir.Add (entry);
                }
                offset += section_size;
            }

            if (dir.Count > 0 && sec5 != null)
                return new Sep5Archive (file, sec5, this, dir);
            return null;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var sent = entry as Sep5Entry;
            var sep5 = arc as Sep5Archive;
            var ent = GetEntryBySectionName (sep5.Sec5File, sent.Name);

            var patch = new byte[sent.Size];
            sep5.File.View.Read (sent.Offset, patch, 0, sent.Size);

            var src = new byte[ent.Size];
            sep5.Sec5File.View.Read (ent.Offset, src, 0, ent.Size);

            if (sent.Name == "CODE")
            {
                DecryptCodeSection (src);
                DecryptCodeSection (patch);
            }

            if (sent.PType == 0)
                return new BinMemoryStream (src, sent.Name);
            if (sent.PType == 1)
                return new BinMemoryStream (patch, sent.Name);

            var data = new byte[sent.UnpackedSize]; 
            using (var mem = new MemoryStream (patch))
            using (var reader = new BinaryReader (mem))
            {
                byte compressed_flag = reader.ReadByte();
                var psizes = new uint[3];
                var usizes = new uint[3];
                var buffers = new byte[3][];
                for (int i = 0; i < 3; ++i)
                    psizes[i] = reader.ReadUInt32();
                for (int i = 0; i < 3; ++i)
                    usizes[i] = reader.ReadUInt32();
                reader.ReadUInt32();
                for (int i = 0; i < 3; ++i)
                {
                    var packed = new byte[psizes[i]];
                    var unpacked = new byte[usizes[i]];
                    reader.Read (packed, 0, packed.Length);
                    if ((compressed_flag & (1 << i)) != 0)
                    {
                        using (var stream = new BinMemoryStream (packed))
                        using (var decompressor = new IarDecompressor (stream))
                            decompressor.Unpack (unpacked);
                        buffers[i] = unpacked;
                    }
                    else
                        buffers[i] = packed;
                }
                ApplyPatch (src, data, buffers[0], buffers[1], buffers[2]);
            }
            return new BinMemoryStream (data, sent.Name);
        }

        protected static void ApplyPatch (byte[] original, byte[] output, byte[] control, byte[] basic, byte[] append)
        {
            int original_index = 0, output_index = 0, basic_index = 0, append_index = 0;
            using (var mem = new MemoryStream (control))
            using (var reader = new BinaryReader (mem))
            {
                int base_size, tail_size, offset;
                for (int i = 0; i < control.Length / 12; ++i)
                {
                    base_size = reader.ReadInt32();
                    tail_size = reader.ReadInt32();
                    offset = reader.ReadInt32();

                    Buffer.BlockCopy (basic, basic_index, output, output_index, base_size);
                    for (int j = 0; j < base_size; j++)
                        output[output_index + j] += original[original_index + j];
                    Buffer.BlockCopy (append, append_index, output, output_index + base_size, tail_size);

                    basic_index += base_size;
                    append_index += tail_size;
                    original_index += base_size + offset;
                    output_index += base_size + tail_size;
                }
            }
        }
    }

    internal sealed class Res2Reader : IDisposable
    {
        BinaryReader                m_resr;
        byte[]                      m_strings;
        Dictionary<int, string>     m_string_cache = new Dictionary<int, string>();

        public Res2Reader (Stream input)
        {
            m_resr = new BinaryReader (input, Encodings.cp932, true);
        }

        public Dictionary<string, Dictionary<int, Entry>> Read ()
        {
            int section_size = m_resr.ReadInt32();
            m_strings = m_resr.ReadBytes (section_size);
            if (m_strings.Length != section_size)
                return null;
            var map = new Dictionary<string, Dictionary<int, Entry>> (StringComparer.InvariantCultureIgnoreCase);
            int count = m_resr.ReadInt32();
            for (int i = 0; i < count; ++i)
            {
                string name = ReadString();
                string type = ReadString();
                string arc_type = ReadString();
                int param_count = ReadInteger();
                string arc_name = null;
                int? arc_index = null;
                for (int j = 0; j < param_count; ++j)
                {
                    string param_name = ReadString();
                    if ("path" == param_name)
                        arc_name = ReadString();
                    else if ("arc-index" == param_name)
                        arc_index = ReadInteger();
                    else if ("arc-path" == param_name)
                        name = ReadString();
                    else
                        SkipObject();
                }
                if (!string.IsNullOrEmpty (arc_name))
                {
                    arc_name = Path.GetFileName (arc_name);
                    if (!map.ContainsKey (arc_name))
                        map[arc_name] = new Dictionary<int, Entry>();
                    var entry = new Entry
                    {
                        Name = name,
                        Type = type,
                    };
                    if (arc_index == null)
                        arc_index = map[arc_name].Count;
                    map[arc_name][arc_index.Value] = entry;
                }
            }
            return map.Count > 0 ? map : null;
        }

        string ReadString ()
        {
            int n = m_resr.ReadByte();
            if (0x90 != (n & 0xF8))
                throw new InvalidFormatException ("[ReadString] SEC5 deserialization error");

            int offset = ReadNumber (n);
            if (offset >= m_strings.Length)
                throw new InvalidFormatException ("[ReadString] SEC5 deserialization error");
            string s;
            if (!m_string_cache.TryGetValue (offset, out s))
            {
                int str_length = LittleEndian.ToInt32 (m_strings, offset);
                s = Encodings.cp932.GetString (m_strings, offset+4, str_length);
                m_string_cache[offset] = s;
            }
            return s;
        }

        int ReadInteger ()
        {
            int n = m_resr.ReadByte();
            if (0 != (n & 0xE0))
            {
                if (0x80 != (n & 0xF8))
                    throw new InvalidFormatException ("[ReadInteger] SEC5 deserialization error");
                n = ReadNumber (n);
            }
            else
            {
                n = (n & 0xF) - (n & 0x10);
            }
            return n;
        }

        void SkipObject ()
        {
            int n = m_resr.ReadByte();
            if (0 != (n & 0xE0))
                ReadNumber (n);
        }

        int ReadNumber (int length_code)
        {
            int count = (length_code & 7) + 1;
            if (count > 4)
                throw new InvalidFormatException ("[ReadNumber] SEC5 deserialization error");
            int n = 0;
            int rank = 0;
            for (int i = 0; i < count; ++i)
            {
                int b = m_resr.ReadByte();
                n |= b << rank;
                rank += 8;
            }
            if (count <= 3)
            {
                int sign = n & (1 << (8 * count - 1));
                if (sign != 0)
                    n -= sign << 1;
            }
            return n;
        }

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_resr.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }
}
