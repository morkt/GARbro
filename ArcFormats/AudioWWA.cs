//! \file       AudioWWA.cs
//! \date       Mon Aug 10 11:48:29 2015
//! \brief      Wild Bug compressed audio format.
//
// Copyright (C) 2015 by morkt
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
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.WildBug
{
    [Export(typeof(AudioFormat))]
    public class WwaAudio : AudioFormat
    {
        public override string         Tag { get { return "WWA"; } }
        public override string Description { get { return "Wild Bug's compressed audio format"; } }
        public override uint     Signature { get { return 0x1A585057; } } // 'WPX'
        
        public override SoundInput TryOpen (Stream file)
        {
            var header = new byte[0x10];
            if (header.Length != file.Read (header, 0, header.Length))
                return null;
            if (!Binary.AsciiEqual (header, 4, "WAV"))
                return null;
            int count = header[0xE];
            int dir_size = header[0xF];
            if (1 != header[0xC] || 0 == count || 0 == dir_size)
                return null;

            return new WwaInput (file, count, dir_size);
        }

        public override void Write (SoundInput source, Stream output)
        {
            throw new System.NotImplementedException ("WwaFormat.Write not implemenented");
        }
    }

    internal class WwaInput : SoundInput
    {
        public override string SourceFormat { get { return "raw"; } }

        public override int SourceBitrate
        {
            get { return (int)Format.AverageBytesPerSecond * 8; }
        }

        public WwaInput (Stream file, int count, int dir_size) : base (null)
        {
            file.Position = 0x10;
            var header = new byte[count * dir_size];
            if (header.Length != file.Read (header, 0, header.Length))
                throw new InvalidFormatException();

            var section = WpxSection.Find (header, 0x20, count, dir_size);
            if (null == section || section.UnpackedSize < 0x10 || section.DataFormat != 0x80)
                throw new InvalidFormatException();
            file.Position = section.Offset;
            Format = ReadFormat (file);

            section = WpxSection.Find (header, 0x21, count, dir_size);
            if (null == section)
                throw new InvalidFormatException();

            var reader = new WwaReader (file, section);
            var data = reader.Unpack (section.DataFormat);
            if (null == data)
                throw new InvalidFormatException();
            Source = new MemoryStream (data);
            file.Dispose();
        }

        static WaveFormat ReadFormat (Stream file)
        {
            var format = new WaveFormat();
            using (var input = new ArcView.Reader (file))
            {
                format.FormatTag = input.ReadUInt16 ();
                format.Channels = input.ReadUInt16 ();
                format.SamplesPerSecond = input.ReadUInt32 ();
                format.AverageBytesPerSecond = input.ReadUInt32 ();
                format.BlockAlign = input.ReadUInt16 ();
                format.BitsPerSample = input.ReadUInt16 ();
            }
            return format;
        }

        #region IO.Stream methods
        public override long Position
        {
            get { return Source.Position; }
            set { Source.Position = value; }
        }

        public override bool CanSeek { get { return Source.CanSeek; } }

        public override long Seek (long offset, SeekOrigin origin)
        {
            return Source.Seek (offset, origin);
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            return Source.Read (buffer, offset, count);
        }

        public override int ReadByte ()
        {
            return Source.ReadByte();
        }
        #endregion
    }

    internal class WwaReader : WpxDecoder
    {
        public WwaReader (Stream input, WpxSection section) : base (input, section)
        {
            ResetInput();
        }

        public byte[] Unpack (int flags) // sub_46B16C
        {
            if (0 == (flags & 0x80) && 0 != PackedSize)
            {
                if (0 != (flags & 8))
                {
                    if (0 != (flags & 4))
                        throw new NotImplementedException ();
                    else if (0 != (flags & 2))
                        return UnpackVB(); // sub_461B34
                }
                throw new NotImplementedException ();
            }
            else
                return ReadUncompressed();
        }

        byte[] UnpackVB ()
        {
            m_available = FillBuffer();
            if (0 == m_available)
                return null;
            var ref_table = new byte[0x10000];
            int v6 = -1 & 3;
            m_output[0] = m_buffer[0];
            int dst = 1;
            int remaining = m_output.Length - 1;
            m_current = 1 + v6 + 128;

            if (!FillRefTable (ref_table, 1 + v6))
                return null;
            while (remaining > 0)
            {
                while (0 != GetNextBit())
                {
                    int v23 = 0;
                    int v24 = 0;
                    int v25 = 16384;
                    for (;;)
                    {
                        ++v23;
                        if (0 != GetNextBit())
                            v24 |= v25;
                        if (ref_table[2 * v24] == v23)
                            break;
                        v25 >>= 1;
                        if (0 == v25)
                            return null;
                    }
                    m_output[dst++] = ref_table[2 * v24 + 1];
                    --remaining;
                    if (0 == remaining)
                        return m_output;
                }
                int v26 = ReadNext();
                int src_offset = dst - v26 - 1;
                int count = 2;
                if (0 == GetNextBit())
                {
                    count += ReadCount();
                }
                if (remaining < count)
                    return null;
                Binary.CopyOverlapped (m_output, src_offset, dst, count);
                dst += count;
                remaining -= count;
            }
            return m_output;
        }
    }
}
