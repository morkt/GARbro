//! \file       ArcKogado.cs
//! \date       Sun Aug 24 22:01:05 2014
//! \brief      Kogado game engine archive implementation.
//
// Copyright (C) 2014 by morkt
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
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.Kogado
{
    public class KogadoEntry : PackedEntry
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
        public override string         Tag { get { return "KOGADO"; } }
        public override string Description { get { return arcStrings.KogadoDescription; } }
        public override uint     Signature { get { return 0x61507948; } } // 'HyPa'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return true; } }

        public PakOpener ()
        {
            Extensions = new string[] { "pak" };
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
                var entry = FormatCatalog.Instance.Create<KogadoEntry> (name);
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
            var packed_entry = entry as KogadoEntry;
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
                return new CryptoStream (input, new NotTransform(), CryptoStreamMode.Read);
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
                return new MemoryStream (unpacked);
            }
            finally
            {
                input.Dispose();
            }
        }

        internal class OutputEntry : KogadoEntry
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

    public sealed class Crc16 : ICheckSum
    {
        private ushort m_value = 0xffff;

        public uint Value { get { return m_value; } }

        public void Update (byte[] buf, int pos, int len)
        {
            for (int i = 0; i < len; ++i)
            {
                m_value = (ushort)(Crc16Table[(m_value^buf[pos+i]) & 0xff] ^ (m_value >> 8));
            }
        }

        private static readonly ushort[] Crc16Table = {
            0x0000, 0x1189, 0x2312, 0x329b, 0x4624, 0x57ad, 0x6536, 0x74bf,
            0x8c48, 0x9dc1, 0xaf5a, 0xbed3, 0xca6c, 0xdbe5, 0xe97e, 0xf8f7,
            0x1081, 0x0108, 0x3393, 0x221a, 0x56a5, 0x472c, 0x75b7, 0x643e,
            0x9cc9, 0x8d40, 0xbfdb, 0xae52, 0xdaed, 0xcb64, 0xf9ff, 0xe876,
            0x2102, 0x308b, 0x0210, 0x1399, 0x6726, 0x76af, 0x4434, 0x55bd,
            0xad4a, 0xbcc3, 0x8e58, 0x9fd1, 0xeb6e, 0xfae7, 0xc87c, 0xd9f5,
            0x3183, 0x200a, 0x1291, 0x0318, 0x77a7, 0x662e, 0x54b5, 0x453c,
            0xbdcb, 0xac42, 0x9ed9, 0x8f50, 0xfbef, 0xea66, 0xd8fd, 0xc974,
            0x4204, 0x538d, 0x6116, 0x709f, 0x0420, 0x15a9, 0x2732, 0x36bb,
            0xce4c, 0xdfc5, 0xed5e, 0xfcd7, 0x8868, 0x99e1, 0xab7a, 0xbaf3,
            0x5285, 0x430c, 0x7197, 0x601e, 0x14a1, 0x0528, 0x37b3, 0x263a,
            0xdecd, 0xcf44, 0xfddf, 0xec56, 0x98e9, 0x8960, 0xbbfb, 0xaa72,
            0x6306, 0x728f, 0x4014, 0x519d, 0x2522, 0x34ab, 0x0630, 0x17b9,
            0xef4e, 0xfec7, 0xcc5c, 0xddd5, 0xa96a, 0xb8e3, 0x8a78, 0x9bf1,
            0x7387, 0x620e, 0x5095, 0x411c, 0x35a3, 0x242a, 0x16b1, 0x0738,
            0xffcf, 0xee46, 0xdcdd, 0xcd54, 0xb9eb, 0xa862, 0x9af9, 0x8b70,
            0x8408, 0x9581, 0xa71a, 0xb693, 0xc22c, 0xd3a5, 0xe13e, 0xf0b7,
            0x0840, 0x19c9, 0x2b52, 0x3adb, 0x4e64, 0x5fed, 0x6d76, 0x7cff,
            0x9489, 0x8500, 0xb79b, 0xa612, 0xd2ad, 0xc324, 0xf1bf, 0xe036,
            0x18c1, 0x0948, 0x3bd3, 0x2a5a, 0x5ee5, 0x4f6c, 0x7df7, 0x6c7e,
            0xa50a, 0xb483, 0x8618, 0x9791, 0xe32e, 0xf2a7, 0xc03c, 0xd1b5,
            0x2942, 0x38cb, 0x0a50, 0x1bd9, 0x6f66, 0x7eef, 0x4c74, 0x5dfd,
            0xb58b, 0xa402, 0x9699, 0x8710, 0xf3af, 0xe226, 0xd0bd, 0xc134,
            0x39c3, 0x284a, 0x1ad1, 0x0b58, 0x7fe7, 0x6e6e, 0x5cf5, 0x4d7c,
            0xc60c, 0xd785, 0xe51e, 0xf497, 0x8028, 0x91a1, 0xa33a, 0xb2b3,
            0x4a44, 0x5bcd, 0x6956, 0x78df, 0x0c60, 0x1de9, 0x2f72, 0x3efb,
            0xd68d, 0xc704, 0xf59f, 0xe416, 0x90a9, 0x8120, 0xb3bb, 0xa232,
            0x5ac5, 0x4b4c, 0x79d7, 0x685e, 0x1ce1, 0x0d68, 0x3ff3, 0x2e7a,
            0xe70e, 0xf687, 0xc41c, 0xd595, 0xa12a, 0xb0a3, 0x8238, 0x93b1,
            0x6b46, 0x7acf, 0x4854, 0x59dd, 0x2d62, 0x3ceb, 0x0e70, 0x1ff9,
            0xf78f, 0xe606, 0xd49d, 0xc514, 0xb1ab, 0xa022, 0x92b9, 0x8330,
            0x7bc7, 0x6a4e, 0x58d5, 0x495c, 0x3de3, 0x2c6a, 0x1ef1, 0x0f78,
        };
    }

    internal class MarielEncoder
    {
        public void Unpack (Stream input, byte[] dest, int dest_size)
        {
            int out_pos = 0;
            using (var reader = new BinaryReader (input, Encoding.ASCII, true))
            {
                uint bits = 0;
                while (dest_size > 0)
                {
                    bool carry = 0 != (bits & 0x80000000);
                    bits <<= 1;
                    if (0 == bits)
                    {
                        bits = reader.ReadUInt32();
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
                        count = reader.ReadUInt16();
                    }
                    if (offset >= 0x0b)
                    {
                        offset -= 0x0b;
                        offset <<= 8;
                        offset |= reader.ReadByte();
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
    }
}
