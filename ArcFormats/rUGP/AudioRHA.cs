//! \file       AudioRHA.cs
//! \date       Tue Nov 08 16:33:10 2016
//! \brief      rUGP engine audio object.
//
// Copyright (C) 2016-2017 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Rugp
{
    [Export(typeof(AudioFormat))]
    public class RhaAudio : AudioFormat
    {
        public override string         Tag { get { return "RHA"; } }
        public override string Description { get { return "rUGP engine compressed audio (MP3)"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            ushort schema = Binary.BigEndian (file.ReadUInt16());
            if (4 == (schema & 0x10F))
            {
                file.Position = 0;
                schema = 0;
            }
            else if (0x10B != schema)
                return null;
            var mp3 = new MemoryStream();
            try
            {
                if (!ConvertToMp3 (file, mp3, schema))
                {
                    mp3.Dispose();
                    return null;
                }
                mp3.Position = 0;
                var sound = new Mp3Input (mp3);
                file.Dispose();
                return sound;
            }
            catch
            {
                mp3.Dispose();
                throw;
            }
        }

        bool ConvertToMp3 (IBinaryStream input, Stream mp3, ushort schema)
        {
            byte[] frame_buffer = null;
            using (var output = new BinaryWriter (mp3, System.Text.Encoding.Default, true))
            {
                while (input.PeekByte() != -1)
                {
                    ushort rha_header = Binary.BigEndian (input.ReadUInt16());
                    uint header;
                    int add_len = 0;
                    byte add_value = 0;
                    if (0 == schema)
                    {
                        header = 0xFFFB0000u | rha_header;
                    }
                    else
                    {
                        if (0 != (rha_header & 0x1000)) // RHAF_LASTZEROADD
                        {
                            add_len = input.ReadUInt16();
                            add_value = 0;
                        }
                        else if (0 != (rha_header & 0x2000)) // RHAF_LASTFULLADD
                        {
                            add_len = input.ReadUInt16();
                            add_value = 0xFF;
                        }
                        header = RhaToMp3Header (rha_header);
                    }
                    int frame_length = GetFrameLength (header);
                    if (0 == frame_length || add_len > frame_length)
                        return false;
                    if (null == frame_buffer || frame_length > frame_buffer.Length)
                        frame_buffer = new byte[frame_length];

                    int read_length = frame_length - add_len;
                    if (read_length != input.Read (frame_buffer, 0, read_length))
                        break;
                    for (int i = 0; i < add_len; ++i)
                        frame_buffer[read_length+i] = add_value;

                    output.Write (Binary.BigEndian (header));
                    output.Write (frame_buffer, 0, frame_length);

                    if (0 == (header & (1 << 16))) // CRC bit
                        output.Write (input.ReadUInt16());
                }
            }
            return mp3.Length > 0;
        }

        internal static uint RhaToMp3Header (uint header)
        {
		    return (header & 0xF) << 4 | (header & 0x7F0) << 5 | (header & 0x800) << 8 | 0xFFF30004;
        }

        internal static int GetFrameLength (uint header)
        {
            int lsf, freq;
            if (0 == (header & (1 << 20)))
            {
                lsf = 1;
                freq = (int)((header >> 10 ) & 3) + 6;
            }
            else
            {
                lsf = (int)~(header >> 19) & 1;
                freq = (int)((header >> 10 ) & 3) + (lsf * 3);
            }
            int bitrate_index = (int)((header >> 12) & 0xF);
            if (0 == bitrate_index || 0xF == bitrate_index)
                return 0;
            int padding = (int)((header >> 9) & 1);
            int frame_length = BitRates[lsf, bitrate_index] * 144000;
            frame_length /= Mp3Freqs[freq] << lsf;
            frame_length += padding - 4;
            return frame_length;
        }

        static readonly int[] Mp3Freqs = { 44100, 48000, 32000, 22050, 24000, 16000, 11025, 12000, 8000 };

        static readonly short[,] BitRates = {
            { 0, 32, 40, 48, 56, 64, 80, 96,112,128,160,192,224,256,320 },
            { 0,  8, 16, 24, 32, 40, 48, 56, 64, 80, 96,112,128,144,160 }
        };
    }
}
