//! \file       ArcYPF.cs
//! \date       Mon Jul 14 14:40:06 2014
//! \brief      YPF resource format implementation.
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
using System.Text;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZLibNet;
using GameRes.Formats.Strings;
using GameRes.Formats.Properties;
using GameRes.Utility;

namespace GameRes.Formats.YuRis
{
    public class YpfOptions : ResourceOptions
    {
        public uint     Key { get; set; }
        public uint Version { get; set; }
    }

    [Export(typeof(ArchiveFormat))]
    public class YpfOpener : ArchiveFormat
    {
        public override string Tag { get { return "YPF"; } }
        public override string Description { get { return arcStrings.YPFDescription; } }
        public override uint Signature { get { return 0x00465059; } }
        public override bool IsHierarchic { get { return true; } }
        public override bool CanCreate { get { return true; } }

        private const uint DefaultKey = 0xffffffff;

        public override ArcFile TryOpen (ArcView file)
        {
            uint version = file.View.ReadUInt32 (4);
            uint count = file.View.ReadUInt32 (8);
            uint dir_size = file.View.ReadUInt32 (12);
            if (dir_size < count * 0x17 || count > 0xfffff)
                return null;
            if (dir_size > file.View.Reserve (0x20, dir_size))
                return null;
            var parser = new Parser (file, version, count, dir_size);

            uint key = QueryEncryptionKey();
            var dir = parser.ScanDir (key);
            if (0 == dir.Count)
                return null;

            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var packed_entry = entry as PackedEntry;
            if (null == packed_entry || !packed_entry.IsPacked)
                return input;
            else
                return new ZLibStream (input, CompressionMode.Decompress);
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new YpfOptions
            {
                Key     = Settings.Default.YPFKey,
                Version = Settings.Default.YPFVersion,
            };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetYPF();
        }

        public override object GetCreationWidget ()
        {
            return new GUI.CreateYPFWidget();
        }

        uint QueryEncryptionKey ()
        {
            var options = Query<YpfOptions> (arcStrings.YPFNotice);
            return options.Key;
        }

        internal class YpfEntry : PackedEntry
        {
            public byte[]   IndexName;
            public uint     NameHash;
            public byte     FileType;
            public uint     CheckSum;
        }

        delegate uint ChecksumFunc (byte[] data);

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            var ypf_options = GetOptions<YpfOptions> (options);
            if (null == ypf_options)
                throw new ArgumentException ("Invalid archive creation options", "options");
            if (ypf_options.Key > 0xff)
                throw new InvalidEncryptionScheme (arcStrings.MsgCreationKeyRequired);
            if (0 == ypf_options.Version)
                throw new InvalidFormatException (arcStrings.MsgInvalidVersion);

            int callback_count = 0;
            var encoding = Encodings.cp932.WithFatalFallback();

            ChecksumFunc Checksum = data => Crc32.Compute (data, 0, data.Length);

            uint data_offset = 0x20;
            var file_table = new List<YpfEntry>();
            foreach (var entry in list)
            {
                try
                {
                    string file_name = entry.Name;
                    byte[] name_buf = encoding.GetBytes (file_name);
                    if (name_buf.Length > 0xff)
                        throw new InvalidFileName (entry.Name, arcStrings.MsgFileNameTooLong);
                    uint hash = Checksum (name_buf);
                    byte file_type = GetFileType (ypf_options.Version, file_name);

                    for (int i = 0; i < name_buf.Length; ++i)
                        name_buf[i] = (byte)(name_buf[i] ^ ypf_options.Key);

                    file_table.Add (new YpfEntry {
                        Name = file_name,
                        IndexName = name_buf,
                        NameHash = hash,
                        FileType = file_type,
                        IsPacked = 0 == file_type,
                    });
                    data_offset += (uint)(0x17 + name_buf.Length);
                }
                catch (EncoderFallbackException X)
                {
                    throw new InvalidFileName (entry.Name, arcStrings.MsgIllegalCharacters, X);
                }
            }
            file_table.Sort ((a, b) => a.NameHash.CompareTo (b.NameHash));

            output.Position = data_offset;
            uint current_offset = data_offset;
            foreach (var entry in file_table)
            {
                if (null != callback)
                    callback (callback_count++, entry, arcStrings.MsgAddingFile);

                entry.Offset = current_offset;
                using (var input = File.OpenRead (entry.Name))
                {
                    var file_size = input.Length;
                    if (file_size > uint.MaxValue || current_offset + file_size > uint.MaxValue)
                        throw new FileSizeException();
                    entry.UnpackedSize = (uint)file_size;
                    using (var checked_stream = new CheckedStream (output, new Adler32()))
                    {
                        if (entry.IsPacked)
                        {
                            using (var zstream = new ZLibStream (checked_stream, CompressionMode.Compress,
                                                                 CompressionLevel.Level9, true))
                            {
                                input.CopyTo (zstream);
                                zstream.Flush();
                                entry.Size = (uint)zstream.TotalOut;
                            }
                        }
                        else
                        {
                            input.CopyTo (checked_stream);
                            entry.Size = entry.UnpackedSize;
                        }
                        checked_stream.Flush();
                        entry.CheckSum = checked_stream.CheckSumValue;
                        current_offset += entry.Size;
                    }
                }
            }

            if (null != callback)
                callback (callback_count++, null, arcStrings.MsgWritingIndex);

            output.Position = 0;
            using (var writer = new BinaryWriter (output, encoding, true))
            {
                writer.Write (Signature);
                writer.Write (ypf_options.Version);
                writer.Write (file_table.Count);
                writer.Write (data_offset);
                writer.BaseStream.Seek (0x20, SeekOrigin.Begin);
                foreach (var entry in file_table)
                {
                    writer.Write (entry.NameHash);
                    byte name_len = (byte)~Parser.Decrypt (ypf_options.Version, (byte)entry.IndexName.Length);
                    writer.Write (name_len);
                    writer.Write (entry.IndexName);
                    writer.Write (entry.FileType);
                    writer.Write (entry.IsPacked);
                    writer.Write (entry.UnpackedSize);
                    writer.Write (entry.Size);
                    writer.Write ((uint)entry.Offset);
                    writer.Write (entry.CheckSum);
                }
            }
        }

        static byte GetFileType (uint version, string name)
        {
            // 0x0F7: 0-ybn, 1-bmp, 2-png, 3-jpg, 4-gif, 5-avi, 6-wav, 7-ogg, 8-psd
            // 0x122, 0x12C, 0x196: 0-ybn, 1-bmp, 2-png, 3-jpg, 4-gif, 5-wav, 6-ogg, 7-psd
            string ext = Path.GetExtension (name).TrimStart ('.').ToLower();
            if ("ybn" == ext) return 0;
            if ("bmp" == ext) return 1;
            if ("png" == ext) return 2;
            if ("jpg" == ext || "jpeg" == ext) return 3;
            if ("gif" == ext) return 4;
            if ("avi" == ext && 0xf7 == version) return 5;
            byte type = 0;
            if ("wav" == ext) type = 5;
            else if ("ogg" == ext) type = 6;
            else if ("psd" == ext) type = 7;
            if (0xf7 == version && 0 != type)
                ++type;
            return type;
        }

        private class Parser
        {
            ArcView m_file;
            uint    m_version;
            uint    m_count;
            uint    m_dir_size;
            
            public Parser (ArcView file, uint version, uint count, uint dir_size)
            {
                m_file = file;
                m_count = count;
                m_dir_size = dir_size;
                m_version = version;
            }
            // 4-name_checksum, 1-name_count, *-name, 1-file_type
	        // 1-pack_flag, 4-size, 4-packed_size, 4-offset, 4-packed_adler32

            public List<Entry> ScanDir (uint key)
            {
                uint dir_offset = 0x20;
                uint dir_remaining = m_dir_size;
                var dir = new List<Entry> ((int)m_count);
                for (uint num = 0; num < m_count; ++num)
                {
                    if (dir_remaining < 0x17)
                        break;
                    dir_remaining -= 0x17;

                    uint name_size = Decrypt (m_version, (byte)(m_file.View.ReadByte (dir_offset+4) ^ 0xff));
                    if (name_size > dir_remaining)
                        break;
                    dir_remaining -= name_size;
                    dir_offset += 5;
                    if (0 == name_size)
                        break;
                    if (0xffffffff == key)
                    {
                        if (name_size < 4)
                            break;
                        // assume filename contains '.' and 3-characters extension.
                        key = (uint)(m_file.View.ReadByte (dir_offset+name_size-4) ^ 0x2e);
                    }
                    byte[] raw_name = new byte[name_size];
                    for (int i = 0; i < name_size; ++i)
                    {
                        raw_name[i] = (byte)(m_file.View.ReadByte (dir_offset) ^ key);
                        ++dir_offset;
                    }
                    string name = Encodings.cp932.GetString (raw_name);
                    // 0x0F7: 0-ybn, 1-bmp, 2-png, 3-jpg, 4-gif, 5-avi, 6-wav, 7-ogg, 8-psd
                    // 0x122, 0x12C, 0x196: 0-ybn, 1-bmp, 2-png, 3-jpg, 4-gif, 5-wav, 6-ogg, 7-psd
                    int type_id = m_file.View.ReadByte (dir_offset);
                    string type = "";
                    switch (type_id)
                    {
                    case 0:
                        type = "script";
                        break;
                    case 1: case 2: case 3: case 4:
                        type = "image";
                        break;
                    case 5:
                        type = 0xf7 == m_version ? "video" : "audio";
                        break;
                    case 6:
                    case 7:
                        type = "audio";
                        break;
                    }
                    var entry = new PackedEntry { Name = name, Type = type };
                    entry.IsPacked      = 1 == m_file.View.ReadByte (dir_offset+1);
                    entry.UnpackedSize  = m_file.View.ReadUInt32 (dir_offset+2);
                    entry.Size          = m_file.View.ReadUInt32 (dir_offset+6);
                    entry.Offset        = m_file.View.ReadUInt32 (dir_offset+10);
                    if (entry.CheckPlacement (m_file.MaxOffset))
                        dir.Add (entry);
                    dir_offset += 0x12;
                }
                return dir;
            }

            static readonly byte[] s_crypt_table = {
                0x03,0x48,0x06,0x35,		// 0x122, 0x196
                0x0C,0x10,0x11,0x19,0x1C,0x1E,	// 0x0F7
                0x09,0x0B,0x0D,0x13,0x15,0x1B,	// 0x12C
                0x20,0x23,0x26,0x29,
                0x2C,0x2F,0x2E,0x32,
            };
            // 0xFF 0x0F7 "Four-Leaf" adler32
            // 0x34 0x122 "Neko Koi!" crc32
            // 0x28 0x12C "Suzukaze no Melt" (no recovery - 00 00 00 00)
            // 0xFF 0x196 "Mamono Musume-tachi to no Rakuen ~Slime & Scylla~"

            static public byte Decrypt (uint version, byte value)
            {
                int pos = 4;
                if (version >= 0x100)
                {
                    if (version >= 0x12c && version < 0x196)
                        pos = 10;
                    else
                        pos = 0;
                }
                pos = Array.IndexOf (s_crypt_table, value, pos);
                if (-1 == pos)
                    return value;
                if (0 != (pos & 1))
                    return s_crypt_table[pos-1];
                else
                    return s_crypt_table[pos+1];
            }
        }
    }
}
