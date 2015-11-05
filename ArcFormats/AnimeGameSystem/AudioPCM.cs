//! \file       AudioPCM.cs
//! \date       Thu Nov 05 05:33:48 2015
//! \brief      AnimeGameSystem raw PCM audio.
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

namespace GameRes.Formats.Ags
{
    [Export(typeof(AudioFormat))]
    public class PcmAudio : WaveAudio
    {
        public override string         Tag { get { return "PCM/AGS"; } }
        public override string Description { get { return "AnimeGameSystem PCM audio"; } }
        public override uint     Signature { get { return 0; } }

        public PcmAudio ()
        {
            Extensions = new string[] { "pcm" };
        }

        public override SoundInput TryOpen (Stream file)
        {
            uint signature = FormatCatalog.ReadSignature (file) & 0xFFFFFF;
            if (0x564157 != signature) // 'WAV'
                return null;
            return new PcmInput (file);
        }
    }

    public class PcmInput : SoundInput
    {
        public override string SourceFormat { get { return "raw"; } }

        public override int SourceBitrate
        {
            get { return (int)Format.AverageBytesPerSecond * 8; }
        }

        public PcmInput (Stream file) : base (null)
        {
            file.Position = 3;
            int type = file.ReadByte();
            int data_length = (int)file.Length - 4;
            var format = new WaveFormat();
            format.FormatTag                = 1;
            format.Channels                 = (ushort)((type & 1) + 1);
            type &= ~1;
            if (0xA == type)
            {
                format.SamplesPerSecond     = 44100;
                format.BitsPerSample        = 16;
            }
            else if (6 == type)
            {
                format.SamplesPerSecond     = 22050;
                format.BitsPerSample        = 16;
            }
            else if (4 == type)
            {
                format.SamplesPerSecond     = 22050;
                format.BitsPerSample        = 8;
            }
            else
                throw new NotSupportedException ("Not supported PCM format");
            format.BlockAlign               = (ushort)(format.Channels*format.BitsPerSample/8);
            format.AverageBytesPerSecond    = format.SamplesPerSecond*format.BlockAlign;
            this.Format = format;
            this.PcmSize = data_length;
            this.Source = new StreamRegion (file, 4, data_length);
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
}
