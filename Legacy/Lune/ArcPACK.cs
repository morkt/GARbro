//! \file       ArcPACK.cs
//! \date       2018 Feb 03
//! \brief      Lune Adv Game resource archive.
//
// Copyright (C) 2018 by morkt
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
using System.Windows.Media;
using GameRes.Utility;

// [001110][Lune] Wasurekaketa Kokoro no Kakera
// [010413][Sarang] Tokyo Yuuyuu

namespace GameRes.Formats.Lune
{
    [Export(typeof(ArchiveFormat))]
    [ExportMetadata("Priority", -2)]
    public class PackOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PACK/LUNE"; } }
        public override string Description { get { return "Lune Adv Game engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PackOpener ()
        {
            Extensions = new string[] { "dat", "wda" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint first_offset = file.View.ReadUInt32 (0);
            if (first_offset <= 8 || first_offset >= file.MaxOffset || 0 != (first_offset & 7))
                return null;
            int count = (int)(first_offset / 8);
            if (!IsSaneCount (count))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            string type = file.Name.HasAnyOfExtensions (".wda", ".bgm") ? "audio"
                        : file.Name.HasExtension (".scr") ? "script"
                        : "image";
            if (base_name == "pack")
                base_name = Path.GetExtension (file.Name).TrimStart ('.');
            uint index_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D5}", base_name, i),
                    Type = type,
                    Offset = file.View.ReadUInt32 (index_offset),
                    Size   = file.View.ReadUInt32 (index_offset+4),
                };
                if (entry.Offset < first_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (entry.Size > 0)
                    dir.Add (entry);
                index_offset += 8;
            }
            if (dir.Count == 0 || dir[dir.Count-1].Offset + dir[dir.Count-1].Size != file.MaxOffset)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!arc.File.Name.HasAnyOfExtensions (".wda", ".bgm"))
                return base.OpenEntry (arc, entry);
            uint sample_rate = arc.File.Name.HasExtension (".bgm") ? 44100u : 22050u;
            var format = new WaveFormat {
                FormatTag = 1,
                Channels = 1,
                SamplesPerSecond = sample_rate,
                BlockAlign = 2,
                BitsPerSample = 16,
            };
            format.SetBPS();
            byte[] wav_header;
            using (var output = new MemoryStream (0x2C))
            {
                WaveAudio.WriteRiffHeader (output, format, entry.Size);
                wav_header = output.ToArray();
            }
            var pcm_data = arc.File.CreateStream (entry.Offset, entry.Size);
            return new PrefixStream (wav_header, pcm_data);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (0 == input.Signature)
                return new Pack2ImageDecoder (input);
            else if (arc.File.Name.HasExtension (".msk"))
                return new PackMaskDecoder (input);
            else
                return new PackImageDecoder (input);
        }
    }

    internal class PackImageDecoder : BinaryImageDecoder
    {
        protected int   m_start_pos;
        protected int   m_stride;
        protected int   m_first_pixel_size;
        protected byte  m_max_pixel = 0x20;


        public PixelFormat       Format { get; protected set; }

        public PackImageDecoder (IBinaryStream input) : base (input)
        {
            ReadHeader();
            m_start_pos = (int)m_input.Position;
            m_stride = (int)Info.Width * Format.BitsPerPixel / 8;
        }

        protected virtual void ReadHeader ()
        {
            Format = PixelFormats.Bgr24;
            Info = new ImageMetaData {
                Width = m_input.ReadUInt16(),
                Height = m_input.ReadUInt16(),
                BPP = 24,
            };
            m_first_pixel_size = 1;
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = m_start_pos;
            var data = ReadDataBytes();
            using (var bits = new MsbBitStream (m_input.AsStream, true))
            {
                var pixels = new byte[m_stride * (int)Info.Height];
                if (24 == Info.BPP)
                    Unpack24bpp (bits, data, pixels);
                else
                    Unpack8bpp (bits, data, pixels);
                return ImageData.Create (Info, Format, null, pixels);
            }
        }

        protected void Unpack24bpp (MsbBitStream bits, byte[] data, byte[] output)
        {
            int src = 0, dst = 0;
            for (int i = 0; i < m_first_pixel_size; ++i)
                output[dst++] = data[src++];
            while (dst < output.Length)
            {
                int ctl = bits.GetBits (2);
                byte v;
                if (0 == ctl)
                {
                    v = data[src++];
                }
                else
                {
                    v = output[dst-3];
                    if (ctl == 2)
                    {
                        if (bits.GetNextBit() != 0)
                            v -= 1;
                        else
                            v += 1;
                    }
                    else if (ctl == 3)
                    {
                        ctl = bits.GetBits (2);
                        if (ctl == 2)
                        {
                            if (bits.GetNextBit() != 0)
                                v -= 3;
                            else
                                v += 3;
                        }
                        else if (ctl == 3)
                        {
                            ctl = bits.GetBits (2);
                            if (ctl == 2)
                            {
                                if (bits.GetNextBit() != 0)
                                    v -= 5;
                                else
                                    v += 5;
                            }
                            else if (ctl == 3)
                            {
                                switch (bits.GetBits (2))
                                {
                                case 3:  v -= 7; break;
                                case 2:  v += 7; break;
                                case 1:  v -= 6; break;
                                default: v += 6; break;
                                }
                            }
                            else if (ctl == 1)
                                v -= 4;
                            else
                                v += 4;
                        }
                        else if (ctl == 1)
                            v -= 2;
                        else
                            v += 2;
                    }
                }
                output[dst++] = v;
            }
        }

        protected void Unpack8bpp (MsbBitStream bits, byte[] data, byte[] output)
        {
            int src = 0;
            int dst = 0;
            byte init_value = data[src++];
            output[dst++] = init_value;
            int bit_count = 0;
            int y = 0;
            int x = 1;
            while (dst < output.Length)
            {
                int ctl = bits.GetBits (2);
                if (0 == ctl)
                {
                    int count;
                    if (bit_count > 0)
                        count = bits.GetBits (14 - bit_count);
                    else
                        count = bits.GetBits (6);
                    while (count --> 0 && dst < output.Length)
                    {
                        if (y == 0 || x + 1 == m_stride)
                            output[dst] = init_value;
                        else
                            output[dst] = output[dst - m_stride + 1];
                        ++dst;
                        if (++x == m_stride)
                        {
                            x = 0;
                            ++y;
                        }
                    }
                    bit_count = 0;
                    continue;
                }
                else if (1 == ctl)
                {
                    bit_count += 2;
                    if (0 == x)
                        output[dst] = init_value;
                    else
                        output[dst] = output[dst - m_stride - 1];
                }
                else if (2 == ctl)
                {
                    bit_count += 2;
                    output[dst] = data[src++];
                }
                else
                {
                    bit_count += 3;
                    if (bits.GetNextBit() != 0)
                        output[dst] = output[dst - m_stride];
                    else
                        output[dst] = output[dst-1];
                }
                ++dst;
                if (++x == m_stride)
                {
                    x = 0;
                    ++y;
                }
                if (bit_count >= 8)
                    bit_count -= 8;
            }
            if (8 == Info.BPP)
            {
                byte max = m_max_pixel; // System.Linq.Enumerable.Max (output);
                if (max != 0 && max != 0xFF)
                {
                    for (int i = 0; i < output.Length; ++i)
                        output[i] = (byte)(output[i] * 0xFF / max);
                }
            }
        }

        protected byte[] ReadDataBytes ()
        {
            uint data_pos = m_input.ReadUInt32();
            long ctl_pos = m_input.Position;
            m_input.Seek (data_pos, SeekOrigin.Current);
            var data = m_input.ReadBytes ((int)(m_input.Length - data_pos));
            if (0 == data.Length)
                throw new InvalidFormatException();
            m_input.Position = ctl_pos;
            return data;
        }
    }

    internal sealed class PackMaskDecoder : PackImageDecoder
    {
        public PackMaskDecoder (IBinaryStream input) : base (input) { }

        protected override void ReadHeader ()
        {
            Format = PixelFormats.Gray8;
            Info = new ImageMetaData {
                Width = m_input.ReadUInt16(),
                Height = m_input.ReadUInt16(),
                BPP = 8
            };
        }
    }

    internal class Pack2ImageDecoder : PackImageDecoder
    {
        public Pack2ImageDecoder (IBinaryStream input) : base (input) { }

        protected override void ReadHeader ()
        {
            Info = new ImageMetaData {
                OffsetX = m_input.ReadInt16(),
                OffsetY = m_input.ReadInt16(),
                Width   = m_input.ReadUInt16(),
                Height  = m_input.ReadUInt16(),
                BPP     = m_input.ReadUInt16(),
            };
            if (Info.BPP < 4 || Info.BPP > 24)
                throw new InvalidFormatException();
            m_first_pixel_size = 3;
            Format = Info.BPP == 24 ? PixelFormats.Bgr24
                   : Info.BPP == 4  ? PixelFormats.Gray4
                                    : PixelFormats.Gray8;
        }
    }
}
