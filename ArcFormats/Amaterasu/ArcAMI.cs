//! \file       ArcAMI.cs
//! \date       Thu Jul 03 09:40:40 2014
//! \brief      Muv-Luv Amaterasu Translation archive.
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
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using GameRes.Compression;
using GameRes.Formats.Strings;

namespace GameRes.Formats.Amaterasu
{
    internal class AmiEntry : PackedEntry
    {
        public uint Id;

        private Lazy<string> m_ext;
        private Lazy<string> m_name;
        private Lazy<string> m_type;
        public override string Name
        {
            get { return m_name.Value; }
            set { m_name = new Lazy<string> (() => value); }
        }
        public override string Type
        {
            get { return m_type.Value; }
            set { m_type = new Lazy<string> (() => value); }
        }

        public AmiEntry (uint id, Func<string> ext_factory)
        {
            Id = id;
            m_ext = new Lazy<string> (ext_factory);
            m_name = new Lazy<string> (GetName);
            m_type = new Lazy<string> (GetEntryType);
        }

        private string GetName ()
        {
            return string.Format ("{0:x8}.{1}", Id, m_ext.Value);
        }

        private string GetEntryType ()
        {
            var ext = m_ext.Value;
            if ("grp" == ext)
                return "image";
            if ("scr" == ext)
                return "script";
            return "";
        }
    }

    internal class AmiOptions : ResourceOptions
    {
        public bool     UseBaseArchive;
        public string   BaseArchive;
    }

    [Export(typeof(ArchiveFormat))]
    public class AmiOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AMI"; } }
        public override string Description { get { return Strings.arcStrings.AMIDescription; } }
        public override uint     Signature { get { return 0x00494D41; } } // 'AMI'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return true; } }

        public AmiOpener ()
        {
            Extensions = new string[] { "ami", "amr" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (count <= 0)
                return null;
            uint base_offset = file.View.ReadUInt32 (8);
            long max_offset = file.MaxOffset;
            if (base_offset >= max_offset)
                return null;

            uint cur_offset = 16;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                if (cur_offset+16 > base_offset)
                    return null;
                uint id = file.View.ReadUInt32 (cur_offset);
                uint offset = file.View.ReadUInt32 (cur_offset+4);
                uint size = file.View.ReadUInt32 (cur_offset+8);
                uint packed_size = file.View.ReadUInt32 (cur_offset+12);

                var entry = new AmiEntry (id, () => {
                    uint signature = file.View.ReadUInt32 (offset);
                    if (0x00524353 == signature)
                        return "scr";
                    else if (0 != packed_size || 0x00505247 == signature)
                        return "grp";
                    else
                        return "dat";
                });

                entry.Offset = offset;
                entry.UnpackedSize = size;
                entry.IsPacked = 0 != packed_size;
                entry.Size   = entry.IsPacked ? packed_size : size;
                if (!entry.CheckPlacement (max_offset))
                    return null;
                dir.Add (entry);
                cur_offset += 16;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = base.OpenEntry (arc, entry);
            var packed_entry = entry as AmiEntry;
            if (null == packed_entry || !packed_entry.IsPacked)
                return input;
            else
                return new ZLibStream (input, CompressionMode.Decompress);
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            ArcFile base_archive = null;
            var ami_options = GetOptions<AmiOptions> (options);
            if (null != ami_options && ami_options.UseBaseArchive && !string.IsNullOrEmpty (ami_options.BaseArchive))
            {
                var base_file = new ArcView (ami_options.BaseArchive);
                try
                {
                    if (base_file.View.ReadUInt32(0) == Signature)
                        base_archive = TryOpen (base_file);
                    if (null == base_archive)
                        throw new InvalidFormatException (string.Format ("{0}: base archive could not be read",
                            Path.GetFileName (ami_options.BaseArchive)));
                    base_file = null;
                }
                finally
                {
                    if (null != base_file)
                        base_file.Dispose();
                }
            }
            try
            {
                var file_table = new SortedDictionary<uint, PackedEntry>();
                if (null != base_archive)
                {
                    foreach (AmiEntry entry in base_archive.Dir)
                        file_table[entry.Id] = entry;
                }
                int update_count = UpdateFileTable (file_table, list);
                if (0 == update_count)
                    throw new InvalidFormatException (arcStrings.AMINoFiles);

                uint file_count = (uint)file_table.Count;
                if (null != callback)
                    callback ((int)file_count+1, null, null);

                int callback_count = 0;
                long start_offset = output.Position;
                uint data_offset = file_count * 16 + 16;
                output.Seek (data_offset, SeekOrigin.Current);
                foreach (var entry in file_table)
                {
                    if (null != callback)
                        callback (callback_count++, entry.Value, arcStrings.MsgAddingFile);
                    long current_offset = output.Position;
                    if (current_offset > uint.MaxValue)
                        throw new FileSizeException();
                    if (entry.Value is AmiEntry)
                        CopyAmiEntry (base_archive, entry.Value, output);
                    else
                        entry.Value.Size = WriteAmiEntry (entry.Value, output);
                    entry.Value.Offset = (uint)current_offset;
                }
                if (null != callback)
                    callback (callback_count++, null, arcStrings.MsgWritingIndex);
                output.Position = start_offset;
                using (var header = new BinaryWriter (output, Encoding.ASCII, true))
                {
                    header.Write (Signature);
                    header.Write (file_count);
                    header.Write (data_offset);
                    header.Write ((uint)0);
                    foreach (var entry in file_table)
                    {
                        header.Write (entry.Key);
                        header.Write ((uint)entry.Value.Offset);
                        header.Write ((uint)entry.Value.UnpackedSize);
                        header.Write ((uint)entry.Value.Size);
                    }
                }
            }
            finally
            {
                if (null != base_archive)
                    base_archive.Dispose();
            }
        }

        int UpdateFileTable (IDictionary<uint, PackedEntry> table, IEnumerable<Entry> list)
        {
            int update_count = 0;
            foreach (var entry in list)
            {
                if (entry.Type != "image" && !entry.Name.HasExtension (".scr"))
                    continue;
                uint id;
                if (!uint.TryParse (Path.GetFileNameWithoutExtension (entry.Name), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out id))
                    continue;
                PackedEntry existing;
                if (table.TryGetValue (id, out existing) && !(existing is AmiEntry))
                {
                    var file_new = new FileInfo (entry.Name);
                    if (!file_new.Exists)
                        continue;
                    var file_old = new FileInfo (existing.Name);
                    if (file_new.LastWriteTime <= file_old.LastWriteTime)
                        continue;
                }
                table[id] = new PackedEntry
                {
                    Name = entry.Name,
                    Type = entry.Type
                };
                ++update_count;
            }
            return update_count;
        }

        void CopyAmiEntry (ArcFile base_archive, Entry entry, Stream output)
        {
            using (var input = base_archive.File.CreateStream (entry.Offset, entry.Size))
                input.CopyTo (output);
        }

        uint WriteAmiEntry (PackedEntry entry, Stream output)
        {
            uint packed_size = 0;
            using (var input = VFS.OpenBinaryStream (entry))
            {
                long file_size = input.Length;
                if (file_size > uint.MaxValue)
                    throw new FileSizeException();
                entry.UnpackedSize = (uint)file_size;
                if ("image" == entry.Type)
                {
                    packed_size = WriteImageEntry (entry, input, output);
                }
                else
                {
                    input.AsStream.CopyTo (output);
                }
            }
            return packed_size;
        }

        static Lazy<GrpFormat> s_grp_format = new Lazy<GrpFormat> (() => 
            FormatCatalog.Instance.ImageFormats.OfType<GrpFormat>().FirstOrDefault());

        uint WriteImageEntry (PackedEntry entry, IBinaryStream input, Stream output)
        {
            var grp = s_grp_format.Value;
            if (null == grp) // probably never happens
                throw new FileFormatException ("GRP image encoder not available");
            bool is_grp = grp.Signature == input.Signature;
            input.Position = 0;
            var start = output.Position;
            using (var zstream = new ZLibStream (output, CompressionMode.Compress, CompressionLevel.Level9, true))
            {
                if (is_grp)
                {
                    input.AsStream.CopyTo (zstream);
                }
                else
                {
                    var image = ImageFormat.Read (input);
                    if (null == image)
                        throw new InvalidFormatException (string.Format (arcStrings.MsgInvalidImageFormat, entry.Name));
                    grp.Write (zstream, image);
                    entry.UnpackedSize = (uint)zstream.TotalIn;
                }
            }
            return (uint)(output.Position - start);
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new AmiOptions {
                UseBaseArchive = Properties.Settings.Default.AMIUseBaseArchive,
                BaseArchive    = Properties.Settings.Default.AMIBaseArchive,
            };
        }

        public override object GetCreationWidget ()
        {
            return new GUI.CreateAMIWidget();
        }
    }

    public class AmiScriptData : ScriptData
    {
        public uint Id;
        public uint Type;
    }

    [Export(typeof(ScriptFormat))]
    public class ScrFormat : ScriptFormat
    {
        public override string Tag { get { return "SCR/AMI"; } }
        public override string Description { get { return Strings.arcStrings.SCRDescription; } }
        public override uint Signature { get { return 0x00524353; } }

        public override ScriptData Read (string name, Stream stream)
        {
            if (Signature != FormatCatalog.ReadSignature (stream))
                return null;
            uint script_id = Convert.ToUInt32 (name, 16);
            uint max_offset = (uint)Math.Min (stream.Length, 0xffffffff);

            using (var file = new BinaryReader (stream, Encodings.cp932, true))
            {
                uint script_type = file.ReadUInt32();
                var script = new AmiScriptData {
                    Id = script_id,
                    Type = script_type
                };
                uint count = file.ReadUInt32();
                for (uint i = 0; i < count; ++i)
                {
                    uint offset = file.ReadUInt32();
                    if (offset >= max_offset)
                        throw new InvalidFormatException ("Invalid offset in script data file");
                    int size = file.ReadInt32();
                    uint id = file.ReadUInt32();
                    var header_pos = file.BaseStream.Position;
                    file.BaseStream.Position = offset;
                    byte[] line = file.ReadBytes (size);
                    if (line.Length != size)
                        throw new InvalidFormatException ("Premature end of file");
                    string text = Encodings.cp932.GetString (line);

                    script.TextLines.Add (new ScriptLine { Id = id, Text = text });
                    file.BaseStream.Position = header_pos;
                }
                return script;
            }
        }

        public string GetName (ScriptData script_data)
        {
            var script = script_data as AmiScriptData;
            if (null != script)
                return script.Id.ToString ("x8");
            else
                return null;
        }

        struct IndexEntry
        {
            public uint offset, size, id;
        }

        public override void Write (Stream stream, ScriptData script_data)
        {
            var script = script_data as AmiScriptData;
            if (null == script)
                throw new ArgumentException ("Illegal ScriptData", "script_data");
            using (var file = new BinaryWriter (stream, Encodings.cp932, true))
            {
                file.Write (Signature);
                file.Write (script.Type);
                uint count = (uint)script.TextLines.Count;
                file.Write (count);
                var index_pos = file.BaseStream.Position;
                file.Seek ((int)count*12, SeekOrigin.Current);
                var index = new IndexEntry[count];
                int i = 0;
                foreach (var line in script.TextLines)
                {
                    var text = Encodings.cp932.GetBytes (line.Text);
                    index[i].offset = (uint)file.BaseStream.Position;
                    index[i].size   = (uint)text.Length;
                    index[i].id     = line.Id;
                    file.Write (text);
                    file.Write ((byte)0);
                    ++i;
                }
                var end_pos = file.BaseStream.Position;
                file.BaseStream.Position = index_pos;
                foreach (var entry in index)
                {
                    file.Write (entry.offset);
                    file.Write (entry.size);
                    file.Write (entry.id);
                }
                file.BaseStream.Position = end_pos;
            }
        }
    }
}
