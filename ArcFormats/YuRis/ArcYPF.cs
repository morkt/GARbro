//! \file       ArcYPF.cs
//! \date       Mon Jul 14 14:40:06 2014
//! \brief      YPF resource format implementation.
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
using System.Text;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Compression;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.YuRis
{
    public class YpfOptions : ResourceOptions
    {
        public uint      Key { get; set; }
        public uint  Version { get; set; }
        public string Scheme { get; set; }
    }

    internal class YpfEntry : PackedEntry
    {
        public byte[]   IndexName;
        public uint     NameHash;
        public byte     FileType;
        public uint     CheckSum;
    }

    [Serializable]
    public class YpfScheme
    {
        public byte[]   SwapTable;
        public byte     Key;
        public bool     GuessKey;
        public uint     ExtraHeaderSize;
        public uint     ScriptKey;

        public YpfScheme () { }

        public YpfScheme (byte[] swap_table, byte key, uint script_key = 0)
        {
            SwapTable = swap_table;
            Key = key;
            GuessKey = false;
            ExtraHeaderSize = 0;
            ScriptKey = script_key;
        }

        public YpfScheme (byte[] swap_table)
        {
            SwapTable = swap_table;
            GuessKey = true;
            ExtraHeaderSize = 0;
        }
    }

    public class YpfArchive : ArcFile
    {
        public uint     ScriptKey;

        public YpfArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, uint script_key)
            : base (arc, impl, dir)
        {
            ScriptKey = script_key;
        }
    }

    [Serializable]
    public class YuRisScheme : ResourceScheme
    {
        public Dictionary<string, YpfScheme>    KnownSchemes;
    }

    [Export(typeof(ArchiveFormat))]
    public class YpfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "YPF"; } }
        public override string Description { get { return arcStrings.YPFDescription; } }
        public override uint     Signature { get { return 0x00465059; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return true; } }

        public YpfOpener ()
        {
            Signatures = new uint[] { 0x00465059, 0x00905A4D, 0 };
        }

        static public Dictionary<string, YpfScheme> KnownSchemes { get { return DefaultScheme.KnownSchemes; } }

        static YuRisScheme DefaultScheme = new YuRisScheme { KnownSchemes = new Dictionary<string, YpfScheme>() };

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (YuRisScheme)value; }
        }

        public override ArcFile TryOpen (ArcView file)
        {
            long ypf_offset = 0;
            if (file.View.AsciiEqual (0, "MZ"))
            {
                ypf_offset = FindYser (file);
            }
            if (!file.View.AsciiEqual (ypf_offset, "YPF\0"))
                return null;

            uint version  = file.View.ReadUInt32 (ypf_offset+4);
            int count     = file.View.ReadInt32 (ypf_offset+8);
            uint dir_size = file.View.ReadUInt32 (ypf_offset+12);
            if (!IsSaneCount (count) || dir_size < count * 0x17)
                return null;
            if (dir_size > file.View.Reserve (ypf_offset+0x20, dir_size))
                return null;
            var parser = new Parser (file, version, count, dir_size);

            var scheme = QueryEncryptionScheme (file.Name, version);
            var dir = parser.ScanDir (scheme, ypf_offset);
            if (null == dir || 0 == dir.Count)
                return null;

            if (scheme.ScriptKey != 0)
                return new YpfArchive (file, this, dir, scheme.ScriptKey);
            else
                return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var packed_entry = entry as PackedEntry;
            var ypf = arc as YpfArchive;
            Stream input = base.OpenEntry (arc, entry);
            if (null != packed_entry && packed_entry.IsPacked)
                input = new ZLibStream (input, CompressionMode.Decompress);
            uint unpacked_size = null == packed_entry ? entry.Size : packed_entry.UnpackedSize;
            if (null == ypf || 0 == ypf.ScriptKey || unpacked_size <= 0x20
                || !entry.Name.HasExtension (".ybn"))
                return input;
            using (input)
            {
                var data = new byte[unpacked_size];
                input.Read (data, 0, data.Length);
                if (Binary.AsciiEqual (data, 0, "YSTB"))
                    DecryptYstb (data, ypf.ScriptKey);
                return new BinMemoryStream (data, entry.Name);
            }
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new YpfOptions
            {
                Key     = Properties.Settings.Default.YPFKey,
                Version = Properties.Settings.Default.YPFVersion,
                Scheme  = Properties.Settings.Default.YPFScheme,
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

        YpfScheme QueryEncryptionScheme (string arc_name, uint version)
        {
            var title = FormatCatalog.Instance.LookupGame (arc_name);
            if (string.IsNullOrEmpty (title))
                title = FormatCatalog.Instance.LookupGame (arc_name, @"..\*.exe");
            YpfScheme scheme;
            if (!string.IsNullOrEmpty (title) && KnownSchemes.TryGetValue (title, out scheme))
                return scheme;
            var options = Query<YpfOptions> (arcStrings.YPFNotice);
            if (!KnownSchemes.TryGetValue (options.Scheme, out scheme) || null == scheme)
                scheme = new YpfScheme {
                    SwapTable   = GuessSwapTable (version),
                    GuessKey    = true,
                    ExtraHeaderSize = version >= 0x1D9 ? 4u : version == 0xDE ? 8u : 0u,
                };
            return scheme;
        }

        internal long FindYser (ArcView file)
        {
            var exe = new ExeFile (file);
            var offset = exe.FindAsciiString (exe.Overlay, "YSER", 0x10);
            if (-1 == offset)
                return 0;
            uint header_size = file.View.ReadUInt32 (offset+4);
            return offset + header_size;
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
            var scheme = new YpfScheme {
                SwapTable   = GuessSwapTable (ypf_options.Version),
                Key         = (byte)ypf_options.Key
            };
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
                            var start = output.Position;
                            using (var zstream = new ZLibStream (checked_stream, CompressionMode.Compress,
                                                                 CompressionLevel.Level9, true))
                            {
                                input.CopyTo (zstream);
                            }
                            entry.Size = (uint)(output.Position - start);
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
                    byte name_len = (byte)~Parser.DecryptLength (scheme.SwapTable, (byte)entry.IndexName.Length);
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
            // 0x1F4:               0-ybn, 1-bmp, 2-png, 3-jpg, 4-gif, 5-wav, 6-ogg, 7-psd, 8-ycg 9-psb
            string ext = Path.GetExtension (name).TrimStart ('.').ToLower();
            if ("ybn" == ext) return 0;
            if ("bmp" == ext) return 1;
            if ("png" == ext) return 2;
            if ("jpg" == ext || "jpeg" == ext) return 3;
            if ("gif" == ext) return 4;
            if ("avi" == ext && 0xf7 == version) return 5;
            if ("ycg" == ext) return 8;
            if ("psb" == ext) return 9;
            byte type = 0;
            if ("wav" == ext) type = 5;
            else if ("ogg" == ext) type = 6;
            else if ("psd" == ext) type = 7;
            if (0xf7 == version && 0 != type)
                ++type;
            return type;
        }

        byte[] GuessSwapTable (uint version)
        {
            if (0x1F4 == version)
            {
                YpfScheme scheme;
                if (KnownSchemes.TryGetValue ("Unionism Quartet", out scheme))
                    return scheme.SwapTable;
            }
            if (version < 0x100)
                return SwapTable04;
            else if (version >= 0x12c && version < 0x196)
                return SwapTable10;
            else
                return SwapTable00;
        }

        unsafe void DecryptYstb (byte[] data, uint key)
        {
            if (data.Length <= 0x20)
                return;
            fixed (byte* raw = data)
            {
                uint* header = (uint*)raw;
                uint version = header[1];
                int first_item, last_item;
                if (version >= 0x1CB || 0x12C == version || 0x19A == version || 0x1C3 == version || 0x19C == version || 0x198 == version)
                {
                    first_item = 3;
                    last_item = 7;
                }
                else
                {
                    first_item = 2;
                    last_item = 4;
                }
                uint total = 0x20;
                // check sizes
                for (int i = first_item; i < last_item; ++i)
                {
                    if (header[i] >= data.Length)
                        return;
                    total += header[i];
                    if (total > data.Length)
                        return;
                }
                if (total != data.Length)
                    return;
                byte* data8 = raw+0x20;
                for (int i = first_item; i < last_item; ++i)
                {
                    uint size = header[i];
                    if (0 == size)
                        continue;
                    uint* data32 = (uint*)data8;
                    for (uint j = size / 4; j != 0; --j)
                        *data32++ ^= key;
                    data8 = (byte*)data32;
                    uint k = key;
                    for (uint j = size & 3; j != 0; --j)
                    {
                        *data8++ ^= (byte)k;
                        k >>= 8;
                    }
                }
            }
        }

        private class Parser
        {
            ArcView m_file;
            uint    m_version;
            int     m_count;
            uint    m_dir_size;
            
            public Parser (ArcView file, uint version, int count, uint dir_size)
            {
                m_file = file;
                m_count = count;
                m_dir_size = dir_size;
                m_version = version;
            }
            // int32-name_checksum, byte-name_count, *-name, byte-file_type
	        // byte-pack_flag, int32-size, int32-packed_size, int32-offset, int32-file_checksum

            public List<Entry> ScanDir (YpfScheme scheme, long base_offset = 0)
            {
                long dir_offset = base_offset + 0x20;
                uint dir_remaining = m_dir_size;
                var dir = new List<Entry> (m_count);
                byte key = scheme.Key;
                bool guess_key = scheme.GuessKey;
                uint extra_size = 0x12 + scheme.ExtraHeaderSize;
                for (int num = 0; num < m_count; ++num)
                {
                    if (dir_remaining < 5+extra_size)
                        return null;
                    dir_remaining -= 5+extra_size;

                    uint name_size = DecryptLength (scheme.SwapTable, (byte)(m_file.View.ReadByte (dir_offset+4) ^ 0xff));
                    if (name_size > dir_remaining)
                        return null;
                    dir_remaining -= name_size;
                    dir_offset += 5;
                    if (0 == name_size)
                        return null;
                    byte[] raw_name = m_file.View.ReadBytes (dir_offset, name_size);
                    dir_offset += name_size;
                    if (guess_key)
                    {
                        if (name_size < 4)
                            return null;
                        // assume filename contains '.' and 3-characters extension.
                        key = (byte)(raw_name[name_size-4] ^ '.');
                        guess_key = false;
                    }
                    for (uint i = 0; i < name_size; ++i)
                    {
                        raw_name[i] ^= key;
                    }
                    string name = Encodings.cp932.GetString (raw_name);
                    // 0x0F7:               0-ybn, 1-bmp, 2-png, 3-jpg, 4-gif, 5-avi, 6-wav, 7-ogg, 8-psd
                    // 0x122, 0x12C, 0x196: 0-ybn, 1-bmp, 2-png, 3-jpg, 4-gif, 5-wav, 6-ogg, 7-psd
                    // 0x1F4:               0-ybn, 1-bmp, 2-png, 3-jpg, 4-gif, 5-wav, 6-ogg, 7-psd, 8-ycg 9-psb
                    int type_id = m_file.View.ReadByte (dir_offset);
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                    if (string.IsNullOrEmpty (entry.Type))
                    {
                        switch (type_id)
                        {
                        case 0:
                            entry.Type = "script";
                            break;
                        case 1: case 2: case 3: case 4: case 8:
                            entry.Type = "image";
                            break;
                        case 5:
                            entry.Type = 0xf7 == m_version ? "video" : "audio";
                            break;
                        case 6:
                            entry.Type = "audio";
                            break;
                        case 7:
                            entry.Type = 0xf7 == m_version ? "audio" : "image";
                            break;
                        }
                    }
                    entry.IsPacked      = 0 != m_file.View.ReadByte (dir_offset+1);
                    entry.UnpackedSize  = m_file.View.ReadUInt32 (dir_offset+2);
                    entry.Size          = m_file.View.ReadUInt32 (dir_offset+6);
                    entry.Offset        = m_file.View.ReadUInt32 (dir_offset+10) + base_offset;
                    if (entry.CheckPlacement (m_file.MaxOffset))
                        dir.Add (entry);
                    dir_offset += extra_size;
                }
                return dir;
            }

            static public byte DecryptLength (byte[] swap_table, byte value)
            {
                int pos = Array.IndexOf (swap_table, value);
                if (-1 == pos)
                    return value;
                if (0 != (pos & 1))
                    return swap_table[pos-1];
                else
                    return swap_table[pos+1];
            }
        }

        static public byte[] SwapTable00 = {
            0x03, 0x48, 0x06, 0x35,
            0x0C, 0x10, 0x11, 0x19, 0x1C, 0x1E,
            0x09, 0x0B, 0x0D, 0x13, 0x15, 0x1B, 0x20, 0x23, 0x26, 0x29, 0x2C, 0x2F, 0x2E, 0x32,
        };
        static public byte[] SwapTable04 = {
            0x0C, 0x10, 0x11, 0x19, 0x1C, 0x1E,
            0x09, 0x0B, 0x0D, 0x13, 0x15, 0x1B, 0x20, 0x23, 0x26, 0x29, 0x2C, 0x2F, 0x2E, 0x32,
        };
        static public byte[] SwapTable10 = {
            0x09, 0x0B, 0x0D, 0x13, 0x15, 0x1B, 0x20, 0x23, 0x26, 0x29, 0x2C, 0x2F, 0x2E, 0x32,
        };
    }
}
