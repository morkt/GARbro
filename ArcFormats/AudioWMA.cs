//! \file       AudioWMA.cs
//! \date       Sun Oct 23 21:29:19 2016
//! \brief      Windows Media Audio format.
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
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.MediaFoundation;
using NAudio.Utils;
using NAudio.Wave;

namespace GameRes.Formats
{
    [Export(typeof(AudioFormat))]
    public class WmaAudio : AudioFormat
    {
        public override string         Tag { get { return "WMA"; } }
        public override string Description { get { return "Windows Media Audio format"; } }
        public override uint     Signature { get { return 0x75B22630; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            return new WmaInput (file.AsStream);
        }
    }

    public class WmaInput : SoundInput
    {
        int                     m_bitrate;
        MediaFoundationReader   m_reader;

        public override long Position
        {
            get { return m_reader.Position; }
            set { m_reader.Position = value; }
        }

        public override bool CanSeek { get { return true; } }

        public override int SourceBitrate
        {
            get { return m_bitrate; }
        }

        public override string SourceFormat { get { return "wma"; } }

        public WmaInput (Stream file) : base (file)
        {
            var reader = new CustomMediaFoundationReader (file);
            if (reader.Duration != 0)
                m_bitrate = (int)(file.Length * 80000000L / reader.Duration);
            else
                m_bitrate = reader.WaveFormat.AverageBytesPerSecond * 8;
            m_reader = reader;
            var format = new GameRes.WaveFormat {
                FormatTag                = (ushort)m_reader.WaveFormat.Encoding,
                Channels                 = (ushort)m_reader.WaveFormat.Channels,
                SamplesPerSecond         = (uint)m_reader.WaveFormat.SampleRate,
                BitsPerSample            = (ushort)m_reader.WaveFormat.BitsPerSample,
                BlockAlign               = (ushort)m_reader.BlockAlign,
                AverageBytesPerSecond    = (uint)m_reader.WaveFormat.AverageBytesPerSecond,
            };
            this.Format = format;
            this.PcmSize = m_reader.Length;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            return m_reader.Read (buffer, offset, count);
        }

        #region IDisposable Members
        bool _wma_disposed;
        protected override void Dispose (bool disposing)
        {
            if (!_wma_disposed)
            {
                if (disposing)
                {
                    m_reader.Dispose();
                }
                _wma_disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }

    /// <summary>
    /// Custom implementation of MediaFoundationReader.
    /// NAudio uses MFCreateMFByteStreamOnStreamEx API call which is not available in Windows 7.
    /// </summary>
    internal class CustomMediaFoundationReader : MediaFoundationReader
    {
        private readonly Stream m_stream;

        /// <summary>
        /// Specifies the duration of a presentation, in 100-nanosecond units. 
        /// </summary>
        public long Duration { get; private set; }

        public CustomMediaFoundationReader (Stream stream, MediaFoundationReaderSettings settings = null)
        {
            m_stream = stream;
            Init (settings);
        }

        protected override IMFSourceReader CreateReader (MediaFoundationReaderSettings settings)
        {
            IMFByteStream byteStream;
            MFCreateMFByteStreamOnStream (new ComStream (m_stream), out byteStream);
            var source_reader = MediaFoundationApi.CreateSourceReaderFromByteStream (byteStream);

            source_reader.SetStreamSelection (-2, false);
            source_reader.SetStreamSelection (-3, true);
            source_reader.SetCurrentMediaType (-3, IntPtr.Zero, new MediaType
            {
                MajorType = MediaTypes.MFMediaType_Audio,
                SubType = settings.RequestFloatOutput ? AudioSubtypes.MFAudioFormat_Float : AudioSubtypes.MFAudioFormat_PCM
            }.MediaFoundationObject);

            Duration = GetDuration (source_reader);

            return source_reader;
        }

        private static long GetDuration (IMFSourceReader reader)
        {
            var variantPtr = Marshal.AllocHGlobal (MarshalHelpers.SizeOf<PropVariant>());
            try
            {
                int hResult = reader.GetPresentationAttribute (MediaFoundationInterop.MF_SOURCE_READER_MEDIASOURCE,
                    MediaFoundationAttributes.MF_PD_DURATION, variantPtr);
                if (hResult == MediaFoundationErrors.MF_E_ATTRIBUTENOTFOUND)
                    return 0;
                if (hResult != 0)
                    Marshal.ThrowExceptionForHR (hResult);

                var variant = MarshalHelpers.PtrToStructure<PropVariant> (variantPtr);
                return (long)variant.Value;
            }
            finally 
            {
                PropVariant.Clear (variantPtr);
                Marshal.FreeHGlobal (variantPtr);
            }
        }

        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
        static extern void MFCreateMFByteStreamOnStream (IStream pStream, out IMFByteStream ppByteStream);
    }

    /// <summary>
    /// Implementation of Com IStream.
    /// NAudio implementation is inaccessible (private).
    /// </summary>
    internal class ComStream : ProxyStream, IStream
    {
        public ComStream (Stream stream) : base (Synchronized (stream))
        {
        }

        void IStream.Clone (out IStream ppstm)
        {
            ppstm = null;
        }

        void IStream.Commit (int grfCommitFlags)
        {
            Flush();
        }

        void IStream.CopyTo (IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten)
        {
        }

        void IStream.LockRegion (long libOffset, long cb, int dwLockType)
        {
        }

        void IStream.Read (byte[] pv, int cb, IntPtr pcbRead)
        {
            if (!CanRead)
                throw new InvalidOperationException ("Stream is not readable.");
            int read = Read (pv, 0, cb);
            if (pcbRead != IntPtr.Zero)
                Marshal.WriteInt32 (pcbRead, read);
        }

        void IStream.Revert()
        {
        }

        void IStream.Seek (long dlibMove, int dwOrigin, IntPtr plibNewPosition)
        {
            SeekOrigin origin = (SeekOrigin) dwOrigin;
            long val = Seek (dlibMove, origin);
            if (plibNewPosition != IntPtr.Zero)
                Marshal.WriteInt64 (plibNewPosition, val);
        }

        void IStream.SetSize (long libNewSize)
        {
            SetLength (libNewSize);
        }

        void IStream.Stat (out System.Runtime.InteropServices.ComTypes.STATSTG pstatstg, int grfStatFlag)
        {
            const int STGM_READ = 0x00000000;
            const int STGM_WRITE = 0x00000001;
            const int STGM_READWRITE = 0x00000002;

            var tmp = new System.Runtime.InteropServices.ComTypes.STATSTG { type = 2, cbSize = Length, grfMode = 0 };

            if (CanWrite && CanRead)
                tmp.grfMode |= STGM_READWRITE;
            else if (CanRead)
                tmp.grfMode |= STGM_READ;
            else if (CanWrite)
                tmp.grfMode |= STGM_WRITE;
            else
                throw new ObjectDisposedException ("Stream");

            pstatstg = tmp;
        }

        void IStream.UnlockRegion (long libOffset, long cb, int dwLockType)
        {
        }

        void IStream.Write (byte[] pv, int cb, IntPtr pcbWritten)
        {
            if (!CanWrite)
                throw new InvalidOperationException ("Stream is not writeable.");
            Write (pv, 0, cb);
            if (pcbWritten != IntPtr.Zero)
                Marshal.WriteInt32 (pcbWritten, cb);
        }
    }
}
