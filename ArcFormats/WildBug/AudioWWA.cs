//! \file       AudioWWA.cs
//! \date       Mon Aug 10 11:48:29 2015
//! \brief      Wild Bug compressed audio format.
//
// Copyright (C) 2015-2016 by morkt
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

            file.Position = 0x10;
            header = new byte[count * dir_size];
            if (header.Length != file.Read (header, 0, header.Length))
                throw new InvalidFormatException();

            var section = WpxSection.Find (header, 0x20, count, dir_size);
            if (null == section || section.UnpackedSize < 0x10 || section.DataFormat != 0x80)
                throw new InvalidFormatException();
            var fmt = new byte[section.UnpackedSize];
            file.Position = section.Offset;
            file.Read (fmt, 0, section.UnpackedSize);

            section = WpxSection.Find (header, 0x21, count, dir_size);
            if (null == section)
                throw new InvalidFormatException();

            var reader = new WwaReader (file, section);
            var data = reader.Unpack (section.DataFormat);
            if (null == data)
                throw new InvalidFormatException();

            int total_size = 20 + fmt.Length + data.Length;
            using (var wav_file = new MemoryStream (20 + fmt.Length))
            using (var wav = new BinaryWriter (wav_file))
            {
                wav.Write (Wav.Signature);
                wav.Write (total_size);
                wav.Write (0x45564157); // 'WAVE'
                wav.Write (0x20746d66); // 'fmt '
                wav.Write (fmt.Length);
                wav.Write (fmt);
                wav.Write (0x61746164); // 'data'
                wav.Write (data.Length);
                var wav_header = wav_file.ToArray();
                var data_stream = new MemoryStream (data);
                var source = new PrefixStream (wav_header, data_stream);
                var sound = new WaveInput (source);
                file.Dispose();
                return sound;
            }
        }

        public override void Write (SoundInput source, Stream output)
        {
            throw new System.NotImplementedException ("WwaFormat.Write not implemenented");
        }
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
                else if (0 != (flags & 4))
                    throw new NotImplementedException ();
                else if (0 != (flags & 2))
                    return UnpackV2();
                throw new NotImplementedException ();
            }
            else
                return ReadUncompressed();
        }

        byte[] UnpackV2 () // 0x02 format
        {
            m_available = FillBuffer();
            if (0 == m_available)
                return null;

            int step = 4;
            if (m_available < step + 0x80)
                return null;

            int v9 = 3;
            int dst = 0;
            m_output[dst++] = m_buffer[0];
            int remaining = m_output.Length - 1;
            m_current = 1 + v9 + 128; // within m_buffer

            var ref_table = new byte[0x10000];
            if (!FillRefTable (ref_table, 1 + v9))
                return null;
            while (remaining > 0)
            {
                while (0 != GetNextBit())
                {
                    int v20 = 0;
                    int v21 = 0;
                    v9 = 16384;
                    for (;;)
                    {
                        ++v20;
                        if (0 != GetNextBit())
                            v21 |= v9;
                        if (ref_table[2 * v21] == v20)
                            break;
                        v9 >>= 1;
                        if (0 == v9)
                            return null;
                    }
                    m_output[dst++] = ref_table[2 * v21 + 1];
                    --remaining;
                    if (0 == remaining)
                        return m_output;
                }
                int offset;
                int count = 2;
                if (0 != GetNextBit())
                {
                    offset = ReadNext();
                }
                else
                {
                    offset  = ReadNext();
                    offset |= ReadNext() << 8;
                    count = 3;
                }
                if (0 == GetNextBit())
                {
                    count += ReadCount();
                }
                if (remaining < count)
                    return null;
                Binary.CopyOverlapped (m_output, dst - offset - 1, dst, count);
                dst += count;
                remaining -= count;
            }
            return m_output;
        }

        byte[] UnpackVB ()
        {
            m_available = FillBuffer();
            if (0 == m_available)
                return null;
            int v6 = 3;
            int dst = 0;
            m_output[dst++] = m_buffer[0];
            int remaining = m_output.Length - 1;
            m_current = 1 + v6 + 128;

            var ref_table = new byte[0x10000];
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
