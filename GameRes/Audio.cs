//! \file       Audio.cs
//! \date       Tue Nov 04 17:35:54 2014
//! \brief      audio format class.
//
// Copyright (C) 2014 by morkt
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
using System.IO;
using System.Linq;

namespace GameRes
{
    public struct WaveFormat
    {
        public ushort FormatTag;
        public ushort Channels;
        public   uint SamplesPerSecond;
        public   uint AverageBytesPerSecond;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;

        public void SetBPS ()
        {
            AverageBytesPerSecond = (uint)(SamplesPerSecond * Channels * BitsPerSample / 8);
        }
    }

    public abstract class SoundInput : Stream
    {
        public abstract int   SourceBitrate { get; }
        public abstract string SourceFormat { get; }

        public WaveFormat Format { get; protected set; }
        public Stream     Source { get; protected set; }
        public long      PcmSize { get; protected set; }


        protected SoundInput (Stream input)
        {
            Source = input;
        }

        public virtual void Reset ()
        {
            Position = 0;
        }

        #region System.IO.Stream methods
        public override bool  CanRead { get { return Source.CanRead; } }
        public override bool CanWrite { get { return false; } }
        public override long   Length { get { return PcmSize; } }

        public override void Flush()
        {
            Source.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Begin)
                Position = offset;
            else if (origin == SeekOrigin.Current)
                Position += offset;
            else
                Position = Length + offset;
            return Position;
        }

        public override void SetLength (long length)
        {
            throw new System.NotSupportedException ("SoundInput.SetLength method is not supported");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new System.NotSupportedException ("SoundInput.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new System.NotSupportedException ("SoundInput.WriteByte method is not supported");
        }
        #endregion

        #region IDisposable Members
        bool disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    Source.Dispose();
                }
                disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }

    /// <summary>
    /// Class representing raw PCM sound input.
    /// </summary>
    public class RawPcmInput : SoundInput
    {
        public override string SourceFormat { get { return "raw"; } }

        public override int SourceBitrate
        {
            get { return (int)Format.AverageBytesPerSecond * 8; }
        }

        public RawPcmInput (Stream file, WaveFormat format) : base (file)
        {
            this.Format = format;
            this.PcmSize = file.Length;
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

    public abstract class AudioFormat : IResource
    {
        public override string Type { get { return "audio"; } }

        public abstract SoundInput TryOpen (IBinaryStream file);

        public virtual void Write (SoundInput source, Stream output)
        {
            throw new System.NotImplementedException ("AudioFormat.Write not implemenented");
        }

        public static SoundInput Read (IBinaryStream file)
        {
            foreach (var impl in FormatCatalog.Instance.FindFormats<AudioFormat> (file.Name, file.Signature))
            {
                try
                {
                    file.Position = 0;
                    SoundInput sound = impl.TryOpen (file);
                    if (null != sound)
                        return sound;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (System.Exception X)
                {
                    FormatCatalog.Instance.LastError = X;
                }
            }
            return null;
        }

        public static AudioFormat Wav { get { return s_WavFormat.Value; } }

        static readonly Lazy<AudioFormat> s_WavFormat = new Lazy<AudioFormat> (() => FormatCatalog.Instance.AudioFormats.FirstOrDefault (x => x.Tag == "WAV"));
    }
}
