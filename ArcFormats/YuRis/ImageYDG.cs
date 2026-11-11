//! \file       ImageYDG.cs
//! \date       Sun Apr 12 15:08:10 2026
//! \brief      YU-RIS compressed image.
//
// Copyright (C) 2026 by morkt
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

using GameRes.Formats.QoiCodec;
using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Media;

namespace GameRes.Formats.YuRis
{
    internal class YdgMetaData : ImageMetaData
    {
        public int HeaderLength;
    }

    internal class YdgSlice
    {
        public uint   Offset;
        public uint   Size;
        public byte[] Data;
        public int    X;
        public int    Y;
        public int    Height;
    }

    [Export(typeof(ImageFormat))]
    public class YdgFormat : ImageFormat
    {
        public override string         Tag { get { return "YDG"; } }
        public override string Description { get { return "YU-RIS compressed image format"; } }
        public override uint     Signature { get { return 0x00474459; } } // 'YDG\0'

        public YdgFormat ()
        {
            Extensions = new string[] { "ydg" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x30);
            if (!header.AsciiEqual ("YDG\0"))
                return null;
            if (!header.AsciiEqual (4, "YU-RIS"))
                return null;
            return new YdgMetaData
            {
                Width        = header.ToUInt16 (0x20),
                Height       = header.ToUInt16 (0x22),
                BPP          = 32,
                HeaderLength = header.ToInt32 (0x10),
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (YdgMetaData) info;
            var reader = new YdgReader (stream, meta);
            reader.Unpack ();
            return ImageData.Create (meta, reader.Format, null, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("YdgFormat.Write not implemented");
        }
    }

    internal sealed class YdgReader
    {
        readonly IBinaryStream m_input;
        readonly YdgMetaData   m_info;
        readonly byte[]        m_output;

        public PixelFormat Format { get; private set; }
        public byte[]        Data { get { return m_output; } }

        public YdgReader (IBinaryStream input, YdgMetaData info)
        {
            m_input = input;
            m_info = info;
            m_output = new byte[4*(int)info.Width*(int)info.Height];
            Format = PixelFormats.Bgra32;
        }

        public void Unpack ()
        {
            var slices = ReadSlices ();
            var options = new ParallelOptions { MaxDegreeOfParallelism = 4 };
            Parallel.ForEach (slices, options, slice =>
            {
                var pixels = DecodeSlice (slice.Data, out int slice_width, out int slice_height);
                if (slice_height > slice.Height)
                    throw new InvalidFormatException ();
                var height = Math.Min (slice_height, slice.Height);
                CopySlice (pixels, slice_width, height, slice.X, slice.Y);
            });
        }

        YdgSlice[] ReadSlices ()
        {
            m_input.Position = m_info.HeaderLength;
            var count = m_input.ReadInt32 ();
            if (count <= 0)
                throw new InvalidFormatException ();
            var slices = new YdgSlice[count];
            var y = 0;
            for (var i = 0; i < slices.Length; i++)
            {
                var slice = new YdgSlice
                {
                    Offset = m_input.ReadUInt32 (),
                    Size   = m_input.ReadUInt32 (),
                    X      = m_input.ReadUInt16 (),
                    Y      = y,
                    Height = m_input.ReadUInt16 (),
                };
                y += slice.Height;
                m_input.ReadUInt32 ();
                slices[i] = slice;
            }
            foreach (var slice in slices)
            {
                m_input.Position = slice.Offset;
                slice.Data = m_input.ReadBytes ((int)slice.Size);
            }
            return slices;
        }

        byte[] DecodeSlice (byte[] data, out int width, out int height)
        {
            if (Binary.AsciiEqual (data, 0, "RIFF") && Binary.AsciiEqual (data, 8, "WEBP"))
                return DecodeWebP (data, out width, out height);
            return DecodeQoi (data, out width, out height);
        }

        void CopySlice (byte[] pixels, int src_width, int src_height, int dst_x, int dst_y)
        {
            var copy_width = Math.Min (src_width, (int)m_info.Width-dst_x);
            var copy_height = Math.Min (src_height, (int)m_info.Height-dst_y);
            if (copy_width <= 0 || copy_height <= 0)
                return;
            var src_stride = 4*src_width;
            var dst_stride = 4*(int)m_info.Width;
            var dst = dst_y*dst_stride + 4*dst_x;
            var row_bytes = 4*copy_width;
            var src = 0;
            for (var y = 0; y < copy_height; y++)
            {
                Buffer.BlockCopy (pixels, src, m_output, dst, row_bytes);
                src += src_stride;
                dst += dst_stride;
            }
        }

        byte[] DecodeWebP (byte[] data, out int width, out int height)
        {
            width = 0;
            height = 0;
            WebPCodec.Load ();
            if (1 != WebPCodec.WebPGetInfo (data, (UIntPtr)data.Length, ref width, ref height))
                throw new InvalidFormatException ("WebP image decoder failed.");
            var stride = 4*width;
            var output = new byte[stride*height];
            var handle = GCHandle.Alloc (output, GCHandleType.Pinned);
            try
            {
                if (IntPtr.Zero == WebPCodec.WebPDecodeBGRAInto (data, (UIntPtr)data.Length, handle.AddrOfPinnedObject (), (UIntPtr)output.Length, stride))
                    throw new InvalidFormatException ("WebP image decoder failed.");
            }
            finally
            {
                handle.Free ();
            }
            return output;
        }

        byte[] DecodeQoi (byte[] data, out int width, out int height)
        {
            using (var input = new BinMemoryStream (data))
            {
                if (0x66696F71 != input.ReadInt32 ()) // "qoif"
                    throw new InvalidFormatException ("Invalid QOI signature");
                width = (int)Binary.BigEndian (input.ReadUInt32 ());
                height = (int)Binary.BigEndian (input.ReadUInt32 ());
                var channels = input.ReadByte ();
                var colorspace = input.ReadByte ();
                var count = 4*width*height;
                var output = new byte[count];
                var qoi = new QoiDecodeStream (input);
                uint pixel = 0;
                var run = 0;
                var dst = 0;
                while (dst < count)
                {
                    if (run > 1)
                        --run;
                    else
                    {
                        run = qoi.Read (out pixel);
                    }
                    output[dst  ] = (byte)pixel;
                    output[dst+1] = (byte)(pixel >> 8);
                    output[dst+2] = (byte)(pixel >> 16);
                    output[dst+3] = (byte)(pixel >> 24);
                    dst += 4;
                }
                return output;
            }
        }
    }
}
