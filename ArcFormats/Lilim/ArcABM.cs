//! \file       ArcABM.cs
//! \date       Tue Aug 04 23:40:47 2015
//! \brief      LiLiM/Le.Chocolat multi-frame compressed bitmaps.
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
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Lilim
{
    internal class AbmArchive : ArcFile
    {
        public readonly AbmImageData FrameInfo;

        public AbmArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, AbmImageData info)
            : base (arc, impl, dir)
        {
            FrameInfo = info;
        }
    }

    internal class AbmImageData : ImageMetaData
    {
        public int      Mode;
        public uint     BaseOffset;
        public uint     FrameOffset;

        public AbmImageData Clone ()
        {
            return this.MemberwiseClone() as AbmImageData;
        }
    }

    internal class AbmEntry : PackedEntry
    {
        public int Index;
    }

    [Export(typeof(ArchiveFormat))]
    public class AbmOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ABM"; } }
        public override string Description { get { return "LiLiM/Le.Chocolat multi-frame bitmap"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if ('B' != file.View.ReadByte (0) || 'M' != file.View.ReadByte (1))
                return null;
            int type = file.View.ReadSByte (0x1C);
            if (type != 1 && type != 2)
                return null;

            int count = file.View.ReadInt16 (0x3A);
            if (!IsSaneCount (count))
                return null;

            uint width = file.View.ReadUInt32 (0x12);
            uint height = file.View.ReadUInt32 (0x16);
            int pixel_size = 2 == type ? 4 : 3;
            uint bitmap_data_size = width*height*(uint)pixel_size;

            var dir = new List<Entry> (count);
            long next_offset = file.View.ReadUInt32 (0x42);
            uint current_offset = 0x46;
            string base_name = Path.GetFileNameWithoutExtension (file.Name);
            for (int i = 0; i < count; ++i)
            {
                var entry = new AbmEntry {
                    Name = string.Format ("{0}#{1:D4}", base_name, i),
                    Type = "image",
                    Offset = next_offset,
                    Index = i,
                };
                if (i + 1 != count)
                {
                    next_offset = file.View.ReadUInt32 (current_offset);
                    current_offset += 4;
                }
                else
                    next_offset = file.MaxOffset;
                if (next_offset <= entry.Offset)
                    return null;
                entry.Size = (uint)(next_offset - entry.Offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.UnpackedSize = 0x12 + bitmap_data_size;
                dir.Add (entry);
            }
            var image_info = new AbmImageData
            {
                Width = (uint)width,
                Height = (uint)height,
                BPP = pixel_size * 8,
                Mode = type,
                BaseOffset = (uint)dir[0].Offset,
            };
            return new AbmArchive (file, this, dir, image_info);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var abm = arc as AbmArchive;
            var frame = entry as AbmEntry;
            if (null == abm || null == frame)
                return base.OpenImage (arc, entry);

            var frame_info = abm.FrameInfo;
            if (frame.Index != 0)
            {
                frame_info = frame_info.Clone();
                frame_info.FrameOffset = (uint)frame.Offset;
            }
            var input = arc.File.CreateStream (0, (uint)arc.File.MaxOffset);
            return new AbmReader (input, frame_info);
        }
    }

    internal sealed class AbmReader : IImageDecoder
    {
        IBinaryStream   m_input;
        byte[]          m_output;
        AbmImageData    m_info;
        ImageData       m_image;
        int             m_bpp;

        public Stream            Source { get { return m_input.AsStream; } }
        public ImageFormat SourceFormat { get { return null; } }
        public ImageMetaData       Info { get { return m_info; } }
        public ImageData          Image
        {
            get
            {
                if (null == m_image)
                    m_image = ReadImage();
                return m_image;
            }
        }

        public AbmReader (IBinaryStream file, AbmImageData info)
        {
            m_info = info;
            m_input = file;
        }

        ImageData ReadImage ()
        {
            if (2 == m_info.Mode)
            {
                m_bpp = 32;
                m_input.Position = m_info.BaseOffset;
                m_output = UnpackV2 (m_input);
            }
            else if (1 == m_info.Mode || 32 == m_info.Mode || 24 == m_info.Mode)
            {
                if (1 == m_info.Mode)
                    m_bpp = 24;
                else
                    m_bpp = m_info.Mode;

                int total_length = (int)(m_info.Width * m_info.Height * m_bpp / 8);
                m_output = new byte[total_length];
                m_input.Position = m_info.BaseOffset;
                if (1 == m_info.Mode)
                {
                    if (total_length != m_input.Read (m_output, 0, (total_length)))
                        throw new EndOfStreamException ();
                }
                else if (24 == m_bpp)
                    UnpackStream24 (m_input, m_output, total_length);
                else
                    UnpackStream32 (m_input, m_output, total_length);
            }
            else
                throw new NotImplementedException();
            if (0 != m_info.FrameOffset)
                m_output = Unpack();
            PixelFormat format = 24 == m_bpp ? PixelFormats.Bgr24 : PixelFormats.Bgra32;
            return ImageData.Create (m_info, format, null, m_output);
        }

        int frame_x;
        int frame_y;
        int frame_w;
        int frame_h;

        byte[] Unpack ()
        {
            m_input.Position = m_info.FrameOffset;
            if (1 == m_info.Mode)
                return UnpackStream24 (m_input, m_output, m_output.Length);
            if (2 == m_info.Mode)
            {
                var frame = UnpackV2 (m_input);
                CopyFrame (frame);
                return m_output;
            }
            throw new NotImplementedException();
        }

        byte[] UnpackV2 (IBinaryStream input)
        {
            frame_x = input.ReadInt32();
            frame_y = input.ReadInt32();
            frame_w = input.ReadInt32();
            frame_h = input.ReadInt32();
            if (frame_x < 0 || frame_y < 0 || frame_w <= 0 || frame_h <= 0)
                throw new InvalidFormatException();
            int total_length = frame_w * frame_h * m_bpp / 8;
            byte[] output = new byte[total_length];
            input.ReadByte(); // position number
            return UnpackStream32 (input, output, total_length);
        }

        byte[] UnpackStream24 (IBinaryStream input, byte[] output, int total_length)
        {
            int dst = 0;
            while (dst < total_length)
            {
                int v = input.ReadUInt8();
                if (0 == v)
                {
                    int count = input.ReadUInt8();
                    if (0 == count)
                        continue;
                    dst += count;
                }
                else if (0xff == v)
                {
                    int count = input.ReadUInt8();
                    if (0 == count)
                        continue;
                    count = Math.Min (count, total_length-dst);
                    input.Read (output, dst, count);
                    dst += count;
                }
                else
                {
                    output[dst++] = input.ReadUInt8();
                }
            }
            return output;
        }

        byte[] UnpackStream32 (IBinaryStream input, byte[] output, int total_length)
        {
            int dst = 0;
            int component = 0;
            while (dst < total_length)
            {
                byte v = input.ReadUInt8();
                if (0 == v)
                {
                    int count = input.ReadUInt8();
                    if (0 == count)
                        continue;
                    for (int i = 0; i < count; ++i)
                    {
                        ++dst;
                        if (++component == 3)
                        {
                            ++dst;
                            component = 0;
                        }
                    }
                }
                else if (0xff == v)
                {
                    int count = input.ReadUInt8();
                    if (0 == count)
                        continue;
                    for (int i = 0; i < count && dst < total_length; ++i)
                    {
                        output[dst++] = input.ReadUInt8();
                        if (++component == 3)
                        {
                            output[dst++] = 0xff;
                            component = 0;
                        }
                    }
                }
                else
                {
                    output[dst++] = input.ReadUInt8();
                    if (++component == 3)
                    {
                        output[dst++] = v;
                        component = 0;
                    }
                }
            }
            return output;
        }

        void CopyFrame (byte[] frame)
        {
            if (frame_x >= m_info.Width || frame_y >= m_info.Height)
                return;
            int pixel_size = m_bpp / 8;
            int line_size = Math.Min (frame_w, (int)m_info.Width-frame_x) * pixel_size;
            int left = frame_x * pixel_size;
            int bottom = Math.Min (frame_y+frame_h, (int)m_info.Height);
            int stride = (int)m_info.Width * pixel_size;
            int src = 0;
            for (int y = frame_y; y < bottom; ++y)
            {
                int dst = stride * y + left;
                Buffer.BlockCopy (frame, src, m_output, dst, line_size);
                src += frame_w * pixel_size;
            }
        }

        #region IDisposable Members
        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
            GC.SuppressFinalize (this);
        }
        #endregion
    }
}
