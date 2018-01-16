//! \file       ImagePNG.cs
//! \date       Sat Jul 05 00:09:15 2014
//! \brief      PNG image implementation.
//
// Copyright (C) 2014-2015 by morkt
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

using System.IO;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.Windows.Media.Imaging;
using GameRes.Utility;
using System.Windows.Media;

namespace GameRes
{
    [Export(typeof(ImageFormat))]
    public class PngFormat : ImageFormat
    {
        public override string         Tag { get { return "PNG"; } }
        public override string Description { get { return "Portable Network Graphics image"; } }
        public override uint     Signature { get { return 0x474e5089; } }
        public override bool      CanWrite { get { return true; } }

        public static readonly byte[] HeaderBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var decoder = new PngBitmapDecoder (file.AsStream,
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames[0];
            frame.Freeze();
            return new ImageData (frame, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add (BitmapFrame.Create (image.Bitmap, null, null, null));
            if (0 == image.OffsetX && 0 == image.OffsetY)
            {
                encoder.Save (file);
                return;
            }
            using (var mem_stream = new MemoryStream())
            {
                encoder.Save (mem_stream);
                byte[] buf = mem_stream.GetBuffer();
                long header_pos = 8;
                mem_stream.Position = header_pos;
                uint header_length = ReadChunkLength (mem_stream);
                file.Write (buf, 0, (int)(header_pos+header_length+12));
                WriteOffsChunk (file, image);
                mem_stream.Position = header_pos+header_length+12;
                mem_stream.CopyTo (file);
            }
        }

        uint ReadChunkLength (Stream file)
        {
            int length = file.ReadByte() << 24;
            length |= file.ReadByte() << 16;
            length |= file.ReadByte() << 8;
            length |= file.ReadByte();
            return (uint)length;
        }

        void WriteOffsChunk (Stream file, ImageData image)
        {
            using (var membuf = new MemoryStream (32))
            {
                using (var bin = new BinaryWriter (membuf, Encoding.ASCII, true))
                {
                    bin.Write (Binary.BigEndian ((uint)9));
                    char[] tag = { 'o', 'F', 'F', 's' };
                    bin.Write (tag);
                    bin.Write (Binary.BigEndian ((uint)image.OffsetX));
                    bin.Write (Binary.BigEndian ((uint)image.OffsetY));
                    bin.Write ((byte)0);
                    bin.Flush();
                    uint crc = Crc32.Compute (membuf.GetBuffer(), 4, 13);
                    bin.Write (Binary.BigEndian (crc));
                }
                file.Write (membuf.GetBuffer(), 0, 9+12); // chunk + size+id+crc
            }
        }

        void SkipBytes (IBinaryStream file, uint num)
        {
            if (file.CanSeek)
                file.Seek (num, SeekOrigin.Current);
            else
            {
                for (int i = 0; i < num / 4; ++i)
                    file.ReadInt32();
                for (int i = 0; i < num % 4; ++i)
                    file.ReadByte();
            }
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.ReadUInt32();
            if (file.ReadUInt32() != 0x0a1a0a0d)
                return null;
            uint chunk_size = Binary.BigEndian (file.ReadUInt32());
            byte[] chunk_type = file.ReadBytes (4);
            if (!Binary.AsciiEqual (chunk_type, "IHDR"))
                return null;

            var meta = new ImageMetaData();
            meta.Width   = Binary.BigEndian (file.ReadUInt32());
            meta.Height  = Binary.BigEndian (file.ReadUInt32());
            int bpp = file.ReadByte();
            if (bpp != 1 && bpp != 2 && bpp != 4 && bpp != 8 && bpp != 16)
                return null;
            int color_type = file.ReadByte();
            switch (color_type)
            {
            case 2: meta.BPP = bpp*3; break;
            case 3: meta.BPP = 24; break;
            case 4: meta.BPP = bpp*2; break;
            case 6: meta.BPP = bpp*4; break;
            case 0: meta.BPP = bpp; break;
            default: return null;
            }
            SkipBytes (file, 7);

            for (;;)
            {
                chunk_size = Binary.BigEndian (file.ReadUInt32());
                file.Read (chunk_type, 0, 4);
                if (Binary.AsciiEqual (chunk_type, "IDAT") || Binary.AsciiEqual (chunk_type, "IEND"))
                    break;
                if (Binary.AsciiEqual (chunk_type, "oFFs"))
                {
                    int x = Binary.BigEndian (file.ReadInt32());
                    int y = Binary.BigEndian (file.ReadInt32());
                    if (0 == file.ReadByte())
                    {
                        meta.OffsetX = x;
                        meta.OffsetY = y;
                    }
                    break;
                }
                SkipBytes (file, chunk_size+4);
            }
            return meta;
        }

        public static long FindChunk (IBinaryStream file, string chunk)
        {
            try
            {
                var buf = new byte[4];
                file.Position = 8;
                while (-1 != file.PeekByte())
                {
                    long chunk_offset = file.Position;
                    uint chunk_size = Binary.BigEndian (file.ReadUInt32());
                    if (4 != file.Read (buf, 0, 4))
                        break;
                    if (Binary.AsciiEqual (buf, chunk))
                        return chunk_offset;
                    file.Position += chunk_size + 4;
                }
            }
            catch { /* ignore errors */ }
            return -1L;
        }
    }
}
