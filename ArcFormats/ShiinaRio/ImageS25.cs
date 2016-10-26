//! \file       ImageS25.cs
//! \date       Sat Apr 18 17:00:54 2015
//! \brief      ShiinaRio S25 multi-image format.
//
// Copyright (C) 2015-2016 by morkt
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
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.ShiinaRio
{
    internal class S25MetaData : ImageMetaData
    {
        public uint FirstOffset;
        public bool Incremental;
    }

    [Export(typeof(ImageFormat))]
    public class S25Format : ImageFormat
    {
        public override string         Tag { get { return "S25"; } }
        public override string Description { get { return "ShiinaRio image format"; } }
        public override uint     Signature { get { return 0x00353253; } } // 'S25'

        // in current implementation, only the first frame is returned.
        // per-frame access is provided by S25Opener class.

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 4;
            int count = file.ReadInt32();
            if (count < 0 || count > 0xfffff)
                return null;
            uint first_offset = 0;
            for (int i = 0; i < count && 0 == first_offset; ++i)
                first_offset = file.ReadUInt32();
            if (0 == first_offset)
                return null;
            file.Position = first_offset;
            var info = new S25MetaData();
            info.Width = file.ReadUInt32();
            info.Height = file.ReadUInt32();
            info.OffsetX = file.ReadInt32();
            info.OffsetY = file.ReadInt32();
            info.FirstOffset = first_offset+0x14;
            info.Incremental = 0 != (file.ReadUInt32() & 0x80000000u);
            info.BPP = 32;
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var reader = new Reader (stream, (S25MetaData)info))
                return reader.Image;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("S25Format.Write not implemented");
        }

        internal sealed class Reader : IImageDecoder
        {
            IBinaryStream   m_input;
            int             m_width;
            int             m_height;
            uint            m_origin;
            byte[]          m_output;
            bool            m_incremental;
            ImageMetaData   m_info;
            ImageData       m_image;

            public Stream       Input { get { m_input.Position = 0; return m_input.AsStream; } }
            public ImageMetaData Info { get { return m_info; } }
            public ImageFormat Format { get { return null; } }

            public ImageData Image
            {
                get
                {
                    if (null == m_image)
                    {
                        var pixels = Unpack();
                        m_image = ImageData.Create (m_info, PixelFormats.Bgra32, null, pixels);
                    }
                    return m_image;
                }
            }

            public byte[]   Data { get { return m_output; } }

            public Reader (IBinaryStream file, S25MetaData info)
            {
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_output = new byte[m_width * m_height * 4];
                m_input = file;
                m_origin = info.FirstOffset;
                m_incremental = info.Incremental;
                m_info = info;
            }

            public byte[] Unpack ()
            {
                m_input.Position = m_origin;
                if (m_incremental)
                    return UnpackIncremental();
                var rows = new uint[m_height];
                for (int i = 0; i < rows.Length; ++i)
                    rows[i] = m_input.ReadUInt32();
                var row_buffer = new byte[m_width];
                int dst = 0;
                for (int y = 0; y < m_height && dst < m_output.Length; ++y)
                {
                    uint row_pos = rows[y];
                    m_input.Position = row_pos;
                    int row_length = m_input.ReadUInt16();
                    row_pos += 2;
                    if (0 != (row_pos & 1))
                    {
                        m_input.ReadByte();
                        --row_length;
                    }
                    if (row_buffer.Length < row_length)
                        row_buffer = new byte[row_length];
                    m_input.Read (row_buffer, 0, row_length);
                    dst = UnpackLine (row_buffer, dst);
                }
                return m_output;
            }

            void UpdateRepeatCount (Dictionary<uint, int> rows_count)
            {
                m_input.Position = 4;
                int count = m_input.ReadInt32();
                var frames = new List<uint> (count);
                for (int i = 0; i < count; ++i)
                {
                    var offset = m_input.ReadUInt32();
                    if (0 != offset)
                        frames.Add (offset);
                }
                foreach (var offset in frames)
                {
                    if (offset+0x14 == m_origin)
                        continue;
                    m_input.Position = offset+4;
                    int height = m_input.ReadInt32();
                    m_input.Position = offset+0x14;
                    for (int i = 0; i < height; ++i)
                    {
                        var row_offset = m_input.ReadUInt32();
                        if (rows_count.ContainsKey (row_offset))
                            ++rows_count[row_offset];
                    }
                }
            }

            byte[] UnpackIncremental ()
            {
                var rows = new uint[m_height];
                var rows_count = new Dictionary<uint, int> (m_height);
                for (int i = 0; i < rows.Length; ++i)
                {
                    uint offset = m_input.ReadUInt32();
                    rows[i] = offset;
                    if (rows_count.ContainsKey (offset))
                        ++rows_count[offset];
                    else
                        rows_count[offset] = 1;
                }
                UpdateRepeatCount (rows_count);
                var input_rows = new Dictionary<uint, byte[]> (m_height);
                var input_lines = new byte[m_height][];
                for (int y = 0; y < m_height; ++y)
                {
                    uint row_pos = rows[y];
                    if (input_rows.ContainsKey (row_pos))
                    {
                        input_lines[y] = input_rows[row_pos];
                        continue;
                    }
                    var row = ReadLine (row_pos, rows_count[row_pos]);
                    input_rows[row_pos] = row;
                    input_lines[y] = row;
                }
                int dst = 0;
                foreach (var line in input_lines)
                {
                    dst = UnpackLine (line, dst);
                }
                return m_output;
            }

            int UnpackLine (byte[] line, int dst)
            {
                int row_pos = 0;
                for (int x = m_width; x > 0 && dst < m_output.Length && row_pos < line.Length; )
                {
                    if (0 != (row_pos & 1))
                    {
                        ++row_pos;
                    }
                    int count = LittleEndian.ToUInt16 (line, row_pos);
                    row_pos += 2;
                    int method = count >> 13;
                    int skip = (count >> 11) & 3;
                    if (0 != skip)
                    {
                        row_pos += skip;
                    }
                    count &= 0x7ff;
                    if (0 == count)
                    {
                        count = LittleEndian.ToInt32 (line, row_pos);
                        row_pos += 4;
                    }
                    if (count > x) count = x;
                    x -= count;
                    byte b, g, r, a;

                    switch (method)
                    {
                    case 2:
                        for (int i = 0; i < count && row_pos < line.Length; ++i)
                        {
                            m_output[dst++] = line[row_pos++];
                            m_output[dst++] = line[row_pos++];
                            m_output[dst++] = line[row_pos++];
                            m_output[dst++] = 0xff;
                        }
                        break;
                    case 3:
                        b = line[row_pos++];
                        g = line[row_pos++];
                        r = line[row_pos++];
                        for (int i = 0; i < count; ++i)
                        {
                            m_output[dst++] = b;
                            m_output[dst++] = g;
                            m_output[dst++] = r;
                            m_output[dst++] = 0xff;
                        }
                        break;
                    case 4:
                        for (int i = 0; i < count && row_pos < line.Length; ++i)
                        {
                            a = line[row_pos++];
                            m_output[dst++] = line[row_pos++];
                            m_output[dst++] = line[row_pos++];
                            m_output[dst++] = line[row_pos++];
                            m_output[dst++] = a;
                        }
                        break;
                    case 5:
                        a = line[row_pos++];
                        b = line[row_pos++];
                        g = line[row_pos++];
                        r = line[row_pos++];
                        for (int i = 0; i < count; ++i)
                        {
                            m_output[dst++] = b;
                            m_output[dst++] = g;
                            m_output[dst++] = r;
                            m_output[dst++] = a;
                        }
                        break;
                    default:
                        dst += count * 4;
                        break;
                    }
                }
                return dst;
            }

            byte[] ReadLine (uint offset, int repeat)
            {
                m_input.Position = offset;
                int row_length = m_input.ReadUInt16();
                if (0 != (offset & 1))
                {
                    m_input.ReadByte();
                    --row_length;
                }
                var row = new byte[row_length];
                m_input.Read (row, 0, row.Length);
                int row_pos = 0;
                for (int x = m_width; x > 0; )
                {
                    if (0 != (row_pos & 1))
                    {
                        ++row_pos;
                    }
                    int count = LittleEndian.ToUInt16 (row, row_pos);
                    row_pos += 2;
                    int method = count >> 13;
                    int skip = (count >> 11) & 3;
                    if (0 != skip)
                    {
                        row_pos += skip;
                    }
                    count &= 0x7ff;
                    if (0 == count)
                    {
                        count = LittleEndian.ToInt32 (row, row_pos);
                        row_pos += 4;
                    }
                    if (count < 0 || count > x) count = x;
                    x -= count;

                    switch (method)
                    {
                    case 2:
                        for (int j = 0; j < repeat; ++j)
                        {
                            for (int i = 3; i < count*3 && row_pos+i < row.Length; ++i)
                            {
                                row[row_pos+i] += row[row_pos+i-3];
                            }
                        }
                        row_pos += count*3;
                        break;
                    case 3:
                        row_pos += 3;
                        break;
                    case 4:
                        for (int j = 0; j < repeat; ++j)
                        {
                            for (int i = 4; i < count*4 && row_pos+i < row.Length; ++i)
                            {
                                row[row_pos+i] += row[row_pos+i-4];
                            }
                        }
                        row_pos += count*4;
                        break;
                    case 5:
                        row_pos += 4;
                        break;
                    default:
                        break;
                    }
                }
                return row;
            }

            #region IDisposable Members
            public void Dispose ()
            {
                GC.SuppressFinalize (this);
            }
            #endregion
        }
    }
}
