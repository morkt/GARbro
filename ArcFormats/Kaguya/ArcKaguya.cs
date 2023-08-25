//! \file       ArcKaguya.cs
//! \date       Mon Jun 01 07:03:03 2015
//! \brief      KaGuYa archive format.
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

namespace GameRes.Formats.Kaguya
{
    internal class AriEntry : PackedEntry
    {
        public ushort   Mode;
    }

    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/ARI"; } }
        public override string Description { get { return "KaGuYa script engine resource archive"; } }
        public override uint     Signature { get { return 0x314c4657; } } // 'WFL1'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "arc" };
            ContainedFormats = new[] { "AP", "APS3", "OGG", "DAT/GENERIC" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var reader = new IndexReader (this);
            var dir = reader.ReadIndex (file);
            if (null == dir || 0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var packed_entry = entry as PackedEntry;
            if (null == packed_entry || !packed_entry.IsPacked)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            if (0 == packed_entry.UnpackedSize)
                packed_entry.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset-4);
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
            using (var reader = new LzReader (input, entry.Size, packed_entry.UnpackedSize))
            {
                reader.Unpack();
                return new BinMemoryStream (reader.Data, entry.Name);
            }
        }
    }

    internal class IndexReader
    {
        ArchiveFormat   m_format;
        byte[]          m_name_buf = new byte[0x20];
        List<Entry>     m_dir = new List<Entry>();

        public IndexReader (ArchiveFormat format)
        {
            m_format = format;
        }

        public List<Entry> ReadIndex (ArcView file)
        {
            string ari_name = Path.ChangeExtension (file.Name, "ari");
            List<Entry> dir = null;
            if (file.Name != ari_name && VFS.FileExists (ari_name))
                dir = ReadAriIndex (file, ari_name);
            if (null == dir || 0 == dir.Count)
                dir = BuildIndex (file);
            return dir;
        }

        List<Entry> ReadAriIndex (ArcView file, string ari_name)
        {
            long arc_offset = 4;
            using (var ari = VFS.OpenView (ari_name))
            {
                long index_offset = 0;
                while (index_offset+4 < ari.MaxOffset)
                {
                    int name_len = ari.View.ReadInt32 (index_offset);
                    var name = ReadName (ari, index_offset+4, name_len);
                    if (null == name)
                        return null;
                    var entry = new AriEntry { Name = name };
                    index_offset += name_len + 4;
                    entry.Mode = ari.View.ReadUInt16 (index_offset);
                    entry.Size = ari.View.ReadUInt32 (index_offset+2);
                    entry.UnpackedSize = 0;
                    SetType (entry);
                    index_offset += 6;
                    arc_offset += name_len + 10;
                    if (1 == entry.Mode)
                    {
                        entry.IsPacked = true;
                        arc_offset += 4;
                    }
                    entry.Offset = arc_offset;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    arc_offset += entry.Size;
                    m_dir.Add (entry);
                }
            }
            return m_dir;
        }

        List<Entry> BuildIndex (ArcView file)
        {
            long arc_offset = 4;
            while (arc_offset+4 < file.MaxOffset)
            {
                int name_len = file.View.ReadInt32 (arc_offset);
                var name = ReadName (file, arc_offset+4, name_len);
                if (null == name)
                    return null;
                var entry = new AriEntry { Name = name };
                arc_offset += name_len + 4;
                entry.Mode = file.View.ReadUInt16 (arc_offset);
                entry.Size = file.View.ReadUInt32 (arc_offset+2);
                SetType (entry);
                arc_offset += 6;
                if (1 == entry.Mode)
                {
                    entry.IsPacked = true;
                    entry.UnpackedSize = file.View.ReadUInt32 (arc_offset);
                    arc_offset += 4;
                }
                entry.Offset = arc_offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                arc_offset += entry.Size;
                m_dir.Add (entry);
            }
            return m_dir;
        }

        void SetType (AriEntry entry)
        {
            if (2 == entry.Mode)
                entry.Type = "audio";
            else if (1 == entry.Mode)
                entry.Type = "image";
            else
                entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name, m_format.ContainedFormats);
        }

        string ReadName (ArcView file, long offset, int name_len)
        {
            if (name_len <= 0 || offset+name_len+6 > file.MaxOffset || name_len > 0x100)
                return null;
            if (name_len > m_name_buf.Length)
                m_name_buf = new byte[name_len];
            file.View.Read (offset, m_name_buf, 0, (uint)name_len);
            return DecryptName (m_name_buf, name_len).TrimStart ('\\');
        }

        string DecryptName (byte[] name_buf, int name_len)
        {
            for (int i = 0; i < name_len; ++i)
                name_buf[i] ^= 0xff;
            return Encodings.cp932.GetString (name_buf, 0, name_len);
        }
    }

    internal sealed class LzReader : IDisposable, IDataUnpacker
    {
        MsbBitStream    m_input;
        byte[]          m_output;

        public byte[] Data { get { return m_output; } }

        public LzReader (Stream input, uint packed_size, uint unpacked_size)
        {
            m_input = new MsbBitStream (input, true);
            m_output = new byte[unpacked_size];
        }

        public void Unpack ()
        {
            int dst = 0;
            int frame_pos = 1;
            byte[] frame = new byte[4096];
            int frame_mask = frame.Length - 1;

            while (dst < m_output.Length)
            {
                int bit = m_input.GetNextBit();
                if (-1 == bit)
                    break;
                if (0 != bit)
                {
                    int data = m_input.GetBits (8);
                    m_output[dst++] = (byte)data;
                    frame[frame_pos++] = (byte)data;
                    frame_pos &= frame_mask;
                }
                else
                {
                    int win_offset = m_input.GetBits (12);
                    if (-1 == win_offset || 0 == win_offset)
                        break;

                    int count = m_input.GetBits(4) + 2;
                    for (int i = 0; i < count; i++)
                    {
                        byte data = frame[(win_offset + i) & frame_mask];
                        m_output[dst++] = data;
                        frame[frame_pos++] = data;
                        frame_pos &= frame_mask;
                    }
                }
            }
        }

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "TBL")]
    [ExportMetadata("Target", "DAT/GENERIC")]
    public class TblFormat : ResourceAlias { }
}
