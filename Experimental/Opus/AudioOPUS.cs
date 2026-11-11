//! \file       AudioOPUS.cs
//! \date       Thu Dec 15 11:38:40 2016
//! \brief      Handler for Ogg/Opus containers.
//
// Copyright (C) 2016 by morkt
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
using Concentus;
using Concentus.Oggfile;
using Concentus.Structs;

namespace GameRes.Formats.Opus
{
    [Export(typeof(AudioFormat))]
    public class OpusAudio : AudioFormat
    {
        public override string         Tag { get { return "OPUS"; } }
        public override string Description { get { return "Ogg/Opus audio format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            if (file.Signature != 0x5367674F) // 'OggS'
                return null;
            var header = file.ReadHeader (0x1C);
            int table_size = header[0x1A];
            if (table_size < 1)
                return null;
            int header_size = header[0x1B];
            if (header_size < 0x10)
                return null;
            int header_pos = 0x1B + table_size;
            header = file.ReadHeader (header_pos + header_size);
            if (!header.AsciiEqual (header_pos, "OpusHead"))
                return null;
            int channels = header[header_pos+9];
//            int rate = header.ToInt32 (header_pos+0xC);
            int rate = 48000;
            file.Position = 0;
            var decoder = OpusCodecFactory.CreateDecoder (rate, channels);
            var ogg_in = new OpusOggReadStream (decoder, file.AsStream);
            var pcm = new MemoryStream();
            try
            {
                using (var output = new BinaryWriter (pcm, System.Text.Encoding.UTF8, true))
                {
                    while (ogg_in.HasNextPacket)
                    {
                        var packet = ogg_in.DecodeNextPacket();
                        if (packet != null)
                        {
                            for (int i = 0; i < packet.Length; ++i)
                                output.Write (packet[i]);
                        }
                    }
                }
                var format = new WaveFormat
                {
                    FormatTag = 1,
                    Channels = (ushort)channels,
                    SamplesPerSecond = (uint)rate,
                    BitsPerSample = 16,
                };
                format.BlockAlign = (ushort)(format.Channels*format.BitsPerSample/8);
                format.AverageBytesPerSecond = format.SamplesPerSecond*format.BlockAlign;
                pcm.Position = 0;
                var sound = new RawPcmInput (pcm, format);
                file.Dispose();
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
