//! \file       AudioWAV.cs
//! \date       Tue Nov 04 18:22:37 2014
//! \brief      WAVE audio format implementation.
//
// Copyright (C) 2014-2015 by morkt
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

using System.ComponentModel.Composition;
using System.IO;
using System.Text;

namespace GameRes
{
    public class WaveInput : SoundInput
    {
        long        m_data_offset;

        public override long Position
        {
            get { return m_input.Position - m_data_offset; }
            set { m_input.Position = m_data_offset + value; }
        }

        public override bool CanSeek { get { return m_input.CanSeek; } }

        public override int SourceBitrate
        {
            get { return (int)Format.AverageBytesPerSecond * 8; }
        }

        public WaveInput (Stream file) : base (file)
        {
            using (var input = new BinaryReader (m_input, System.Text.Encoding.UTF8, true))
            {
                input.BaseStream.Seek (8, SeekOrigin.Current);
                uint header = input.ReadUInt32();
                if (header != 0x45564157)
                    throw new InvalidFormatException ("Invalid WAVE file format.");

                bool found_fmt = false;
                bool found_data = false;
                long current_offset = input.BaseStream.Position;

                while (!found_fmt || !found_data)
                {
                    uint header0 = input.ReadUInt32();
                    uint header1 = input.ReadUInt32();

                    if (!found_fmt && 0x20746d66 == header0)
                    {
                        if (header1 < 0x10)
                            throw new InvalidFormatException ("Invalid WAVE file format");

                        ushort tag = input.ReadUInt16();
                        var format = new WaveFormat();
                        format.FormatTag                = tag;
                        format.Channels                 = input.ReadUInt16();
                        format.SamplesPerSecond         = input.ReadUInt32();
                        format.AverageBytesPerSecond    = input.ReadUInt32();
                        format.BlockAlign               = input.ReadUInt16();
                        format.BitsPerSample            = input.ReadUInt16();
                        format.ExtraSize                = input.ReadUInt16();
                        this.Format = format;

                        found_fmt = true;
                        current_offset += 8 + ((header1 + 1) & ~1);
                        input.BaseStream.Seek (current_offset, SeekOrigin.Begin);
                        continue;
                    }
                    if (!found_data && 0x61746164 == header0)
                    {
                        found_data = true;
                        m_data_offset = current_offset + 8;
                        this.PcmSize = header1;
                        if (found_fmt)
                            break;
                    }
                    long chunk_size = (header1 + 1) & ~1;
                    input.BaseStream.Seek (chunk_size, SeekOrigin.Current);

                    current_offset += 8 + chunk_size;
                }
                this.Reset();
            }
        }

        public override void Reset ()
        {
            m_input.Seek (m_data_offset, SeekOrigin.Begin);
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            if (SeekOrigin.Begin == origin)
                offset += m_data_offset;
            else if (SeekOrigin.Current == origin)
                offset = m_input.Position + offset;
            else if (SeekOrigin.End == origin)
                offset = m_data_offset + PcmSize + offset;

            if (offset < m_data_offset)
                offset = m_data_offset;
            else if (offset > m_data_offset + PcmSize)
                offset = m_data_offset + PcmSize;

            offset = m_input.Seek (offset, SeekOrigin.Begin);
            return offset - m_data_offset;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            long remaining = PcmSize - Position;
            if (count > remaining)
                count = (int)remaining;
            return m_input.Read (buffer, offset, count);
        }

        public override int ReadByte ()
        {
            if (Position < PcmSize)
                return m_input.ReadByte();
            else
                return -1;
        }
    }

    [Export(typeof(AudioFormat))]
    public class WaveAudio : AudioFormat
    {
        public override string         Tag { get { return "WAV"; } }
        public override string Description { get { return "Wave audio format"; } }
        public override uint     Signature { get { return 0x46464952; } } // 'RIFF'

        public override SoundInput TryOpen (Stream file)
        {
            SoundInput sound = new WaveInput (file);
            if (0x674f == sound.Format.FormatTag || 0x6771 == sound.Format.FormatTag)
            {
                try
                {
                    var ogg = AudioFormat.Read (sound);
                    if (null != ogg)
                    {
                        sound.Dispose();
                        sound = ogg;
                    }
                }
                catch { /* ignore errors */ }
            }
            return sound;
        }

        public override void Write (SoundInput source, Stream output)
        {
            using (var buffer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                uint total_size = (uint)(0x2e + source.PcmSize);
                buffer.Write (Signature);
                buffer.Write (total_size);
                buffer.Write (0x45564157); // 'WAVE'
                buffer.Write (0x20746d66); // 'fmt '
                buffer.Write (0x12);
                buffer.Write (source.Format.FormatTag);
                buffer.Write (source.Format.Channels);
                buffer.Write (source.Format.SamplesPerSecond);
                buffer.Write (source.Format.AverageBytesPerSecond);
                buffer.Write (source.Format.BlockAlign);
                buffer.Write (source.Format.BitsPerSample);
                buffer.Write ((ushort)0);
                buffer.Write (0x61746164); // 'data'
                buffer.Write (source.PcmSize);
                source.Position = 0;
                source.CopyTo (output);
            }
        }
    }
}
