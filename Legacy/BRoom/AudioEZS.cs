//! \file       AudioEZS.cs
//! \date       2019 Jan 04
//! \brief      Studio B-Room audio format.
//
// Copyright (C) 2019 by morkt
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

namespace GameRes.Formats.BRoom
{
    [Export(typeof(AudioFormat))]
    public class EzsAudio : AudioFormat
    {
        public override string         Tag { get { return "EZS"; } }
        public override string Description { get { return "Studio B-Room audio format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        const byte DefaultKey = 0xDD;

        public override SoundInput TryOpen (IBinaryStream file)
        {
            if (!file.Name.HasExtension ("EZS"))
                return null;
            var header = file.ReadHeader (0x1B);
            uint pcm_size = header.ToUInt32 (0);
            if (pcm_size > file.Length)
                return null;
            byte key = header[4];
            var format = new WaveFormat {
                FormatTag = header.ToUInt16 (5),
                Channels = header.ToUInt16 (7),
                SamplesPerSecond = header.ToUInt32 (9),
                AverageBytesPerSecond = header.ToUInt32 (13),
                BlockAlign = header.ToUInt16 (17),
                BitsPerSample = header.ToUInt16 (19),
            };
            ushort cbSize = header.ToUInt16 (21);
//            key ^= DefaultKey;
            key = (byte)cbSize;
            cbSize ^= key;
            format.BitsPerSample ^= key;
            if (format.BitsPerSample != 8 && format.BitsPerSample != 16)
                return null;
            format.AverageBytesPerSecond ^= cbSize;
            format.BlockAlign ^= (ushort)format.AverageBytesPerSecond;
            format.Channels ^= format.BlockAlign;
            if (format.Channels < 1 || format.Channels > 2)
                return null;
            format.SamplesPerSecond ^= format.Channels;
            format.FormatTag ^= format.BitsPerSample;
            var input = new StreamRegion (file.AsStream, 0x1B);
            return new RawPcmInput (input, format);
        }
    }
}
