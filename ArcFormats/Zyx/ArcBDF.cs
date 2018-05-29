//! \file       ArcBDF.cs
//! \date       Sun Aug 23 03:17:14 2015
//! \brief      Zyx multi-frame image package.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Zyx
{
    internal class BdfFrame : Entry
    {
        public int  Number;
        public int  Width;
        public int  Height;
        public bool Incremental;
    }

    internal class BdfArchive : ArcFile
    {
        public byte[] FirstFrame;

        public ImageMetaData Info { get; private set; }

        public BdfArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir)
            : base (arc, impl, dir)
        {
            var base_frame = dir.First() as BdfFrame;
            Info = new ImageMetaData {
                Width = (uint)base_frame.Width,
                Height = (uint)base_frame.Height,
                BPP = 24
            };
        }

        public byte[] ReadFrame (BdfFrame frame)
        {
            byte[] pixels;
            if (!frame.Incremental)
            {
                if (null != FirstFrame && 0 == frame.Number)
                    return FirstFrame;
                pixels = new byte[frame.Width * frame.Height * 3];
                using (var input = File.CreateStream (frame.Offset, frame.Size))
                    DecodeFrame (input, pixels);
                if (null == FirstFrame && 0 == frame.Number)
                    FirstFrame = pixels;
            }
            else
            {
                if (null == FirstFrame)
                {
                    var base_frame = Dir.First() as BdfFrame;
                    FirstFrame = new byte[base_frame.Width * base_frame.Height * 3];
                    using (var input = File.CreateStream (base_frame.Offset, base_frame.Size))
                        DecodeFrame (input, FirstFrame);
                }
                pixels = FirstFrame.Clone() as byte[];
                int i = 1;
                foreach (BdfFrame entry in Dir.Skip(1))
                {
                    if (i++ > frame.Number)
                        break;
                    using (var input = File.CreateStream (entry.Offset, entry.Size))
                        DecodeFrame (input, pixels, entry.Incremental);
                }
            }
            return pixels;
        }

        private void DecodeFrame (IBinaryStream input, byte[] output, bool incremental = false)
        {
            int v2 = incremental ? 6 : 4;
            int dst = 0;
            while (dst < output.Length)
            {
                int count, offset;
                int cmd = input.ReadByte();
                if (-1 == cmd)
                    break;
                switch (cmd)
                {
                case 0:
                    {
                        int src = dst-3;
                        count = input.ReadByte();
                        for (int i = 0; i < count; ++i)
                        {
                            output[dst++] = output[src];
                            output[dst++] = output[src+1];
                            output[dst++] = output[src+2];
                        }
                        break;
                    }
                case 1:
                    {
                        count  = 3 * input.ReadByte();
                        offset = 3 * input.ReadByte();
                        int src = dst - offset;
                        Binary.CopyOverlapped (output, src, dst, count);
                        dst += count;
                        break;
                    }
                case 2:
                    {
                        count  = 3 * input.ReadByte();
                        offset = 3 * input.ReadUInt16();
                        int src = dst - offset;
                        Binary.CopyOverlapped (output, src, dst, count);
                        dst += count;
                        break;
                    }
                case 3:
                    {
                        offset = 3 * input.ReadByte();
                        int src = dst - offset;
                        output[dst++] = output[src++];
                        output[dst++] = output[src++];
                        output[dst++] = output[src++];
                        break;
                    }
                case 4:
                    {
                        offset = 3 * input.ReadUInt16();
                        int src = dst - offset;
                        output[dst++] = output[src++];
                        output[dst++] = output[src++];
                        output[dst++] = output[src++];
                        break;
                    }
                default:
                    if (v2 < 5 || cmd > 6)
                    {
                        count = (cmd - v2) * 3;
                        input.Read (output, dst, count);
                        dst += count;
                    }
                    else
                    {
                        if (5 == cmd)
                        {
                            count = input.ReadByte();
                        }
                        else
                        {
                            count = input.ReadUInt16();
                        }
                        dst += 3 * count;
                    }
                    break;
                }
            }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class BdfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BDF"; } }
        public override string Description { get { return "Zyx multi-frame image package"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (count <= 0 || count > 100)
                return null;
            int first_offset = file.View.ReadInt32 (4);
            if (0 != first_offset)
                return null;
            string base_name = Path.GetFileNameWithoutExtension (file.Name);

            int base_offset = 4 + count * 0x1C;
            int index_offset = 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new BdfFrame {
                    Number = i,
                    Name = string.Format ("{0}#{1:D2}", base_name, i),
                    Type = "image",
                    Offset = base_offset + file.View.ReadUInt32 (index_offset),
                    Size = file.View.ReadUInt32 (index_offset+4),
                    Incremental = 0 != file.View.ReadInt32 (index_offset+8),
                    Width = file.View.ReadInt32 (index_offset+0x14),
                    Height = file.View.ReadInt32 (index_offset+0x18),
                };
                if (entry.Size > 0)
                {
                    if (entry.Size < 4 || entry.Width <= 0 || entry.Height <= 0
                        || !entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                index_offset += 0x1C;
            }
            return new BdfArchive (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var bdf = arc as BdfArchive;
            var frame = entry as BdfFrame;
            if (null == bdf || null == frame)
                return base.OpenImage (arc, entry);
            return new BdfImageDecoder (bdf, frame);
        }
    }

    internal sealed class BdfImageDecoder : BinaryImageDecoder
    {
        BdfArchive          m_bdf;
        BdfFrame            m_frame;

        public BdfImageDecoder (BdfArchive arc, BdfFrame entry)
            : base (arc.File.CreateStream (entry.Offset, entry.Size), arc.Info)
        {
            m_bdf = arc;
            m_frame = entry;
        }

        protected override ImageData GetImageData ()
        {
            var pixels = m_bdf.ReadFrame (m_frame);
            return ImageData.Create (Info, PixelFormats.Bgr24, null, pixels);
        }
    }
}
