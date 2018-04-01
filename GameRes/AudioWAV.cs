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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using NAudio.Wave;

namespace GameRes
{
    public class WaveInput : SoundInput
    {
        WaveStream  m_reader;

        public override long Position
        {
            get { return m_reader.Position; }
            set { m_reader.Position = value; }
        }

        public override bool CanSeek { get { return m_reader.CanSeek; } }

        public override int SourceBitrate
        {
            get { return (int)Format.AverageBytesPerSecond * 8; }
        }

        public override string SourceFormat { get { return "wav"; } }

        static readonly HashSet<WaveFormatEncoding> ConversionRequired = new HashSet<WaveFormatEncoding> {
            WaveFormatEncoding.Adpcm,       // 2
            WaveFormatEncoding.MuLaw,       // 7
            WaveFormatEncoding.DviAdpcm,    // 0x11
        };

        public WaveInput (Stream file) : base (file)
        {
            m_reader = new WaveFileReader (file);
            var wf = m_reader.WaveFormat;
            if (ConversionRequired.Contains (wf.Encoding))
            {
                var wav = WaveFormatConversionStream.CreatePcmStream (m_reader);
                wf = wav.WaveFormat;
                m_reader = wav;
            }
            var format = new GameRes.WaveFormat();
            format.FormatTag                = (ushort)wf.Encoding;
            format.Channels                 = (ushort)wf.Channels;
            format.SamplesPerSecond         = (uint)wf.SampleRate;
            format.BitsPerSample            = (ushort)wf.BitsPerSample;
            format.BlockAlign               = (ushort)wf.BlockAlign;
            format.AverageBytesPerSecond    = (uint)wf.AverageBytesPerSecond;
            this.Format = format;
            this.PcmSize = m_reader.Length;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            return m_reader.Read (buffer, offset, count);
        }

        #region IDisposable Members
        protected override void Dispose (bool disposing)
        {
            if (null != m_reader)
            {
                if (disposing)
                {
                    m_reader.Dispose();
                }
                m_reader = null;
                base.Dispose (disposing);
            }
        }
        #endregion
    }

    [Export(typeof(AudioFormat))]
    public class WaveAudio : AudioFormat
    {
        public override string         Tag { get { return "WAV"; } }
        public override string Description { get { return "Wave audio format"; } }
        public override uint     Signature { get { return 0x46464952; } } // 'RIFF'
        public override bool      CanWrite { get { return true; } }

        static readonly HashSet<ushort> EmbeddedFormats = new HashSet<ushort> {
            0x674f, 0x6751, 0x6771, // Vorbis
            0x0055, // MpegLayer3
        };

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x16);
            if (!header.AsciiEqual (8, "WAVE"))
                return null;
            ushort tag = header.ToUInt16 (0x14);
            if (0xFFFF == tag || 0x676F == tag || 0x6770 == tag || 0x674F == tag)
                return null;
            file.Position = 0;
            SoundInput sound = new WaveInput (file.AsStream);
            if (EmbeddedFormats.Contains (sound.Format.FormatTag))
            {
                var bin = new BinaryStream (sound, file.Name);
                try
                {
                    var embedded = AudioFormat.Read (bin);
                    if (null != embedded)
                    {
                        sound = embedded;
                    }
                }
                catch
                {
                    sound.Position = 0;
                }
            }
            return sound;
        }

        public override void Write (SoundInput source, Stream output)
        {
            WriteRiffHeader (output, source.Format, (uint)source.PcmSize);
            source.Position = 0;
            source.CopyTo (output);
        }

        public static void WriteRiffHeader (Stream output, WaveFormat format, uint pcm_size)
        {
            using (var buffer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                uint total_size = (uint)(0x24 + pcm_size);
                buffer.Write (Wav.Signature);
                buffer.Write (total_size);
                buffer.Write (0x45564157); // 'WAVE'
                buffer.Write (0x20746d66); // 'fmt '
                buffer.Write (0x10);
                buffer.Write (format.FormatTag);
                buffer.Write (format.Channels);
                buffer.Write (format.SamplesPerSecond);
                buffer.Write (format.AverageBytesPerSecond);
                buffer.Write (format.BlockAlign);
                buffer.Write (format.BitsPerSample);
                buffer.Write (0x61746164); // 'data'
                buffer.Write (pcm_size);
            }
        }
    }
}
