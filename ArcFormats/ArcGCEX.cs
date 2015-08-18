//! \file       ArcGCEX.cs
//! \date       Tue Aug 18 04:25:02 2015
//! \brief      G2 engine resources archive.
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
using System.Diagnostics;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.G2
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/G2"; } }
        public override string Description { get { return "G2 engine resource archive"; } }
        public override uint     Signature { get { return 0x58454347; } } // 'GCEX'
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (0 != file.View.ReadInt32 (4))
                return null;
            long index_offset = file.View.ReadInt64 (8);
            if (index_offset >= file.MaxOffset)
                return null;
            if (!file.View.AsciiEqual (index_offset, "GCE3"))
                return null;
            int count = file.View.ReadInt32 (index_offset+0x18);
            if (!IsSaneCount (count))
                return null;
            bool index_packed = 0x11 == file.View.ReadInt32 (index_offset+4);
            uint index_size = file.View.ReadUInt32 (index_offset+8);
            byte[] index = null;
            if (index_packed)
            {
                index_size -= 0x28;
                int unpacked_size = file.View.ReadInt32 (index_offset+0x20);
                using (var input = file.CreateStream (index_offset+0x28, index_size))
                using (var reader = new GceReader (input, unpacked_size))
                    index = reader.Data;
            }
            else
            {
                index_size -= 0x20;
                index = new byte[index_size];
                if (index.Length != file.View.Read (index_offset+0x20, index, 0, index_size))
                    return null;
            }
            int current_index = 0;
            int current_filename = 0x20*count;
            long current_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int name_length = LittleEndian.ToUInt16 (index, current_filename);
                if (current_filename+2+name_length > index.Length)
                    return null;
                uint size = LittleEndian.ToUInt32 (index, current_index+0x18);
                if (size != 0)
                {
                    string name = Encodings.cp932.GetString (index, current_filename+2, name_length);
                    var entry = new PackedEntry
                    {
                        Name = name,
                        Type = FormatCatalog.Instance.GetTypeFromName (name),
                        Offset = current_offset,
                        Size = size,
                        UnpackedSize = LittleEndian.ToUInt32 (index, current_index+0x10),
                    };
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    entry.IsPacked = entry.Size != entry.UnpackedSize;
                    current_offset += entry.Size;
                    dir.Add (entry);
                }
                current_index += 0x20;
                current_filename += 2 + name_length;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (0 == entry.Size)
                return Stream.Null;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pentry = entry as PackedEntry;
            if (null == pentry || !pentry.IsPacked)
                return input;
            if (!arc.File.View.AsciiEqual (entry.Offset, "GCE"))
            {
                Trace.WriteLine ("Packed entry is not GCE", entry.Name);
                return input;
            }
            using (input)
            using (var reader = new GceReader (input, (int)pentry.UnpackedSize))
            {
                return new MemoryStream (reader.Data);
            }
        }
    }

    internal class GceReader : IDisposable
    {
        BinaryReader    m_input;
        int             m_unpacked_size;
        byte[]          m_output = null;
        int             m_dst;

        public byte[] Data
        {
            get
            {
                if (null == m_output)
                {
                    m_output = new byte[m_unpacked_size];
                    Unpack();
                }
                return m_output;
            }
        }

        public GceReader (Stream input, int unpacked_size)
        {
            m_input = new ArcView.Reader (input);
            m_unpacked_size = unpacked_size;
        }

        private void Unpack ()
        {
            m_dst = 0;
            byte[] id = new byte[4];
            while (m_input.BaseStream.Position < m_input.BaseStream.Length)
            {
                if (4 != m_input.Read (id, 0, 4))
                    break;
                int segment_length = m_input.ReadInt32();
                if (Binary.AsciiEqual (id, "GCE1"))
                {
                    m_input.ReadInt32();
                    int data_length = m_input.ReadInt32();

                    m_input.ReadInt32();
                    int cmd_len = m_input.ReadInt32();
                    long cmd_pos = m_input.BaseStream.Position + data_length;
                    ReadControlStream (cmd_pos, cmd_len);

                    int next = m_dst + segment_length;
                    UnpackGce1Segment (data_length, segment_length);
                    m_dst = next;
                    m_input.BaseStream.Position = cmd_pos + cmd_len;
                }
                else if (Binary.AsciiEqual (id, "GCE0"))
                {
                    if (segment_length != m_input.Read (m_output, m_dst, segment_length))
                        throw new EndOfStreamException();
                    m_dst += segment_length;
                }
                else
                {
                    throw new InvalidFormatException ("Unknown compression type in GCE stream");
                }
            }
        }

        int[] m_frame = new int[0x10000];

        void UnpackGce1Segment (int data_len, int segment_length)
        {
            for (int i = 0; i < m_frame.Length; ++i)
                m_frame[i] = 0;
            int frame_pos = 0;

            int data_pos = 0;
            int dst_end = m_dst + segment_length;
            while (data_pos < data_len && m_dst < dst_end)
            {
                int n = GetLength();
                while (n --> 0)
                {
                    m_frame[frame_pos] = m_dst;
                    byte b = m_input.ReadByte();
                    data_pos++;
                    frame_pos = ((frame_pos << 8) | b) & 0xFFFF;
                    m_output[m_dst++] = b;
                }
                if (m_dst >= dst_end)
                    break;
                n = GetLength() + 1;
                int src = m_frame[frame_pos];
                while (n --> 0)
                {
                    m_frame[frame_pos] = m_dst;
                    frame_pos = ((frame_pos << 8) | m_output[src]) & 0xFFFF;
                    m_output[m_dst++] = m_output[src++];
                }
            }
        }

        int GetLength ()
        {
            int v = 0;
            if (0 == GetBit())
            {
                int digits = 0;
                while (0 == GetBit())
                    ++digits;
                v = 1 << digits;
                while (digits --> 0)
                    v |= GetBit() << digits;
            }
            return v;
        }

        byte[] m_control;
        int     m_control_pos;
        int     m_control_len;
        int     m_bit_pos;

        void ReadControlStream (long pos, int length)
        {
            var data_pos = m_input.BaseStream.Position;
            if (null == m_control || m_control.Length < length)
                m_control = new byte[length];
            m_input.BaseStream.Position = pos;
            if (length != m_input.Read (m_control, 0, length))
                throw new EndOfStreamException();
            m_control_pos = 0;
            m_control_len = length;
            m_input.BaseStream.Position = data_pos;
            m_bit_pos = 8;
        }

        int GetBit ()
        {
            if (0 == m_bit_pos--)
            {
                ++m_control_pos;
                m_bit_pos = 7;
                --m_control_len;
                if (0 == m_control_len)
                    throw new EndOfStreamException();
            }
            return 1 & (m_control[m_control_pos] >> m_bit_pos);
        }

        #region IDisposable Members
        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                if (m_input != null)
                    m_input.Dispose();
                m_disposed = true;
                GC.SuppressFinalize (this);
            }
        }
        #endregion
    }
}
