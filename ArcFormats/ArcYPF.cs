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

namespace GameRes.Formats
{
    public class YpfOptions : ResourceOptions
    {
        public uint Key { get; set; }
    }

    [Export(typeof(ArchiveFormat))]
    public class YpfOpener : ArchiveFormat
    {
        public override string Tag { get { return "YPF"; } }
        public override string Description { get { return arcStrings.YPFDescription; } }
        public override uint Signature { get { return 0x00465059; } }
        public override bool IsHierarchic { get { return true; } }

        private const uint DefaultKey = 0xffffffff;

        public override ArcFile TryOpen (ArcView file)
        {
            uint version = file.View.ReadUInt32 (4);
            uint count = file.View.ReadUInt32 (8);
            uint dir_size = file.View.ReadUInt32 (12);
            if (dir_size < count * 0x17 || count > 0xfffff)
                return null;
            if (dir_size != file.View.Reserve (0x20, dir_size))
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
            return new YpfOptions { Key = Settings.Default.YPFKey };
        }

        public override ResourceOptions GetOptions (object w)
        {
            var widget = w as GUI.WidgetYPF;
            if (null != widget)
            {
                uint last_key = widget.GetKey() ?? DefaultKey;
                Settings.Default.YPFKey = last_key;
                return new YpfOptions { Key = last_key };
            }
            return this.GetDefaultOptions();
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetYPF();
        }

        uint QueryEncryptionKey ()
        {
            var args = new ParametersRequestEventArgs
            {
                Notice = arcStrings.YPFNotice,
            };
            FormatCatalog.Instance.InvokeParametersRequest (this, args);
            if (!args.InputResult)
                throw new OperationCanceledException();
            return GetOptions<YpfOptions> (args.Options).Key;
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

                    uint name_size = Decrypt ((byte)(m_file.View.ReadByte (dir_offset+4) ^ 0xff));
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

            public uint Decrypt (byte value)
            {
                int pos = s_crypt_table.Length - 0x14;
                if (m_version >= 0x100)
                {
                    pos -= 4;
                    if (m_version >= 0x12c && m_version < 0x196)
                        pos += 10;
                }
                pos = Array.FindIndex (s_crypt_table, pos, x => x == value);
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
