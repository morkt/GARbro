//! \file       ArcEPK.cs
//! \date       Sat Jan 07 11:30:02 2017
//! \brief      Ellefin Game System resource archive.
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
using GameRes.Compression;
using GameRes.Formats.Lucifen;
using GameRes.Utility;

/// <summary>
/// Ellefin Game System is a Lucifen predecessor.
/// </summary>
namespace GameRes.Formats.Ellefin
{
    internal class EpkEntry : PackedEntry
    {
        public int DirIndex;
    }

    internal class EpkInfo : LpkInfo
    {
        public bool IndexEncrypted;
    }

    [Export(typeof(ArchiveFormat))]
    public class EpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "EPK/Ellefin"; } }
        public override string Description { get { return "Ellefin Game System resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public EpkOpener ()
        {
            Signatures = new uint[] { 0x1A4B5045, 0x1E4B5045, 0 };
        }

        static readonly EncryptionScheme DefaultScheme = new EncryptionScheme
        {
            BaseKey = new LpkOpener.Key (0xA6BD375E, 0x375D916B), ContentXor = 0xD9, RotatePattern = 0x17236351
        };

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "EPK"))
                return null;
            int flags = file.View.ReadByte (3);
            if (0 == (flags & 2))
                return null;
            var scheme = DefaultScheme;
            uint arc_key   = scheme.BaseKey.Key1;
            uint index_key = scheme.BaseKey.Key2;
            var arc_info = new EpkInfo
            {
                AlignedOffset = 0 != (flags & 1),
                Flag1         = 0 != (flags & 2),
                WholeCrypt    = 0 != (flags & 4),
                IsEncrypted   = 0 != (flags & 8),
                IndexEncrypted = 0 != (flags & 0xF0),
                PackedEntries = true,
            };
            uint index_size = file.View.ReadUInt32 (4);
            if (arc_info.IndexEncrypted)
            {
                var base_name = Path.GetFileNameWithoutExtension (file.Name).ToUpperInvariant();
                var name_bytes = Encodings.cp932.GetBytes (base_name);
                int back = name_bytes.Length-1;
                for (int i = 0; i < name_bytes.Length; ++i)
                {
                    arc_key   ^= name_bytes[back-i];
                    index_key ^= name_bytes[i];
                    arc_key    = Binary.RotR (arc_key, 8);
                    index_key  = Binary.RotL (index_key, 8);
                }
                index_size ^= index_key;
            }
            arc_info.Key = arc_key;
            if (arc_info.AlignedOffset)
                index_size <<= 11;
            if (!arc_info.IndexEncrypted || arc_info.AlignedOffset)
                index_size -= 8;
            if (index_size >= file.MaxOffset)
                return null;
            var index = file.View.ReadBytes (8, index_size);
            if (arc_info.IndexEncrypted)
                scheme.DecryptIndex (index, index.Length, index_key);

            var reader = new EpkIndexReader (arc_info);
            var dir = reader.Read (index);
            if (null == dir)
                return null;
            if (arc_info.IndexEncrypted)
                return new LuciArchive (file, this, dir, scheme, arc_info);
            else
                return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            var epk_ent = entry as PackedEntry;
            if (null == epk_ent)
                return input;
            if (epk_ent.IsPacked)
            {
                input = new LzssStream (input);
            }
            var epk = arc as LuciArchive;
            if (null == epk || (!epk.Info.WholeCrypt && !epk.Info.IsEncrypted && null == epk.Info.Prefix))
                return input;
            var data = new byte[epk_ent.UnpackedSize];
            using (input)
            {
                input.Read (data, 0, data.Length);
            }
            if (epk.Info.WholeCrypt)
            {
                epk.Scheme.DecryptContent (data);
            }
            if (epk.Info.IsEncrypted)
            {
                int count = Math.Min (data.Length, 0x10);
                epk.Scheme.DecryptEntry (data, count, epk.Info.Key);
            }
            var header = epk.Info.Prefix;
            if (header != null && header.Length <= data.Length)
                Buffer.BlockCopy (header, 0, data, 0, header.Length);
            return new BinMemoryStream (data, entry.Name);
        }
    }

    internal sealed class EpkIndexReader
    {
        IBinaryStream   m_index;
        EpkInfo         m_info;
        List<Entry>     m_dir;
        byte[]          m_name_buf;
        bool            m_wide_offset;

        public EpkIndexReader (EpkInfo info)
        {
            m_info = info;
        }

        public List<Entry> Read (byte[] index)
        {
            using (m_index = new BinMemoryStream (index))
            {
                int count = m_index.ReadInt32();
                if (!ArchiveFormat.IsSaneCount (count))
                    return null;
                m_dir = new List<Entry> (count);
                if (m_info.IndexEncrypted)
                    ParseEncryptedIndex (count);
                else
                    ParseRegularIndex (count);
                return m_dir.Count > 0 ? m_dir : null;
            }
        }

        void ParseEncryptedIndex (int count)
        {
            int header_length = m_index.ReadUInt8();
            if (0 != header_length)
            {
                m_info.Prefix = m_index.ReadBytes (header_length);
            }
            m_wide_offset = m_index.ReadByte() != 0;
            int name_tree_length = m_index.ReadInt32();
            var entry_table_offset = m_index.Position + name_tree_length;

            m_name_buf = new byte[0x110];
            TraverseIndex (m_index.Position, 0);
            if (m_dir.Count != count)
                throw new InvalidFormatException();

            foreach (EpkEntry entry in m_dir)
            {
                m_index.Position = entry_table_offset + entry.DirIndex * 12;
                ReadEntry (entry);
            }
        }

        void ParseRegularIndex (int count)
        {
            for (int i = 0; i < count; ++i)
            {
                int name_length = m_index.ReadByte();
                var name = m_index.ReadCString (name_length);
                var entry = FormatCatalog.Instance.Create<EpkEntry> (name);
                ReadEntry (entry);
                m_dir.Add (entry);
            }
        }

        void ReadEntry (PackedEntry entry)
        {
            entry.Offset        = m_index.ReadUInt32();
            entry.Size          = m_index.ReadUInt32();
            entry.UnpackedSize  = m_index.ReadUInt32();
            if (m_info.AlignedOffset)
            {
                entry.Offset <<= 11;
                entry.Size   <<= 11; // ???
            }
            entry.IsPacked = entry.UnpackedSize != 0;
            if (!entry.IsPacked)
                entry.UnpackedSize = entry.Size;
        }

        void TraverseIndex (long pos, int name_length)
        {
            if (name_length >= m_name_buf.Length)
                throw new InvalidFormatException ("Entry filename is too long");
            m_index.Position = pos;
            int count = m_index.ReadByte();
            for (int i = 0; i < count; ++i)
            {
                byte next_letter = m_index.ReadUInt8();
                int next_offset = m_wide_offset ? m_index.ReadInt32() : (int)m_index.ReadUInt16();
                if (0 == next_letter)
                {
                    var name = Encodings.cp932.GetString (m_name_buf, 0, name_length);
                    var entry = FormatCatalog.Instance.Create<EpkEntry> (name);
                    entry.DirIndex = next_offset;
                    m_dir.Add (entry);
                }
                else
                {
                    m_name_buf[name_length] = next_letter;
                    pos = m_index.Position;
                    TraverseIndex (pos + next_offset, name_length+1);
                    m_index.Position = pos;
                }
            }
        }
    }
}
