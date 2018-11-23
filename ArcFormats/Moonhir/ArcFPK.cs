//! \file       ArcFPK.cs
//! \date       Fri Sep 16 04:23:31 2016
//! \brief      MoonhirGames resources archive.
//
// Copyright (C) 2016 by morkt
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
using System.Linq;
using GameRes.Utility;

namespace GameRes.Formats.MoonhirGames
{
    internal class FpkEntry : Entry
    {
        public bool IsEncrypted;
    }

    internal class FpkArchive : ArcFile
    {
        public readonly uint Key;

        public FpkArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, uint key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Serializable]
    public class Fpk0100Scheme : ResourceScheme
    {
        public uint[] KnownKeys;
    }

    [Export(typeof(ArchiveFormat))]
    public class FpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "FPK/MOONHIR"; } }
        public override string Description { get { return "MoonhirGames engine resource archive"; } }
        public override uint     Signature { get { return 0x4B5046; } } // 'FPK'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public static uint[] KnownKeys = { 0 };

        public override ResourceScheme Scheme
        {
            get { return new Fpk0100Scheme { KnownKeys = KnownKeys }; }
            set { KnownKeys = ((Fpk0100Scheme)value).KnownKeys; }
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "0100"))
                return null;
            int count = file.View.ReadInt32 (0xC);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (8);

            var arc_name = Path.GetFileName (file.Name);
            var fbx_type = arc_name.StartsWith ("scr", StringComparison.OrdinalIgnoreCase) ? "" : "image";
            var dir = new List<Entry> (count);
            bool has_encrypted = false;
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset+12, 12);
                var entry = Create<FpkEntry> (name);
                entry.IsEncrypted = 0 != file.View.ReadUInt32 (index_offset);
                entry.Offset = file.View.ReadUInt32 (index_offset+4);
                entry.Size   = file.View.ReadUInt32 (index_offset+8);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (name.HasExtension (".fbx"))
                    entry.Type = fbx_type;
                has_encrypted = has_encrypted || entry.IsEncrypted;
                dir.Add (entry);
                index_offset += 0x18;
            }
            if (!has_encrypted)
                return new ArcFile (file, this, dir);
            var enc_entry = dir.Cast<FpkEntry>().FirstOrDefault (e => e.IsEncrypted && e.Size > 8);
            if (null == enc_entry)
                return new ArcFile (file, this, dir);
            var key = FindKey (file, enc_entry);
            if (null == key)
            {
                Trace.WriteLine (string.Format ("{0}: unknown encryption key", file.Name), "[FPK]");
                return new ArcFile (file, this, dir);
            }
            return new FpkArchive (file, this, dir, key.Value);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var farc = arc as FpkArchive;
            var fent = entry as FpkEntry;
            Stream input;
            byte[] header;
            if (null == farc || null == fent || !fent.IsEncrypted)
            {
                if (fent != null && fent.IsEncrypted)
                    throw new UnknownEncryptionScheme();
                input = arc.File.CreateStream (entry.Offset, entry.Size);
                header = new byte[0x10];
                input.Read (header, 0, 0x10);
                input.Position = 0;
            }
            else
            {
                var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
                Decrypt (data, 0, data.Length, farc.Key);
                int length = LittleEndian.ToInt32 (data, data.Length-8);
                input = new BinMemoryStream (data, 0, length, entry.Name);
                header = data;
            }
            if (!Binary.AsciiEqual (header, "FBX\x01"))
                return input;
            using (input)
            {
                int packed_size = LittleEndian.ToInt32 (header, 8);
                int unpacked_size = LittleEndian.ToInt32 (header, 0xC);
                input.Position = header[7];
                var unpacked = UnpackFbx (input, packed_size, unpacked_size);
                return new BinMemoryStream (unpacked, entry.Name);
            }
        }

        uint? FindKey (ArcView file, Entry entry)
        {
            if (entry.Size < 8)
                return null;
            var offset = entry.Offset + entry.Size - 8;
            uint t1 = file.View.ReadUInt32 (offset+4);
            uint t0 = file.View.ReadUInt32 (offset);
            // l = (a - x - m + 7) ^ ((x - ((b - x - m + 4) ^ x)) >> 7) ^ ((x * 2 + m - 4) << 7);
            foreach (uint key in KnownKeys)
            {
                uint k1 = key + entry.Size - 4;
                uint k2 = ((key - ((t1 - k1) ^ key)) >> 7) ^ ((k1 + key) << 7);
                uint test_length = ((((t0 - (k1 - 3)) ^ k2) + 3) & ~3u) + 8;
                if (entry.Size == test_length)
                    return key;
            }
            return null;
        }

        unsafe void Decrypt (byte[] data, int index, int length, uint key)
        {
            if (length < 8)
                return;
            fixed (byte* data8 = &data[index])
            {
                uint* data32 = (uint*)data8;
                uint* dptr = data32 + length / 4 - 1;
                uint k1 = key + (uint)length - 4;
                uint k2 = key;
                while (dptr >= data32)
                {
                    *dptr = (*dptr - k1) ^ k2;
                    k2 = ((k2 - *dptr) >> 7) ^ ((k1 + k2) << 7);
                    k1 -= 3;
                    --dptr;
                }
            }
        }

        byte[] UnpackFbx (Stream input, int packed_size, int unpacked_size)
        {
            var output = new byte[unpacked_size];
            int dst = 0;
            int ctl = 1;
            while (dst < output.Length)
            {
                if (1 == ctl)
                {
                    ctl = input.ReadByte();
                    if (-1 == ctl)
                        break;
                    ctl |= 0x100;
                }
                int count, offset;
                switch (ctl & 3)
                {
                case 0:
                    output[dst++] = (byte)input.ReadByte();
                    break;
                case 1:
                    count = input.ReadByte();
                    if (-1 == count)
                        return output;
                    count = Math.Min (count + 2, output.Length - dst);
                    input.Read (output, dst, count);
                    dst += count;
                    break;
                case 2:
                    offset  = input.ReadByte() << 8;
                    offset |= input.ReadByte();
                    if (-1 == offset)
                        return output;
                    count = Math.Min ((offset & 0x1F) + 4, output.Length - dst);
                    offset >>= 5;
                    Binary.CopyOverlapped (output, dst - offset - 1, dst, count);
                    dst += count;
                    break;
                case 3:
                    int exctl = input.ReadByte();
                    if (-1 == exctl)
                        return output;
                    count = exctl & 0x3F;
                    switch (exctl >> 6)
                    {
                    case 0:
                        count = count << 8 | input.ReadByte();
                        if (-1 == count)
                            return output;
                        count = Math.Min (count + 0x102, output.Length - dst);
                        input.Read (output, dst, count);
                        dst += count;
                        break;
                    case 1:
                        offset  = input.ReadByte() << 8;
                        offset |= input.ReadByte();
                        count = count << 5 | offset & 0x1F;
                        count = Math.Min (count + 0x24, output.Length - dst);
                        offset >>= 5;
                        Binary.CopyOverlapped (output, dst - offset - 1, dst, count);
                        dst += count;
                        break;
                    case 3:
                        input.Seek (count, SeekOrigin.Current);
                        ctl = 1 << 2;
                        break;
                    default:
                        break;
                    }
                    break;
                }
                ctl >>= 2;
            }
            return output;
        }
    }
}
