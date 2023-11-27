//! \file       ArcDAT.cs
//! \date       2022 May 24
//! \brief      Pias resource archive.
//
// Copyright (C) 2022-2023 by morkt
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
    enum ResourceType
    {
        Undefined = -1,
        Graphics = 1,
        Sound = 2,
    }

    internal class IndexReader
    {
        internal const bool UseOffsetAsName = true;

        protected ArcView       m_arc;
        protected ResourceType  m_res;
        protected List<Entry>   m_dir;

        public bool IsEncrypted { get; protected set; }

        public IndexReader (ArcView arc, ResourceType res)
        {
            m_arc = arc;
            m_res = res;
            m_dir = null;
        }

        public List<Entry> GetIndex ()
        {
            if (m_res > 0)
            {
                var text_name = VFS.ChangeFileName (m_arc.Name, "text.dat");
                if (!VFS.FileExists (text_name))
                    return null;
                IBinaryStream input = VFS.OpenBinaryStream (text_name);
                try
                {
                    if (DatOpener.EncryptedSignatures.Contains (input.Signature))
                        return null;
                    var reader = new TextReader (input);
                    m_dir = reader.GetResourceList ((int)m_res);
                }
                finally
                {
                    input.Dispose();
                }
            }
            if (null == m_dir)
                m_dir = new List<Entry>();
            if (!FillEntries())
                return null;
            return m_dir;
        }

        protected bool FillEntries ()
        {
            uint header_size = 4;
            string entry_type = "audio";
            if (ResourceType.Graphics == m_res)
            {
                header_size = 8;
                entry_type = "image";
            }
            for (int i = m_dir.Count - 1; i >= 0; --i)
            {
                var entry = m_dir[i];
                entry.Size = m_arc.View.ReadUInt32 (entry.Offset) + header_size;
                entry.Name = i.ToString("D4");
                entry.Type = entry_type;
            }
            var known_offsets = new HashSet<long> (m_dir.Select (e => e.Offset));
            long offset = 0;
            while (offset < m_arc.MaxOffset)
            {
                uint entry_size = m_arc.View.ReadUInt32(offset);
                if (uint.MaxValue == entry_size)
                {
                    entry_size = 4;
                }
                else
                {
                    entry_size += header_size;
                    if (!known_offsets.Contains (offset))
                    {
                        var entry = new Entry {
                            Name = GetName (offset, m_dir.Count),
                            Type = entry_type,
                            Offset = offset,
                            Size = entry_size,
                        };
                        if (!entry.CheckPlacement (m_arc.MaxOffset))
                            return false;
                        m_dir.Add (entry);
                    }
                }
                offset += entry_size;
            }
            return true;
        }

        internal string GetName (long offset, int num)
        {
            return UseOffsetAsName ? offset.ToString ("D8") : num.ToString("D4");
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag => "DAT/PIAS";
        public override string Description => "Pias resource archive";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public DatOpener ()
        {
            Signatures = new[] { 0x0002C026u, 0u };
        }

        internal static readonly HashSet<uint> EncryptedSignatures = new HashSet<uint> { 0x03184767u };

        public override ArcFile TryOpen (ArcView file)
        {
            var arc_name = Path.GetFileName (file.Name).ToLowerInvariant();

            ResourceType resource_type = ResourceType.Undefined;
            if ("sound.dat" == arc_name)
                resource_type = ResourceType.Sound;
            else if ("graph.dat" == arc_name)
                resource_type = ResourceType.Graphics;
            else if ("voice.dat" != arc_name && "music.dat" != arc_name)
                return null;

            var index = new IndexReader (file, resource_type);
            var dir = index.GetIndex();
            if (null == dir)
                return null;
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
            return OpenAudioEntry (arc, entry, format);
        }

        public Stream OpenAudioEntry (ArcFile arc, Entry entry, WaveFormat format)
        {
            uint size = arc.File.View.ReadUInt32 (entry.Offset);
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
