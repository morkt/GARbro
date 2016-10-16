//! \file       ImageALB.cs
//! \date       Fri Mar 11 18:27:42 2016
//! \brief      SLG system obfuscated image format.
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Slg
{
    internal class AlbMetaData : ImageMetaData
    {
        public int              UnpackedSize;
        public ImageFormat      Format;
        public ImageMetaData    Info;
    }

    [Export(typeof(ImageFormat))]
    public class AlbFormat : ImageFormat
    {
        public override string         Tag { get { return "ALB"; } }
        public override string Description { get { return "SLG system image format"; } }
        public override uint     Signature { get { return 0x31424C41; } } // 'ALB1'

        static readonly Lazy<ImageFormat> Dds = new Lazy<ImageFormat> (() => ImageFormat.FindByTag ("DDS"));

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x10);
            int unpacked_size = header.ToInt32 (8);
            using (var alb = new AlbStream (stream, unpacked_size))
            using (var s = new SeekableStream (alb))
            using (var file = new BinaryStream (s, stream.Name))
            {
                uint signature = file.Signature;
                ImageFormat format;
                if (Png.Signature == signature)
                    format = ImageFormat.Png;
                else if (Dds.Value.Signature == signature)
                    format = Dds.Value;
                else
                    format = ImageFormat.Jpeg;
                file.Position = 0;
                var info = format.ReadMetaData (file);
                if (null == info)
                    return null;
                return new AlbMetaData
                {
                    Width   = info.Width,
                    Height  = info.Height,
                    OffsetX = info.OffsetX,
                    OffsetY = info.OffsetY,
                    BPP     = info.BPP,
                    Format  = format,
                    Info    = info,
                    UnpackedSize = unpacked_size,
                };
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (AlbMetaData)info;
            stream.Position = 0x10;
            using (var alb = new AlbStream (stream, meta.UnpackedSize))
            using (var s = new SeekableStream (alb))
            using (var file = new BinaryStream (s, stream.Name))
                return meta.Format.Read (file, meta.Info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AlbFormat.Write not implemented");
        }
    }

    internal class AlbStream : Stream
    {
        IBinaryStream       m_input;
        int                 m_unpacked_size;
        IEnumerator<int>    m_iterator;
        byte[]              m_output;
        int                 m_output_pos;
        int                 m_output_end;

        public override bool CanRead  { get { return true; } }
        public override bool CanSeek  { get { return false; } }
        public override bool CanWrite { get { return false; } }

        public AlbStream (IBinaryStream source, int unpacked_size)
        {
            m_input = source;
            m_unpacked_size = unpacked_size;
            m_iterator = ReadSeq();
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            m_output = buffer;
            m_output_pos = offset;
            m_output_end = offset + count;
            if (!m_iterator.MoveNext())
                return 0;
            return m_iterator.Current - offset;
        }

        byte[,] m_dict = new byte[256,2];

        IEnumerator<int> ReadSeq ()
        {
            var stack = new byte[256];
            while (m_input.PeekByte() != -1)
            {
                int packed_size = UnpackDict();
                int src = 0;
                int stack_pos = 0;
                for (;;)
                {
                    byte s;
                    if (stack_pos > 0)
                    {
                        s = stack[--stack_pos];
                    }
                    else if (src < packed_size)
                    {
                        s = m_input.ReadUInt8();
                        src++;
                    }
                    else
                    {
                        break;
                    }
                    if (m_dict[s,0] == s)
                    {
                        while (m_output_pos == m_output_end)
                            yield return m_output_pos;
                        m_output[m_output_pos++] = s;
                    }
                    else
                    {
                        stack[stack_pos++] = m_dict[s,1];
                        stack[stack_pos++] = m_dict[s,0];
                    }
                }
            }
            yield return m_output_pos;
        }

        int UnpackDict ()
        {
            if (m_input.ReadInt16() != 0x4850) // 'PH'
                throw new InvalidFormatException();
            int table_size = m_input.ReadUInt16();
            int packed_size = m_input.ReadUInt16();
            bool is_packed = m_input.ReadUInt8() != 0;
            byte marker = m_input.ReadUInt8();
            if (is_packed)
            {
                for (int i = 0; i < 256; )
                {
                    byte b = m_input.ReadUInt8();
                    if (marker == b)
                    {
                        int count = m_input.ReadUInt8();
                        for (int j = 0; j < count; ++j)
                        {
                            m_dict[i,0] = (byte)i;
                            m_dict[i,1] = 0;
                            ++i;
                        }
                    }
                    else
                    {
                        m_dict[i,0] = b;
                        m_dict[i,1] = m_input.ReadUInt8();
                        ++i;
                    }
                }
            }
            else
            {
                for (int i = 0; i < 256; ++i)
                {
                    m_dict[i,0] = m_input.ReadUInt8();
                    m_dict[i,1] = m_input.ReadUInt8();
                }
            }
            return packed_size;
        }

        #region IO.Stream Members
        public override long Length { get { return m_unpacked_size; } }
        public override long Position
        {
            get { throw new NotSupportedException ("AlbStream.Position not supported"); }
            set { throw new NotSupportedException ("AlbStream.Position not supported"); }
        }

        public override void Flush()
        {
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            throw new NotSupportedException ("AlbStream.Seek method is not supported");
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("AlbStream.SetLength method is not supported");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("AlbStream.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new NotSupportedException ("AlbStream.WriteByte method is not supported");
        }
        #endregion

        #region IDisposable Members
        bool _alb_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!_alb_disposed)
            {
                if (disposing)
                {
                    m_iterator.Dispose();
                }
                _alb_disposed = true;
            }
            base.Dispose (disposing);
        }
        #endregion
    }
}
