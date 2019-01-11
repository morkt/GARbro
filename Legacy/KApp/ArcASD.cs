//! \file       ArcASD.cs
//! \date       2019 Jan 11
//! \brief      Spiel resource archive.
//
// Copyright (C) 2019 by morkt
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

namespace GameRes.Formats.KApp
{
    [Export(typeof(ArchiveFormat))]
    public class AsdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ASD/KTOOL"; } }
        public override string Description { get { return "KApp engine resource archive"; } }
        public override uint     Signature { get { return 0x6F6F746B; } } // 'ktool210'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint index_pos = 0x10;
            uint next_offset = file.View.ReadUInt32 (index_pos);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                index_pos += 4;
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D4}", base_name, i),
                    Offset = next_offset,
                };
                next_offset = file.View.ReadUInt32 (index_pos);
                entry.Size = (uint)(next_offset - entry.Offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            DetectFileTypes (file, dir);
            return new ArcFile (file, this, dir);
        }

        void DetectFileTypes (ArcView file, List<Entry> dir)
        {
            foreach (var entry in dir)
            {
                var type = file.View.ReadUInt32 (entry.Offset+0xC);
                switch (type)
                {
                case 0xB713E4: entry.Type = "audio"; break;
                case 0xB29EA4:
                case 0x973768: entry.Type = "image"; break;
                }
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            uint id = arc.File.View.ReadUInt32 (entry.Offset+0xC);
            if (id != 0xB713E4)
                return base.OpenEntry (arc, entry);
            return OpenAudio (arc, entry);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var info = CgdMetaData.FromStream (input, 0);
            input.Position = 0;
            if (null == info)
                return ImageFormatDecoder.Create (input);
            return new CgdDecoder (input, info);
        }

        Stream OpenAudio (ArcFile arc, Entry entry)
        {
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
            {
                var header = input.ReadHeader (0x20);
                int header_size = header.ToUInt16 (10);
                var format = new WaveFormat {
                    FormatTag = header.ToUInt16 (0x10),
                    Channels = header.ToUInt16 (0x12),
                    SamplesPerSecond = header.ToUInt32 (0x14),
                    AverageBytesPerSecond = header.ToUInt32 (0x18),
                    BlockAlign = header.ToUInt16 (0x1C),
                    BitsPerSample  = header.ToUInt16 (0x1E),
                };
                input.Position = header_size + 0x10;
                var data = new byte[header.ToInt32 (0)];
                KTool.Unpack (input, data, header[8]);
                var output = new MemoryStream (data.Length);
                WaveAudio.WriteRiffHeader (output, format, (uint)data.Length);
                output.Write (data, 0, data.Length);
                output.Position = 0;
                return output;
            }
        }
    }
}
