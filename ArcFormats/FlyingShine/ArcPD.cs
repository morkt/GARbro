//! \file       ArcPD.cs
//! \date       Thu Aug 14 19:10:02 2014
//! \brief      PD archive format implementation.
//
// Copyright (C) 2014-2017 by morkt
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
using System.Linq;
using System.Text;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.Fs
{
    internal class PackPlusArchive : ArcFile
    {
        public PackPlusArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir)
            : base (arc, impl, dir)
        {
        }
    }

    public class PdOptions : ResourceOptions
    {
        public bool ScrambleContents { get; set; }
    }

    [Export(typeof(ArchiveFormat))]
    public class PdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PD"; } }
        public override string Description { get { return arcStrings.PDDescription; } }
        public override uint     Signature { get { return 0x6b636150; } } // Pack
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return true; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint version = file.View.ReadUInt32 (4);
            if (0x796c6e4f != version && 0x73756c50 != version) // 'Only' || 'Plus'
                return null;
            int count = file.View.ReadInt32 (0x40);
            if (!IsSaneCount (count) || count * 0x90 >= file.MaxOffset)
                return null;
            bool encrypted = 0x73756c50 == version;
            long cur_offset = 0x48;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (cur_offset, 0x80);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadInt64 (cur_offset+0x80);
                entry.Size = file.View.ReadUInt32 (cur_offset+0x88);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (name.HasExtension (".dsf"))
                    entry.Type = "script";
                dir.Add (entry);
                cur_offset += 0x90;
            }
            return encrypted ? new PackPlusArchive (file, this, dir) : new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (arc is PackPlusArchive)
                input = new XoredStream (input, 0xFF);
            return input;
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new PdOptions { ScrambleContents = Properties.Settings.Default.PDScrambleContents };
        }

        public override object GetCreationWidget ()
        {
            return new GUI.CreatePDWidget();
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            int file_count = list.Count();
            if (file_count > 0x4000)
                throw new InvalidFormatException (arcStrings.MsgTooManyFiles);
            if (null != callback)
                callback (file_count+2, null, null);
            int callback_count = 0;
            var pd_options = GetOptions<PdOptions> (options);

            using (var writer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                writer.Write (Signature);
                if (pd_options.ScrambleContents)
                    writer.Write ((uint)0x73756c50);
                else
                    writer.Write ((uint)0x796c6e4f);
                output.Seek (0x38, SeekOrigin.Current);
                writer.Write (file_count);
                writer.Write ((int)0);
                long dir_offset = output.Position;

                if (null != callback)
                    callback (callback_count++, null, arcStrings.MsgWritingIndex);

                var encoding = Encodings.cp932.WithFatalFallback();
                byte[] name_buf = new byte[0x80];
                int previous_size = 0;

                // first, write names only
                foreach (var entry in list)
                {
                    string name = Path.GetFileName (entry.Name);
                    try
                    {
                        int size = encoding.GetBytes (name, 0, name.Length, name_buf, 0);
                        for (int i = size; i < previous_size; ++i)
                            name_buf[i] = 0;
                        previous_size = size;
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
                    writer.BaseStream.Seek (16, SeekOrigin.Current);
                }

                // now, write files and remember offset/sizes
                long current_offset = 0x240000 + dir_offset;
                output.Seek (current_offset, SeekOrigin.Begin);
                foreach (var entry in list)
                {
                    if (null != callback)
                        callback (callback_count++, entry, arcStrings.MsgAddingFile);

                    entry.Offset = current_offset;
                    using (var input = File.OpenRead (entry.Name))
                    {
                        var size = input.Length;
                        if (size > uint.MaxValue)
                            throw new FileSizeException();
                        current_offset += size;
                        entry.Size = (uint)size;
                        if (pd_options.ScrambleContents)
                            CopyScrambled (input, output);
                        else
                            input.CopyTo (output);
                    }
                }

                if (null != callback)
                    callback (callback_count++, null, arcStrings.MsgUpdatingIndex);

                // at last, go back to directory and write offset/sizes
                dir_offset += 0x80;
                foreach (var entry in list)
                {
                    writer.BaseStream.Position = dir_offset;
                    writer.Write (entry.Offset);
                    writer.Write ((long)entry.Size);
                    dir_offset += 0x90;
                }
            }
        }

        void CopyScrambled (Stream input, Stream output)
        {
            byte[] buffer = new byte[81920];
            for (;;)
            {
                int read = input.Read (buffer, 0, buffer.Length);
                if (0 == read)
                    break;
                for (int i = 0; i < read; ++i)
                    buffer[i] = (byte)~buffer[i];
                output.Write (buffer, 0, read);
            }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class FlyingShinePdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PD/2"; } }
        public override string Description { get { return "Flying Shine resource archive version 2"; } }
        public override uint     Signature { get { return 0x69796c46; } } // 'Flyi'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "ngShinePDFile\0"))
                return null;
            uint crc = file.View.ReadUInt16 (0x12);
            byte key  = file.View.ReadByte (0x14);
            int count = file.View.ReadInt32 (0x1c);
            if (!IsSaneCount (count))
                return null;
            uint index_size = (uint)(0x30 * count);
            if (index_size > file.View.Reserve (0x20, index_size))
                return null;
            var enc = Encodings.cp932;
            var buf = new byte[0x30];
            long index_offset = 0x20;
            var dir = new List<Entry> (count);
            for (uint i = 0; i < count; ++i)
            {
                file.View.Read (index_offset, buf, 0, 0x30);
                DecodeEntry (buf, key);
                int len = Array.IndexOf (buf, (byte)0);
                if (len <= 0 || len >= 0x24)
                    return null;
                string name = enc.GetString (buf, 0, len);
                var entry = Create<Entry> (name);
                uint shift  = LittleEndian.ToUInt32 (buf, 0x24);
                entry.Offset = LittleEndian.ToUInt32 (buf, 0x28) - shift;
                entry.Size   = LittleEndian.ToUInt32 (buf, 0x2c) - shift;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x30;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Name.HasExtension (".ogg") && entry.Size > 0x22)
                return OpenOgg (arc, entry);
            if (!entry.Name.HasAnyOfExtensions (".def", ".dsf") || entry.Size < 2)
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            byte key = (byte)(data[data.Length-1] ^ 0xA);
            if (0xD == (data[data.Length-2] ^ key))
            {
                DecodeEntry (data, key);
            }
            return new BinMemoryStream (data);
        }

        Stream OpenOgg (ArcFile arc, Entry entry)
        {
            const uint header_length = 0x23;
            var header = arc.File.View.ReadBytes (entry.Offset, header_length);
            if (!(header.AsciiEqual (0, "OggS") &&
                  header[0x1A] != 1 && header[0x1B] == 0x1E && header[0x1C] == 1 &&
                  header.AsciiEqual (0x1D, "vorbis")))
                return base.OpenEntry (arc, entry);
            header[0x1A] = 1;
            var rest = arc.File.CreateStream (entry.Offset+header_length, entry.Size-header_length);
            return new PrefixStream (header, rest);
        }

        void DecodeEntry (byte[] buf, byte key)
        {
            for (int i = 0; i < buf.Length; ++i)
                buf[i] ^= key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class Pd3Opener : ArchiveFormat
    {
        public override string         Tag { get { return "PD/3"; } }
        public override string Description { get { return "Flying Shine resource archive version 3"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int index_count = file.View.ReadInt32 (0);
            int count = file.View.ReadInt32 (4);
            uint total_size = file.View.ReadUInt32 (0xC);
            if (index_count < count || !IsSaneCount (index_count) || !IsSaneCount (count))
                return null;
            uint index_size = 0x11C * (uint)index_count;
            if (index_size >= file.MaxOffset - 0x18)
                return null;
            uint index_offset = 0x18;
            long base_offset = index_size + index_offset;
            if (base_offset + total_size != file.MaxOffset)
                return null;
            var dir = new List<Entry> (count);
            for (int i = 0; i < index_count; ++i)
            {
                if (0 != file.View.ReadByte (index_offset))
                {
                    var name = file.View.ReadString (index_offset, 0x104);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Size = file.View.ReadUInt32 (index_offset+0x108);
                    entry.Offset = base_offset + file.View.ReadUInt32 (index_offset+0x10C);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                index_offset += 0x11C;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!entry.Name.HasAnyOfExtensions (".def", ".dsf"))
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            for (int i = 0; i < data.Length; ++i)
                data[i] = Binary.RotByteR (data[i], 4);
            return new BinMemoryStream (data);
        }
    }
}
