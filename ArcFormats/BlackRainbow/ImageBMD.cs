//! \file       ImageBMD.cs
//! \date       Wed Mar 25 09:35:06 2015
//! \brief      Black Rainbow BMD image format implementation.
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.BlackRainbow
{
    internal class BmdMetaData : ImageMetaData
    {
        public uint PackedSize;
        public  int Flag;
    }

    [Export(typeof(ImageFormat))]
    public class BmdFormat : ImageFormat
    {
        public override string         Tag { get { return "BMD"; } }
        public override string Description { get { return "Black Rainbow bitmap format"; } }
        public override uint     Signature { get { return 0x444d425fu; } } // '_BMD'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x14];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;

            return new BmdMetaData
            {
                Width = LittleEndian.ToUInt32 (header, 8),
                Height = LittleEndian.ToUInt32 (header, 12),
                BPP = 32,
                PackedSize = LittleEndian.ToUInt32 (header, 4),
                Flag = LittleEndian.ToInt32 (header, 0x10),
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as BmdMetaData;
            if (null == meta)
                throw new ArgumentException ("BmdFormat.Read should be supplied with BmdMetaData", "info");

            stream.Position = 0x14;
            int image_size = (int)(meta.Width*meta.Height*4);
            using (var reader = new LzssReader (stream, (int)meta.PackedSize, image_size))
            {
                PixelFormat format = meta.Flag != 0 ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
                reader.Unpack();
                return ImageData.Create (meta, format, null, reader.Data, (int)meta.Width*4);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var output = new BinaryWriter (file, Encoding.ASCII, true))
            using (var writer = new Writer (image.Bitmap))
            {
                writer.Pack();
                output.Write (Signature);
                output.Write (writer.Size);
                output.Write (image.Width);
                output.Write (image.Height);
                output.Write (writer.HasAlpha ? 1 : 0);
                output.Write (writer.Data, 0, (int)writer.Size);
            }
        }

        internal class Writer : IDisposable
        {
            const int MinChunkSize = 3;
            const int MaxChunkSize = MinChunkSize+0xf;
            const int FrameSize = 0x1000;

            byte[]          m_input;
            MemoryStream    m_output;
            bool            m_has_alpha = false;
            byte[]          m_frame = new byte[FrameSize];

            public byte[]   Data { get { return m_output.GetBuffer(); } }
            public uint     Size { get { return (uint)m_output.Length; } }
            public bool HasAlpha { get { return m_has_alpha; } }

            public Writer (BitmapSource bitmap)
            {
                if (bitmap.Format != PixelFormats.Bgra32)
                    bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgra32, null, 0);

                m_input = new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];
                bitmap.CopyPixels (m_input, bitmap.PixelWidth*4, 0);
                for (int i = 3; i < m_input.Length; i += 4)
                {
                    if (0xff != m_input[i])
                    {
                        m_has_alpha = true;
                        break;
                    }
                }
                if (!m_has_alpha)
                    for (int i = 3; i < m_input.Length; i += 4)
                        m_input[i] = 0;
                m_output = new MemoryStream();
            }

            public void Pack ()
            {
                int frame_pos = 0x1000 - 18;
                int src = 0;
                while (src < m_input.Length)
                {
                    int chunk_size;
                    int offset = FindChunk (src, out chunk_size);
                    if (-1 == offset)
                    {
                        PutByte (m_input[src]);
                        chunk_size = 1;
                    }
                    else
                        PutChunk (offset, chunk_size);
                    for (int i = 0; i < chunk_size; ++i)
                    {
                        m_frame[frame_pos++] = m_input[src++];
                        frame_pos &= 0xfff;
                    }
                }
                Flush();
            }

            struct Chunk
            {
                public short  Offset;
                public byte   Data;

                public Chunk (byte b)
                {
                    Offset = -1;
                    Data = b;
                }

                public Chunk (int offset, int count)
                {
                    Debug.Assert (offset < 0x1000 && count >= MinChunkSize && count <= MaxChunkSize);
                    Offset = (short)offset;
                    Data = (byte)((count - MinChunkSize) & 0x0f);
                }
            }

            List<Chunk> m_queue = new List<Chunk> (8);

            void PutByte (byte b)
            {
                m_queue.Add (new Chunk (b));
                if (8 == m_queue.Count)
                    Flush();
            }

            void PutChunk (int offset, int size)
            {
                m_queue.Add (new Chunk (offset, size));
                if (8 == m_queue.Count)
                    Flush();
            }

            void Flush ()
            {
                if (0 == m_queue.Count)
                    return;
                int ctl = 0;
                int bit = 1;
                for (int i = 0; i < m_queue.Count; ++i)
                {
                    if (m_queue[i].Offset < 0)
                        ctl |= bit;
                    bit <<= 1;
                }
                m_output.WriteByte ((byte)ctl);
                for (int i = 0; i < m_queue.Count; ++i)
                {
                    var chunk = m_queue[i];
                    if (chunk.Offset >= 0)
                    {
                        byte lo = (byte)(chunk.Offset & 0xff);
                        byte hi = (byte)((chunk.Offset & 0xf00) >> 4);
                        hi |= chunk.Data;
                        m_output.WriteByte (lo);
                        m_output.WriteByte (hi);
                    }
                    else
                        m_output.WriteByte (chunk.Data);
                }
                m_queue.Clear();
            }

            private int FindChunk (int pos, out int size)
            {
                size = 0;
                int chunk_limit = Math.Min (MaxChunkSize, m_input.Length-pos);
                if (chunk_limit < MinChunkSize)
                    return -1;
                int offset = -1;
                for (int i = 0; i < m_frame.Length; )
                {
                    int first = Array.IndexOf (m_frame, m_input[pos], i);
                    if (-1 == first)
                        break;
                    int j = 1;
                    while (j < chunk_limit && m_frame[(first+j)&0xfff] == m_input[pos+j])
                        ++j;
                    if (j > size && j >= MinChunkSize)
                    {
                        offset = first;
                        size = j;
                        if (chunk_limit == j)
                            break;
                    }
                    i = first + 1;
                }
                return offset;
            }

            #region IDisposable Members
            bool disposed = false;

            public void Dispose ()
            {
                Dispose (true);
                GC.SuppressFinalize (this);
            }

            protected virtual void Dispose (bool disposing)
            {
                if (!disposed)
                {
                    if (disposing)
                    {
                        m_output.Dispose();
                    }
                    disposed = true;
                }
            }
            #endregion
        }
    }
}
