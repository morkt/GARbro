//! \file       ArcWAG.cs
//! \date       Sun Mar 13 23:35:58 2016
//! \brief      Hexenhaus resource archive.
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
using GameRes.Utility;

namespace GameRes.Formats.Hexenhaus
{
    [Export(typeof(ArchiveFormat))]
    public class WagOpener : ArchiveFormat
    {
        public override string         Tag { get { return "WAG/IAF"; } }
        public override string Description { get { return "Hexenhaus resource archive"; } }
        public override uint     Signature { get { return 0x5F464149; } } // 'IAF_'
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public WagOpener ()
        {
            Extensions = new string[] { "wag" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int type = file.View.ReadUInt16 (4);
            int count = file.View.ReadInt32 (6);
            if (!IsSaneCount (count))
                return null;

            using (var enc = file.CreateStream())
            using (var dec = new EncryptedStream (enc))
            using (var index = new BinaryReader (dec))
            {
                dec.Position = 0x4A;
                var offsets = new uint[count];
                for (int i = 0; i < count; ++i)
                {
                    offsets[i] = index.ReadUInt32();
                }
                var name_buffer = new byte[0x100];
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    index.BaseStream.Position = offsets[i];
                    uint signature = index.ReadUInt32();
                    if (signature != 0x41544144) // 'DATA'
                        continue;
                    int section_count = index.ReadInt32();
                    index.ReadInt16();
                    var entry = new Entry { Offset = offsets[i] };
                    for (int s = 0; s < section_count; ++s)
                    {
                        signature = index.ReadUInt32();
                        if (0x44474D49 == signature) // 'IMGD'
                        {
                            entry.Offset = index.BaseStream.Position - 4;
                            uint imgd_size = index.ReadUInt32();
                            entry.Size = imgd_size + 0x10;
                            index.BaseStream.Seek (imgd_size + 2, SeekOrigin.Current);
                        }
                        else if (0x454E4E46 == signature) // 'FNNE'
                        {
                            int name_length = index.ReadInt32()-2;
                            index.ReadInt16();
                            if (name_length > name_buffer.Length)
                                name_buffer = new byte[name_length];
                            index.Read (name_buffer, 0, name_length);
                            entry.Name = Encodings.cp932.GetString (name_buffer, 0, name_length);
                            entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name);
                            index.ReadInt16();
                        }
                        else
                        {
                            var section_size = index.ReadUInt32();
                            // section not supported, skip
                            index.BaseStream.Seek (section_size+2, SeekOrigin.Current);
                            if (0x415A4F4D != signature) // 'MOZA'
                                Trace.WriteLine (string.Format ("Unknown section 0x{0:X8}", signature), "[WAG/IAF]");
                        }
                    }
                    if (entry.Size > 0 && !string.IsNullOrEmpty (entry.Name))
                        dir.Add (entry);
                }
                if (0 == dir.Count)
                    return null;
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new EncryptedStream (input);
        }
    }

    internal class EncryptedStream : ProxyStream
    {
        public EncryptedStream (Stream input, bool leave_open = false)
            : base (input, leave_open)
        {
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = BaseStream.Read (buffer, offset, count);
            for (int i = 0; i < read; ++i)
                buffer[offset+i] = Binary.RotByteR (buffer[offset+i], 4);
            return read;
        }

        public override int ReadByte ()
        {
            int b = BaseStream.ReadByte();
            if (b != -1)
                b = Binary.RotByteR ((byte)b, 4);
            return b;
        }
    }

    [Export(typeof(ImageFormat))]
    public class ImgdFormat : PngFormat
    {
        public override string         Tag { get { return "IMGD/WAG"; } }
        public override string Description { get { return "WAG archive PNG image"; } }
        public override uint     Signature { get { return 0x44474D49; } } // 'IMGD'

        public ImgdFormat ()
        {
            Extensions = new string[] { "png" };
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            ImageMetaData info;
            using (var png = new StreamRegion (stream, 0x10, true))
                info = base.ReadMetaData (png);
            if (null == info)
                return null;
            stream.Seek (-14, SeekOrigin.End);
            var cntr = new byte[12];
            stream.Read (cntr, 0, 12);
            if (Binary.AsciiEqual (cntr, "CNTR"))
            {
                info.OffsetX = LittleEndian.ToInt32 (cntr, 4);
                info.OffsetY = LittleEndian.ToInt32 (cntr, 8);
            }
            return info;
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            using (var png = new StreamRegion (stream, 0x10, true))
                return base.Read (png, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ImgdFormat.Write not implemented");
        }
    }
}
