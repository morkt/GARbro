//! \file       ArcEME.cs
//! \date       Tue Mar 15 08:13:00 2016
//! \brief      Emon Engine (えもんエンジン) resource archives.
//
// Copyright (C) 2016-2017 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.EmonEngine
{
    [Export(typeof(ArchiveFormat))]
    public class EmeOpener : ArchiveFormat
    {
        public override string         Tag { get { return "EME"; } }
        public override string Description { get { return "Emon Engine resource archive"; } } // 'えもんエンジン'
        public override uint     Signature { get { return 0x44455252; } } // 'RREDATA'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public EmeOpener ()
        {
            Extensions = new string[] { "eme", "rre" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "ATA "))
                return null;
            int count = file.View.ReadInt32 (file.MaxOffset-4);
            if (!IsSaneCount (count))
                return null;

            uint index_size = (uint)count * 0x60;
            var index_offset = file.MaxOffset - 4 - index_size;
            var key = file.View.ReadBytes (index_offset - 40, 40);
            var index = file.View.ReadBytes (index_offset, index_size);

            int current_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                Decrypt (index, current_offset, 0x60, key);
                var name = Binary.GetCString (index, current_offset, 0x40);
                var entry = FormatCatalog.Instance.Create<EmEntry> (name);
                entry.LzssFrameSize = LittleEndian.ToUInt16 (index, current_offset+0x40);
                entry.LzssInitPos   = LittleEndian.ToUInt16 (index, current_offset+0x42);
                if (entry.LzssFrameSize != 0)
                    entry.LzssInitPos = (entry.LzssFrameSize - entry.LzssInitPos) % entry.LzssFrameSize;
                entry.SubType       = LittleEndian.ToInt32  (index, current_offset+0x48);
                entry.Size          = LittleEndian.ToUInt32 (index, current_offset+0x4C);
                entry.UnpackedSize  = LittleEndian.ToUInt32 (index, current_offset+0x50);
                entry.Offset        = LittleEndian.ToUInt32 (index, current_offset+0x54);
                entry.IsPacked      = entry.UnpackedSize != entry.Size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (3 == entry.SubType)
                    entry.Type = "script";
                else if (4 == entry.SubType)
                    entry.Type = "image";
                dir.Add (entry);
                current_offset += 0x60;
            }
            return new EmeArchive (file, this, dir, key);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var ement = entry as EmEntry;
            var emarc = arc as EmeArchive;
            if (null == ement || null == emarc)
                return base.OpenEntry (arc, entry);
            if (3 == ement.SubType)
                return OpenScript (emarc, ement);
            else if (5 == ement.SubType && entry.Size > 4)
                return OpenT5 (emarc, ement);
            else
                return base.OpenEntry (arc, entry);
        }

        Stream OpenScript (EmeArchive arc, EmEntry entry)
        {
            var header = arc.File.View.ReadBytes (entry.Offset, 12);
            Decrypt (header, 0, 12, arc.Key);
            if (0 == entry.LzssFrameSize)
            {
                var input = arc.File.CreateStream (entry.Offset+12, entry.Size);
                return new PrefixStream (header, input);
            }
            int unpacked_size = LittleEndian.ToInt32 (header, 4);
            if (0 != unpacked_size && unpacked_size < entry.UnpackedSize)
            {
                uint packed_size = LittleEndian.ToUInt32 (header, 0);
                int part1_size = (int)entry.UnpackedSize - unpacked_size;
                var data = new byte[entry.UnpackedSize];
                using (var input = arc.File.CreateStream (entry.Offset+12+packed_size, entry.Size))
                using (var lzss = new LzssStream (input))
                {
                    lzss.Config.FrameSize = entry.LzssFrameSize;
                    lzss.Config.FrameInitPos = entry.LzssInitPos;
                    lzss.Read (data, 0, part1_size);
                }
                using (var input = arc.File.CreateStream (entry.Offset+12, packed_size))
                using (var lzss = new LzssStream (input))
                {
                    lzss.Config.FrameSize = entry.LzssFrameSize;
                    lzss.Config.FrameInitPos = entry.LzssInitPos;
                    lzss.Read (data, part1_size, unpacked_size);
                }
                return new BinMemoryStream (data, entry.Name);
            }
            else
            {
                var input = arc.File.CreateStream (entry.Offset+12, entry.Size);
                var lzss = new LzssStream (input);
                lzss.Config.FrameSize = entry.LzssFrameSize;
                lzss.Config.FrameInitPos = entry.LzssInitPos;
                return lzss;
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            const int header_size = 32;
            var ement = (EmEntry)entry;
            if (ement.SubType != 4)
                return base.OpenImage (arc, entry);
            var emarc = (EmeArchive)arc;
            var header = arc.File.View.ReadBytes (entry.Offset, header_size);
            Decrypt (header, 0, header_size, emarc.Key);
            var info = new EmMetaData {
                LzssFrameSize = ement.LzssFrameSize,
                LzssInitPos = ement.LzssInitPos,
                BPP = header.ToUInt16 (0) & 0xFF,
                Width = header.ToUInt16 (2),
                Height = header.ToUInt16 (4),
                Colors = header.ToUInt16 (6),
                Stride = header.ToInt32 (8),
                OffsetX = header.ToInt32 (0xC),
                OffsetY = header.ToInt32 (0x10),
                DataOffset = header_size,
            };
            uint entry_size = entry.Size + header_size;
            if (0 != info.Colors && header[0] != 7)
                entry_size += (uint)Math.Max (info.Colors, 3) * 4;
            var input = arc.File.CreateStream (entry.Offset, entry_size);
            return new EmeImageDecoder (input, info);
        }

        Stream OpenT5 (EmeArchive arc, EmEntry entry)
        {
            var header = arc.File.View.ReadBytes (entry.Offset, 4);
            Decrypt (header, 0, 4, arc.Key);
            var input = arc.File.CreateStream (entry.Offset+4, entry.Size-4);
            return new PrefixStream (header, input);
        }

        internal static unsafe void Decrypt (byte[] buffer, int offset, int length, byte[] routine)
        {
            if (null == buffer)
                throw new ArgumentNullException ("buffer", "Buffer cannot be null.");
            if (offset < 0)
                throw new ArgumentOutOfRangeException ("offset", "Buffer offset should be non-negative.");
            if (buffer.Length - offset < length)
                throw new ArgumentException ("Buffer offset and length are out of bounds.");
            fixed (byte* data8 = &buffer[offset])
            {
                uint* data32 = (uint*)data8;
                int length32 = length / 4;
                int key_index = routine.Length;
                for (int i = 7; i >= 0; --i)
                {
                    key_index -= 4;
                    uint key = LittleEndian.ToUInt32 (routine, key_index);
                    switch (routine[i])
                    {
                    case 1:
                        for (int j = 0; j < length32; ++j)
                            data32[j] ^= key;
                        break;
                    case 2:
                        for (int j = 0; j < length32; ++j)
                        {
                            uint v = data32[j];
                            data32[j] = v ^ key;
                            key = v;
                        }
                        break;
                    case 4:
                        for (int j = 0; j < length32; ++j)
                            data32[j] = ShiftValue (data32[j], key);
                        break;
                    case 8:
                        InitTable (buffer, offset, length, key);
                        break;
                    }
                }
            }
        }

        static uint ShiftValue (uint val, uint key)
        {
            int shift = 0;
            uint result = 0;
            for (int i = 0; i < 32; ++i)
            {
                shift += (int)key;
                result |= ((val >> i) & 1) << shift;
            }
            return result;
        }

        static void InitTable (byte[] buffer, int offset, int length, uint key)
        {
            var table = new byte[length];
            int x = 0;
            for (int i = 0; i < length; ++i)
            {
                x += (int)key;
                while (x >= length)
                    x -= length;
                table[x] = buffer[offset+i];
            }
            Buffer.BlockCopy (table, 0, buffer, offset, length);
        }
    }

    internal class EmEntry : PackedEntry
    {
        public int LzssFrameSize;
        public int LzssInitPos;
        public int SubType;
    }

    internal class EmeArchive : ArcFile
    {
        public readonly byte[] Key;

        public EmeArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }
}
