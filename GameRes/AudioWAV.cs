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
using NAudio.Wave;

namespace GameRes
{
    public class WaveInput : SoundInput
    {
        public override long Position
        {
            get { return m_input.Position; }
            set { m_input.Position = value; }
        }

        public override bool CanSeek { get { return m_input.CanSeek; } }

        public override int SourceBitrate
        {
            get { return (int)Format.AverageBytesPerSecond * 8; }
        }

        public WaveInput (Stream file) : base (file)
        {
            var reader = new WaveFileReader (file);
            m_input = reader;
            var wf = reader.WaveFormat;
            if (WaveFormatEncoding.Adpcm == wf.Encoding || WaveFormatEncoding.MuLaw == wf.Encoding) // 2 || 7
            {
                var wav = WaveFormatConversionStream.CreatePcmStream (reader);
                wf = wav.WaveFormat;
                m_input = wav;
            }
            var format = new GameRes.WaveFormat();
            format.FormatTag                = (ushort)wf.Encoding;
            format.Channels                 = (ushort)wf.Channels;
            format.SamplesPerSecond         = (uint)wf.SampleRate;
            format.BitsPerSample            = (ushort)wf.BitsPerSample;
            format.BlockAlign               = (ushort)wf.BlockAlign;
            format.AverageBytesPerSecond    = (uint)wf.AverageBytesPerSecond;
            this.Format = format;
            this.PcmSize = m_input.Length;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            return m_input.Read (buffer, offset, count);
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
