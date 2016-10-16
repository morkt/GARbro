//! \file       AudioVAW.cs
//! \date       Sat Aug 01 12:28:22 2015
//! \brief      Black Cyc audio file.
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
using System.Linq;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.BlackCyc
{
    [Export(typeof(AudioFormat))]
    public class VawAudio : AudioFormat
    {
        public override string         Tag { get { return "VAW"; } }
        public override string Description { get { return "Black Cyc audio format"; } }
        public override uint     Signature { get { return 0; } }

        public VawAudio ()
        {
            Extensions = new string[] { "vaw", "wgq" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = ResourceHeader.Read (file);
            if (null == header)
                return null;
            AudioFormat format;
            int offset;
            if (0 == header.PackType)
            {
                if (4 != file.Read (header.Bytes, 0, 4))
                    return null;
                if (!Binary.AsciiEqual (header.Bytes, "RIFF"))
                    return null;
                format = Wav;
                offset = 0x40;
            }
            else if (1 == header.PackType)
            {
                return Unpack (file);
            }
            else if (2 == header.PackType)
            {
                format = OggAudio.Instance;
                offset = 0x6C;
            }
            else if (6 == header.PackType && Binary.AsciiEqual (header.Bytes, 0x10, "OGG "))
            {
                format = OggAudio.Instance;
                offset = 0x40;
            }
            else
                return null;
            var input = new StreamRegion (file.AsStream, offset, file.Length-offset);
            return format.TryOpen (new BinaryStream (input, file.Name));
        }

        public override void Write (SoundInput source, Stream output)
        {
            throw new System.NotImplementedException ("EdimFormat.Write not implemenented");
        }

        SoundInput Unpack (IBinaryStream input)
        {
            input.Position = 0x40;
            var header = new byte[0x24];
            if (0x14 != input.Read (header, 0, 0x14))
                return null;
            int fmt_size = LittleEndian.ToInt32 (header, 0x10);
            if (fmt_size + input.Position > input.Length)
                return null;
            int header_size = fmt_size + 0x14;
            if (header_size > header.Length)
                Array.Resize (ref header, header_size);
            if (fmt_size != input.Read (header, 0x14, fmt_size))
                return null;
            int riff_size = LittleEndian.ToInt32 (header, 4) + 8;
            int data_size = riff_size - header_size;
            var pcm = new MemoryStream (riff_size);
            try
            {
                pcm.Write (header, 0, header_size);
                using (var output = new BinaryWriter (pcm, Encoding.Default, true))
                using (var bits = new LsbBitStream (input.AsStream, true))
                {
                    int written = 0;
                    short sample = 0;
                    while (written < data_size)
                    {
                        int c = bits.GetBits (4);
                        if (-1 == c)
                            c = 0;
                        int code = 0;
                        if (c > 0)
                            code = bits.GetBits (c) << (32 - c);
                        code >>= 32 - c;
                        int sign = code >> 31;
                        code ^= 0x4000 >> (15 - c);
                        code -= sign;
                        sample += (short)code;
                        output.Write (sample);
                        written += 2;
                    }
                }
                pcm.Position = 0;
                var sound = Wav.TryOpen (new BinMemoryStream (pcm, input.Name));
                if (sound != null)
                    input.Dispose();
                else
                    pcm.Dispose();
                return sound;
            }
            catch
            {
                pcm.Dispose();
                throw;
            }
        }
    }
}
