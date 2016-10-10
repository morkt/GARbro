//! \file       ArcHXP.cs
//! \date       Sun Nov 08 18:04:58 2015
//! \brief      SH System resource archive.
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
using System.Linq;
using GameRes.Utility;
using System.Text;

namespace GameRes.Formats.SHSystem
{
    [Export(typeof(ArchiveFormat))]
    public class Him4Opener : ArchiveFormat
    {
        public override string         Tag { get { return "HIM4"; } }
        public override string Description { get { return "SH System engine resource archive"; } }
        public override uint     Signature { get { return 0x346D6948; } } // 'Him4'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Him4Opener ()
        {
            Signatures = new uint[] { 0x346D6948, 0x36534853 }; // 'Him4', 'SHS6'
            Extensions = new string[] { "hxp" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            long next_offset = file.View.ReadUInt32 (8);
            uint index_offset = 0xC;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                if (next_offset > file.MaxOffset)
                    return null;
                var entry = new PackedEntry {
                    Name = i.ToString ("D5"),
                    Offset = next_offset,
                };
                next_offset = i + 1 == count ? file.MaxOffset : file.View.ReadUInt32 (index_offset);
                index_offset += 4;
                entry.Size = (uint)(next_offset - entry.Offset);
                dir.Add (entry);
            }
            DetectFileTypes (file, dir);
            return new ArcFile (file, this, dir);
        }

        static protected void DetectFileTypes (ArcView file, List<Entry> dir)
        {
            byte[] signature_buffer = new byte[4];
            foreach (PackedEntry entry in dir)
            {
                uint packed_size   = file.View.ReadUInt32 (entry.Offset);
                uint unpacked_size = file.View.ReadUInt32 (entry.Offset+4);
                entry.IsPacked = 0 != packed_size;
                if (!entry.IsPacked)
                    packed_size = unpacked_size;
                entry.Size = packed_size;
                entry.UnpackedSize = unpacked_size;
                entry.Offset += 8;
                uint signature;
                if (entry.IsPacked)
                {
                    using (var input = file.CreateStream (entry.Offset, Math.Min (packed_size, 0x20u)))
                    using (var reader = new ShsCompression (input))
                    {
                        reader.Unpack (signature_buffer);
                        signature = LittleEndian.ToUInt32 (signature_buffer, 0);
                    }
                }
                else
                {
                    signature = file.View.ReadUInt32 (entry.Offset);
                }
                if (0 != signature)
                {
                    IResource res;
                    if (0x020000 == signature || 0x0A0000 == signature)
                        res = ImageFormat.Tga;
                    else
                        res = AutoEntry.DetectFileType (signature);
                    if (res != null)
                    {
                        entry.Type = res.Type;
                        var ext = res.Extensions.FirstOrDefault();
                        if (!string.IsNullOrEmpty (ext))
                            entry.Name = Path.ChangeExtension (entry.Name, ext);
                    }
                }
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = base.OpenEntry (arc, entry);
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return input;
            var data = new byte[pent.UnpackedSize];
            using (var reader = new ShsCompression (input))
            {
                reader.Unpack (data);
                return new MemoryStream (data);
            }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class Him5Opener : Him4Opener
    {
        public override string         Tag { get { return "HIM5"; } }
        public override string Description { get { return "SH System engine resource archive"; } }
        public override uint     Signature { get { return 0x356D6948; } } // 'Him5'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Him5Opener ()
        {
            Signatures = new uint[] { 0x356D6948, 0x37534853 }; // 'Him5', 'SHS7'
            Extensions = new string[] { "hxp" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadByte (3) - '0';
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            var index = ReadIndex (file, 8, count);
            var dir = new List<Entry>();
            foreach (var section in index)
            {
                int index_offset = section.Item1;
                for (int section_size = section.Item2; section_size > 0; )
                {
                    int entry_size = file.View.ReadByte (index_offset);
                    if (entry_size < 5)
                        break;
                    var entry = new PackedEntry {
                        Offset = Binary.BigEndian (file.View.ReadUInt32 (index_offset+1)),
                        Name = file.View.ReadString (index_offset+5, (uint)entry_size-5),
                    };
                    if (entry.Offset > file.MaxOffset)
                        return null;
                    index_offset += entry_size;
                    section_size -= entry_size;
                    dir.Add (entry);
                }
            }
            DetectFileTypes (file, dir);
            return new ArcFile (file, this, dir);
        }

        internal static List<Tuple<int, int>> ReadIndex (ArcView file, uint index_offset, int count)
        {
            var index = new List<Tuple<int, int>> (count);
            for (int i = 0; i < count; ++i)
            {
                int size   = file.View.ReadInt32 (index_offset);
                int offset = file.View.ReadInt32 (index_offset+4);
                index_offset += 8;
                if (size != 0)
                    index.Add (Tuple.Create (offset, size));
            }
            return index;
        }
    }

    internal class ShsCompression : IDisposable
    {
        BinaryReader    m_input;

        public ShsCompression (Stream input, bool leave_open = false)
        {
            m_input = new BinaryReader (input, Encoding.UTF8, leave_open);
        }

        public int Unpack (byte[] output)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                int count;
                int ctl = m_input.ReadByte();
                if (ctl < 32)
                {
                    switch (ctl)
                    {
                    case 0x1D:
                        count = m_input.ReadByte() + 0x1E;
                        break;
                    case 0x1E:
                        count = Binary.BigEndian (m_input.ReadUInt16()) + 0x11E;
                        break;
                    case 0x1F:
                        count = Binary.BigEndian (m_input.ReadInt32());
                        break;
                    default:
                        count = ctl + 1;
                        break;
                    }
                    count = Math.Min (count, output.Length - dst);
                    m_input.Read (output, dst, count);
                }
                else
                {
                    int offset;
                    if (0 == (ctl & 0x80))
                    {
                        if (0x20 == (ctl & 0x60))
                        {
                            offset = (ctl >> 2) & 7;
                            count = ctl & 3;
                        }
                        else
                        {
                            offset = m_input.ReadByte();
                            if (0x40 == (ctl & 0x60))
                                count = (ctl & 0x1F) + 4;
                            else
                            {
                                offset |= (ctl & 0x1F) << 8;
                                ctl = m_input.ReadByte();
                                if (0xFE == ctl)
                                    count = Binary.BigEndian (m_input.ReadUInt16()) + 0x102;
                                else if (0xFF == ctl)
                                    count = Binary.BigEndian (m_input.ReadInt32());
                                else 
                                    count = ctl + 4;
                            }
                        }
                    }
                    else
                    {
                        count = (ctl >> 5) & 3;
                        offset = ((ctl & 0x1F) << 8) | m_input.ReadByte();
                    }
                    count += 3;
                    offset++;
                    count = Math.Min (count, output.Length-dst);
                    Binary.CopyOverlapped (output, dst-offset, dst, count);
                }
                dst += count;
            }
            return dst;
        }

        #region IDisposable Members
        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }
}
