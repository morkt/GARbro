//! \file       ArcKogado.cs
//! \date       Sun Aug 24 22:01:05 2014
//! \brief      Hypatia game engine archive implementation.
//
// Copyright (C) 2014-2018 by morkt
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
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Text;
using GameRes.Formats.Kogado;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.Hypatia
{
    public class HypEntry : PackedEntry
    {
        // 0 : Not compressed
        // 1 : Mariel compression
        // 2 : Cocotte compression
        // 3 : Xor 0xff encryption
        public byte     CompressionType;
        public bool     HasCheckSum;
        public ushort   CheckSum;
        public long     FileTime;
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/HyPack"; } }
        public override string Description { get { return arcStrings.KogadoDescription; } }
        public override uint     Signature { get { return 0x61507948; } } // 'HyPa'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return true; } }

        public PakOpener ()
        {
            Extensions = new string[] { "pak", "dat" };
            ContainedFormats = new[] {
                "PNG", "BMP", "JPEG", "WBM/HYPATIA",
                "OGG", "WAV", "ADP/HYPATIA",
                "TXT", "SCR"
            };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "HyPack"))
                return null;
            int version = file.View.ReadUInt16 (6);
            int entry_size;
            switch (version)
            {
            case 0x100: entry_size = 32; break;
            case 0x200: entry_size = 40; break;
            case 0x300:
            case 0x301: entry_size = 48; break;
            default: return null;
            }
            long index_offset = 0x10 + file.View.ReadUInt32 (8);
            if (index_offset >= file.MaxOffset)
                return null;
            int entry_count = file.View.ReadInt32 (12);
            if (entry_count <= 0 || entry_count > 0xfffff)
                return null;
            uint index_size = (uint)(entry_count * entry_size);
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            long data_offset = 0x10;

            var dir = new List<Entry> (entry_count);
            for (int i = 0; i < entry_count; ++i)
            {
                string name = file.View.ReadString (index_offset, 0x15);
                string ext  = file.View.ReadString (index_offset+0x15, 3);
                if (0 == name.Length)
                    name = i.ToString ("D5");
                if (0 != ext.Length)
                    name += '.'+ext;
                var entry = Create<HypEntry> (name);
                entry.Offset        = data_offset + file.View.ReadUInt32 (index_offset + 0x18);
                if (version >= 0x200)
                {
                    entry.UnpackedSize  = file.View.ReadUInt32 (index_offset + 0x1c);
                    entry.Size          = file.View.ReadUInt32 (index_offset + 0x20);
                    entry.CompressionType = file.View.ReadByte (index_offset + 0x24);
                    entry.IsPacked      = 0 != entry.CompressionType;
                    if (version >= 0x300)
                    {
                        entry.HasCheckSum = 0 != file.View.ReadByte (index_offset + 0x25);
                        entry.CheckSum  = file.View.ReadUInt16 (index_offset + 0x26);
                        entry.FileTime  = file.View.ReadInt64 (index_offset + 0x28);
                    }
                }
                else
                    entry.Size          = file.View.ReadUInt32 (index_offset + 0x1c);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += entry_size;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var packed_entry = entry as HypEntry;
            if (null == packed_entry || !packed_entry.IsPacked)
                return input;
            if (packed_entry.CompressionType > 3)
            {
                Trace.WriteLine (string.Format ("{1}: Unknown compression type {0}",
                                                packed_entry.CompressionType, packed_entry.Name),
                                 "Kogado.PakOpener.OpenEntry");
                return input;
            }
            if (3 == packed_entry.CompressionType)
                return new InputCryptoStream (input, new NotTransform());
            try
            {
                if (2 == packed_entry.CompressionType)
                {
                    var decoded = new MemoryStream ((int)packed_entry.UnpackedSize);
                    try
                    {
                        var cocotte = new CocotteEncoder();
                        if (!cocotte.Decode (input, decoded))
                            throw new InvalidFormatException ("Invalid Cocotte-encoded stream");
                        decoded.Position = 0;
                        return decoded;
                    }
                    catch
                    {
                        decoded.Dispose();
                        throw;
                    }
                }
                // if (1 == packed_entry.CompressionType)
                var unpacked = new byte[packed_entry.UnpackedSize];
                var mariel = new MarielEncoder();
                mariel.Unpack (input, unpacked, unpacked.Length);
                return new BinMemoryStream (unpacked, entry.Name);
            }
            finally
            {
                input.Dispose();
            }
        }

        internal class OutputEntry : HypEntry
        {
            public byte[] IndexName;
            public byte[] IndexExt;
        }

        // files inside archive are aligned to 0x10 boundary.
        // to convert DateTime structure into entry time:
        // entry.FileTime = file_info.CreationTimeUtc.Ticks;
        //
        // last two bytes of archive is CRC16 of the whole file

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            const long data_offset = 0x10;
            var encoding = Encodings.cp932.WithFatalFallback();
            int callback_count = 0;

            var output_list = new List<OutputEntry> (list.Count());
            foreach (var entry in list)
            {
                try
                {
                    string name = Path.GetFileNameWithoutExtension (entry.Name);
                    string ext  = Path.GetExtension (entry.Name);
                    byte[] name_buf = new byte[0x15];
                    byte[] ext_buf  = new byte[3];
                    encoding.GetBytes (name, 0, name.Length, name_buf, 0);
                    if (!string.IsNullOrEmpty (ext))
                    {
                        ext = ext.TrimStart ('.').ToLowerInvariant();
                        encoding.GetBytes (ext, 0, ext.Length, ext_buf, 0);
                    }
                    var out_entry = new OutputEntry
                    {
                        Name      = entry.Name,
                        IndexName = name_buf,
                        IndexExt  = ext_buf,
                    };
                    output_list.Add (out_entry);
                }
                catch (EncoderFallbackException X)
                {
                    throw new InvalidFileName (entry.Name, arcStrings.MsgIllegalCharacters, X);
                }
                catch (ArgumentException X)
                {
                    throw new InvalidFileName (entry.Name, arcStrings.MsgFileNameTooLong, X);
                }
            }

            if (null != callback)
                callback (output_list.Count+2, null, null);

            output.Position = data_offset;
            uint current_offset = 0;
            foreach (var entry in output_list)
            {
                if (null != callback)
                    callback (callback_count++, entry, arcStrings.MsgAddingFile);

                entry.FileTime = File.GetCreationTimeUtc (entry.Name).Ticks;
                entry.Offset = current_offset;
                entry.CompressionType = 0;
                using (var input = File.OpenRead (entry.Name))
                {
                    var size = input.Length;
                    if (size > uint.MaxValue || current_offset + size + 0x0f > uint.MaxValue)
                        throw new FileSizeException();
                    entry.Size = (uint)size;
                    entry.UnpackedSize = entry.Size;
                    using (var checked_stream = new CheckedStream (output, new Crc16()))
                    {
                        input.CopyTo (checked_stream);
                        entry.HasCheckSum = true;
                        entry.CheckSum = (ushort)checked_stream.CheckSumValue;
                    }
                    current_offset += (uint)size + 0x0f;
                    current_offset &= ~0x0fu;
                    output.Position = data_offset + current_offset;
                }
            }

            if (null != callback)
                callback (callback_count++, null, arcStrings.MsgUpdatingIndex);

            // at last, go back to directory and write offset/sizes
            uint index_offset = current_offset;
            using (var index = new BinaryWriter (output, encoding, true))
            {
                foreach (var entry in output_list)
                {
                    index.Write (entry.IndexName);
                    index.Write (entry.IndexExt);
                    index.Write ((uint)entry.Offset);
                    index.Write (entry.UnpackedSize);
                    index.Write (entry.Size);
                    index.Write (entry.CompressionType);
                    index.Write (entry.HasCheckSum);
                    index.Write (entry.CheckSum);
                    index.Write (entry.FileTime);
                }
                index.BaseStream.Position = 0;
                index.Write (Signature);
                index.Write (0x03006b63);
                index.Write (index_offset);
                index.Write (output_list.Count);

                if (null != callback)
                    callback (callback_count++, null, arcStrings.MsgCalculatingChecksum);

                output.Position = 0;
                using (var checked_stream = new CheckedStream (output, new Crc16()))
                {
                    checked_stream.CopyTo (Stream.Null);
                    index.Write ((ushort)checked_stream.CheckSumValue);
                }
            }
        }
    }

    internal class MarielEncoder
    {
        public void Unpack (IBinaryStream input, byte[] dest, int dest_size)
        {
            int out_pos = 0;
            uint bits = 0;
            while (dest_size > 0)
            {
                bool carry = 0 != (bits & 0x80000000);
                bits <<= 1;
                if (0 == bits)
                {
                    bits = input.ReadUInt32();
                    carry = 0 != (bits & 0x80000000);
                    bits = (bits << 1) | 1u;
                }
                int b = input.ReadByte();
                if (-1 == b)
                    break;
                if (!carry)
                {
                    dest[out_pos++] = (byte)b;
                    dest_size--;
                    continue;
                }
                int offset = (b & 0x0f) + 1;
                int count = ((b >> 4) & 0x0f) + 1;
                if (0x0f == count)
                {
                    b = input.ReadByte();
                    if (-1 == b)
                        break;
                    count = (byte)b;
                }
                else if (count > 0x0f)
                {
                    count = input.ReadUInt16();
                }
                if (offset >= 0x0b)
                {
                    offset -= 0x0b;
                    offset <<= 8;
                    offset |= input.ReadUInt8();
                }
                if (count > dest_size)
                    count = dest_size;
                int src = out_pos - offset;
                if (src < 0 || src >= out_pos)
                    break;
                Binary.CopyOverlapped (dest, src, out_pos, count);
                out_pos += count;
                dest_size -= count;
            }
        }
    }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "SND")]
    [ExportMetadata("Target", "OGG")]
    public class SndFormat : ResourceAlias { }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "WPN")]
    [ExportMetadata("Target", "TXT")]
    public class WpnFormat : ResourceAlias { }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "WPS")]
    [ExportMetadata("Target", "SCR")]
    public class WpsFormat : ResourceAlias { }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "TBL")]
    [ExportMetadata("Target", "SCR")]
    public class TblFormat : ResourceAlias { }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "BGM")]
    [ExportMetadata("Target", "WAV")]
    public class BgmFormat : ResourceAlias { }
}
