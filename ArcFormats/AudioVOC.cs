//! \file       AudioVOC.cs
//! \date       Sun May 29 03:01:14 2016
//! \brief      Creative Voice audio format.
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
using GameRes.Utility;

namespace GameRes.Formats.Creative
{
    internal sealed class VocReader : IDisposable
    {
        int             m_version;
        int             m_header_size;
        BinaryReader    m_input;
        WaveFormat      m_format;

        public WaveFormat Format { get { return m_format; } }

        public VocReader (Stream input, byte[] header)
        {
            m_header_size = LittleEndian.ToUInt16 (header, 0x14);
            if (m_header_size < 0x1A)
                throw new InvalidFormatException();
            m_version = LittleEndian.ToUInt16 (header, 0x16);
            m_input = new ArcView.Reader (input);
        }

        public Stream ConvertToPcm ()
        {
            m_input.BaseStream.Position = m_header_size;
            bool format_read = false;
            var pcm = new MemoryStream();
            try
            {
                for (;;)
                {
                    int block_type = m_input.BaseStream.ReadByte();
                    if (-1 == block_type || 0 == block_type)
                        break;
                    int block_size = m_input.ReadUInt16();
                    block_size |= m_input.ReadByte() << 16;
                    uint freq;
                    int codec = -1;
                    switch (block_type)
                    {
                    case 1:
                        freq = m_input.ReadByte();
                        codec = m_input.ReadByte();
                        Copy (block_size-2, pcm);
                        m_format.Channels = 1;
                        m_format.SamplesPerSecond = 1000000u / (256u - freq);
                        m_format.BitsPerSample = 8;
                        format_read = true;
                        break;
                    case 2:
                        Copy (block_size, pcm);
                        break;
                    case 8:
                        freq = m_input.ReadUInt16();
                        codec = m_input.ReadByte();
                        m_format.Channels = (ushort)(m_input.ReadByte()+1);
                        m_format.SamplesPerSecond = 256000000u / (Format.Channels * (65536u - freq));
                        m_format.BitsPerSample = 8;
                        format_read = true;
                        break;
                    case 9:
                        m_format.SamplesPerSecond = m_input.ReadUInt32();
                        m_format.BitsPerSample = m_input.ReadByte();
                        m_format.Channels = (ushort)(m_input.ReadByte()+1);
                        codec = m_input.ReadUInt16();
                        format_read = true;
                        m_input.ReadInt32();
                        Copy (block_size-12, pcm);
                        break;
                    default:
                        m_input.BaseStream.Seek (block_size, SeekOrigin.Current);
                        break;
                    }
                    if (codec != -1)
                    {
                        if (0 == codec)
                        {
                            m_format.FormatTag = 1;
                        }
                        else if (4 == codec)
                        {
                            m_format.FormatTag = 1;
                            m_format.BitsPerSample = 16;
                        }
                        else if (7 == codec)
                        {
                            m_format.FormatTag = 7;
                        }
                        else
                            throw new NotImplementedException();
                    }
                }
                if (!format_read || 0 == Format.Channels || 0 == pcm.Length)
                    throw new InvalidFormatException();
                m_format.BlockAlign = (ushort)(m_format.Channels * m_format.BitsPerSample / 8);
                m_format.AverageBytesPerSecond = m_format.SamplesPerSecond * m_format.BlockAlign;
                pcm.Position = 0;
                return pcm;
            }
            catch
            {
                pcm.Dispose();
                throw;
            }
        }

        byte[] m_buffer = null;

        void Copy (int block_size, Stream output)
        {
            if (null == m_buffer || m_buffer.Length < block_size)
                m_buffer = new byte[block_size];
            if (block_size != m_input.Read (m_buffer, 0, block_size))
                throw new EndOfStreamException();
            output.Write (m_buffer, 0, block_size);
        }

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }

    [Export(typeof(AudioFormat))]
    public class VocAudio : AudioFormat
    {
        public override string         Tag { get { return "VOC"; } }
        public override string Description { get { return "Creative Voice File"; } }
        public override uint     Signature { get { return 0x61657243; } } // 'Crea'

        public override SoundInput TryOpen (Stream file)
        {
            var header = new byte[0x1A];
            if (header.Length != file.Read (header, 0, header.Length))
                return null;
            if (!Binary.AsciiEqual (header, 0, "Creative Voice File\x1A"))
                return null;
            using (var reader = new VocReader (file, header))
            {
                var pcm = reader.ConvertToPcm();
                if (null == pcm)
                    return null;
                return new RawPcmInput (pcm, reader.Format);
            }
        }
    }
}


