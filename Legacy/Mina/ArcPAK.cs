//! \file       ArcPAK.cs
//! \date       2023 Oct 09
//! \brief      Mina resource archive.
//
// Copyright (C) 2023 by morkt
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
using System.IO;
using System.Text;
using System.Windows.Media;

// [010223][Mina] Storia ~Ouma no Mori no Himegimi-tachi~

namespace GameRes.Formats.Mina
{
    [Export(typeof(ArchiveFormat))]
    public class BmpPakOpener : ArchiveFormat
    {
        public override string         Tag => "PAK/MINA/BMP";
        public override string Description => "Mina bitmap archive";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".PAK"))
                return null;
            int pos;
            for (pos = 0; pos < 0x10; ++pos)
            {
                if (0 == file.View.ReadByte (pos))
                    break;
            }
            if (pos >= 0x10 || pos <= 4 || !file.View.AsciiEqual (pos-4, ".BMP"))
                return null;
            using (var input = file.CreateStream())
            {
                var dir = new List<Entry>();
                while (input.PeekByte() != -1)
                {
                    var name = input.ReadCString();
                    if (name.Length > 0x10)
                        return null;
                    var entry = Create<Entry> (name);
                    entry.Offset = input.Position;
                    input.Seek (5, SeekOrigin.Current);
                    uint size = input.ReadUInt32();
                    entry.Size = size + 9;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    input.Seek (size, SeekOrigin.Current);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new BitmapDecoder (input);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class WavPakOpener : ArchiveFormat
    {
        public override string         Tag => "PAK/MINA/WAV";
        public override string Description => "Mina audio archive";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".PAK"))
                return null;
            int pos;
            for (pos = 4; pos < 0x14; ++pos)
            {
                if (0 == file.View.ReadByte (pos))
                    break;
            }
            if (pos >= 0x14 || pos <= 8 || !file.View.AsciiEqual (pos-4, ".WAV"))
                return null;
            using (var input = file.CreateStream())
            {
                var dir = new List<Entry>();
                while (input.PeekByte() != -1)
                {
                    uint data_size = input.ReadUInt32();
                    var name = input.ReadCString();
                    if (name.Length > 0x10)
                        return null;
                    var entry = Create<Entry> (name);
                    entry.Offset = input.Position;
                    uint fmt_size = input.ReadUInt32();
                    if (fmt_size < 0x10)
                        return null;
                    entry.Size = data_size + fmt_size + 4;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    input.Seek (data_size + fmt_size, SeekOrigin.Current);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            uint fmt_size = arc.File.View.ReadUInt32 (entry.Offset);
            uint pcm_size = entry.Size - 4 - fmt_size;
            using (var mem = new MemoryStream ((int)fmt_size))
            {
                using (var buffer = new BinaryWriter (mem, Encoding.ASCII, true))
                {
                    buffer.Write (AudioFormat.Wav.Signature);
                    buffer.Write (entry.Size+0x10);
                    buffer.Write (0x45564157); // 'WAVE'
                    buffer.Write (0x20746d66); // 'fmt '
                    buffer.Write (fmt_size);
                    var fmt = arc.File.View.ReadBytes (entry.Offset+4, fmt_size);
                    buffer.Write (fmt, 0, fmt.Length);
                    buffer.Write (0x61746164); // 'data'
                    buffer.Write (pcm_size);
                }
                var header = mem.ToArray();
                var data = arc.File.CreateStream (entry.Offset+4+fmt_size, pcm_size);
                return new PrefixStream (header, data);
            }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class ScriptPakOpener : ArchiveFormat
    {
        public override string         Tag => "PAK/MINA/SPT";
        public override string Description => "Mina scripts archive";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public ScriptPakOpener ()
        {
            ContainedFormats = new[] { "SCR" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!VFS.IsPathEqualsToFileName (file.Name, "SCRIPT.PAK"))
                return null;
            using (var input = file.CreateStream())
            {
                var dir = new List<Entry>();
                while (input.PeekByte() != -1)
                {
                    var name = input.ReadCString();
                    if (name.Length > 0x10)
                        return null;
                    var entry = Create<Entry> (name);
                    entry.Size = input.ReadUInt32();
                    entry.Offset = input.Position;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    input.Seek (entry.Size, SeekOrigin.Current);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            var mem = new MemoryStream (data.Length);
            int pos = 0;
            while (pos < data.Length)
            {
                int len = data[pos]+1;
                int num = data.ToUInt16 (1);
                pos += 3;
                for (int j = 0; j < len; ++j)
                    data[pos+j] = Binary.RotByteR (data[pos+j], 4);
                mem.Write (data, pos, len);
                mem.WriteByte (0xD);
                mem.WriteByte (0xA);
                pos += len;
            }
            mem.Position = 0;
            return mem;
        }
    }

    internal class BmpMetaData : ImageMetaData
    {
        public byte Flags;
        public bool IsCompressed => (Flags & 1) != 0;
    }

    internal class BitmapDecoder : IImageDecoder
    {
        IBinaryStream   m_input;
        BmpMetaData     m_info;
        ImageData       m_image;

        public Stream            Source => m_input.AsStream;
        public ImageFormat SourceFormat => null;
        public ImageMetaData       Info => m_info;
        public ImageData          Image => m_image ?? (m_image = Unpack());

        public BitmapDecoder (IBinaryStream input)
        {
            m_input = input;
            m_info = new BmpMetaData {
                Width  = input.ReadUInt16(),
                Height = input.ReadUInt16(),
                Flags  = input.ReadUInt8(),
            };
            m_info.BPP = m_info.IsCompressed ? 32 : 24;
        }

        ImageData Unpack ()
        {
            m_input.Position = 9;
            if (m_info.IsCompressed)
            {
                return RleUnpack();
            }
            else
            {
                int bitmap_size = m_info.iWidth * 3 * m_info.iHeight;
                var pixels = m_input.ReadBytes (bitmap_size);
                return ImageData.Create (m_info, PixelFormats.Rgb24, null, pixels);
            }
        }

        ImageData RleUnpack ()
        {
            int stride = m_info.iWidth * 4;
            var output = new byte[stride * m_info.iHeight];
            byte alpha = 0;
            int count = 0;
            int dst = 0;
            while (dst < output.Length)
            {
                if (--count <= 0)
                {
                    alpha = m_input.ReadUInt8();
                    count = m_input.ReadUInt8();
                }
                if (alpha != 0)
                {
                    output[dst+2] = m_input.ReadUInt8();
                    output[dst+1] = m_input.ReadUInt8();
                    output[dst  ] = m_input.ReadUInt8();
                    output[dst+3] = alpha;
                }
                dst += 4;
            }
            return ImageData.Create (m_info, PixelFormats.Bgra32, null, output, stride);
        }

        #region IDisposable members
        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }
}
