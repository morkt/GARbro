//! \file       ImageAinos.cs
//! \date       Sun Apr 05 04:16:27 2015
//! \brief      Ainos image format.
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
using System.Linq;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Ags
{
    public class CgMetaData : ImageMetaData
    {
        public int Type;
        public int Right, Bottom;
    }

    internal class AniEntry : Entry
    {
        public int  FrameIndex;
        public int  FrameType;
        public int  KeyFrame;
    }

    [Export(typeof(ArchiveFormat))]
    public class AniOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ANI"; } }
        public override string Description { get { return "Anime Game System animation resource"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint first_offset = file.View.ReadUInt32 (0);
            if (first_offset < 4 || file.MaxOffset > int.MaxValue || first_offset >= file.MaxOffset || 0 != (first_offset & 3))
                return null;
            int frame_count = (int)(first_offset / 4);
            if (frame_count > 10000)
                return null;
            long index_offset = 4;

            var frame_table = new uint[frame_count];
            frame_table[0] = first_offset;
            for (int i = 1; i < frame_count; ++i)
            {
                var offset = file.View.ReadUInt32 (index_offset);
                index_offset += 4;
                if (offset < first_offset || offset >= file.MaxOffset)
                    return null;
                frame_table[i] = offset;
            }

            var frame_map = new Dictionary<uint, byte>();
            foreach (var offset in frame_table)
            {
                if (!frame_map.ContainsKey (offset))
                {
                    byte frame_type = file.View.ReadByte (offset);
                    if (frame_type >= 0x20)
                        return null;
                    frame_map[offset] = frame_type;
                }
            }

            int last_key_frame = 0;
            var dir = new List<Entry>();
            for (int i = 0; i < frame_count; ++i)
            {
                var offset = frame_table[i];
                int frame_type = frame_map[offset];
                if (1 == frame_type)
                    continue;
                frame_type &= 0xF;
                if (0 == frame_type || 0xA == frame_type)
                    last_key_frame = dir.Count;
                var entry = new AniEntry
                {
                    Name = string.Format ("{0:D4}.tga", i),
                    Type = "image",
                    Offset = offset,
                    FrameType = frame_type,
                    KeyFrame = last_key_frame,
                    FrameIndex = dir.Count,
                };
                dir.Add (entry);
            }
            if (0 == dir.Count)
                return null;

            var ordered = dir.OrderBy (e => e.Offset).ToList();
            for (int i = 0; i < ordered.Count; ++i)
            {
                var entry = ordered[i] as AniEntry;
                long next_offset = file.MaxOffset;
                for (int j = i+1; j <= ordered.Count; ++j)
                {
                    next_offset = j == ordered.Count ? file.MaxOffset : ordered[j].Offset;
                    if (next_offset != entry.Offset)
                        break;
                }
                entry.Size = (uint)(next_offset - entry.Offset);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var ani = (AniEntry)entry;
            byte[] key_frame = null;
            if (ani.KeyFrame != ani.FrameIndex)
            {
                var dir = (List<Entry>)arc.Dir;
                for (int i = ani.KeyFrame; i < ani.FrameIndex; ++i)
                {
                    var frame = dir[i];
                    using (var s = arc.File.CreateStream (frame.Offset, frame.Size))
                    {
                        var frame_info = Cg.ReadMetaData (s) as CgMetaData;
                        if (null == frame_info)
                            break;
                        using (var reader = new CgFormat.Reader (s, frame_info, key_frame))
                        {
                            reader.Unpack();
                            key_frame = reader.Data;
                        }
                    }
                }
            }
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            CgMetaData info = null;
            try
            {
                info = Cg.ReadMetaData (input) as CgMetaData;
            }
            catch
            {
                input.Dispose();
                throw;
            }
            if (null == info)
            {
                input.Position = 0;
                return input;
            }
            using (input)
            using (var reader = new CgFormat.Reader (input, info, key_frame))
            {
                reader.Unpack();
                return TgaStream.Create (info, reader.Data);
            }
        }

        static Lazy<ImageFormat> s_Cg = new Lazy<ImageFormat> (() => ImageFormat.FindByTag ("CG"));

        ImageFormat Cg { get { return s_Cg.Value; } }
    }

    [Export(typeof(ImageFormat))]
    public class CgFormat : ImageFormat
    {
        public override string         Tag { get { return "CG"; } }
        public override string Description { get { return "Anime Game System image format"; } }
        public override uint     Signature { get { return 0; } }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CgFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            int sig = file.ReadByte();
            if (sig >= 0x20)
                return null;
            int width  = file.ReadInt16();
            int height = file.ReadInt16();
            if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
                return null;
            var meta = new CgMetaData
            {
                Width = (uint)width,
                Height = (uint)height,
                BPP = 24,
                Type = sig,
            };
            if (0 != (sig & 7))
            {
                meta.OffsetX = file.ReadInt16();
                meta.OffsetY = file.ReadInt16();
                meta.Right   = file.ReadInt16();
                meta.Bottom  = file.ReadInt16();
                if (meta.OffsetX > meta.Right || meta.OffsetY > meta.Bottom ||
                    meta.Right > width || meta.Bottom > height ||
                    meta.OffsetX < 0 || meta.OffsetY < 0)
                    return null;
            }
            return meta;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (CgMetaData)info;
            using (var reader = new Reader (stream, meta))
            {
                reader.Unpack();
                return ImageData.Create (info, PixelFormats.Bgr24, null, reader.Data, (int)info.Width*3);
            }
        }

        internal sealed class Reader : IDisposable
        {
            IBinaryStream   m_input;
            byte[]          m_output;
            int             m_type;
            int             m_width;
            int             m_height;
            int             m_left;
            int             m_top;
            int             m_right;
            int             m_bottom;

            public byte[] Data { get { return m_output; } }

            public Reader (IBinaryStream file, CgMetaData info, byte[] base_image = null)
            {
                m_type = info.Type;
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_left = info.OffsetX;
                m_top = info.OffsetY;
                m_right = info.Right == 0 ? m_width : info.Right;
                m_bottom = info.Bottom == 0 ? m_height : info.Bottom;
                m_output = base_image ?? new byte[3*m_width*m_height];
                m_input = file;
                ShiftTable = InitShiftTable();

                if (0 != (info.Type & 7))
                    m_input.Position = 13;
                else
                    m_input.Position = 5;
            }

            static readonly short[] ShiftX = new short[] { // 409b6c
                0, -1, -3, -2, -1, 0, 1, 2
            };
            static readonly short[] ShiftY = new short[] { // 409b7c
                0, 0, -1, -1, -1, -1, -1, -1
            };
            readonly int[] ShiftTable;

            private int[] InitShiftTable ()
            {
                var table = new int[8];
                for (int i = 0; i < 8; ++i)
                {
                    table[i] = 3 * (ShiftX[i] + ShiftY[i] * m_width);
                }
                return table;
            }

            public void Unpack ()
            {
                if (0 != (m_type & 0x10))
                    UnpackRGB();
                else
                    UnpackIndexed();
            }

            public void UnpackRGB ()
            {
                int right = 3 * (m_width * m_top + m_right);
                int left = 3 * (m_width * m_top + m_left);
                for (int i = m_top; i != m_bottom; ++i)
                {
                    int dst = left;
                    while (dst != right)
                    {
                        byte v9 = m_input.ReadUInt8();
                        if (0 != (v9 & 0x80))
                        {
                            if (0 != (v9 & 0x40))
                            {
                                m_output[dst] = (byte)(m_output[dst - 3] + ((v9 >> 3) & 6) - 2);
                                m_output[dst + 1] = (byte)(m_output[dst - 2] + ((v9 >> 1) & 6) - 2);
                                m_output[dst + 2] = (byte)(m_output[dst - 1] + ((v9 & 3) + 127) * 2);
                            }
                            else
                            {
                                byte v15 = m_input.ReadUInt8();
                                m_output[dst] = (byte)(((v9 << 1) + (v15 & 1)) << 1);
                                m_output[dst + 1] = (byte)(v15 & 0xfe);
                                m_output[dst + 2] = m_input.ReadUInt8();
                            }
                            dst += 3;
                            continue;
                        }
                        uint shift = (uint)v9 >> 4;
                        int count = v9 & 0xF;
                        if (0 == count)
                        {
                            count = (int)m_input.ReadUInt8() + 15;
                            if (270 == count)
                            {
                                int v12;
                                do
                                {
                                    v12 = m_input.ReadUInt8();
                                    count += v12;
                                }
                                while (v12 == 0xff);
                            }
                        }
                        if (0 != shift)
                        {
                            int src = dst + ShiftTable[shift];
                            Binary.CopyOverlapped (m_output, src, dst, count * 3);
                        }
                        dst += 3 * count;
                    }
                    left += m_width*3; //640*3;
                    right += m_width*3; //640*3;
                }
            }

            public void UnpackIndexed ()
            {
                byte[] palette = m_input.ReadBytes (0x180);
                int right = 3 * (m_width * m_top + m_right);
                int left = 3 * (m_width * m_top + m_left); // 3 * (Rect.left + 640 * Rect.top);
                for (int i = m_top; i != m_bottom; ++i)
                {
                    int dst = left;
                    while (dst != right)
                    {
                        byte v13 = m_input.ReadUInt8();
                        if (0 != (v13 & 0x80))
                        {
                            int color = 3 * (v13 & 0x7F);
                            m_output[dst] = palette[color];
                            m_output[dst+1] = palette[color+1];
                            m_output[dst+2] = palette[color+2];
                            dst += 3;
                            continue;
                        }
                        uint shift = (uint)v13 >> 4;
                        int count = v13 & 0xF;
                        if (0 == count)
                        {
                            count = m_input.ReadUInt8() + 15;
                            if (270 == count)
                            {
                                int v16;
                                do
                                {
                                    v16 = m_input.ReadUInt8();
                                    count += v16;
                                }
                                while (v16 == 0xff);
                            }
                        }
                        if (0 != shift)
                        {
                            int src = dst + ShiftTable[shift];
                            Binary.CopyOverlapped (m_output, src, dst, count * 3);
                        }
                        dst += 3 * count;
                    }
                    right += m_width*3;
                    left += m_width*3;
                }
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
