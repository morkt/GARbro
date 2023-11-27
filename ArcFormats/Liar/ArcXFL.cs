//! \file       ArcXFL.cs
//! \date       Mon Jun 30 21:18:29 2014
//! \brief      XFL resource format implementation.
//
// Copyright (C) 2014-2016 by morkt
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
using GameRes.Formats.Strings;

namespace GameRes.Formats.Liar
{
    [Export(typeof(ArchiveFormat))]
    public class XflOpener : ArchiveFormat
    {
        public override string         Tag { get { return "XFL"; } }
        public override string Description { get { return Strings.arcStrings.XFLDescription; } }
        public override uint     Signature { get { return 0x0001424c; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return true; } }

        public override ArcFile TryOpen (ArcView file)
        {
            var dir = ReadDirectory (file, 0, file.MaxOffset, "");
            if (dir != null)
                return new ArcFile (file, this, dir);
            else
                return null;
        }

        internal List<Entry> ReadDirectory (ArcView file, long base_offset, long max_offset, string base_dir)
        {
            uint dir_size = file.View.ReadUInt32 (base_offset+4);
            int count     = file.View.ReadInt32 (base_offset+8);
            if (!IsSaneCount (count))
                return null;
            long data_offset = base_offset + dir_size + 12;
            if (dir_size >= max_offset || data_offset >= max_offset)
                return null;

            file.View.Reserve (base_offset, (uint)(data_offset - base_offset));
            long cur_offset = base_offset + 12;

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                if (cur_offset+40 > data_offset)
                    return null;
                string name = file.View.ReadString (cur_offset, 32);
                var entry_offset = data_offset + file.View.ReadUInt32 (cur_offset+32);
                var entry_size   = file.View.ReadUInt32 (cur_offset+36);
                List<Entry> subdir = null;
                name = VFS.CombinePath (base_dir, name);
                if (name.HasExtension (".xfl") && file.View.ReadUInt32 (entry_offset) == Signature)
                {
                    subdir = ReadDirectory (file, entry_offset, entry_offset + entry_size, name);
                }
                if (subdir != null && subdir.Count > 0)
                {
                    dir.AddRange (subdir);
                }
                else
                {

                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = entry_offset;
                    entry.Size = entry_size;
                    if (!entry.CheckPlacement (max_offset))
                        return null;
                    dir.Add (entry);
                }
                cur_offset += 40;
            }
            return dir;
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            using (var writer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                writer.Write (Signature);
                int list_size = list.Count();
                uint dir_size = (uint)(list_size * 40);
                writer.Write (dir_size);
                writer.Write (list_size);

                var encoding = Encodings.cp932.WithFatalFallback();

                byte[] name_buf = new byte[32];
                int callback_count = 0;

                if (null != callback)
                    callback (callback_count++, null, arcStrings.MsgWritingIndex);

                // first, write names only
                foreach (var entry in list)
                {
                    string name = Path.GetFileName (entry.Name);
                    try
                    {
                        int size = encoding.GetBytes (name, 0, name.Length, name_buf, 0);
                        if (size < name_buf.Length)
                            name_buf[size] = 0;
                    }
                    catch (EncoderFallbackException X)
                    {
                        throw new InvalidFileName (entry.Name, arcStrings.MsgIllegalCharacters, X);
                    }
                    catch (ArgumentException X)
                    {
                        throw new InvalidFileName (entry.Name, arcStrings.MsgFileNameTooLong, X);
                    }
                    writer.Write (name_buf);
                    writer.BaseStream.Seek (8, SeekOrigin.Current);
                }

                // now, write files and remember offset/sizes
                uint current_offset = 0;
                foreach (var entry in list)
                {
                    if (null != callback)
                        callback (callback_count++, entry, arcStrings.MsgAddingFile);

                    entry.Offset = current_offset;
                    using (var input = File.Open (entry.Name, FileMode.Open, FileAccess.Read))
                    {
                        var size = input.Length;
                        if (size > uint.MaxValue || current_offset + size > uint.MaxValue)
                            throw new FileSizeException();
                        current_offset += (uint)size;
                        entry.Size = (uint)size;
                        input.CopyTo (output);
                    }
                }

                if (null != callback)
                    callback (callback_count++, null, arcStrings.MsgUpdatingIndex);

                // at last, go back to directory and write offset/sizes
                long dir_offset = 12+32;
                foreach (var entry in list)
                {
                    writer.BaseStream.Position = dir_offset;
                    writer.Write ((uint)entry.Offset);
                    writer.Write (entry.Size);
                    dir_offset += 40;
                }
            }
        }
    }

    public class GscScriptData : ScriptData
    {
        public byte[] Header;
        public byte[] Code;
        public byte[] Footer;
    }

    //[Export(typeof(ScriptFormat))]
    public class GscFormat : GenericScriptFormat
    {
        public override string Tag { get { return "GSC"; } }
        public override string Description { get { return Strings.arcStrings.GSCDescription; } }
        public override uint Signature { get { return 0; } }

        public override ScriptData Read (string name, Stream stream)
        {
            using (var file = new BinaryReader (stream, Encodings.cp932, true))
            {
                uint signature = file.ReadUInt32();
                if (signature != file.BaseStream.Length)
                    return null;
                uint header_size = file.ReadUInt32();
                if (header_size > 0x24 || header_size < 0x14)
                    return null;
                uint code_size = file.ReadUInt32();
                uint text_index_size = file.ReadUInt32();
                uint text_size = file.ReadUInt32();
                byte[] header_data = file.ReadBytes ((int)header_size-0x14);
                byte[] code = file.ReadBytes ((int)code_size);
                uint[] index = new uint[text_index_size/4];
                for (int i = 0; i < index.Length; ++i)
                {
                    index[i] = file.ReadUInt32();
                    if (index[i] >= text_size)
                        return null;
                }
                long text_offset = header_size + code_size + text_index_size;

                var script = new GscScriptData();
                script.Header = header_data;
                script.Code = code;
                for (int i = 0; i < index.Length; ++i)
                {
                    file.BaseStream.Position = text_offset + index[i];
                    string text = StreamExtension.ReadCString (file.BaseStream);
                    script.TextLines.Add (new ScriptLine { Id = (uint)i, Text = text });
                }
                long footer_pos = text_offset + text_size;
                file.BaseStream.Position = footer_pos;
                script.Footer = new byte[file.BaseStream.Length - footer_pos];
                file.BaseStream.Read (script.Footer, 0, script.Footer.Length);
                return script;
            }
        }

        public override void Write (Stream stream, ScriptData script_data)
        {
            var script = script_data as GscScriptData;
            if (null == script)
                throw new InvalidFormatException();
            using (var file = new BinaryWriter (stream, Encodings.cp932, true))
            {
                long file_size_pos = file.BaseStream.Position;
                file.Write ((int)0);

                file.Write ((int)(script.Header.Length + 0x14));
                file.Write ((int)script.Code.Length);
                int line_count = script.TextLines.Count;
                int text_index_size = line_count * 4;
                file.Write (text_index_size);

                long text_size_pos = file.BaseStream.Position;
                file.Write ((int)0);
                if (0 < script.Header.Length)
                    file.Write (script.Header);
                if (0 < script.Code.Length)
                    file.Write (script.Code);

                long text_index_pos = file.BaseStream.Position;
                var index = new uint[line_count];
                file.BaseStream.Seek (text_index_size, SeekOrigin.Current);
                int i = 0;
                long text_pos = file.BaseStream.Position;
                uint current_pos = 0;
                foreach (var line in script.TextLines)
                {
                    index[i] = current_pos;
                    var text = Encodings.cp932.GetBytes (line.Text);
                    file.Write (text);
                    file.Write ((byte)0);
                    ++i;
                    current_pos += (uint)text.Length + 1;
                }
                uint text_size = (uint)(file.BaseStream.Position - text_pos);
                if (0 < script.Footer.Length)
                    file.Write (script.Footer);
                uint file_size = (uint)(file.BaseStream.Position - file_size_pos);
                file.BaseStream.Position = file_size_pos;
                file.Write (file_size);
                file.BaseStream.Position = text_size_pos;
                file.Write (text_size);
                file.BaseStream.Position = text_index_pos;
                foreach (var offset in index)
                    file.Write (offset);
                file.BaseStream.Position = file_size_pos + file_size;
            }
        }
    }
}
