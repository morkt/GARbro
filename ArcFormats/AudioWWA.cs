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

    internal class WwaReader
    {
        Stream      m_input;
        int         m_packed_size;
        byte[]      m_output;

        public byte[] Data { get { return m_output; } }

        public WwaReader (Stream input, WpxSection section)
        {
            m_input = input;
            m_input.Position = section.Offset;
            m_packed_size = section.PackedSize;
            m_output = new byte[section.UnpackedSize];
        }

        public byte[] Unpack (int format)
        {
            throw new NotImplementedException();
            // sub_46B9B7 ??
        }
    }
}
