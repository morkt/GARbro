//! \file       AudioPCM.cs
//! \date       Mon Oct 03 22:21:15 2016
//! \brief      Ice Soft PCM audio file.
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

using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Ice
{
    [Export(typeof(AudioFormat))]
    public class IceAudio : AudioFormat
    {
        public override string         Tag { get { return "PCM/ICE"; } }
        public override string Description { get { return "Ice Soft PCM audio"; } }
        public override uint     Signature { get { return 0; } }

        public IceAudio ()
        {
            Extensions = new string[] { "" };
            Signatures = new uint[] { 0x010001, 0x020001 };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x16);
            ushort tag = header.ToUInt16 (0);
            if (tag != 1)
                return null;
            int extra_size = header.ToUInt16 (0x10);
            if (0 != extra_size)
                return null;
            uint pcm_size = header.ToUInt32 (0x12);
            if (pcm_size + 0x16 != file.Length)
                return null;
            var format = new WaveFormat { FormatTag = tag };
            format.Channels = header.ToUInt16 (2);
            if (format.Channels != 1 && format.Channels != 2)
                return null;
            format.SamplesPerSecond     = header.ToUInt32 (4);
            format.AverageBytesPerSecond = header.ToUInt32 (8);
            format.BlockAlign           = header.ToUInt16 (0xC);
            format.BitsPerSample        = header.ToUInt16 (0xE);
            if (0 == format.AverageBytesPerSecond
                || format.SamplesPerSecond * format.BlockAlign != format.AverageBytesPerSecond)
                return null;
            var pcm = new StreamRegion (file.AsStream, 0x16, pcm_size);
            return new RawPcmInput (pcm, format);
        }
    }
}
