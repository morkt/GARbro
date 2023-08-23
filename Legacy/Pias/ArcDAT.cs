//! \file       ArcDAT.cs
//! \date       2022 May 24
//! \brief      Pias resource archive.
//
// Copyright (C) 2022 by morkt
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

using GameRes.Utility;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;

// [990528][Pias] Galfro ~Gal's Frontier~

namespace GameRes.Formats.Pias
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/PIAS"; } }
        public override string Description { get { return "Pias resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            var arc_name = Path.GetFileName (file.Name).ToLowerInvariant();

            string entry_type = null;
            if ("voice.dat" == arc_name || "music.dat" == arc_name || "sound.dat" == arc_name)
                entry_type = "audio";
            else if ("graph.dat" == arc_name)
                entry_type = "image";
            else
                return null;

            int resource_type = -1;
            if ("graph.dat" == arc_name)
                resource_type = 1;
            else if ("sound.dat" == arc_name)
                resource_type = 2;

            uint header_size = 1 == resource_type ? 8u : 4u;
            List<Entry> dir = null;
            if (resource_type > 0)
            {
                var text_name = VFS.ChangeFileName (file.Name, "text.dat");
                if (!VFS.FileExists (text_name))
                    return null;
                using (var text_dat = VFS.OpenBinaryStream (text_name))
                {
                    var reader = new TextReader (text_dat);
                    dir = reader.GetResourceList (resource_type);
                    if (dir != null)
                    {
                        for (int i = dir.Count - 1; i >= 0; --i)
                        {
                            var entry = dir[i];
                            entry.Size = file.View.ReadUInt32 (entry.Offset) + header_size;
                            entry.Name = i.ToString("D4");
                            entry.Type = entry_type;
                        }
                    }
                }
            }
            if (null == dir)
                dir = new List<Entry>();
            var known_offsets = new HashSet<long> (dir.Select (e => e.Offset));
            long offset = 0;
            while (offset < file.MaxOffset)
            {
                uint entry_size = file.View.ReadUInt32(offset) + header_size;
                if (!known_offsets.Contains (offset))
                {
                    var entry = new Entry {
                        Name = dir.Count.ToString("D4"),
                        Type = entry_type,
                        Offset = offset,
                        Size = entry_size,
                    };
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                offset += entry_size;
            }
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.OpenBinaryEntry (entry);
            return new GraphImageDecoder (input);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Type != "audio")
                return base.OpenEntry (arc, entry);
            uint size = arc.File.View.ReadUInt32 (entry.Offset);
            var format = new WaveFormat
            {
                FormatTag = 1,
                SamplesPerSecond = 22050,
                BitsPerSample = 8,
            };
            if (VFS.IsPathEqualsToFileName (arc.File.Name, "sound.dat"))
            {
                format.Channels = 2;
                format.BlockAlign = 2;
            }
            else
            {
                format.Channels = 1;
                format.BlockAlign = 1;
            }
            format.SetBPS();
            byte[] header;
            using (var buffer = new MemoryStream())
            {
                WaveAudio.WriteRiffHeader (buffer, format, size);
                header = buffer.ToArray();
            }
            var data = arc.File.CreateStream (entry.Offset+4, entry.Size-4);
            return new PrefixStream (header, data);
        }
    }

    internal class TextReader
    {
        IBinaryStream m_input;

        public TextReader (IBinaryStream input)
        {
            m_input = input;
        }

        public List<Entry> GetResourceList (int resource_type)
        {
            List<Entry> dir = null;
            while (m_input.PeekByte() != -1)
            {
                byte op_code = m_input.ReadUInt8();
                if (op_code != 0x68)
                {
                    Trace.WriteLine (string.Format ("unknown opcode 0x{0:X2} in text.dat", op_code), "DAT/PIAS");
                    return null;
                }
                int type = ReadInt();
                int count = ReadInt();
                Action<uint> action = off => {};
                if (type == resource_type)
                {
                    if (!ArchiveFormat.IsSaneCount (count))
                        return null;
                    dir = new List<Entry> (count);
                    action = off => dir.Add (new Entry { Offset = off });
                }
                for (int i = 0; i < count; ++i)
                {
                    uint offset = m_input.ReadUInt32();
                    action (offset);
                }
                if (type == resource_type)
                    break;
            }
            return dir;
        }

        private int ReadInt ()
        {
            int result = m_input.ReadUInt8();
            int code = result & 0xC0;
            if (0 == code)
                return result;
            result = (result & 0x3F) << 8 | m_input.ReadUInt8();
            if (0x40 == code)
                return result;
            result = result << 8 | m_input.ReadUInt8();
            if (0x80 == code)
                return result;
            return result << 8 | m_input.ReadUInt8();
        }
    }

    internal class GraphImageDecoder : BinaryImageDecoder
    {
        public GraphImageDecoder (IBinaryStream input) : base (input, new ImageMetaData { BPP = 16 })
        {
            m_input.ReadInt32(); // skip size
            Info.Width  = m_input.ReadUInt16();
            Info.Height = m_input.ReadUInt16();
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 8;
            int plane_size = Info.iWidth * Info.iHeight;
            var pixels = new byte[plane_size * 2];
            int dst = 0;
            for (int p = 0; p < plane_size; )
            {
                ushort word = m_input.ReadUInt16();
                if ((word & 0x8000) != 0)
                {
                    int count = ((word >> 12) & 7) + 2;
                    p += count;
                    int offset = (word & 0xFFF) * 2;
                    count = Math.Min (count * 2, pixels.Length - dst);
                    Binary.CopyOverlapped (pixels, dst - offset, dst, count);
                    dst += count;
                }
                else
                {
                    LittleEndian.Pack (word, pixels, dst);
                    dst += 2;
                    ++p;
                }
            }
            int stride = Info.iWidth * 2;
            return ImageData.Create (Info, PixelFormats.Bgr555, null, pixels, stride);
        }
    }
}
