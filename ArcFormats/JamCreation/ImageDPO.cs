//! \file       ImageDPO.cs
//! \date       2019 Mar 20
//! \brief      Jam Creation tiled image format.
//
// Copyright (C) 2019 by morkt
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
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.JamCreation
{
    internal class DpoMetaData : ImageMetaData
    {
        public int              Version;
        public IList<string>    Files;
        public int              LayoutOffset;
    }

    [Export(typeof(ImageFormat))]
    public class DpoFormat : ImageFormat
    {
        public override string         Tag { get { return "DPO"; } }
        public override string Description { get { return "Jam Creation tiled image format"; } }
        public override uint     Signature { get { return 0x69766944; } } // 'Divided Picture'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x30);
            if (!header.AsciiEqual ("Divided Picture") || header.ToInt32 (0x10) != 1)
                return null;
            int version = header.ToInt32 (0x14);
            if (version != 1 && version != 2)
                return null;
            int info_pos = header.ToInt32 (0x18);
            if (header.ToInt32 (0x1C) < 4)
                return null;
            int name_table_pos = header.ToInt32 (0x20);
            int name_table_size = header.ToInt32 (0x24);
            int layout_pos = header.ToInt32 (0x28);

            file.Position = info_pos;
            ushort width  = file.ReadUInt16();
            ushort height = file.ReadUInt16();

            file.Position = name_table_pos;
            int name_count = file.ReadUInt16();
            if (name_count * 32 + 2 != name_table_size)
                return null;
            var dir_name = VFS.GetDirectoryName (file.Name);
            var files = new List<string> (name_count);
            for (int i = 0; i < name_count; ++i)
            {
                var name = file.ReadCString (0x20);
                if (name.StartsWith (@".\"))
                    name = name.Substring (2);
                name = VFS.CombinePath (dir_name, name);
                files.Add (name);
            }
            return new DpoMetaData {
                Width = width,
                Height = height,
                BPP = 32,
                Version = version,
                LayoutOffset = layout_pos,
                Files = files,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new DpoReader (file, (DpoMetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DpoFormat.Write not implemented");
        }
    }

    internal class DpoReader
    {
        IBinaryStream   m_input;
        DpoMetaData     m_info;

        public DpoReader (IBinaryStream input, DpoMetaData info)
        {
            m_input = input;
            m_info = info;
            m_file_map = new Lazy<BitmapSource>[m_info.Files.Count];
            for (int i = 0; i < m_info.Files.Count; ++i)
            {
                string filename = m_info.Files[i];
                m_file_map[i] = new Lazy<BitmapSource> (() => LoadBitmap (filename));
            }
        }

        Lazy<BitmapSource>[] m_file_map;

        public ImageData Unpack ()
        {
            m_input.Position = m_info.LayoutOffset;
            int count = m_input.ReadInt32();
            int tile_size = m_input.ReadInt32();
            m_tile_stride = tile_size * 4;
            m_tile_buffer = new byte[tile_size * m_tile_stride];
            var canvas = new WriteableBitmap (m_info.iWidth, m_info.iHeight,
                ImageData.DefaultDpiX, ImageData.DefaultDpiY, PixelFormats.Bgra32, null);
            var tile_def = new float[8];
            for (int i = 0; i < count; ++i)
            {
                for (int j = 0; j < 8; ++j)
                {
                    tile_def[j] = ReadFloat();
                }
                int file_num = m_input.ReadUInt16();
                int x = m_input.ReadUInt16();
                int y = m_input.ReadUInt16();
                var source = m_file_map[file_num].Value;
                int src_x = (int)(source.PixelWidth  * tile_def[0]);
                int src_y = (int)(source.PixelHeight * tile_def[4]);
                int src_w = tile_size;
                int src_h = tile_size;
                if (m_info.Version > 1)
                {
                    src_w = m_input.ReadUInt16();
                    src_h = m_input.ReadUInt16();
                }
                var rect = new Int32Rect (src_x, src_y, src_w, src_h);
                CopyTile (canvas, x, y, source, rect);
            }
            canvas.Freeze();
            return new ImageData (canvas, m_info);
        }

        int     m_tile_stride;
        byte[]  m_tile_buffer;

        void CopyTile (WriteableBitmap canvas, int x, int y, BitmapSource source, Int32Rect rect)
        {
            source.CopyPixels (rect, m_tile_buffer, m_tile_stride, 0);
            var width  = Math.Min (rect.Width,  canvas.PixelWidth  - x);
            var height = Math.Min (rect.Height, canvas.PixelHeight - y);
            var src_rect = new Int32Rect (0, 0, width, height);
            canvas.WritePixels (src_rect, m_tile_buffer, m_tile_stride, x, y);
        }

        BitmapSource LoadBitmap (string filename)
        {
            using (var input = VFS.OpenBinaryStream (filename))
            {
                var image = ImageFormat.Read (input);
                if (null == image)
                    throw new InvalidFormatException();
                var bitmap = image.Bitmap;
                if (bitmap.Format.BitsPerPixel != 32)
                    bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgra32, null, 0);
                return bitmap;
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        struct Union
        {
            [FieldOffset(0)]
            public uint u;
            [FieldOffset(0)]
            public float f;
        }

        Union m_flt_buffer = new Union();

        float ReadFloat ()
        {
            m_flt_buffer.u = m_input.ReadUInt32();
            return m_flt_buffer.f;
        }
    }
}
